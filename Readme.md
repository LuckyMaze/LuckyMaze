# LuckyMaze

The .NET 10 backend for LuckyMaze — auth, players, bets, and the game loop. See
[`CLAUDE.md`](./CLAUDE.md) for the project overview and [`docs/dev_setup.md`](./docs/dev_setup.md)
for local development.

## Deploying the API container

Published images: `ghcr.io/luckymaze/luckymaze-api` (built by
[`.github/workflows/docker-publish.yml`](./.github/workflows/docker-publish.yml) on every merge to
`main`, tagged `latest` and by short commit SHA). The image is the API only — no frontend, no
database, no Pocket ID/OIDC provider. Postgres and the Angular frontend are deployed separately;
see `compose.dev.yml` for a Postgres example.

### Required configuration

Two things the container will not start without:

| Env var | What it is |
|---|---|
| `ConnectionStrings__LuckyMazeDatabase` | Npgsql connection string. Migrations apply automatically on startup; the target database is created if it doesn't exist. |
| `LocalLogin__SigningKey` | Base64, at least 32 bytes (`openssl rand -base64 32`). Signs the local-login access tokens. Never reuse the dev value from `docs/dev_setup.md`. |

### Optional configuration

| Env var | What it is |
|---|---|
| `Cors__AllowedOrigins__0`, `__1`, ... | Origins allowed to call the API from a browser. Defaults to `http://localhost:4200`/`5173` (the dev frontend) — **set this to your deployed frontend's actual origin**, or the browser will reject every request with a CORS error. |
| `Hardware__PicoPort` | Serial device for the LED panel's Raspberry Pi Pico, e.g. `/dev/ttyACM0`. Left unset, the LED side logs commands instead of sending them (mock mode) — safe to run without hardware attached. |
| `Hardware__MoonrakerUrl` | Base URL of the Moonraker instance fronting Klipper on the magnetic-carriage Pi, e.g. `http://192.168.1.50:7125`. Left unset, G-code is logged instead of sent (mock mode). |
| `Hardware__CellSizeMm` | Physical size of one maze cell in mm, for translating cell coordinates into carriage G-code moves. Default `30.0`. |
| `Oidc__Authority`, `Oidc__ClientId`, ... | Wires up a real OIDC identity provider alongside local login. Unset by default — see [Toamaisutaa's OIDC docs](https://docs.toamaisutaa.pianonic.ch/oidc). |

### Giving the container access to the hardware

The Pico is a **USB serial device** on whatever host runs the container; Klipper/Moonraker run on a
**separate Pi**, reached over the network. They need different things from Docker:

- **Serial (Pico):** pass the specific device node through with `--device`, and add the container to
  the group that owns it (`dialout` on Raspberry Pi OS/Debian) so the non-root container user can
  open it without `--privileged`:

  ```sh
  ls -l /dev/ttyACM0                      # confirm the device and its owning group
  getent group dialout                    # note the GID, e.g. dialout:x:20:
  ```

- **Moonraker (Klipper Pi):** this is a plain HTTP call to another machine on the network — Docker's
  default bridge network can already reach it. No special flag needed beyond pointing
  `Hardware__MoonrakerUrl` at that Pi's address. Use `--network host` instead only if the container
  otherwise can't reach your LAN (e.g. an isolated bridge/VLAN setup).

### Example

```sh
docker run -d \
  --name luckymaze-api \
  --device=/dev/ttyACM0 \
  --group-add 20 \
  -p 8080:8080 \
  -e ConnectionStrings__LuckyMazeDatabase="Host=192.168.1.10;Port=5432;Database=luckymaze;Username=postgres;Password=<password>" \
  -e LocalLogin__SigningKey="<base64, at least 32 bytes>" \
  -e Cors__AllowedOrigins__0="https://maze.example.com" \
  -e Hardware__PicoPort="/dev/ttyACM0" \
  -e Hardware__MoonrakerUrl="http://192.168.1.50:7125" \
  ghcr.io/luckymaze/luckymaze-api:latest
```

Replace the `--group-add` GID with whatever `getent group dialout` reported on your host — it isn't
guaranteed to be `20` everywhere. Omit both `Hardware__*` variables entirely to run without physical
hardware attached; the LED and carriage commands just get logged instead of sent.
