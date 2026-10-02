# Retirement Drawdown Simulator

Replays a retirement plan (spending strategy + investment strategy) from **every year in recorded market history since 1871** (optionally every month) and reports the percentage of start months where the money lasted until the age of death.

- **Backend:** .NET 10 Web API (`backend/`). No database: market data is held in memory (see `notes/restoring-the-database.md`)
- **Frontend:** React + TypeScript + Vite (`frontend/`)
- **Spec:** `notes/specification.md`

## Running it

Prerequisites: .NET 10 SDK, Node 20+.

```bash
# API on http://localhost:5047 — loads 1871–2009 history from the seed file,
# then fetches 2010–now from live sources
cd backend/Finance.Api
dotnet run --launch-profile http

# Frontend dev server on http://localhost:5173 (proxies /api to the backend)
cd frontend
npm install
npm run dev
```

To serve everything from the API alone, run `npm run build` in `frontend/`. The build goes to `backend/Finance.Api/wwwroot`, and the API serves it at http://localhost:5047.

Tests: `cd backend && dotnet test`

## Market data (all free)

| Period | Source |
|---|---|
| 1871–2009 | `backend/Finance.Api/SeedData/history.csv`, built by `tools/build_history.py` from the Bank of England *Millennium of Macroeconomic Data* and Robert Shiller's monthly S&P data |
| 2010–now | Fetched by the API: Yahoo Finance (SWDA.L MSCI World ETF, IGLT.L UK gilts ETF), ONS CPI (D7BT), Bank of England Bank Rate (IUMABEDR) |

The API's `MarketDataUpdater` runs at startup and then every 24 hours. It validates the new data and replaces the live months (2010 onwards). The live months are also saved to a snapshot file (`MarketData:SnapshotPath`, by default in the local application data folder), so a restart has them before its first fetch finishes. The last 20 runs since the API started are kept in memory. You can also trigger it from **Market data → Check for new data**, or with `POST /api/market/refresh`.

To rebuild the seed file:

```bash
pip install pandas openpyxl xlrd
python3 tools/build_history.py
```

**Proxies to be aware of:** before 2010, "global shares" are US shares converted to GBP, because no free monthly global index exists before about 1970. Bonds are UK government bonds throughout.

## How the simulation works

- Everything runs in **real terms**: each month's nominal return is deflated by that month's actual UK CPI. The user's constant planned inflation is used only for the Nominal view and for the "skip inflation rise" rules.
- Each month:
  1. Spending is set yearly, on each retirement anniversary.
  2. The withdrawal is taken; if it can't be paid, the path has failed.
  3. Returns are applied.
  4. Fees are deducted.
  5. The portfolio is rebalanced if the investment strategy says so.
- **Success rate** = complete paths that never ran out (and ended at or above "Leave at least") ÷ complete paths. Start months too recent to cover the whole retirement are drawn as dashed "partial" lines but are excluded from the percentage.

## Saved scenarios

Scenarios are saved in the visitor's browser (`localStorage`), so they stay on that device and are not shared between visitors.

## Ask Claude

Below the analysis, visitors can chat with Claude (`claude-opus-5`) about their plan. Claude receives the current inputs and results. It can also call a `run_simulation` tool to test what-if changes, such as "spend £5k more from 67", against the same market history.

- **Each visitor uses their own Anthropic API key.** They paste it into the "Ask Claude" field, and the chat panel only appears once a key is present. The key is kept in their browser (session storage, or local storage if they tick "Remember on this device"). It is sent in the `X-Anthropic-Key` header with each question, passed straight to Anthropic, and never stored or logged by the server.
- **Optional server key.** Set `ANTHROPIC_API_KEY` (or `Anthropic:ApiKey` in `appsettings.Development.json`) to let anyone use the chat without their own key. Do this only for private deployments, because every question is then billed to you.
- **Serve over HTTPS if the site is public.** Visitors' keys travel from the browser to your API.

## API

| Method | Path | |
|---|---|---|
| POST | `/api/simulate` | Body: `SimulationInput` (all fields optional, defaults = screenshot scenario) |
| GET | `/api/market/summary` | Coverage, returns, sources, last update |
| GET | `/api/market/months` | Full monthly history |
| GET | `/api/market/updates` | Update log since the API started |
| POST | `/api/market/refresh` | Fetch live data now |
| GET | `/api/health` | Health check for the host |
| GET | `/api/ask/status` | Whether the server has its own Anthropic key |
| POST | `/api/ask` | Ask Claude (server-sent events). Optional `X-Anthropic-Key` header |
