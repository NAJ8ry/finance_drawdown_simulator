#!/usr/bin/env python3
"""
Builds the historical seed dataset (monthly GBP returns) used by the simulator.

Output: backend/Finance.Api/SeedData/history.csv
Columns: month (YYYY-MM), equity, bond, cash, inflation
  - each value is the NOMINAL return for that month in GBP (0.01 = +1%),
    except `inflation` which is the month's UK CPI change.

Sources (all free):
  * Bank of England, "A millennium of macroeconomic data for the UK" (v3.1)
      - M15  $/£ exchange rate 1791-2016
      - M10  Spliced consol yield (to 1934) and spliced 10-year gilt yield (1935+)
      - M9   Prime short-term paper discount rate (to 1922), Treasury bill rate (1923+)
      - M6   Spliced monthly CPI (1914-07+)
      - A47  Annual CPI (preferred measure) - interpolated monthly before 1914-07
  * Robert Shiller, "Irrational Exuberance" monthly data (ie_data.xls)
      - S&P Composite real total-return price and US CPI, 1871+

Equities before 2010 are the US S&P Composite total return converted to GBP.
There is no free monthly global equity index before ~1970, and the US has been the
largest single component of world indices; this is documented as a proxy in the app.
Bonds are UK government bonds (consols to 1934, 10-year gilts after).
Cash is UK Treasury bills / prime bills.

Months from 2010-01 onwards are fetched live by the API (see MarketDataUpdater).

Usage:  python3 tools/build_history.py [--cache DIR]
Requires: pandas, openpyxl, xlrd
"""
import argparse
import os
import sys
import urllib.request

import numpy as np
import pandas as pd

BOE_URL = ("https://www.bankofengland.co.uk/-/media/boe/files/statistics/research-datasets/"
           "a-millennium-of-macroeconomic-data-for-the-uk.xlsx")
SHILLER_URLS = [
    "https://img1.wsimg.com/blobby/go/e5e77e0b-59d1-44d9-ab25-4763ac982e53/downloads/ie_data.xls",
    "http://www.econ.yale.edu/~shiller/data/ie_data.xls",
]
FIRST_MONTH = pd.Period("1871-01", "M")
LAST_MONTH = pd.Period("2009-12", "M")  # live data takes over from 2010-01

MONTHS = {m: i + 1 for i, m in enumerate(
    ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"])}


def download(url, path):
    if os.path.exists(path):
        return path
    print(f"Downloading {url}")
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(req, timeout=120) as r, open(path, "wb") as f:
        f.write(r.read())
    return path


def boe_monthly(xlsx, sheet, col):
    """Return a monthly Series (PeriodIndex) from a BoE 'M' sheet (year in col 0, month name in col 1)."""
    d = pd.read_excel(xlsx, sheet_name=sheet, header=None)
    years = pd.to_numeric(d.iloc[:, 0], errors="coerce")
    months = d.iloc[:, 1].map(lambda m: MONTHS.get(str(m).strip()[:3]))
    vals = pd.to_numeric(d.iloc[:, col], errors="coerce")
    ok = years.notna() & months.notna() & vals.notna()
    idx = pd.PeriodIndex([pd.Period(year=int(y), month=int(m), freq="M")
                          for y, m in zip(years[ok], months[ok])])
    s = pd.Series(vals[ok].values, index=idx)
    return s[~s.index.duplicated(keep="last")].sort_index()


def boe_annual(xlsx, sheet, col):
    d = pd.read_excel(xlsx, sheet_name=sheet, header=None)
    years = pd.to_numeric(d.iloc[:, 0], errors="coerce")
    vals = pd.to_numeric(d.iloc[:, col], errors="coerce")
    ok = years.notna() & vals.notna()
    return pd.Series(vals[ok].values, index=years[ok].astype(int).values)


def shiller(xls):
    d = pd.read_excel(xls, sheet_name="Data", header=None, skiprows=8)
    date = pd.to_numeric(d.iloc[:, 0], errors="coerce")
    cpi = pd.to_numeric(d.iloc[:, 4], errors="coerce")
    real_tr = pd.to_numeric(d.iloc[:, 9], errors="coerce")
    ok = date.notna() & cpi.notna() & real_tr.notna()
    idx = pd.PeriodIndex([pd.Period(year=int(x), month=int(round((x - int(x)) * 100)), freq="M")
                          for x in date[ok]])
    # Shiller's real TR price = nominal TR index deflated by US CPI; recover nominal (scale irrelevant)
    return pd.Series((real_tr[ok] * cpi[ok]).values, index=idx)


def par_bond_return(y_prev, y_cur, years=10.0):
    """One-month total return of a par bond bought at y_prev, revalued at y_cur (annual comp.)."""
    c = y_prev
    n = years - 1.0 / 12.0
    price = c / y_cur * (1 - (1 + y_cur) ** -n) + (1 + y_cur) ** -n
    return price - 1 + c / 12.0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--cache", default=os.path.join(os.path.dirname(__file__), ".cache"))
    args = ap.parse_args()
    os.makedirs(args.cache, exist_ok=True)

    boe = download(BOE_URL, os.path.join(args.cache, "boe.xlsx"))
    shil = None
    for u in SHILLER_URLS:
        try:
            shil = download(u, os.path.join(args.cache, "ie_data.xls"))
            break
        except Exception as e:  # noqa: BLE001
            print(f"  failed: {e}", file=sys.stderr)
    if shil is None:
        sys.exit("Could not download Shiller data")

    months = pd.period_range(FIRST_MONTH, LAST_MONTH, freq="M")

    # --- Equities: US total return in USD -> GBP
    us_tr = shiller(shil).reindex(months)
    usd_per_gbp = boe_monthly(boe, "M15. Mthly $-£ 1791-2015", 2).reindex(months).ffill()
    eq_level = us_tr / usd_per_gbp
    equity = eq_level.pct_change()

    # --- Bonds: consols to 1934, 10-year gilts from 1935
    consol = boe_monthly(boe, "M10. Mthly long-term rates", 10).reindex(months).ffill() / 100
    gilt10 = boe_monthly(boe, "M10. Mthly long-term rates", 30).reindex(months).ffill() / 100
    bond = pd.Series(index=months, dtype=float)
    for i in range(1, len(months)):
        m = months[i]
        if m.year < 1935 or (m.year == 1935 and m.month == 1):
            yp, yc = consol.iloc[i - 1], consol.iloc[i]
            bond.iloc[i] = yp / yc - 1 + yp / 12
        else:
            bond.iloc[i] = par_bond_return(gilt10.iloc[i - 1], gilt10.iloc[i])

    # --- Cash: prime bills to 1922, Treasury bills from 1923 (annual % rate, earned over the month)
    prime = boe_monthly(boe, "M9. Mthly short-term rates", 14)
    tbill = boe_monthly(boe, "M9. Mthly short-term rates", 19)
    rate = pd.concat([prime[prime.index < pd.Period("1923-01", "M")],
                      tbill[tbill.index >= pd.Period("1923-01", "M")]])
    rate = rate[~rate.index.duplicated()].reindex(months).ffill() / 100
    cash = rate.shift(1) / 12

    # --- UK CPI: annual (interpolated, value placed at July) until 1914-07, monthly spliced after
    annual = boe_annual(boe, "A47. Wages and prices", 3)
    anchor = pd.Series({pd.Period(year=y, month=7, freq="M"): v for y, v in annual.items()
                        if 1869 <= y <= 1915})
    full = pd.period_range(anchor.index.min(), anchor.index.max(), freq="M")
    log_interp = np.log(anchor.reindex(full).astype(float))
    ordinals = np.array([p.ordinal for p in full])
    known = ~log_interp.isna().values
    log_interp[:] = np.interp(ordinals, ordinals[known], log_interp.values[known])
    cpi_early = np.exp(log_interp)
    cpi_monthly = boe_monthly(boe, "M6. Mthly prices and wages", 18)
    splice = pd.Period("1914-07", "M")
    scale = cpi_early[splice] / cpi_monthly[splice]
    cpi = pd.concat([cpi_early[cpi_early.index < splice], cpi_monthly[cpi_monthly.index >= splice] * scale])
    cpi = cpi[~cpi.index.duplicated()].reindex(months).ffill()
    inflation = cpi.pct_change()

    out = pd.DataFrame({"equity": equity, "bond": bond, "cash": cash, "inflation": inflation}).iloc[1:]
    missing = out[out.isna().any(axis=1)]
    if len(missing):
        sys.exit(f"Missing values in {len(missing)} months, e.g. {missing.head()}")

    dest = os.path.join(os.path.dirname(__file__), "..", "backend", "Finance.Api", "SeedData", "history.csv")
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    out.index = out.index.astype(str)
    out.index.name = "month"
    out.round(8).to_csv(dest)

    def ann(s):
        return (1 + s).prod() ** (12 / len(s)) - 1

    print(f"Wrote {len(out)} months {out.index[0]}..{out.index[-1]} to {os.path.normpath(dest)}")
    real = lambda s: (1 + s) / (1 + out["inflation"]) - 1  # noqa: E731
    for c in ["equity", "bond", "cash"]:
        print(f"  {c:8s} nominal {ann(out[c]):6.2%}  real {ann(real(out[c])):6.2%}  "
              f"min {out[c].min():7.2%}  max {out[c].max():7.2%}")
    print(f"  inflation {ann(out['inflation']):6.2%}")


if __name__ == "__main__":
    main()
