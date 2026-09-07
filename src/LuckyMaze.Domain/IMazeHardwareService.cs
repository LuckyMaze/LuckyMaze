using LuckyMaze.Domain.Enums;

namespace LuckyMaze.Domain
{
    public interface IMazeHardwareService
    {
        Task InitializeAsync(Maze maze);
        Task ShowStepAsync(int x, int y, Direction direction);
        Task FlashWinnerAsync(string exitName);
        Task ResetAsync();

        /// <summary>
        /// Parks at the true physical origin (not the panel center ResetAsync uses) and marks it as
        /// safe to trust on the next boot - only for the real shutdown flow, right before the host
        /// agent cuts power. See docs/deployment.md ("Homing").
        /// </summary>
        Task PrepareForShutdownAsync();

        /// <summary>
        /// Declares the carriage's current physical position as (0, 0) - no motor movement, this
        /// rig has no endstops. For the admin panel's manual "Set Home" action: trusts the operator
        /// has actually hand-parked it there, no marker check.
        /// </summary>
        Task EstablishHomeAsync();

        /// <summary>
        /// Same as <see cref="EstablishHomeAsync"/>, but only if the last shutdown genuinely parked
        /// at the true origin (see <see cref="PrepareForShutdownAsync"/>) - safe to call
        /// unconditionally at startup, since it's a no-op after an ordinary redeploy that didn't go
        /// through a real shutdown. Returns whether it actually homed.
        /// </summary>
        Task<bool> TryAutoHomeAsync();
    }
}
