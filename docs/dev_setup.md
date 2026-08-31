# LuckyMaze — Dev Setup

## Prerequisites

- .NET 10 SDK
- Docker Desktop
- `dotnet-ef` global tool: `dotnet tool install --global dotnet-ef`

## Start the containers

```bash
docker compose -f compose.dev.yml up -d
```

This starts **Postgres** on port `3135` (mapped from container port `5432`).

Authentication is [Toamaisutaa](https://github.com/PianoNic/Toamaisutaa)'s local
username/password login by default — no separate identity provider container is
required. OIDC bearer validation is still available and config-driven (`Oidc:Authority`
etc., see `appsettings.json`) if a real identity provider is wired up later.

## Configure secrets

All secrets live in **user secrets**.

### Option 1 — CLI

```bash
cd src/LuckyMaze.API

dotnet user-secrets set "ConnectionStrings:LuckyMazeDatabase" "Host=localhost;Port=3135;Database=luckymaze-dev;Username=postgres;Password=d4vpas8w0rd13!!!"

dotnet user-secrets set "LocalLogin:SigningKey" "<base64, at least 32 bytes>"
```

`LocalLogin:SigningKey` signs the access tokens issued by `/auth/login` and
`/auth/register`, and Toamaisutaa refuses to start local login without one. Generate a
dev key with:

```bash
openssl rand -base64 32
```

To verify:

```bash
dotnet user-secrets list
```

### Option 2 — Edit `secrets.json` directly

In Visual Studio: right-click the `LuckyMaze.API` project → **Manage User Secrets**.

Paste in:

```json
{
  "ConnectionStrings": {
    "LuckyMazeDatabase": "Host=localhost;Port=3135;Database=luckymaze-dev;Username=postgres;Password=d4vpas8w0rd13!!!"
  },
  "LocalLogin": {
    "SigningKey": "<base64, at least 32 bytes>"
  }
}
```

Self-registration is on by default (`LocalLogin:AllowSelfRegistration` in
`appsettings.json`) — `POST /auth/register` creates an account and signs it in.
See [Toamaisutaa's password-login docs](https://docs.toamaisutaa.pianonic.ch/password-login)
for the full endpoint list.

## Run the API

```bash
cd src/LuckyMaze.API
dotnet run
```

Migrations are applied automatically on startup.
OpenAPI / Swagger UI is available at `/swagger`.

## Migrations

From the `scripts/` folder:

| Command | What it does |
|---|---|
| `migration add <Name>` | Create a new migration |
| `migration update` | Apply pending migrations |
| `migration list` | List all migrations |
| `migration remove` | Remove the last (unapplied) migration |
| `migration drop` | Drop the database |

Windows uses `migration.bat`, Linux/Mac use `./migration.sh`.

## Stop & reset

```bash
docker compose -f compose.dev.yml down          # stop, keep data
docker compose -f compose.dev.yml down -v       # stop, wipe data
```
