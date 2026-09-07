using System.IO.Ports;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LuckyMaze.Domain;
using LuckyMaze.Domain.Enums;

namespace LuckyMaze.Infrastructure.Services
{
    /// <summary>
    /// Talks to the LuckyMaze/LEDController firmware (a 64x64 HUB75 panel on a Raspberry Pi Pico).
    /// The panel's serial protocol is fixed size and request/response: CLEAR, GRID + 64 rows of 64
    /// walkable/blocked cells, MOVE x y [size], COLOR and CENTER, each answered with one OK/ERR
    /// line. MOVE just places the dot (no pathfinding on the Pico's side - see move_dot_to in the
    /// LEDController repo for why). See https://github.com/LuckyMaze/LEDController for the
    /// authoritative protocol.
    /// </summary>
    public class MazeHardwareService : IMazeHardwareService, IDisposable
    {
        private const int PanelSize = 64;

        // MOVE just places the dot directly and redraws - no pathfinding or per-pixel animation
        // on the Pico's side (that used to run a full BFS over the 64x64 grid on every MOVE,
        // which could exhaust a Pico's RAM - see LuckyMaze/LEDController's move_dot_to). So this
        // only needs to cover one command's round-trip, not a multi-second animation, but stays
        // a bit generous: too short doesn't just log a warning, the Pico still finishes and
        // writes its response after we've given up reading it, and that stray line desyncs every
        // reply after it.
        private static readonly int PicoReadTimeoutMs = (int)TimeSpan.FromSeconds(3).TotalMilliseconds;

        private readonly ILogger<MazeHardwareService> _logger;

        private readonly string? _picoPortName;
        private readonly string? _klippySocketPath;
        private readonly string? _homeMarkerPath;
        private readonly IServiceScopeFactory _scopeFactory;

        private SerialPort? _serialPort;
        private bool _isSerialInitialized = false;
        private int _klippyRequestId = 0;

        // The maze/raster transform (pitch = cell + wall in pixels, corridor = cell interior size,
        // and the offset that centers the maze on the panel) is shared between InitializeAsync and
        // ShowStepAsync, so both agree on where a given maze cell lands.
        private int _rasterPitch = 1;
        private int _rasterCorridor = 1;
        private int _rasterOffsetX;
        private int _rasterOffsetY;

        public MazeHardwareService(
            IConfiguration configuration,
            ILogger<MazeHardwareService> logger,
            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;

            // These stay in .env, not GameSettings: they're tied to this specific container's
            // device/volume mounts (compose.hardware.yml), so changing them needs a restart
            // regardless of where the value lives. Everything else about carriage calibration -
            // pixel pitch, origin offset, axis inversion, feed rates, acceleration - lives in
            // GameSettings instead (see GetCalibrationAsync), editable live from the admin panel
            // with no restart or redeploy.
            _picoPortName = configuration["Hardware:PicoPort"];
            _klippySocketPath = configuration["Hardware:KlippySocketPath"];

            var stateDirectory = configuration["Hardware:StateDirectory"];
            _homeMarkerPath = string.IsNullOrWhiteSpace(stateDirectory)
                ? null
                : Path.Combine(stateDirectory, "carriage-at-home");

            InitializeSerialPort();
        }

        /// <summary>
        /// MazeHardwareService is a singleton (it owns the persistent SerialPort), so it can't
        /// constructor-inject the scoped GameSettings/DbContext - it resolves them fresh through a
        /// scope on every call instead, the same pattern GameManager uses for the same reason.
        /// </summary>
        private async Task<GameSettings> GetCalibrationAsync()
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var settingsService = scope.ServiceProvider.GetRequiredService<IGameSettingsService>();
            return await settingsService.GetSettingsAsync();
        }

        private void InitializeSerialPort()
        {
            if (string.IsNullOrWhiteSpace(_picoPortName))
            {
                _logger.LogInformation("Hardware Pico Serial Port not configured. Running Pico in MOCK mode.");
                return;
            }

            try
            {
                _serialPort = new SerialPort(_picoPortName, 115200, Parity.None, 8, StopBits.One)
                {
                    NewLine = "\n",
                    ReadTimeout = PicoReadTimeoutMs,
                    WriteTimeout = 500,
                    // CircuitPython's USB console only starts streaming output once DTR is
                    // asserted - the same way a terminal app "opening" the port does. Without
                    // this, every command still runs on the Pico (CLEAR genuinely clears, GRID
                    // genuinely loads) but the reply never reaches the host, so every read times
                    // out even though nothing actually failed. Confirmed against real hardware.
                    DtrEnable = true,
                    RtsEnable = true,
                };
                _serialPort.Open();
                _isSerialInitialized = true;
                _logger.LogInformation("Successfully opened serial connection to Pico on {Port} at 115200 baud.", _picoPortName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open serial port {Port}. Pico will run in MOCK mode.", _picoPortName);
                _serialPort = null;
                _isSerialInitialized = false;
            }
        }

        /// <summary>
        /// Writes one line and reads the single OK/ERR reply the controller sends back for every
        /// command except GRID's raw rows.
        /// </summary>
        private async Task<string?> SendCommandAsync(string command)
        {
            if (!_isSerialInitialized || _serialPort == null)
            {
                _logger.LogInformation("[HARDWARE MOCK (Pico Serial)] {Cmd}", command.Trim());
                return null;
            }

            try
            {
                return await Task.Run(() =>
                {
                    lock (_serialPort)
                    {
                        if (!_serialPort.IsOpen)
                            return null;

                        _serialPort.WriteLine(command);
                        var response = _serialPort.ReadLine();

                        if (response.StartsWith("ERR", StringComparison.Ordinal))
                            _logger.LogWarning("Pico refused command '{Cmd}': {Response}", command.Trim(), response);

                        return response;
                    }
                });
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Timed out waiting for the Pico to answer '{Cmd}'.", command.Trim());
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed exchanging serial command '{Cmd}' with the Pico.", command.Trim());
                return null;
            }
        }

        // Klipper's own docs (docs/API_Server.md, "gcode/script"): "The JSON response message is
        // sent when the processing of the script fully completes" - a slow move genuinely holds the
        // response, so the timeout has to be generous rather than snappy.
        private static readonly TimeSpan KlippyRequestTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Sends one G-code command straight to Klipper's own Unix domain socket API - no
        /// Moonraker, no HTTP. This deployment runs plain Klipper on the same host as this
        /// container, with the socket bind-mounted in (see the deployment docs in the repo root
        /// README). The wire format is one JSON object per request/response, each terminated by a
        /// single 0x03 (ETX) byte instead of a newline - see
        /// https://github.com/Klipper3d/klipper/blob/master/docs/API_Server.md.
        /// </summary>
        private async Task SendGCodeAsync(string gcode)
        {
            if (string.IsNullOrWhiteSpace(_klippySocketPath))
            {
                _logger.LogInformation("[HARDWARE MOCK (Klipper socket)] Executing: {GCode}", gcode);
                return;
            }

            try
            {
                using var cts = new CancellationTokenSource(KlippyRequestTimeout);
                using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(_klippySocketPath), cts.Token);

                var request = JsonSerializer.Serialize(new
                {
                    id = Interlocked.Increment(ref _klippyRequestId),
                    method = "gcode/script",
                    @params = new { script = gcode }
                });

                var payload = new byte[Encoding.UTF8.GetByteCount(request) + 1];
                var written = Encoding.UTF8.GetBytes(request, payload);
                payload[written] = 0x03;

                await socket.SendAsync(payload, SocketFlags.None, cts.Token);

                var buffer = new byte[4096];
                var received = await socket.ReceiveAsync(buffer, SocketFlags.None, cts.Token);
                var response = Encoding.UTF8.GetString(buffer, 0, received).TrimEnd('\x03');

                _logger.LogDebug("Klipper response for '{GCode}': {Response}", gcode, response);

                if (response.Contains("\"error\"", StringComparison.Ordinal))
                    _logger.LogWarning("Klipper reported an error for '{GCode}': {Response}", gcode, response);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Timed out waiting for Klipper to finish '{GCode}' (>{Timeout}s).", gcode, KlippyRequestTimeout.TotalSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send G-Code command '{GCode}' to Klipper via {SocketPath}.", gcode, _klippySocketPath);
            }
        }

        public async Task InitializeAsync(Maze maze)
        {
            _logger.LogInformation("Initializing physical maze layout.");

            // The carriage is about to move for this round, so whatever "is it still at the true
            // origin" fact the boot-time marker recorded is no longer current - see
            // PrepareForShutdownAsync/TryAutoHomeAsync.
            ClearHomeMarker();

            var settings = await GetCalibrationAsync();

            var cells = JsonSerializer.Deserialize<List<MazeCell>>(maze.GridData) ?? new();
            var raster = BuildRaster(maze.Width, maze.Height, cells);

            await SendCommandAsync("CLEAR");
            await SendGridAsync(raster);

            var exits = JsonSerializer.Deserialize<List<MazeExit>>(maze.Exits) ?? new();
            foreach (var exit in exits)
            {
                var (ex, ey, ew, eh) = ExitRasterRect(exit, maze.Height);
                await SendCommandAsync($"EXIT {ex} {ey} {ew} {eh}");
            }

            await SendGCodeAsync("G90"); // Absolute positioning

            // Tunable live from GameSettings rather than baked into printer.cfg, so acceleration
            // can be adjusted per-rig without touching Klipper's own config or restarting it.
            if (settings.AccelerationMmPerSec2 is { } accel)
                await SendGCodeAsync($"M204 S{accel}");

            // Move the magnetic carriage to the center start cell - the same raster pixel the LED
            // dot's very first MOVE will also target, so both land in the same place.
            var (startRasterX, startRasterY) = ToRasterCoords(maze.Width / 2, maze.Height / 2);
            var (startX, startY) = PhysicalMm(startRasterX, startRasterY, settings);
            await SendGCodeAsync($"G1 X{startX:F1} Y{startY:F1} F{settings.TravelFeedRateMmPerMin}");
        }

        /// <summary>
        /// Sends the 64x64 walkable/blocked grid the Pico expects: the literal "GRID" command
        /// followed by exactly <see cref="PanelSize"/> raw rows (no per-row response), then reads
        /// the controller's single "OK GRID"/"ERR GRID ..." reply.
        /// </summary>
        private async Task SendGridAsync(bool[,] raster)
        {
            if (!_isSerialInitialized || _serialPort == null)
            {
                _logger.LogInformation("[HARDWARE MOCK (Pico Serial)] GRID ({Size}x{Size} raster)", PanelSize, PanelSize);
                return;
            }

            try
            {
                var response = await Task.Run(() =>
                {
                    lock (_serialPort)
                    {
                        if (!_serialPort.IsOpen)
                            return null;

                        _serialPort.WriteLine("GRID");

                        for (int y = 0; y < PanelSize; y++)
                        {
                            var row = new StringBuilder(PanelSize);
                            for (int x = 0; x < PanelSize; x++)
                                row.Append(raster[x, y] ? '1' : '0');

                            _serialPort.WriteLine(row.ToString());
                        }

                        return _serialPort.ReadLine();
                    }
                });

                if (response is not null && response.StartsWith("ERR", StringComparison.Ordinal))
                    _logger.LogWarning("Pico refused the grid: {Response}", response);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Timed out waiting for the Pico to acknowledge the grid.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed sending the maze grid to the Pico.");
            }
        }

        // Wall thickness in pixels - matches the LEDController demo firmware's WALLT exactly, so
        // the panel's geometry is pixel-for-pixel identical to it.
        private const int WallThickness = 1;

        /// <summary>
        /// Rasterizes a Width x Height maze (walls on cell edges) into the Pico's fixed 64x64
        /// walkable/blocked grid, using the same geometry as the LEDController demo firmware's
        /// cell_block/link: each cell paints its own fixed-size floor block, the 1px gap between
        /// every pair of cells defaults to wall, and is opened as a single shared slice only where
        /// that specific pair is actually connected. Marking each cell's own edge independently
        /// (the previous approach) double-draws a shared wall from both sides and, symmetrically,
        /// widens a shared opening into floor from both sides too - nothing like the demo's crisp,
        /// consistent corridor width.
        /// </summary>
        private bool[,] BuildRaster(int mazeWidth, int mazeHeight, List<MazeCell> cells)
        {
            var raster = new bool[PanelSize, PanelSize]; // defaults to false (blocked/wall)

            if (mazeWidth <= 0 || mazeHeight <= 0)
                return raster;

            var (pitch, corridor, offsetX, offsetY) = ComputeRasterTransform(mazeWidth, mazeHeight);
            _rasterPitch = pitch;
            _rasterCorridor = corridor;
            _rasterOffsetX = offsetX;
            _rasterOffsetY = offsetY;

            void PaintBlock(int blockX, int blockY, int width, int height)
            {
                for (int dy = 0; dy < height; dy++)
                {
                    for (int dx = 0; dx < width; dx++)
                    {
                        int px = blockX + dx;
                        int py = blockY + dy;
                        if (px >= 0 && px < PanelSize && py >= 0 && py < PanelSize)
                            raster[px, py] = true;
                    }
                }
            }

            foreach (var cell in cells)
            {
                // Each cell's own floor block, matching cell_block(): (c*P + WALLT, r*P + WALLT).
                PaintBlock(
                    offsetX + cell.X * pitch + WallThickness,
                    offsetY + cell.Y * pitch + WallThickness,
                    corridor, corridor);

                // Open a single shared WALLT-wide slice toward the east/south neighbor when
                // connected, matching link() - processed once per pair, from the lower-index side.
                if (!cell.East && cell.X + 1 < mazeWidth)
                {
                    PaintBlock(
                        offsetX + (cell.X + 1) * pitch,
                        offsetY + cell.Y * pitch + WallThickness,
                        WallThickness, corridor);
                }

                if (!cell.South && cell.Y + 1 < mazeHeight)
                {
                    PaintBlock(
                        offsetX + cell.X * pitch + WallThickness,
                        offsetY + (cell.Y + 1) * pitch,
                        corridor, WallThickness);
                }
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                int floorPixels = 0;
                var sb = new StringBuilder();
                for (int y = 0; y < PanelSize; y++)
                {
                    for (int x = 0; x < PanelSize; x++)
                    {
                        if (raster[x, y]) floorPixels++;
                        sb.Append(raster[x, y] ? '.' : '#');
                    }
                    sb.Append('\n');
                }
                _logger.LogDebug(
                    "Raster: {Floor}/{Total} floor pixels ({Pct:P0}), pitch={Pitch} corridor={Corridor} offset=({OffsetX},{OffsetY})\n{Ascii}",
                    floorPixels, PanelSize * PanelSize, (double)floorPixels / (PanelSize * PanelSize),
                    pitch, corridor, offsetX, offsetY, sb.ToString());
            }

            return raster;
        }

        private static (int Pitch, int Corridor, int OffsetX, int OffsetY) ComputeRasterTransform(int mazeWidth, int mazeHeight)
        {
            int longestSide = Math.Max(mazeWidth, mazeHeight);
            int pitch = Math.Max(WallThickness + 1, PanelSize / longestSide);
            int corridor = pitch - WallThickness;

            // Matches the demo's GW = COLS * P + WALLT / OX = (64 - GW) / 2 - the extra WALLT
            // accounts for the trailing wall after the last cell, so a maze that exactly fits (e.g.
            // 21 cells at pitch 3) centers with zero leftover, same as the demo.
            int griddedWidth = mazeWidth * pitch + WallThickness;
            int griddedHeight = mazeHeight * pitch + WallThickness;
            int offsetX = (PanelSize - griddedWidth) / 2;
            int offsetY = (PanelSize - griddedHeight) / 2;

            return (pitch, corridor, offsetX, offsetY);
        }

        private (int X, int Y) ToRasterCoords(int cellX, int cellY)
        {
            int px = _rasterOffsetX + cellX * _rasterPitch + WallThickness + _rasterCorridor / 2;
            int py = _rasterOffsetY + cellY * _rasterPitch + WallThickness + _rasterCorridor / 2;

            return (Math.Clamp(px, 0, PanelSize - 1), Math.Clamp(py, 0, PanelSize - 1));
        }

        /// <summary>
        /// Converts a raster pixel coordinate (the same one sent to the Pico's MOVE) to the
        /// carriage's real-world position in mm. This is the only place panel pixels become
        /// physical distance - both the LED and the magnet always target the same raster
        /// coordinate, so the magnet is guaranteed to sit under the pixel showing the ball rather
        /// than tracking an unrelated, maze-size-dependent distance.
        /// </summary>
        private static (decimal X, decimal Y) PhysicalMm(int rasterX, int rasterY, GameSettings settings)
        {
            int x = settings.InvertX ? PanelSize - 1 - rasterX : rasterX;
            int y = settings.InvertY ? PanelSize - 1 - rasterY : rasterY;

            return (
                settings.OriginOffsetXMm + x * settings.PixelPitchMm,
                settings.OriginOffsetYMm + y * settings.PixelPitchMm);
        }

        /// <summary>
        /// The rectangle to mark green for an exit cell - its own interior floor block, extended
        /// by one wall's thickness toward whichever panel edge it opens onto, so the marker
        /// visibly punches through the boundary wall instead of stopping at it. Matches
        /// open_exit()/cell_block() in the LEDController demo firmware pixel-for-pixel.
        /// </summary>
        private (int X, int Y, int Width, int Height) ExitRasterRect(MazeExit exit, int mazeHeight)
        {
            int blockX = _rasterOffsetX + exit.X * _rasterPitch + WallThickness;
            int blockY = _rasterOffsetY + exit.Y * _rasterPitch + WallThickness;

            if (exit.Y == 0)
                return (blockX, blockY - WallThickness, _rasterCorridor, _rasterCorridor + WallThickness);
            if (exit.Y == mazeHeight - 1)
                return (blockX, blockY, _rasterCorridor, _rasterCorridor + WallThickness);
            if (exit.X == 0)
                return (blockX - WallThickness, blockY, _rasterCorridor + WallThickness, _rasterCorridor);

            // exit.X == mazeWidth - 1, the only remaining border MazeGenerator ever assigns.
            return (blockX, blockY, _rasterCorridor + WallThickness, _rasterCorridor);
        }

        public async Task ShowStepAsync(int x, int y, Direction direction)
        {
            _logger.LogInformation("Pushed step to hardware: ({X}, {Y}) moving {Direction}", x, y, direction);

            var settings = await GetCalibrationAsync();

            var (px, py) = ToRasterCoords(x, y);
            var (posX, posY) = PhysicalMm(px, py, settings);

            // Dispatched together, not sequentially: MOVE's reply is instant (the Pico just
            // draws, no animation), but G1's reply - per Klipper's own API_Server docs - only
            // arrives once the physical move fully completes. Awaiting MOVE before even starting
            // the G1 meant the LED showed the ball at its new position well before the carriage
            // physically got there - a visible "LED jumps ahead, ball catches up" lag, confirmed
            // on real hardware.
            var gcodeTask = SendGCodeAsync($"G1 X{posX:F1} Y{posY:F1} F{settings.StepFeedRateMmPerMin}");
            var ledTask = SendCommandAsync($"MOVE {px} {py} {_rasterCorridor}");
            await Task.WhenAll(gcodeTask, ledTask);
        }

        public async Task FlashWinnerAsync(string exitName)
        {
            _logger.LogInformation("Flashing winner exit: {ExitName}", exitName);

            var settings = await GetCalibrationAsync();

            // The Pico has no dedicated "win" command; celebrate with a green text overlay instead.
            await SendCommandAsync("COLOR 00FF00");
            await SendCommandAsync($"CENTER {exitName}");

            // A small side-to-side wiggle in place. This has to be relative (G91) - the carriage
            // can be anywhere on the maze when a round ends, so an absolute move back toward the
            // origin would yank it clear across the panel instead of wiggling where it stopped.
            // Net displacement is zero (+5, -10, +5) so it ends exactly where it started.
            await SendGCodeAsync("G91");
            await SendGCodeAsync($"G1 X5 F{settings.TravelFeedRateMmPerMin}");
            await SendGCodeAsync($"G1 X-10 F{settings.TravelFeedRateMmPerMin}");
            await SendGCodeAsync($"G1 X5 F{settings.TravelFeedRateMmPerMin}");
            await SendGCodeAsync("G90");
        }

        public async Task ResetAsync()
        {
            _logger.LogInformation("Resetting physical maze components.");

            var settings = await GetCalibrationAsync();

            // CLEAR blanks the grid, the dot and any text overlay on the Pico.
            await SendCommandAsync("CLEAR");

            // Park at the panel's center, not the hand-parked origin corner (0, 0). A round can
            // end anywhere on the panel, and the corner is the single farthest point from most of
            // it - confirmed on real hardware that yanking the magnet that far, that fast, can rip
            // the steel ball off it entirely rather than dragging it along. The center is a
            // shorter, more consistent distance from wherever a round naturally leaves the ball.
            var (centerX, centerY) = PhysicalMm(PanelSize / 2, PanelSize / 2, settings);
            await SendGCodeAsync($"G1 X{centerX:F1} Y{centerY:F1} F{settings.TravelFeedRateMmPerMin}");
        }

        public async Task PrepareForShutdownAsync()
        {
            _logger.LogInformation("Parking at the true origin before shutdown.");

            var settings = await GetCalibrationAsync();

            await SendCommandAsync("CLEAR");

            // Same reasoning as ResetAsync's center-park: a single long fast traverse from wherever
            // the carriage happens to be can rip the ball off the magnet. Routing through the
            // center first keeps each individual leg short, same as a round naturally ends.
            var (centerX, centerY) = PhysicalMm(PanelSize / 2, PanelSize / 2, settings);
            await SendGCodeAsync($"G1 X{centerX:F1} Y{centerY:F1} F{settings.TravelFeedRateMmPerMin}");

            // Klipper's own native (0, 0) - the exact spot last hand-parked and declared as home via
            // EstablishHomeAsync, not PhysicalMm(0, 0, ...): that applies the origin-offset/invert
            // calibration meant for converting a raster pixel to a physical target, which lands
            // somewhere else entirely. This has to be the same physical point EstablishHomeAsync's
            // SET_KINEMATIC_POSITION originally declared, or the marker below would be a lie.
            await SendGCodeAsync($"G1 X0 Y0 F{settings.TravelFeedRateMmPerMin}");

            // A G1's own reply only confirms Klipper finished *processing* the command - i.e. it
            // was accepted into the motion queue - not that the toolhead has physically finished
            // travelling there. M400 blocks until the motion queue is actually flushed. Without
            // this, the marker below (and the real shutdown right after it) could go out while the
            // carriage is still mechanically inbound - confirmed on real hardware.
            await SendGCodeAsync("M400");

            WriteHomeMarker();
        }

        public Task EstablishHomeAsync()
        {
            // No motion, no endstops - this rig has none configured (see docs/deployment.md,
            // "Homing"). This only ever updates Klipper's own bookkeeping of where (0, 0) is,
            // trusting that the carriage is physically sitting there right now.
            return SendGCodeAsync("SET_KINEMATIC_POSITION X=0 Y=0 Z=0");
        }

        public async Task<bool> TryAutoHomeAsync()
        {
            if (_homeMarkerPath is null || !File.Exists(_homeMarkerPath))
            {
                _logger.LogInformation(
                    "No carriage-at-home marker found - skipping auto-home. Either this isn't the " +
                    "first start after a real shutdown, or the last shutdown wasn't clean. Verify " +
                    "the carriage's position and use the admin panel's \"Set Home\" if needed.");
                return false;
            }

            _logger.LogInformation("Carriage-at-home marker found - auto-homing.");
            await EstablishHomeAsync();
            ClearHomeMarker();
            return true;
        }

        private void WriteHomeMarker()
        {
            if (_homeMarkerPath is null) return;

            try
            {
                File.WriteAllText(_homeMarkerPath, DateTime.UtcNow.ToString("O"));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write the carriage-at-home marker at {Path}.", _homeMarkerPath);
            }
        }

        private void ClearHomeMarker()
        {
            if (_homeMarkerPath is null) return;

            try
            {
                File.Delete(_homeMarkerPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear the carriage-at-home marker at {Path}.", _homeMarkerPath);
            }
        }

        public void Dispose()
        {
            if (_serialPort != null)
            {
                try
                {
                    if (_serialPort.IsOpen)
                    {
                        _serialPort.Close();
                    }
                    _serialPort.Dispose();
                }
                catch
                {
                    // Silent close
                }
            }
        }
    }
}
