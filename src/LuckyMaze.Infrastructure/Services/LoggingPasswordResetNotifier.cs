using Microsoft.Extensions.Logging;
using Toamaisutaa.Abstractions;

namespace LuckyMaze.Infrastructure.Services
{
    /// <summary>
    /// Toamaisutaa requires an <see cref="IPasswordResetNotifier"/> before local password login
    /// will start, and ships no implementation - sending mail is not its job. This project has no
    /// SMTP infrastructure, so the reset link is logged instead, the same way the hardware services
    /// fall back to logging when no serial port or Moonraker URL is configured.
    /// </summary>
    public class LoggingPasswordResetNotifier(ILogger<LoggingPasswordResetNotifier> logger) : IPasswordResetNotifier
    {
        public Task SendAsync(ToamaisutaaUser user, string resetToken, CancellationToken cancellationToken = default)
        {
            logger.LogWarning("PASSWORD RESET for {Email}: token {Token}", user.Email, resetToken);
            return Task.CompletedTask;
        }
    }
}
