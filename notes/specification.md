# Retirement Drawdown Simulator — Program Specification

| | |
|---|---|
| **Status** | v0.2 — implemented (see README.md) |
| **Date** | 2026-09-28 |
| **Author** | NAJ8ry |
| **Platform** | Web application |
| **Base currency / market** | GBP, global stocks and bonds |

---

## 1. Overview

### 1.1 Purpose
The program answers one question for someone entering retirement:

> *"If I start drawing from my pot with **this spending strategy** and **this investment strategy**, how likely is it that the money lasts until I die?"*

It answers by **historical backtesting**. The user's plan is replayed from **every month in market history** as a start date. Each replay is one path (one line on the chart), and the result is the **percentage of paths whose balance stays above zero** until the chosen age of death.

### 1.2 Reference design
The UI is modelled on the reference screenshot (*Balance — Constant Inflation Adjustment*):

- A spaghetti chart of the portfolio balance (y-axis, £) against the client's age (x-axis, 60 → 94), with one line per historical start month.
- A **Nominal / Real** toggle.
- A header card showing the **starting withdrawal rate** (e.g. 4%).
- A **Portfolio Assumptions** card (e.g. 60% global stocks / 40% global bonds, 0.5% fees, no tax).
- A legend with Best case, Median, Worst case, Likely / Less likely / Rare bands, Calendar year, and One-offs / goals.
- Zoom in and out, an **Explanation** panel and an **Actions** menu.
- A footer disclaimer: *"For illustrative purposes only."*

### 1.3 Worked example (baseline scenario)
| Input | Value |
|---|---|
| Starting balance | £100,000 |
| Retirement age → age of death | 60 → 94 (34 years, 408 months) |
| Spending strategy | Constant inflation-adjusted, 4% initial withdrawal (£4,000/yr) |
| Investment strategy | 60% global equities / 40% global bonds, rebalanced annually |
| Fees | 0.5% p.a. |
| Tax | None |
| Planned inflation | 2.5% p.a. (constant) |

**Output:** success %, the spaghetti chart, and the best, median and worst paths.

---

## 2. Glossary

| Term | Meaning |
|---|---|
| **Start month** | The historical calendar month used as "month 1 of retirement" for one path. |
| **Path / run** | One simulation of the full retirement from a given start month. |
| **Complete path** | A path with enough historical data to cover retirement age through death age. |
| **Partial path** | A path whose start month is too recent to cover the full retirement. It is plotted but **excluded from the success %**. |
| **Failure** | The balance reaches ≤ £0 before the death month. |
| **Success rate** | Successful complete paths ÷ all complete paths × 100. |
| **Real** | Values in today's money, after removing inflation. |
| **Nominal** | Values in the money of each future year, using the user's planned inflation rate. |
| **Guardrail** | A rule that raises or lowers spending when the portfolio does well or badly. |
| **One-off / goal** | A lump-sum withdrawal or deposit at a given age, e.g. a new car at 70 or a downsizing gain at 75. |

---

## 3. Functional requirements — Inputs

### 3.1 Personal and portfolio inputs
| ID | Input | Type / constraints | Default |
|---|---|---|---|
| IN-01 | Starting portfolio balance | £, > 0 | £100,000 |
| IN-02 | Retirement (start) age | integer 40–90 | 60 |
| IN-03 | Age of death (planning horizon) | integer > IN-02, ≤ 110 | 94 |
| IN-04 | Planned inflation rate | % p.a., constant, −2% to 15% | 2.5% |
| IN-05 | Annual fees | % p.a. of balance, 0–3% | 0.5% |
| IN-06 | Tax | v1: "No tax" only. The field is reserved for later. | None |
| IN-07 | Asset allocation | % global equities / % global bonds / % cash, summing to 100 | 60/40/0 |
| IN-08 | One-offs / goals | List of {age, amount (±£ in today's money), label} | empty |
| IN-09 | Regular income & outgoings | List of {label, income/outgoing, start age, end age (blank = for life), £/yr in today's money, inflation-linked or fixed}. Income pays part of your spending, so less is drawn from the pot. Surplus income is invested. Outgoings add to what is drawn. Fixed amounts lose value at the planned inflation rate from their start age. | none |

### 3.2 Strategy inputs
| ID | Input |
|---|---|
| IN-10 | **Spending strategy**: chosen from the catalogue (§4), with its parameters. |
| IN-11 | **Investment strategy**: chosen from the catalogue (§5), with its parameters. |
| IN-12 | Withdrawal frequency: monthly (default) or annual in advance. |

### 3.3 Scenario management
- FR-01: The user can save, name, duplicate and delete scenarios (one set of inputs = one scenario).
- FR-02: The user can compare 2–4 scenarios side by side (success % and median / worst balance).
- FR-03: Every input is validated with inline error messages, and the simulation re-runs automatically when an input changes (debounced).

---

## 4. Spending (withdrawal) strategy catalogue

> **Implemented as base + combinable adjustments.** The user picks one **base**: S1 constant inflation-adjusted, S2 constant percentage, or S8 remaining life. They can then switch on any combination of **adjustments**, each with its own percentages. The adjustments are applied each year in this order:
> 1. S6 skip inflation after a loss
> 2. S3 good year / bad year, with separate raise % and cut % (0% = never)
> 3. S4 Guyton-Klinger, with its own separate raise % and cut %
> 4. S7 ratchet
> 5. S9 custom rules
> 6. S5 floor and ceiling (last, so it bounds all the others)

Every strategy has the same interface: at each decision point (monthly, or yearly on the retirement anniversary) it receives the **path state** and returns the **withdrawal amount** for the coming period.

The path state contains: current balance, initial balance, the previous withdrawal, the portfolio return over the last 12 months, the peak balance to date, the current age, years remaining, and the current withdrawal rate.

| # | Strategy | Rule | Parameters |
|---|---|---|---|
| S1 | **Constant inflation-adjusted** (screenshot baseline, "4% rule") | Year 1 = rate × initial balance. After that it is fixed in real terms, growing with planned inflation in nominal terms. | initial rate % |
| S2 | **Constant percentage** | Withdraw x% of the current balance each year. Never fails, but income varies. | rate %, optional floor £ |
| S3 | **Simple guardrails (±10%)** — *user's example* | Each year: if the portfolio's trailing 12-month return is ≥ +good threshold, raise spending by the step. If it is ≤ bad threshold, cut spending by the step. Otherwise keep it unchanged in real terms. | step % (default 10), good threshold (default +10%), bad threshold (default −10%), max cumulative raise, max cumulative cut |
| S4 | **Guyton-Klinger guardrails** | If the current withdrawal rate is more than 20% above the initial rate, cut by 10% (capital-preservation rule). If it is more than 20% below, raise by 10% (prosperity rule). Skip the inflation increase after a year with a negative return. | initial rate, upper and lower guardrail %, adjustment %, whether to apply the freeze rule |
| S5 | **Floor and ceiling** | Withdraw x% of the current balance, bounded between a floor and a ceiling expressed relative to the initial real withdrawal. | rate, floor % (e.g. −15%), ceiling % (e.g. +25%) |
| S6 | **Inflation skip after loss** | Like S1, but the inflation rise is skipped for any year after a negative portfolio return. | initial rate |
| S7 | **Ratchet** | Like S1, but the withdrawal steps up permanently by the ratchet % whenever the balance exceeds the initial real balance by the trigger %. It is never cut. | initial rate, trigger % (e.g. 50%), ratchet % (e.g. 10%) |
| S8 | **Remaining-life (RMD-style)** | Withdrawal = balance ÷ years remaining to the death age. It can use an annuity factor with an assumed real return. | assumed real return % |
| S9 | **Custom rule builder** | The user composes `IF <condition> THEN <action>` rules on top of a base strategy (S1 or S2). Conditions include trailing return, drawdown from peak, withdrawal rate, balance vs initial, and age. Actions include ±% change, set to x% of balance, freeze inflation rise, clamp to min or max. Rules are evaluated in order, and the first match or all matches apply (configurable). | rule list |

General rules for every strategy:
- SR-01: The withdrawal is never negative, and it is capped at the current balance.
- SR-02: Optional global **minimum spending floor** (£ real/yr). If the balance cannot fund the floor, the path is marked as having **dropped below the floor**. This is a secondary metric, not a failure.
- SR-03: The strategy's spending series is recorded per path so the UI can show an income chart as well as the balance chart.

---

## 5. Investment strategy catalogue

> **Implemented as base + optional cash buffer.** I6 (cash buffer) is a tick-box that works with any base strategy (I1–I5, I7). The buffer holds N years of the current withdrawal from the pot. In a falling market it is spent first and no rebalancing happens. In a rising market it is topped up at the year end.

| # | Strategy | Rule | Parameters |
|---|---|---|---|
| I1 | **Fixed allocation, periodic rebalance** (baseline) | Hold the target mix and rebalance on a fixed schedule. | allocation, frequency (monthly / quarterly / annual) |
| I2 | **Threshold rebalance** | Rebalance only when an asset drifts more than x% from its target. | allocation, band % |
| I3 | **No rebalancing (buy and hold)** | The initial mix is allowed to drift. | allocation |
| I4 | **Declining equity glide path** | Equity % falls linearly from a start value to an end value over N years. | start %, end %, years |
| I5 | **Rising equity glide path** | Equity % rises over time (the "bond tent"). | start %, end %, years |
| I6 | **Cash buffer / bucket** | Hold X years of spending in cash. In down markets (trailing return < 0 or drawdown > y%), withdraw from cash. In up markets, refill cash from equities. | buffer years, trigger, refill rule |
| I7 | **Withdraw-from-winner** | Take withdrawals from whichever asset is furthest above its target (an implicit rebalance). | allocation |

- IR-01: Withdrawals are taken pro-rata across assets unless the strategy says otherwise.
- IR-02: Transaction costs are ignored in v1.

---

## 6. Simulation engine

### 6.1 Treatment of inflation (key design decision)
Historical returns contain the inflation of their era (e.g. 1970s UK inflation above 20%), while the user specifies one constant planned inflation rate. To keep these consistent:

1. Convert every historical nominal return to a **real return** using the historical UK CPI for the same month.
2. Run the whole simulation **in real terms**. Constant-inflation-adjusted spending is then simply a flat real amount.
3. Every result is shown in real terms (today's money). There is no nominal view: it was dropped because, with no tax modelled, it changes no decision.
4. The user's constant inflation rate is used only for nominal-denominated inputs, such as fixed nominal pensions, and by rules that skip an inflation rise.

This preserves the historical *sequence of real returns*, which is what determines success, while honouring the user's constant inflation assumption.

### 6.2 Monthly step (order of operations)
For each path, and for each month m from 0 to (death age − retirement age) × 12 − 1:

1. **Decision point.** On the retirement anniversary (or monthly, if configured), the spending strategy computes the withdrawal and the investment strategy computes the target allocation.
2. **Withdraw.** Subtract this month's withdrawal and any one-off due this month (withdrawals are made at the start of the month).
3. **Check failure.** If the balance is ≤ 0, record the failure age, set the balance to 0 and stop the path.
4. **Apply returns.** Each asset bucket is multiplied by (1 + that asset's real return for the historical month start + m).
5. **Apply fees.** balance × (1 − annual fee)^(1/12).
6. **Rebalance** if the investment strategy calls for it.
7. **Record** balance, withdrawal and allocation for month m.

### 6.3 Paths and success rate
- EN-01: Run one path for **every year in the dataset** by default, starting in January, or the first month of data for 1871. The user can switch to **every month**, which gives about 12× more paths. The chart and every statistic always use the same set of start dates.
- EN-02: A path is **complete** if start month + horizon ≤ the last month of data. Otherwise it is **partial** and ends at the latest data month.
- EN-03: **Success % = complete paths that never failed ÷ complete paths.** Partial paths are excluded from this figure.
- EN-04: Also report the number of complete paths, the number of partial paths, and how many partial paths **have already failed**. A partial path that has already failed is a known failure, so the UI should flag it (see open question Q3).
- EN-05: Secondary metrics:
  - median and 10th/90th percentile ending balance
  - worst-case age at depletion
  - % of paths below the spending floor
  - average and minimum real income
  - median maximum drawdown
- EN-06: Results must be deterministic, because the same inputs and the same dataset version give the same output.
- EN-07: Performance target: under 1 second for about 1,800 start months × 600 months on a typical laptop. This suggests a vectorised or typed-array implementation and a Web Worker if the engine runs in the browser.

### 6.4 Best, median and worst paths
Paths are ranked by the **real ending balance**. For failed paths, ties are broken by the age of failure, with earlier failure counting as worse. The highlighted Best start and Worst start lines are the top- and bottom-ranked paths, labelled with their start date (e.g. "Worst start: retired Jan 1937"). The Median line is the 50th percentile at each age, not a single path. The shaded bands, labelled Half, 8 in 10 and 9 in 10 of outcomes, are the 25–75, 10–90 and 5–95 percentile envelopes by age.

---

## 7. Market data

### 7.1 Required series (monthly)
| Series | Purpose |
|---|---|
| Global equities, total return, in GBP | Equity returns |
| Global bonds, total return, GBP-hedged | Bond returns |
| UK cash / Treasury bill rate | Cash bucket returns |
| UK CPI (or RPI for early history) | Converting to real returns |
| GBP/USD exchange rate | Converting USD-denominated history into GBP |

### 7.2 History — sources used (all free)
The history runs from **Feb 1871 to the latest complete month**. It is stitched together from the sources below.

| Series | 1871 – 2009 (seed file `SeedData/history.csv`, built by `tools/build_history.py`) | 2010 – now (fetched live by the API) |
|---|---|---|
| Equities (GBP, total return) | US S&P Composite total return (Shiller), converted to GBP with the Bank of England $/£ series | iShares Core MSCI World ETF, SWDA.L (Yahoo Finance) |
| Bonds (GBP, total return) | UK consols to 1934, then 10-year gilts. Returns are derived from Bank of England yields | iShares Core UK Gilts ETF, IGLT.L (Yahoo Finance) |
| Cash | Prime bills to 1922, then Treasury bills (Bank of England) | Bank Rate, monthly average (BoE IADB IUMABEDR) |
| UK CPI | Annual CPI interpolated monthly to 1914, then the monthly spliced CPI (BoE millennium dataset) | ONS CPI index D7BT |

- DA-01: Every stitch point and proxy is shown in the app (Market data tab and Explanation panel).
- DA-02: Before 2010, "global" shares are approximated by US shares in GBP. Bonds are UK gilts rather than GBP-hedged global bonds.
- DA-03: Data is stored in `market_months` with a `source` of `history` or `live`, and every update run is logged in `data_update_logs`.

### 7.3 Automatic updates
- DU-01: A **scheduled job** runs monthly, a few days after month end, and fetches the latest month for each series from its live source or an ETF price proxy.
- DU-02: Validation before publishing:
  - no missing months
  - the return lies within sanity bounds (e.g. ±40% per month)
  - the CPI value is present
  - if a check fails, publishing is held back and an alert is sent
- DU-03: On success, the job publishes a new dataset version. Old versions are kept so earlier results can be reproduced.
- DU-04: The UI shows **"Market data up to: <Month YYYY>"** and the dataset version.
- DU-05: A manual "refresh now" admin action exists, plus a fallback manual CSV upload.
- DU-06: New months turn some partial paths into complete ones, so the success % can change. The UI notes this.

---

## 8. Outputs and UI

### 8.1 Main results screen (matches the screenshot)
- UI-01: **Headline success %**, e.g. *"87% of historical scenarios lasted to age 94"*, with the count *"(1,214 of 1,395 complete paths)"*.
- UI-02: A card showing the **starting withdrawal rate** and a **Portfolio Assumptions** card (allocation, fees, tax).
- UI-03: **Balance chart.**
  - It has one line per path, with age on the x-axis and £ on the y-axis.
  - It shows today's money only (no Nominal / Real toggle; see §6.1) and has zoom in / out.
  - A dashed "Retirement age" marker sits at the start.
  - Partial paths are drawn in a distinct style (e.g. dashed or faded).
  - Failed paths end at the £0 axis.
  - Best, median and worst paths are highlighted, and the percentile bands are shaded.
  - One-offs are marked on the x-axis.
- UI-04: **Calendar-year hover.** Hovering a line highlights it and shows the start month (e.g. *"Retired Jan 1973"*), the balance at the hovered age, and the withdrawal that year.
- UI-05: An **Income chart** tab shows real spending per path, which matters for the variable strategies (S2–S9).
- UI-06: A **Worst starts table** lists the 10 worst start months, with the depletion age or the minimum balance.
- UI-07: A **Percentile table** shows the balance at ages 70, 80, 90 and death at the 10th, 25th, 50th, 75th and 90th percentiles.
- UI-08: An **Explanation** panel describes the method, the data sources and the caveats in plain English.
- UI-09: The **Actions** menu offers: export CSV (paths and summary), export the chart as PNG, save the scenario, compare scenarios, and reset.
- UI-10: A permanent disclaimer reads *"For illustrative purposes only. Past performance is not a guide to future returns. Not financial advice."*

### 8.2 Inputs screen
- A form for §3, with strategy pickers that show only the parameters relevant to the chosen strategy, plus a short description of each strategy.
- A custom rule builder UI for S9.

---

## 9. Non-functional requirements

| ID | Requirement |
|---|---|
| NF-01 | Responsive web app that works on desktop and tablet. The phone layout may simplify the chart. |
| NF-02 | Re-simulation latency under 1s (EN-07). |
| NF-03 | Accessibility: WCAG 2.1 AA, a colour-blind-safe palette, and chart data also available as a table. |
| NF-04 | Currency formatting in £ (en-GB). Ages are integers and the x-axis ticks are yearly. |
| NF-05 | No personal data is needed for v1. Scenarios are stored locally or per account (see Q6). |
| NF-06 | Every result is traceable to a dataset version (DU-03). |

### 9.1 Suggested architecture (to be confirmed at design stage)
- **Frontend:** SPA (e.g. React + TypeScript) with an interactive chart library that can draw about 1,500 lines smoothly, e.g. canvas-based.
- **Engine:** a pure, UI-independent TypeScript (or Python) module. If it is in TypeScript it can run in a Web Worker in the browser for instant feedback.
- **Backend:** a small API that serves the versioned dataset, plus a scheduled worker for data updates (§7.3).
- **Storage:** SQLite or Postgres for the time series and saved scenarios.

---

## 10. Testing and acceptance
- T-01: Unit tests for every strategy, using hand-calculated 2–3 month cases.
- T-02: A zero-volatility check: with constant real returns r and spending set to the annuity payment, the balance reaches exactly £0 at the death age.
- T-03: Reproduce a published result, e.g. US 60/40 at a 4% constant real withdrawal over 30 years, which should be close to the widely cited ~95%+ success. This uses the US-only data option as a check.
- T-04: A data update test runs the ingestion job against fixtures, including failure cases.
- T-05: A property test checks that raising the withdrawal rate never increases the success %, all else equal.
- T-06: A visual test compares the main chart against the reference layout.

---

## 11. Assumptions, open questions and scope

### 11.1 Assumptions
- A single retiree. The horizon ends at a fixed age of death, not a mortality table.
- Returns are the historical sequence of real returns. The user's constant inflation affects nominal inputs only (§6.1).
- No tax in v1, and no transaction costs.

### 11.2 Open questions
| # | Question |
|---|---|
| Q1 | Is the approach to inflation in §6.1 (historical real returns, constant planned inflation for nominal values) acceptable? |
| Q2 | Should "stock going well / badly" in the ±10% guardrail be judged by the trailing 12-month return, the drawdown from peak, or the balance vs the initial balance? The spec currently defaults to the trailing 12-month return. |
| Q3 | Should partial paths that **have already failed** count as failures in the success %? |
| Q4 | Should the success threshold be "> £0" or "> a user-defined legacy amount" (e.g. leave £50k)? |
| Q5 | Should the minimum acceptable income (spending floor) count as failure, or only as a secondary warning? |
| Q6 | Is this a single-user tool or multi-user with accounts? |
| Q7 | ~~Paid data?~~ **Answered:** free data only (see §7.2). |
| Q8 | ~~Monte Carlo?~~ **Answered:** not needed. The tool is purely historical. |

### 11.3 Out of scope (v1)
- Tax (ISA / SIPP / GIA wrappers)
- Couples and mortality tables
- Monte Carlo simulation (not wanted)
- Advice or recommendations
