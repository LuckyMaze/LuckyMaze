using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LuckyMaze.Domain;
using LuckyMaze.Domain.Enums;
using LuckyMaze.Infrastructure;
using LuckyMaze.Infrastructure.Services;

namespace LuckyMaze.Application.Services
{
    public class GameManager : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IMazeGenerator _mazeGenerator;
        private readonly IAiSolver _aiSolver;
        private readonly IMazeHardwareService _hardwareService;
        private readonly IGameNotificationService _notificationService;
        private readonly ILogger<GameManager> _logger;

        private GameState _state = GameState.Idle;
        private Guid? _currentGameId;
        private Maze? _currentMaze;
        private List<Coordinate> _aiPath = new();
        private int _currentStep = 0;
        private int _timerSeconds = 0;
        private GameSettings? _currentGameSettings;

        private readonly ConcurrentDictionary<string, ConnectedPlayer> _players = new();
        private readonly ConcurrentDictionary<string, PlayerBet> _bets = new();

        public GameManager(
            IServiceScopeFactory scopeFactory,
            IMazeGenerator mazeGenerator,
            IAiSolver aiSolver,
            IMazeHardwareService hardwareService,
            IGameNotificationService notificationService,
            ILogger<GameManager> logger)
        {
            _scopeFactory = scopeFactory;
            _mazeGenerator = mazeGenerator;
            _aiSolver = aiSolver;
            _hardwareService = hardwareService;
            _notificationService = notificationService;
            _logger = logger;
        }

        public GameState State => _state;
        public int TimerSeconds => _timerSeconds;
        public Maze? CurrentMaze => _currentMaze;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("GameManager background loop started.");

            try
            {
                // Only actually homes if the last shutdown genuinely parked at the true origin (see
                // MazeHardwareService.PrepareForShutdownAsync) - a no-op after an ordinary redeploy
                // that didn't go through a real shutdown, so this is always safe to call here.
                await _hardwareService.TryAutoHomeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while checking for an auto-home on startup.");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in GameManager tick loop.");
                }

                await Task.Delay(1000, stoppingToken);
            }
        }

        private async Task TickAsync(CancellationToken stoppingToken)
        {
            if (_state == GameState.Countdown)
            {
                _timerSeconds--;
                await _notificationService.BroadcastCountdownTickAsync(_timerSeconds);

                if (_timerSeconds <= 0)
                {
                    await TransitionToBettingAsync();
                }
            }
            else if (_state == GameState.Betting)
            {
                _timerSeconds--;
                await BroadcastGameStateAsync();

                if (_timerSeconds <= 0)
                {
                    await EvaluateBetsAndStartAsync();
                }
            }
            else if (_state == GameState.Starting)
            {
                _timerSeconds--;
                await BroadcastGameStateAsync();

                if (_timerSeconds <= 0)
                {
                    await TransitionToPlayingAsync(stoppingToken);
                }
            }
            else if (_state == GameState.Finished)
            {
                _timerSeconds--;
                if (_timerSeconds <= 0)
                {
                    await ResetToIdleAsync();
                }
            }
        }

        private async Task TransitionToCountdownAsync()
        {
            _state = GameState.Countdown;
            _timerSeconds = 3;
            await BroadcastGameStateAsync();
        }

        private async Task TransitionToBettingAsync()
        {
            _state = GameState.Betting;
            _timerSeconds = 30;
            _bets.Clear();

            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var settingsService = scope.ServiceProvider.GetRequiredService<IGameSettingsService>();
                _currentGameSettings = await settingsService.GetSettingsAsync();

                var dbContext = scope.ServiceProvider.GetRequiredService<LuckyMazeDbContext>();

                int size = (int)_currentGameSettings.MazeSize;
                _currentMaze = _mazeGenerator.Generate(size, size, 2);

                dbContext.Mazes.Add(_currentMaze);
                await dbContext.SaveChangesAsync();

                var game = new Game
                {
                    State = GameState.Betting,
                    MazeId = _currentMaze.Id,
                    StartedAt = DateTime.UtcNow
                };

                dbContext.Games.Add(game);
                await dbContext.SaveChangesAsync();

                _currentGameId = game.Id;
            }

            _logger.LogInformation("Betting started. Generated maze {MazeId} & game {GameId}", _currentMaze.Id, _currentGameId);

            var exits = JsonSerializer.Deserialize<List<MazeExit>>(_currentMaze.Exits) ?? new();
            await _notificationService.BroadcastMazeGeneratedAsync(new
            {
                mazeId = _currentMaze.Id,
                width = _currentMaze.Width,
                height = _currentMaze.Height,
                gridData = JsonSerializer.Deserialize<List<MazeCell>>(_currentMaze.GridData),
                exits = exits
            });

            await BroadcastGameStateAsync();
        }

        private async Task EvaluateBetsAndStartAsync()
        {
            if (_bets.IsEmpty)
            {
                _logger.LogInformation("No bets placed. Resetting back to Idle.");
                await ResetToIdleAsync();
                return;
            }

            _state = GameState.Starting;
            _timerSeconds = 3;

            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<LuckyMazeDbContext>();
                var game = await dbContext.Games.FindAsync(_currentGameId);
                if (game != null)
                {
                    game.State = GameState.Starting;
                    await dbContext.SaveChangesAsync();
                }
            }

            await BroadcastGameStateAsync();
        }

        private async Task TransitionToPlayingAsync(CancellationToken stoppingToken)
        {
            _state = GameState.Playing;

            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<LuckyMazeDbContext>();
                var game = await dbContext.Games.FindAsync(_currentGameId);
                if (game != null)
                {
                    game.State = GameState.Playing;
                    await dbContext.SaveChangesAsync();
                }
            }

            await BroadcastGameStateAsync();

            var startCoord = new Coordinate(_currentMaze!.Width / 2, _currentMaze.Height / 2);
            _aiPath = _aiSolver.Solve(_currentMaze, startCoord);
            _currentStep = 0;

            // Discarded on purpose - the tick loop drives state transitions independently and
            // doesn't wait on this. But an unobserved exception here is silently swallowed by the
            // runtime and never logged anywhere, so it must catch and log its own failures.
            _ = Task.Run(() => RunSimulationAsync(stoppingToken), stoppingToken);
        }

        private async Task RunSimulationAsync(CancellationToken stoppingToken)
        {
            try
            {
                await RunSimulationCoreAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during the AI simulation run.");
            }
        }

        private async Task RunSimulationCoreAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Starting AI simulation run with {Steps} steps.", _aiPath.Count);

            await _hardwareService.InitializeAsync(_currentMaze!);

            while (_currentStep < _aiPath.Count && _state == GameState.Playing && !stoppingToken.IsCancellationRequested)
            {
                var current = _aiPath[_currentStep];

                var dir = Direction.None;
                if (_currentStep > 0)
                {
                    var prev = _aiPath[_currentStep - 1];
                    if (current.Y < prev.Y) dir = Direction.North;
                    else if (current.X > prev.X) dir = Direction.East;
                    else if (current.Y > prev.Y) dir = Direction.South;
                    else if (current.X < prev.X) dir = Direction.West;
                }

                // Dispatched together, not sequentially: ShowStepAsync doesn't return until the
                // carriage physically finishes moving, so awaiting it first before broadcasting
                // meant the LED panel visibly updated well before the web UI's ball caught up.
                var hardwareTask = _hardwareService.ShowStepAsync(current.X, current.Y, dir);
                var broadcastTask = _notificationService.BroadcastAiStepAsync(current.X, current.Y, dir.ToString());
                await Task.WhenAll(hardwareTask, broadcastTask);

                if (_currentStep == _aiPath.Count - 1)
                {
                    await FinishGameAsync(current);
                    break;
                }

                _currentStep++;
                int delayMs = _currentGameSettings?.GameSpeedMs ?? 850;
                await Task.Delay(delayMs, stoppingToken);
            }
        }

        private async Task FinishGameAsync(Coordinate exitCoord)
        {
            _state = GameState.Finished;
            _timerSeconds = 10;

            var exits = JsonSerializer.Deserialize<List<MazeExit>>(_currentMaze!.Exits) ?? new();
            var matchedExit = exits.FirstOrDefault(e => e.X == exitCoord.X && e.Y == exitCoord.Y);
            string winningExitName = matchedExit?.Name ?? "Unknown Exit";

            _logger.LogInformation("AI reached exit: {ExitName}. Processing payouts...", winningExitName);

            await _hardwareService.FlashWinnerAsync(winningExitName);

            decimal totalPool = _bets.Values.Sum(b => b.Amount);
            var winningBets = _bets.Values.Where(b => b.ExitName.Equals(winningExitName, StringComparison.OrdinalIgnoreCase)).ToList();
            decimal winningPool = winningBets.Sum(b => b.Amount);

            var payoutsList = new List<PayoutDetail>();

            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<LuckyMazeDbContext>();

                var dbBets = await dbContext.Bets.Where(b => b.GameId == _currentGameId).ToListAsync();

                // EF Core can't translate a lookup against a live ConcurrentDictionary
                // (ContainsKey) into SQL - materialize the keys into a plain list first, which
                // translates to a normal parameterized IN/ANY clause.
                var connectedExternalIds = _players.Keys.ToList();
                var dbUsers = await dbContext.Users.Where(u => connectedExternalIds.Contains(u.ExternalId)).ToListAsync();

                foreach (var dbBet in dbBets)
                {
                    var player = dbUsers.FirstOrDefault(u => u.Id == dbBet.PlayerId);
                    if (player == null) continue;

                    if (dbBet.ExitName.Equals(winningExitName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Winnings are proportional to the bet's share of the winning pool.
                        decimal earnings = (dbBet.Amount / winningPool) * totalPool;

                        dbBet.Status = BetStatus.Won;
                        player.Balance += earnings;

                        payoutsList.Add(new PayoutDetail
                        {
                            DisplayName = player.DisplayName,
                            BetAmount = dbBet.Amount,
                            PayoutAmount = earnings,
                            NetProfit = earnings - dbBet.Amount
                        });

                        if (_players.TryGetValue(player.ExternalId, out var connectedPlayer))
                        {
                            connectedPlayer.Balance = player.Balance;
                        }
                    }
                    else
                    {
                        dbBet.Status = BetStatus.Lost;

                        payoutsList.Add(new PayoutDetail
                        {
                            DisplayName = player.DisplayName,
                            BetAmount = dbBet.Amount,
                            PayoutAmount = 0,
                            NetProfit = -dbBet.Amount
                        });
                    }
                }

                // Auto-replenish bankrupt players so they can keep playing.
                foreach (var player in dbUsers)
                {
                    if (player.Balance <= 0)
                    {
                        player.Balance = 1000.00m;
                        if (_players.TryGetValue(player.ExternalId, out var connectedPlayer))
                        {
                            connectedPlayer.Balance = player.Balance;
                        }
                    }
                }

                var game = await dbContext.Games.FindAsync(_currentGameId);
                if (game != null)
                {
                    game.State = GameState.Finished;
                    game.WinningExit = winningExitName;
                    game.EndedAt = DateTime.UtcNow;
                }

                await dbContext.SaveChangesAsync();
            }

            await _notificationService.BroadcastGameFinishedAsync(winningExitName, payoutsList);
            await BroadcastGameStateAsync();
        }

        private async Task ResetToIdleAsync()
        {
            _state = GameState.Idle;
            _timerSeconds = 0;
            _currentMaze = null;
            _currentGameId = null;
            _aiPath.Clear();
            _currentStep = 0;
            _bets.Clear();

            foreach (var player in _players.Values)
            {
                player.IsReady = false;
            }

            await _hardwareService.ResetAsync();
            _logger.LogInformation("Game state reset to Idle.");
            await BroadcastGameStateAsync();
        }

        public async Task PlayerConnectedAsync(string externalId, string connectionId)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LuckyMazeDbContext>();
            var user = await dbContext.Users.SingleOrDefaultAsync(u => u.ExternalId == externalId);

            if (user == null) return;

            _players[externalId] = new ConnectedPlayer
            {
                UserId = user.Id,
                ExternalId = user.ExternalId,
                DisplayName = user.DisplayName,
                ConnectionId = connectionId,
                Balance = user.Balance,
                IsReady = false
            };

            _logger.LogInformation("Player connected: {Name} (Conn: {Conn})", user.DisplayName, connectionId);
            await BroadcastGameStateAsync();
        }

        public async Task PlayerDisconnectedAsync(string connectionId)
        {
            var pair = _players.FirstOrDefault(p => p.Value.ConnectionId == connectionId);
            if (pair.Key == null) return;

            _players.TryRemove(pair.Key, out var removedPlayer);
            _logger.LogInformation("Player disconnected: {Name} (Conn: {Conn})", removedPlayer?.DisplayName, connectionId);

            // If a disconnect breaks the start condition mid-countdown, abort the round.
            if (_state == GameState.Countdown)
            {
                int total = _players.Count;
                int ready = _players.Values.Count(p => p.IsReady);
                if (!ShouldStart(total, ready))
                {
                    _logger.LogInformation("Start conditions no longer met after disconnect. Resetting to Idle.");
                    await ResetToIdleAsync();
                }
            }
            else
            {
                await BroadcastGameStateAsync();
            }
        }

        public async Task ToggleReadyAsync(string externalId, bool isReady)
        {
            if (!_players.TryGetValue(externalId, out var player)) return;

            player.IsReady = isReady;
            _logger.LogInformation("Player {Name} toggled ready state to {IsReady}", player.DisplayName, isReady);

            if (_state == GameState.Idle)
            {
                int total = _players.Count;
                int ready = _players.Values.Count(p => p.IsReady);

                if (ShouldStart(total, ready))
                {
                    await TransitionToCountdownAsync();
                    return;
                }
            }

            await BroadcastGameStateAsync();
        }

        public async Task PlaceBetAsync(string externalId, string exitName, decimal amount)
        {
            if (_state != GameState.Betting)
            {
                _logger.LogWarning("Bet rejected: Game not in Betting state.");
                return;
            }

            if (!_players.TryGetValue(externalId, out var player))
                return;

            decimal minBet = _currentGameSettings?.MinBet ?? 1.00m;
            decimal maxBet = _currentGameSettings?.MaxBet ?? 500.00m;

            if (amount <= 0 || player.Balance < amount || amount < minBet || amount > maxBet)
            {
                _logger.LogWarning("Bet rejected: Invalid amount. Balance: {Bal}, Bet: {Bet}, Min: {Min}, Max: {Max}", player.Balance, amount, minBet, maxBet);
                return;
            }

            player.Balance -= amount;

            _bets[externalId] = new PlayerBet
            {
                UserId = player.UserId,
                DisplayName = player.DisplayName,
                ExitName = exitName,
                Amount = amount
            };

            _logger.LogInformation("Player {Name} bet {Amount} on {Exit}", player.DisplayName, amount, exitName);

            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<LuckyMazeDbContext>();

                var dbUser = await dbContext.Users.FindAsync(player.UserId);
                if (dbUser != null)
                {
                    dbUser.Balance = player.Balance;
                }

                dbContext.Bets.Add(new Bet
                {
                    GameId = _currentGameId!.Value,
                    PlayerId = player.UserId,
                    ExitName = exitName,
                    Amount = amount,
                    Status = BetStatus.Pending
                });
                await dbContext.SaveChangesAsync();
            }

            await BroadcastGameStateAsync();

            if (_bets.Count == _players.Count)
            {
                _logger.LogInformation("All connected players have placed bets. Starting game immediately.");
                await EvaluateBetsAndStartAsync();
            }
        }

        private bool ShouldStart(int total, int ready)
        {
            if (total == 0) return false;
            if (total == 1) return ready == 1; // Solo testing
            return ready >= total * 0.75; // 75% rule
        }

        private async Task BroadcastGameStateAsync()
        {
            var gameStateDto = new
            {
                state = _state.ToString(),
                timerSeconds = _timerSeconds,
                players = _players.Values.Select(p => new
                {
                    displayName = p.DisplayName,
                    balance = p.Balance,
                    isReady = p.IsReady,
                    hasBet = _bets.ContainsKey(p.ExternalId)
                }).ToList(),
                bets = _bets.Values.Select(b => new
                {
                    displayName = b.DisplayName,
                    exitName = b.ExitName,
                    amount = b.Amount
                }).ToList()
            };

            await _notificationService.BroadcastGameStateAsync(gameStateDto);
        }
    }
}
