#!/usr/bin/env bash
set -euo pipefail

# Reverts enable.sh: stops the hotspot and gives wlan0 back to NetworkManager as a normal WiFi
# client, which reconnects it to whatever network it already had a saved profile for. Run as
# root - or trigger it from the admin panel's network mode toggle. See docs/deployment.md.

AP_IFACE="wlan0"

if [ "$EUID" -ne 0 ]; then
  echo "Run this as root: sudo $0" >&2
  exit 1
fi

echo "==> Stopping hostapd/dnsmasq/nginx and the static address"
systemctl stop hostapd dnsmasq nginx 2>/dev/null || true
systemctl stop luckymaze-ap-address.service 2>/dev/null || true

echo "==> Removing the HTTPS-reject rule"
nft delete table ip luckymaze 2>/dev/null || true
rm -f /etc/nftables.d/luckymaze-captive-portal.nft

echo "==> Giving $AP_IFACE back to NetworkManager"
rm -f /run/NetworkManager/conf.d/unmanaged-luckymaze-ap.conf /etc/NetworkManager/conf.d/unmanaged-luckymaze-ap.conf
ip addr flush dev "$AP_IFACE" 2>/dev/null || true
systemctl restart NetworkManager

echo "==> Waiting for NetworkManager to reconnect $AP_IFACE"
for _ in $(seq 1 15); do
  state=$(nmcli -t -f DEVICE,STATE device status | awk -F: -v i="$AP_IFACE" '$1==i {print $2}')
  if [ "$state" = "connected" ]; then
    echo "Reconnected."
    break
  fi
  sleep 2
done

echo
echo "Hotspot disabled, $AP_IFACE back under NetworkManager."
