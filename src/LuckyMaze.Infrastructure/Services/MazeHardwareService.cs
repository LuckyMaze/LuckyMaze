using System.IO.Ports;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using LuckyMaze.Domain;
using LuckyMaze.Domain.Enums;

namespace LuckyMaze.Infrastructure.Services
{
    /// <summary>
    /// Talks to the LuckyMaze/LEDController firmware (a 64x64 HUB75 panel on a Raspberry Pi Pico).
    /// The panel's serial protocol is fixed size and request/response: CLEAR, GRID + 64 rows of 64
    /// walkable/blocked cells, MOVE x y, COLOR and CENTER, each answered with one OK/ERR line. See
    /// https://github.com/LuckyMaze/LEDController for the authoritative protocol.
    /// </summary>
    public class MazeHardwareService : IMazeHardwareService, IDisposable
    {
        private const int PanelSize = 64;

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<MazeHardwareService> _logger;

        private readonly string? _picoPortName;
        private readonly string? _moonrakerUrl;
        private readonly decimal _cellSizeMm;

        private SerialPort? _serialPort;
        private bool _isSerialInitialized = false;

        // The maze/raster transform (cell size in pixels and the offset that centers the maze on
        // the panel) is shared between InitializeAsync and ShowStepAsync, so both agree on where a
        // given maze cell lands.
        private int _rasterCellSize = 1;
        private int _rasterOffsetX;
        private int _rasterOffsetY;

        public MazeHardwareService(
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<MazeHardwareService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;

            _picoPortName = configuration["Hardware:PicoPort"];
            _moonrakerUrl = configuration["Hardware:MoonrakerUrl"];
            _cellSizeMm = configuration.GetValue<decimal>("Hardware:CellSizeMm", 30.0m);

            InitializeSerialPort();
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
                    ReadTimeout = 2000,
                    WriteTimeout = 500,
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

        private async Task SendGCodeAsync(string gcode)
        {
            if (string.IsNullOrWhiteSpace(_moonrakerUrl))
            {
                _logger.LogInformation("[HARDWARE MOCK (Klipper GCode)] Executing: {GCode}", gcode);
                return;
            }

            try
            {
                var client = _httpClientFactory.CreateClient();
                var endpoint = $"{_moonrakerUrl.TrimEnd('/')}/printer/gcode/script";
                var payload = new { script = gcode };

                _logger.LogDebug("Sending G-Code to Moonraker: {GCode}", gcode);
                var response = await client.PostAsJsonAsync(endpoint, payload);

                if (!response.IsSuccessStatusCode)
                {
                    var errorMsg = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Moonraker API returned non-success status: {Code}. Details: {Details}", response.StatusCode, errorMsg);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send G-Code command '{GCode}' to Klipper/Moonraker.", gcode);
            }
        }

        public async Task InitializeAsync(Maze maze)
        {
            _logger.LogInformation("Initializing physical maze layout.");

            var cells = JsonSerializer.Deserialize<List<MazeCell>>(maze.GridData) ?? new();
            var raster = BuildRaster(maze.Width, maze.Height, cells);

            await SendCommandAsync("CLEAR");
            await SendGridAsync(raster);

            await SendGCodeAsync("G28"); // Home all axes
            await SendGCodeAsync("G90"); // Absolute positioning

            // Move the magnetic carriage to the center start cell.
            decimal startX = (maze.Width / 2) * _cellSizeMm;
            decimal startY = (maze.Height / 2) * _cellSizeMm;
            await SendGCodeAsync($"G1 X{startX:F1} Y{startY:F1} F3000");
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

        /// <summary>
        /// Rasterizes a Width x Height maze (walls on cell edges) into the Pico's fixed 64x64
        /// walkable/blocked grid, scaling each maze cell to a block of pixels and drawing walls in
        /// the spare pixels between blocks. A maze at the panel's native 64x64 resolution has no
        /// spare pixels for walls, so every cell is simply shown as walkable floor.
        /// </summary>
        private bool[,] BuildRaster(int mazeWidth, int mazeHeight, List<MazeCell> cells)
        {
            var raster = new bool[PanelSize, PanelSize]; // defaults to false (blocked)

            if (mazeWidth <= 0 || mazeHeight <= 0)
                return raster;

            var (cellSize, offsetX, offsetY) = ComputeRasterTransform(mazeWidth, mazeHeight);
            _rasterCellSize = cellSize;
            _rasterOffsetX = offsetX;
            _rasterOffsetY = offsetY;

            foreach (var cell in cells)
            {
                int blockX = offsetX + cell.X * cellSize;
                int blockY = offsetY + cell.Y * cellSize;

                for (int dy = 0; dy < cellSize; dy++)
                {
                    for (int dx = 0; dx < cellSize; dx++)
                    {
                        int px = blockX + dx;
                        int py = blockY + dy;
                        if (px < 0 || px >= PanelSize || py < 0 || py >= PanelSize)
                            continue;

                        // With no spare pixel between blocks (cellSize == 1) there is nowhere to
                        // draw a wall, so every cell is walkable floor.
                        bool isWallPixel = cellSize > 1 && (
                            (dx == 0 && cell.West) ||
                            (dx == cellSize - 1 && cell.East) ||
                            (dy == 0 && cell.North) ||
                            (dy == cellSize - 1 && cell.South));

                        raster[px, py] = !isWallPixel;
                    }
                }
            }

            return raster;
        }

        private static (int CellSize, int OffsetX, int OffsetY) ComputeRasterTransform(int mazeWidth, int mazeHeight)
        {
            int longestSide = Math.Max(mazeWidth, mazeHeight);
            int cellSize = Math.Max(1, PanelSize / longestSide);

            int offsetX = (PanelSize - mazeWidth * cellSize) / 2;
            int offsetY = (PanelSize - mazeHeight * cellSize) / 2;

            return (cellSize, offsetX, offsetY);
        }

        private (int X, int Y) ToRasterCoords(int cellX, int cellY)
        {
            int px = _rasterOffsetX + cellX * _rasterCellSize + _rasterCellSize / 2;
            int py = _rasterOffsetY + cellY * _rasterCellSize + _rasterCellSize / 2;

            return (Math.Clamp(px, 0, PanelSize - 1), Math.Clamp(py, 0, PanelSize - 1));
        }

        public async Task ShowStepAsync(int x, int y, Direction direction)
        {
            _logger.LogInformation("Pushed step to hardware: ({X}, {Y}) moving {Direction}", x, y, direction);

            var (px, py) = ToRasterCoords(x, y);
            await SendCommandAsync($"MOVE {px} {py}");

            decimal posX = x * _cellSizeMm;
            decimal posY = y * _cellSizeMm;
            await SendGCodeAsync($"G1 X{posX:F1} Y{posY:F1} F2400");
        }

        public async Task FlashWinnerAsync(string exitName)
        {
            _logger.LogInformation("Flashing winner exit: {ExitName}", exitName);

            // The Pico has no dedicated "win" command; celebrate with a green text overlay instead.
            await SendCommandAsync("COLOR 00FF00");
            await SendCommandAsync($"CENTER {exitName}");

            // Flash stepper motors or do a victory dance G-code (e.g. wiggle back and forth)
            await SendGCodeAsync("G1 E5 F200"); // Example extruder click or small wiggle
            await SendGCodeAsync("G1 X10 F4000");
            await SendGCodeAsync("G1 X0 F4000");
        }

        public async Task ResetAsync()
        {
            _logger.LogInformation("Resetting physical maze components.");

            // CLEAR blanks the grid, the dot and any text overlay on the Pico.
            await SendCommandAsync("CLEAR");

            // Move stepper motors to safe park coordinates (0, 0)
            await SendGCodeAsync("G1 X0 Y0 F3000");
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
