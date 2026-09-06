using LuckyMaze.Domain.Enums;

namespace LuckyMaze.Domain
{
    public class GameSettings : BaseEntity
    {
        public MazeSize MazeSize { get; set; } = MazeSize.Grid21x21;
        public int GameSpeedMs { get; set; } = 850;
        public decimal MinBet { get; set; } = 1.00m;
        public decimal MaxBet { get; set; } = 500.00m;
    }
}
