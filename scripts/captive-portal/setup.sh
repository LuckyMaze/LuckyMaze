#!/usr/bin/env bash
set -euo pipefail

# Turns this Raspberry Pi's wlan0 into the LuckyMaze cabinet's own WiFi, with a captive portal that
# opens straight into the game instead of a login page. Run once, as root, on the Pi. See
# docs/captive-portal.md for what each piece does and how to change it (SSID, IP range, port).
#
# Assumes: Raspberry Pi OS with NetworkManager (the current default), wlan0 is free to dedicate to
# this - use ethernet if the Pi also needs a real network connection, since one radio can't be an
# access point and a WiFi client at the same time - and the LuckyMaze API container is already
# reachable on this same host at the port given below.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API_PORT="${LUCKYMAZE_PORT:-8080}"
AP_IP="192.168.4.1"
AP_IFACE="wlan0"

if [ "$EUID" -ne 0 ]; then
  echo "Run this as root: sudo $0" >&2
  exit 1
fi

echo "==> Installing hostapd, dnsmasq, nftables"
apt-get update
apt-get install -y hostapd dnsmasq nftables

echo "==> Stopping services while configuring (avoids half-applied state)"
systemctl stop hostapd dnsmasq 2>/dev/null || true

echo "==> Telling NetworkManager to leave $AP_IFACE alone"
mkdir -p /etc/NetworkManager/conf.d
cat > /etc/NetworkManager/conf.d/unmanaged-luckymaze-ap.conf <<EOF
[keyfile]
unmanaged-devices=interface-name:$AP_IFACE
EOF
systemctl restart NetworkManager

echo "==> Giving $AP_IFACE a static address ($AP_IP) at boot"
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
systemctl enable --now luckymaze-ap-address.service

echo "==> Installing hostapd config"
install -m 600 "$SCRIPT_DIR/hostapd.conf" /etc/hostapd/hostapd.conf
if grep -q '^#DAEMON_CONF=' /etc/default/hostapd 2>/dev/null; then
  sed -i 's|^#DAEMON_CONF=.*|DAEMON_CONF="/etc/hostapd/hostapd.conf"|' /etc/default/hostapd
else
  echo 'DAEMON_CONF="/etc/hostapd/hostapd.conf"' >> /etc/default/hostapd
fi

echo "==> Installing dnsmasq config (drop-in - doesn't touch anything else using dnsmasq)"
install -m 644 "$SCRIPT_DIR/dnsmasq.conf" /etc/dnsmasq.d/luckymaze.conf

echo "==> Redirecting HTTP from $AP_IFACE to the game on :$API_PORT, dropping HTTPS"
nft delete table ip luckymaze 2>/dev/null || true
nft -f - <<EOF
table ip luckymaze {
    chain prerouting {
        type nat hook prerouting priority -100;
        iifname "$AP_IFACE" tcp dport 80 redirect to :$API_PORT
        # HTTPS can't be redirected without a certificate every phone already trusts, which isn't
        # happening here - drop it instead of letting it hang, so the OS's connectivity check fails
        # fast on HTTPS and falls back to the HTTP check, which does work. See
        # docs/captive-portal.md.
        iifname "$AP_IFACE" tcp dport 443 drop
    }
}
EOF

mkdir -p /etc/nftables.d
nft list table ip luckymaze > /etc/nftables.d/luckymaze-captive-portal.nft
if ! grep -q "luckymaze-captive-portal.nft" /etc/nftables.conf 2>/dev/null; then
  echo 'include "/etc/nftables.d/luckymaze-captive-portal.nft"' >> /etc/nftables.conf
fi
systemctl enable nftables

echo "==> Starting hostapd and dnsmasq"
systemctl unmask hostapd 2>/dev/null || true
systemctl enable --now hostapd dnsmasq

echo
echo "Done. SSID 'LuckyMaze' should be broadcasting now. Verify with:"
echo "  systemctl status hostapd dnsmasq luckymaze-ap-address"
echo "  nft list table ip luckymaze"
