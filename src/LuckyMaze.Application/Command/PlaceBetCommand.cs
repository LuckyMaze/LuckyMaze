using Mediator;
using Toamaisutaa.Abstractions;
using LuckyMaze.Application.Models;
using LuckyMaze.Application.Services;

namespace LuckyMaze.Application.Command
{
    public record PlaceBetCommand(string ExitName, decimal Amount) : ICommand<Result<Unit>>;

    public class PlaceBetCommandHandler(ICurrentUser currentUser, GameManager gameManager)
        : ICommandHandler<PlaceBetCommand, Result<Unit>>
    {
        public async ValueTask<Result<Unit>> Handle(PlaceBetCommand command, CancellationToken cancellationToken)
        {
            if (!currentUser.IsAuthenticated)
                return Result<Unit>.Failure("Unauthorized");

            var user = await currentUser.GetOrProvisionAsync(cancellationToken);

            await gameManager.PlaceBetAsync(user.Id.ToString(), command.ExitName, command.Amount);
            return Result<Unit>.Success(Unit.Value);
        }
    }
}
