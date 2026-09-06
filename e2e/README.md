# Mock OIDC stack

Authentication defaults to [Toamaisutaa](https://github.com/PianoNic/Toamaisutaa)'s local
username/password login, which needs no identity provider at all — see `docs/dev_setup.md`
for that path. This stack is for exercising the *other* one: a fake OIDC provider
([`mock-oauth2-server`](https://github.com/navikt/mock-oauth2-server)) plus a throwaway
Postgres, handy for local dev, regenerating the API client (`bun run apigen`), and
API-level testing against a real identity provider without running one for real.

The mock issues tokens for `client_id=luckymaze` with a `groups: ["admin"]` claim (see
`mock-oauth2-config.json`), so the synced user lands as an `Admin`.

## Usage

```sh
docker compose -f e2e/compose.e2e.yml up -d
```

Then run the API on the host pointed at the mock (everything stays on `localhost`, so the
token issuer matches what the API validates). `LocalLogin:SigningKey` is required
regardless of which auth path you're testing — Toamaisutaa checks it at startup, not just
when local login is actually used:

```sh
cd src/LuckyMaze.API
ConnectionStrings__LuckyMazeDatabase="Host=localhost;Port=15432;Database=luckymaze-test;Username=postgres;Password=postgres-test" \
Oidc__Authority="http://localhost:18080/default" \
Oidc__ClientId="luckymaze" \
Oidc__RequireHttpsMetadata=false \
Oidc__RoleClaim="groups" \
LocalLogin__SigningKey="$(openssl rand -base64 32)" \
dotnet run
```

- OIDC discovery: `http://localhost:18080/default/.well-known/openid-configuration`
- Swagger / OpenAPI: `http://localhost:5246/swagger/v1/swagger.json`

Tear down with `docker compose -f e2e/compose.e2e.yml down -v`.
