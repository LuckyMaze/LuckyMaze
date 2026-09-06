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

That overlay adds these to the `api` service, all pointed at paths on this same host:

| What | Default | Why |
|---|---|---|
| `devices: - $PicoPort:$PicoPort` | `/dev/ttyACM0` | USB-serial passthrough to the Pico. Confirm the actual path with `ls /dev/serial/by-id/` - it's stable across reboots, unlike `/dev/ttyACM0` if anything else is plugged in first. |
| `group_add: - $DialoutGroupId` | `20` | The container runs as a non-root user; this adds it to the host group that owns the serial device (`dialout` on Raspberry Pi OS/Debian) so it can open the device without `--privileged`. Confirm with `getent group dialout` - `20` is standard on Raspberry Pi OS but isn't guaranteed. |
| `group_add: - $KlipperGroupId` | `1000` | `klippy.sock` is owned by whoever runs Klipper and **their own primary group** - not `dialout`, which only covers the serial device. Confirm with `id <that-user>` (the `gid=` number). Get this wrong and it fails quietly: the panel and betting both work fine, the carriage just never moves, and the API log shows `SocketException (13): Permission denied` on every G-code send. |
| `volumes: - $KlippySocketPath:$KlippySocketPath` | `/home/lucky-user/printer_data/comms/klippy.sock` | Bind-mounts Klipper's own Unix domain socket API straight into the container. **There is no Moonraker in this deployment** - the API talks to `gcode/script` on this socket directly, per [Klipper's API_Server docs](https://github.com/Klipper3d/klipper/blob/master/docs/API_Server.md). Find the real path with `systemctl cat klipper \| grep ExecStart` (the `-a` argument). |
| `volumes: - $HostAgentRequestPathHost:/hostagent/requests` | `/home/lucky-user/hostagent/requests` | Shared with the host agent (below) - the API writes a request file here, the agent picks it up and performs the actual privileged action. |

### Host agent - shutdown and network mode from the admin panel

The API container can't power off the Pi or reconfigure its network by itself - both need real
host privilege a container doesn't have. `scripts/hostagent/agent.sh` is a small service that runs
directly on the Pi (not in Docker) and watches the directory above for request files; the API just
writes one (a bare file, its name is all that matters, nothing in it is ever executed) and the
agent performs the actual action. Install it once:

```bash
sudo scripts/hostagent/install.sh
```

This is what makes two admin panel buttons work:

- **Shut down** - parks the carriage first (the same center-park G-code a normal round reset
  uses), then powers off the Pi. The containers themselves don't need to be stopped separately -
  `systemctl poweroff` takes them down along with everything else.
- **Network mode** (WiFi / Hotspot) - see [`captive-portal.md`](./captive-portal.md) for what this
  actually does. Requires `scripts/captive-portal/install.sh` to have been run too.

Override any of these in `.env` if your paths differ - see `.env.example`.

### Homing

This rig has no endstops configured, so the API never sends `G28`. On the same schedule it would
have homed, it instead sends `SET_KINEMATIC_POSITION X=0 Y=0 Z=0` - telling Klipper the carriage's
current, hand-parked position **is** the origin, which needs no motion and no sensors. Park the
carriage there physically before starting a round.

### Carriage calibration - all live, in the admin panel

Pixel pitch, origin offset, axis inversion, feed rates and acceleration all live in `GameSettings`
now (the admin panel, alongside maze size and bet limits) - not `.env`. They're per-rig physical
tuning, not container infrastructure, so there's no reason they should need a restart or a
redeploy to change; a save takes effect starting with the next round.

- **Pixel Pitch (mm)** - the magnet's target position is derived straight from the same LED panel
  pixel the ball is shown at; this is the only thing converting a pixel into a physical distance,
  so the two stay in step regardless of maze size. 3.0 for a Waveshare P3 64x64.
- **Origin Offset X/Y (mm)** - there are no endstops (see Homing above), so "origin" is wherever
  the carriage was hand-parked, and there's no automatic way to know if that lines up with the
  panel's own top-left pixel underneath it. If the ball tracks consistently offset from the magnet
  by a fixed amount in one direction, nudge this rather than re-parking the carriage by hand.
- **Invert X/Y** - which direction this rig's axes actually point is down to the CoreXY mounting
  and wiring, and varies rig to rig. If the carriage moves the opposite direction from what the
  panel shows (confirmed by watching one axis at a time: does the carriage go right when the ball
  moves right?), turn on Invert for whichever axis is backwards. This mirrors the target around the
  panel's own center rather than negating it, so it stays within the same physical travel range
  instead of trying to go negative from the origin.
- **Step / Travel Feed Rate (mm/min)** - speed for each single-cell move during a round, and for
  the initial move to the maze's start plus the return-to-origin park, respectively. Sent straight
  through as the G-code's `F` value.
- **Acceleration (mm/s²)** - leave blank to use Klipper's own `printer.cfg` limit, or set a value
  to override it via `M204` without touching `printer.cfg` on the Pi directly.

`Hardware__PicoPort`/`KlippySocketPath`/the two group IDs above stay in `.env` - those really are
tied to this container's device/volume mounts, so changing them does need a restart.

## `.env` overrides everything

Every value in `compose.yml`/`compose.hardware.yml` is written `${VAR:-default}`, so anything in
`.env` wins.

`Cors__AllowedOrigins__0` doesn't matter for this deployment - the frontend is served by the same
API on the same origin, so there's no cross-origin request to allow in the first place. It only
matters if you're running the Angular dev server (`ng serve`) against a deployed API, which is a
development setup, not this one.
