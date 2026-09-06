using Mediator;
using Toamaisutaa.Abstractions;
using LuckyMaze.Application.Models;
using LuckyMaze.Application.Services;

namespace LuckyMaze.Application.Command
{
    public record ToggleReadyCommand(bool IsReady) : ICommand<Result<Unit>>;

    public class ToggleReadyCommandHandler(ICurrentUser currentUser, GameManager gameManager)
        : ICommandHandler<ToggleReadyCommand, Result<Unit>>
    {
        public async ValueTask<Result<Unit>> Handle(ToggleReadyCommand command, CancellationToken cancellationToken)
        {
            if (!currentUser.IsAuthenticated)
                return Result<Unit>.Failure("Unauthorized");

            var user = await currentUser.GetOrProvisionAsync(cancellationToken);

            await gameManager.ToggleReadyAsync(user.Id.ToString(), command.IsReady);
            return Result<Unit>.Success(Unit.Value);
        }
    }
}
