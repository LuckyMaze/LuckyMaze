# Deploying LuckyMaze

The published image (`ghcr.io/luckymaze/luckymaze-api`, built by
[`.github/workflows/docker-publish.yml`](../.github/workflows/docker-publish.yml) on every merge to
`main`) is one image containing both the API and the built Angular frontend - the Dockerfile builds
the frontend in its own stage and the API serves it as static files, same origin, no separate
frontend deployment and no CORS needed for it. No database, no identity provider - those stay
separate. This is the whole real deployment: the API, Postgres, and - when the physical rig is
attached - the LED panel's Pico and a bare-metal Klipper instance, **all on the same Raspberry Pi**.

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

The game is live at `http://localhost:8080` - open it in a browser, that's the whole app. This
alone runs with the hardware side in **mock mode** - LED and carriage commands are logged instead
of sent - which is what you want anywhere that isn't the actual cabinet: a dev machine, CI, a
staging host.

Want players to reach it by joining the cabinet's own WiFi instead of typing an address? See
[`captive-portal.md`](./captive-portal.md).

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

### Aligning the carriage with the panel

The magnet's target position is derived straight from the same LED panel pixel the ball is shown
at - `Hardware__PixelPitchMm` (3.0 for a Waveshare P3 64x64) is the only thing converting a pixel
into a physical distance, so the two are always in step with each other regardless of maze size.

What it can't know on its own is exactly where the hand-parked "origin" (see Homing above) sits
relative to the panel's own top-left corner underneath it - there are no endstops to calibrate
that automatically. If the ball tracks consistently offset from the magnet by a fixed amount in
one direction, that's this alignment, not the pixel pitch: nudge `Hardware__OriginOffsetXMm` /
`Hardware__OriginOffsetYMm` (mm) rather than re-parking the carriage by hand each time.

### Movement tuning

If the ball moves too fast, too slow, or too jerkily, that's `Hardware__StepFeedRateMmPerMin`
(speed for each single-cell move during a round) and `Hardware__TravelFeedRateMmPerMin` (speed for
the initial move to the maze's start and the return-to-origin park). Both are plain feed rates in
mm/min, sent straight through as the G-code's `F` value - no redeploy needed to retune, just an
`.env` change and a restart.

Acceleration comes from Klipper's own `printer.cfg` by default. Set
`Hardware__AccelerationMmPerSec2` to override it via `M204` instead - useful for tuning without
touching `printer.cfg` on the Pi directly, but it only takes effect from the next round's
`InitializeAsync` onward, not retroactively.

## `.env` overrides everything

Every value in `compose.yml`/`compose.hardware.yml` is written `${VAR:-default}`, so anything in
`.env` wins.

`Cors__AllowedOrigins__0` doesn't matter for this deployment - the frontend is served by the same
API on the same origin, so there's no cross-origin request to allow in the first place. It only
matters if you're running the Angular dev server (`ng serve`) against a deployed API, which is a
development setup, not this one.
