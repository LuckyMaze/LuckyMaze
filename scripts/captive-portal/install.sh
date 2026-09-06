#!/usr/bin/env bash
set -euo pipefail

# One-time install: packages and config files for the LuckyMaze captive portal. Does NOT turn the
# hotspot on - wlan0 stays a normal NetworkManager-managed WiFi client until enable.sh runs (or
# the admin panel's network mode toggle does the same thing through the host agent). Run once, as
# root, on the Pi. See docs/deployment.md.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
AP_IFACE="wlan0"
AP_IP="192.168.4.1"

if [ "$EUID" -ne 0 ]; then
  echo "Run this as root: sudo $0" >&2
  exit 1
fi

echo "==> Installing hostapd, dnsmasq, nftables"
apt-get update
apt-get install -y hostapd dnsmasq nftables

echo "==> Making sure nothing starts until enable.sh runs"
systemctl stop hostapd dnsmasq 2>/dev/null || true
systemctl disable hostapd dnsmasq 2>/dev/null || true
systemctl unmask hostapd 2>/dev/null || true

echo "==> Installing the static-address unit for $AP_IFACE (only runs while enabled)"
cat > /etc/systemd/system/luckymaze-ap-address.service <<EOF
[Unit]
Description=Static address for $AP_IFACE (LuckyMaze captive portal)
Before=hostapd.service dnsmasq.service
After=network-pre.target NetworkManager.service
Wants=network-pre.target

[Service]
Type=oneshot
RemainAfterExit=yes
ExecStart=/sbin/ip addr flush dev $AP_IFACE
ExecStart=/sbin/ip addr add $AP_IP/24 dev $AP_IFACE
ExecStart=/sbin/ip link set $AP_IFACE up

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl disable luckymaze-ap-address.service 2>/dev/null || true

echo "==> Installing hostapd config"
install -m 600 "$SCRIPT_DIR/hostapd.conf" /etc/hostapd/hostapd.conf
if grep -q '^#DAEMON_CONF=' /etc/default/hostapd 2>/dev/null; then
  sed -i 's|^#DAEMON_CONF=.*|DAEMON_CONF="/etc/hostapd/hostapd.conf"|' /etc/default/hostapd
else
  echo 'DAEMON_CONF="/etc/hostapd/hostapd.conf"' >> /etc/default/hostapd
fi

echo "==> Installing dnsmasq config (drop-in - doesn't touch anything else using dnsmasq)"
install -m 644 "$SCRIPT_DIR/dnsmasq.conf" /etc/dnsmasq.d/luckymaze.conf

echo
echo "Install done. Nothing is running yet - use enable.sh (or the admin panel's network mode"
echo "toggle) to actually turn the hotspot on, and disable.sh (or the panel) to go back to normal"
echo "WiFi. Also run scripts/hostagent/install.sh if you want the admin panel toggle to work."
