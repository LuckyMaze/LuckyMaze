# Deploying LuckyMaze

The published image (`ghcr.io/luckymaze/luckymaze-api`, built by
[`.github/workflows/docker-publish.yml`](../.github/workflows/docker-publish.yml) on every merge to
`main`) is the API only - no frontend, no database, no identity provider. This is the whole real
deployment: the API, Postgres, and - when the physical rig is attached - the LED panel's Pico and a
bare-metal Klipper instance, **all on the same Raspberry Pi**.

## Run it

**1. Copy the env file and set the one required secret:**

```bash
cp .env.example .env
```

Generate a signing key and put it in `.env`:

```bash
openssl rand -base64 32
# LocalLogin__SigningKey=<paste it>
```

**2. Start it:**

```bash
docker compose up -d
```

The API is live at `http://localhost:8080`. This alone runs with the hardware side in **mock
mode** - LED and carriage commands are logged instead of sent - which is what you want anywhere
that isn't the actual cabinet: a dev machine, CI, a staging host.

## On the actual hardware

The Pico (LED panel) and the machine running Klipper are both **this same Pi** - two USB cables in,
nothing over the network. Bring in `compose.hardware.yml` on top of the base file:

```bash
docker compose -f compose.yml -f compose.hardware.yml up -d
```

That overlay adds three things to the `api` service, all pointed at paths on this same host:

| What | Default | Why |
|---|---|---|
| `devices: - $PicoPort:$PicoPort` | `/dev/ttyACM0` | USB-serial passthrough to the Pico. Confirm the actual path with `ls /dev/serial/by-id/` - it's stable across reboots, unlike `/dev/ttyACM0` if anything else is plugged in first. |
| `group_add: - $DialoutGroupId` | `20` | The container runs as a non-root user; this adds it to the host group that owns the serial device (`dialout` on Raspberry Pi OS/Debian) so it can open the device without `--privileged`. Confirm with `getent group dialout` - `20` is standard on Raspberry Pi OS but isn't guaranteed. |
| `volumes: - $KlippySocketPath:$KlippySocketPath` | `/home/lucky-user/printer_data/comms/klippy.sock` | Bind-mounts Klipper's own Unix domain socket API straight into the container. **There is no Moonraker in this deployment** - the API talks to `gcode/script` on this socket directly, per [Klipper's API_Server docs](https://github.com/Klipper3d/klipper/blob/master/docs/API_Server.md). Find the real path with `systemctl cat klipper \| grep ExecStart` (the `-a` argument). |

Override any of the three in `.env` if your paths differ - see `.env.example`.

### Homing

This rig has no endstops configured, so the API never sends `G28`. On the same schedule it would
have homed, it instead sends `SET_KINEMATIC_POSITION X=0 Y=0 Z=0` - telling Klipper the carriage's
current, hand-parked position **is** the origin, which needs no motion and no sensors. Park the
carriage there physically before starting a round.

## `.env` overrides everything

Every value in `compose.yml`/`compose.hardware.yml` is written `${VAR:-default}`, so anything in
`.env` wins. `Cors__AllowedOrigins__0` in particular needs to be your actual deployed frontend's
origin - left at the default, the browser will reject every request from anywhere but
`localhost:4200`.
