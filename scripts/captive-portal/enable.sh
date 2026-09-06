#!/usr/bin/env bash
set -euo pipefail

# Turns this Pi's wlan0 into the LuckyMaze cabinet's own WiFi. Requires install.sh to have been
# run once already. Run as root - or trigger it from the admin panel's network mode toggle, which
# does the same thing through the host agent. See docs/deployment.md.
#
# One radio can't be an access point and a WiFi client at once, so this drops your connection to
# wlan0's current network - SSH included, if that's how you're connected - the moment it runs.

AP_IFACE="wlan0"
AP_IP="192.168.4.1"
API_PORT="${LUCKYMAZE_PORT:-8080}"

if [ "$EUID" -ne 0 ]; then
  echo "Run this as root: sudo $0" >&2
  exit 1
fi

echo "==> Telling NetworkManager to leave $AP_IFACE alone"
mkdir -p /etc/NetworkManager/conf.d
cat > /etc/NetworkManager/conf.d/unmanaged-luckymaze-ap.conf <<EOF
[keyfile]
unmanaged-devices=interface-name:$AP_IFACE
EOF
systemctl restart NetworkManager

echo "==> Bringing up the static address and starting hostapd/dnsmasq"
systemctl enable --now luckymaze-ap-address.service
systemctl unmask hostapd 2>/dev/null || true
systemctl enable --now hostapd dnsmasq

echo "==> Redirecting HTTP from $AP_IFACE to the game on :$API_PORT, rejecting HTTPS"
nft delete table ip luckymaze 2>/dev/null || true
nft -f - <<EOF
table ip luckymaze {
    chain prerouting {
        type nat hook prerouting priority -100;
        iifname "$AP_IFACE" tcp dport 80 redirect to :$API_PORT
    }
    chain input {
        type filter hook input priority 0;
        # HTTPS can't be redirected without a certificate every phone already trusts - reject it
        # (TCP RST) rather than dropping it. A silent drop makes the OS's HTTPS connectivity check
        # hang for a retransmission timeout instead of failing right away, and on some phones
        # (seen: Samsung/One UI) that's long enough that the whole connectivity evaluation gives up
        # before ever trying the HTTP check above - so the sign-in prompt never appears at all.
        # This has to be a separate filter chain, not another rule in the nat chain above - reject
        # isn't reliably supported inside nat-type chains. See docs/captive-portal.md.
        iifname "$AP_IFACE" tcp dport 443 reject with tcp reset
    }
}
EOF

mkdir -p /etc/nftables.d
nft list table ip luckymaze > /etc/nftables.d/luckymaze-captive-portal.nft
if ! grep -q "luckymaze-captive-portal.nft" /etc/nftables.conf 2>/dev/null; then
  echo 'include "/etc/nftables.d/luckymaze-captive-portal.nft"' >> /etc/nftables.conf
fi
systemctl enable --now nftables

echo
echo "Hotspot enabled. SSID 'LuckyMaze' should be broadcasting on $AP_IFACE now."
