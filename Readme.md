# LuckyMaze

The .NET 10 backend for LuckyMaze — auth, players, bets, and the game loop. See
[`CLAUDE.md`](./CLAUDE.md) for the project overview and [`docs/dev_setup.md`](./docs/dev_setup.md)
for local development.

## Deploying

```bash
cp .env.example .env   # set LocalLogin__SigningKey at minimum
docker compose up -d
```

That runs the published `ghcr.io/luckymaze/luckymaze-api` image plus Postgres, with the physical
hardware side (LED panel, Klipper) in mock mode. For the actual cabinet — LED panel and Klipper
both on this same Pi — see [`docs/deployment.md`](./docs/deployment.md) for the hardware overlay
and every other setting.
