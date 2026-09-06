using LuckyMaze.Domain.Enums;

namespace LuckyMaze.Domain
{
    public class GameSettings : BaseEntity
    {
        public MazeSize MazeSize { get; set; } = MazeSize.Grid21x21;
        public int GameSpeedMs { get; set; } = 850;
        public decimal MinBet { get; set; } = 1.00m;
        public decimal MaxBet { get; set; } = 500.00m;

        // Carriage calibration - unlike Hardware:PicoPort/KlippySocketPath/group IDs (tied to this
        // container's device/volume mounts, so they genuinely need a restart), these are pure
        // per-rig physical tuning with no reason to require a redeploy to change.
        public decimal PixelPitchMm { get; set; } = 3.0m;
        public decimal OriginOffsetXMm { get; set; } = 0m;
        public decimal OriginOffsetYMm { get; set; } = 0m;
        public bool InvertX { get; set; } = false;
        public bool InvertY { get; set; } = false;
        public int StepFeedRateMmPerMin { get; set; } = 2400;
        public int TravelFeedRateMmPerMin { get; set; } = 3000;
        public int? AccelerationMmPerSec2 { get; set; }
    }
}
