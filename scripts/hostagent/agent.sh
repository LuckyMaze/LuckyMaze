#!/usr/bin/env bash
set -uo pipefail

# Watches REQUEST_DIR for request files the API container writes (via a bind-mounted shared
# directory - see compose.hardware.yml) and performs the actual privileged host action for each,
# since the containerized API can't power off the Pi or reconfigure its network on its own. Runs
# as root via luckymaze-hostagent.service. See docs/deployment.md.
#
# The request is just a filename match against a fixed set below - its content is never read or
# executed, so this can never run anything beyond the handful of actions already coded here.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CAPTIVE_PORTAL_DIR="$(cd "$SCRIPT_DIR/../captive-portal" && pwd)"
REQUEST_DIR="${LUCKYMAZE_HOSTAGENT_DIR:-/home/lucky-user/hostagent/requests}"
HOTSPOT_REVERT_TIMEOUT_SEC="${LUCKYMAZE_HOTSPOT_TIMEOUT_SEC:-600}"
REVERT_PID_FILE="/run/luckymaze-hotspot-revert.pid"
# Unlike the PID file above, this has to survive a reboot - it's how a pending revert resumes
# after one, instead of a reboot mid-window silently turning a temporary hotspot into a stuck one.
REVERT_DEADLINE_FILE="/var/lib/luckymaze/hotspot-revert-deadline"

mkdir -p "$REQUEST_DIR" "$(dirname "$REVERT_DEADLINE_FILE")"
# World-writable: the API container runs as a non-root user with no shared group with this root
# service, and the only thing trusted here is the filename (see the comment above), not who wrote it.
chmod 777 "$REQUEST_DIR"

cancel_pending_revert() {
  if [ -f "$REVERT_PID_FILE" ]; then
    kill "$(cat "$REVERT_PID_FILE")" 2>/dev/null || true
    rm -f "$REVERT_PID_FILE"
  fi
  rm -f "$REVERT_DEADLINE_FILE"
}

wait_and_revert() {
  local delay="$1"
  (
    sleep "$delay"
    echo "Auto-reverting hotspot after ${delay}s with no confirmation."
    bash "$CAPTIVE_PORTAL_DIR/disable.sh"
    rm -f "$REVERT_PID_FILE" "$REVERT_DEADLINE_FILE"
  ) &
  echo $! > "$REVERT_PID_FILE"
}

schedule_revert() {
  cancel_pending_revert
  echo "$(( $(date +%s) + HOTSPOT_REVERT_TIMEOUT_SEC ))" > "$REVERT_DEADLINE_FILE"
  wait_and_revert "$HOTSPOT_REVERT_TIMEOUT_SEC"
}

# Resume a pending revert left over from before a restart (service restart or full reboot) -
# without this, whatever killed the old process also silently cancels the safety net.
if [ -f "$REVERT_DEADLINE_FILE" ]; then
  remaining=$(( $(cat "$REVERT_DEADLINE_FILE") - $(date +%s) ))
  if [ "$remaining" -le 0 ]; then
    echo "Resuming an overdue hotspot revert from before a restart - reverting now."
    bash "$CAPTIVE_PORTAL_DIR/disable.sh"
    rm -f "$REVERT_DEADLINE_FILE"
  else
    echo "Resuming a pending hotspot revert from before a restart (${remaining}s left)."
    wait_and_revert "$remaining"
  fi
fi

echo "LuckyMaze host agent watching $REQUEST_DIR"

while true; do
  for req in "$REQUEST_DIR"/*.request; do
    [ -e "$req" ] || continue
    name="$(basename "$req" .request)"
    echo "Handling request: $name"

    case "$name" in
      shutdown)
        systemctl poweroff
        ;;
      hotspot-enable)
        bash "$CAPTIVE_PORTAL_DIR/enable.sh" && schedule_revert
        ;;
      hotspot-enable-permanent)
        bash "$CAPTIVE_PORTAL_DIR/enable.sh" && cancel_pending_revert
        ;;
      hotspot-disable)
        cancel_pending_revert
        bash "$CAPTIVE_PORTAL_DIR/disable.sh"
        ;;
      *)
        echo "Unknown request, ignoring: $name" >&2
        ;;
    esac

    rm -f "$req"
  done
  sleep 2
done
