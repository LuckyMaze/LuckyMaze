#!/usr/bin/env bash
set -euo pipefail

# Installs and starts the LuckyMaze host agent - the small service that performs privileged host
# actions (shutdown, WiFi/hotspot toggle) requested by the API container. Run once, as root, on
# the Pi. Assumes this repo is cloned at /home/lucky-user/LuckyMaze - see
# luckymaze-hostagent.service if yours lives elsewhere. See docs/deployment.md.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ "$EUID" -ne 0 ]; then
  echo "Run this as root: sudo $0" >&2
  exit 1
fi

install -m 644 "$SCRIPT_DIR/luckymaze-hostagent.service" /etc/systemd/system/luckymaze-hostagent.service
systemctl daemon-reload
systemctl enable --now luckymaze-hostagent.service

echo
echo "Host agent installed and running. Check with:"
echo "  systemctl status luckymaze-hostagent"
echo "  journalctl -u luckymaze-hostagent -f"
