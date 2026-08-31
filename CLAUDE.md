# Notes for Claude (and humans)

Living notes about non-obvious things in this repo. Keep this short and only add things that are noteworthy enough to matter for future work.

## Project

LuckyMaze is an IDPA school project: a physical/digital maze with a trained AI solver, and players bet via a mobile web app on which exit the AI will choose. **This repo is the .NET 10 backend** — auth, players, bets, and the full game loop. The server-driven game state machine (`GameManager`), maze generator, a tabular Q-learning AI solver, and the physical-hardware output (Raspberry Pi / Pico serial + Klipper G-code, with a mock) all live here and are pushed to clients live over SignalR. Note: the solver is classic Q-learning, **not** an LLM — do **not** add LLM/chat/Semantic Kernel code here.

Layout is clean architecture: `LuckyMaze.API` (controllers, DI), `LuckyMaze.Application` (commands/queries via Mediator source generator), `LuckyMaze.Domain` (entities/enums), `LuckyMaze.Infrastructure` (EF Core + Npgsql, [Toamaisutaa](https://github.com/PianoNic/Toamaisutaa) auth). Tests use TUnit + NSubstitute + EF Core InMemory.

Auth is Toamaisutaa: local username/password login (`/auth/login`, `/auth/register`, ...) is the primary path, with open self-registration — no identity provider needs to run. Generic OIDC bearer validation (`Oidc:Authority` etc.) stays available and config-driven for a future identity provider, but nothing is wired up by default. `LuckyMaze.Domain.User` (balance, role, ready flag) is separate from Toamaisutaa's own user row and is linked to it by `User.ExternalId` (the Toamaisutaa user id) — see `IUserService`/`AddUserSync` in `LuckyMaze.API/Extensions/AuthenticationExtensions.cs`. Roles for a locally issued token come from `LuckyMazeUserRoleProvider` (`User.Role`), not from an identity provider claim.

## Running

```sh
docker compose -f compose.dev.yml up -d   # Postgres (3135)
cd src/LuckyMaze.API && dotnet run         # API; migrations apply on startup
```

User secrets are required (connection string, `LocalLogin:SigningKey`) — see `docs/dev_setup.md`.

## Migrations

Run from `scripts/`: `migration.bat` / `migration.ps1` / `migration.sh` with `add <Name>`, `update`, `list`, `remove`, `drop`. They wrap `dotnet ef` against `src/LuckyMaze.Infrastructure`.

## Workflow rules (enforced)

- **Never work on `main`.** Create an issue (labeled) → branch `feature/<issue#>_PascalCase` or `fix/<issue#>_PascalCase` or `refactor/<issue#>_PascalCase` → PR (labeled) with `Closes #<issue>` → squash-merge + delete branch.
- **Use CLI generators whenever one exists.** `dotnet new`, `dotnet ef migrations add`, `gh issue create`, `gh pr create`, etc.
- **No AI / Claude attribution** in commits or PRs. Ever.
- **No test plans in PRs.** PR body is Summary + `Closes #<issue>` only.
- **Commit subject**: short imperative.
- **PR labels**: `bug`, `feature`, `enhancement`, `refactor`, `documentation`, `stale`.
