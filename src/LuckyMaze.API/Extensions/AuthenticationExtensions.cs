using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using LuckyMaze.Infrastructure.Services;

namespace LuckyMaze.API.Extensions
{
    public static class AuthenticationExtensions
    {
        /// <summary>
        /// Ensures a LuckyMaze <see cref="LuckyMaze.Domain.User"/> row exists for whoever just
        /// authenticated, whichever way they did it - OIDC or Toamaisutaa's local login both end up
        /// here. Toamaisutaa's own <c>ICurrentUser.GetOrProvisionAsync</c> only creates its own
        /// identity row; the game-specific one (balance, role, ready flag) is ours to keep in sync.
        /// </summary>
        public static AuthenticationBuilder AddUserSync(this AuthenticationBuilder builder)
        {
            builder.Services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    options.Events ??= new JwtBearerEvents();
                    var previous = options.Events.OnTokenValidated;

                    options.Events.OnTokenValidated = async context =>
                    {
                        if (previous is not null)
                            await previous(context);

                        if (context.Principal is null)
                            return;

                        // ICurrentUser reads HttpContext.User, which the framework only assigns
                        // after this event returns. Provisioning needs it now.
                        context.HttpContext.User = context.Principal;

                        var userService = context.HttpContext.RequestServices.GetRequiredService<IUserService>();
                        await userService.SyncCurrentUserAsync(context.HttpContext.RequestAborted);
                    };
                });

            return builder;
        }
    }
}
