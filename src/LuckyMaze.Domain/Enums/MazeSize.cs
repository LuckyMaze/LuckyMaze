namespace LuckyMaze.Domain.Enums
{
    public enum MazeSize
    {
        Small16x16 = 16,
        Medium32x32 = 32,
        Large64x64 = 64,

        // 21 cells exactly fills the 64x64 LED panel at a 2px corridor / 1px wall pitch
        // (21 * 3 + 1 = 64) - the same aspect ratio the panel looks best at on real hardware.
        Grid21x21 = 21
    }
}
