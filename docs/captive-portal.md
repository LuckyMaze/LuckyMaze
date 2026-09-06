# WiFi captive portal

The cabinet broadcasts its own WiFi network. Anyone who joins gets the phone's normal "Sign in to
network" prompt - which opens straight into LuckyMaze instead of a login page, because the game is
the only thing being served.

## How it actually works

There's no special "captive portal" software involved - phones already do this detection on their
own, and the pieces below just make sure whatever they check lands on the game.

1. **hostapd** turns the Pi's WiFi radio (`wlan0`) into an access point - broadcasts the `LuckyMaze`
   SSID, open, no password.
2. **dnsmasq** hands out DHCP leases to anyone who joins, and answers *every* DNS query - for any
   domain at all - with the Pi's own address (`192.168.4.1`).
3. **nftables** redirects all port-80 traffic from the WiFi interface to the game, and drops
   port-443 outright.
4. When a phone joins, its OS pings a specific URL to check for real internet - Apple checks
   `captive.apple.com/hotspot-detect.html`, Android checks
   `connectivitycheck.gstatic.com/generate_204`, Windows checks
   `msftconnecttest.com/connecttest.txt`. Because of steps 2 and 3, that request - whatever domain
   it was actually for - arrives at the Pi and gets the game's `index.html` back. That's not the
   "Success"/204/expected-text response the OS wanted, so it concludes there's a portal here and
   opens its built-in browser pointed at that same URL - which resolves to the Pi again, and shows
   the game.
5. HTTPS is dropped rather than redirected, on purpose: there's no way to redirect an HTTPS
   connectivity check without a certificate the phone already trusts, and letting it hang would
   just make the OS wait before falling back. Dropping it fails fast, and every platform's
   connectivity check already has an HTTP fallback for exactly this case.

This is the same mechanism airport and hotel WiFi portals use - nothing LuckyMaze-specific about
steps 1-4, only step 4's *content* (the game itself) is.

## Set it up

Requires the [SPA-bundled deployment](./deployment.md) already running on this Pi - the portal
redirects to whatever port the game listens on.

```bash
sudo LUCKYMAZE_PORT=8080 ./scripts/captive-portal/setup.sh
```

`LUCKYMAZE_PORT` should match `LuckyMaze__Port` from your `.env` (default `8080`). The script:

- installs `hostapd`, `dnsmasq`, `nftables`
- tells NetworkManager to leave `wlan0` alone (so it doesn't fight hostapd for the interface)
- gives `wlan0` a static address (`192.168.4.1`) via a small systemd unit that runs before hostapd
- installs `scripts/captive-portal/hostapd.conf` and `dnsmasq.conf`
- adds the nftables redirect/drop rules, persisted so they survive a reboot
- enables and starts everything

## Why `wlan0` only

One WiFi radio can be an access point or a client, not both at once (reliably) on typical Pi
hardware. If the Pi needs its own internet connection - for updates, remote access, whatever - use
Ethernet for that and leave `wlan0` dedicated to the cabinet's network. This setup doesn't give
connected phones internet access either; there's nothing to share, and the game doesn't need one.

## Changing the SSID, password, or IP range

Edit `scripts/captive-portal/hostapd.conf` (SSID, channel, or add `wpa=2` / `wpa_passphrase=...` for
a password - the portal works either way) and `dnsmasq.conf` (DHCP range), then re-run `setup.sh`.
If you change `192.168.4.1`, update `AP_IP` at the top of `setup.sh` too - the dnsmasq wildcard and
the static address both need to agree.

## Troubleshooting

- **Phone doesn't see the network at all**: `systemctl status hostapd` - a config error there is
  usually a channel/regulatory-domain mismatch. `sudo iw reg get` shows the Pi's current regulatory
  domain.
- **Sees it, connects, but no portal prompt appears**: some phones only show the prompt once, or
  cache "I've seen this network, no portal" per SSID - forget the network on the phone and rejoin.
  Confirm the redirect itself works from another device on the WiFi: `curl -v
  http://captive.apple.com/hotspot-detect.html` should come back with the game's HTML, not a
  connection error.
- **Connects, portal opens, but the page is blank/errors**: that's the app, not the network layer -
  check `docker compose logs api` on the Pi.
