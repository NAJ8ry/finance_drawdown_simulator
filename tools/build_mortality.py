#!/usr/bin/env python3
"""
Builds the UK mortality table used for lifespan-weighted results.

Output: backend/Finance.Api/SeedData/mortality_uk.csv
Columns: year, age, male, female
  - qx: the probability that someone of exact age `age` in calendar year `year` dies before their next birthday
    (converted from ONS "per 100,000" to a probability)

Source (Open Government Licence v3.0, commercial use allowed with attribution):
  Office for National Statistics, "Mortality rates (qx), principal projection, United Kingdom", 2024-based
  (released 15 May 2026), sheets "males period qx" and "females period qx": years 1981-2074, ages 0-100.
  Attribution: "Source: Office for National Statistics licensed under the Open Government Licence v.3.0"

The simulator follows a person's own cohort through these period tables: at age a, someone born in year b uses the
rate for calendar year b + a (the ONS cohort tables are built the same way). See notes/sources-and-licences.md.

Usage:  python3 tools/build_mortality.py [--cache DIR]
Requires: pandas, openpyxl
"""
import argparse
import os
import urllib.request

import pandas as pd

URL = ("https://www.ons.gov.uk/file?uri=/peoplepopulationandcommunity/birthsdeathsandmarriages/lifeexpectancies/"
       "datasets/mortalityratesqxprincipalprojectionunitedkingdom/2024based/ukppp24qx.xlsx")
MIN_AGE = 16  # youngest current age the app accepts


def download(url, path):
    if os.path.exists(path):
        return path
    print(f"Downloading {url}")
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(req, timeout=120) as r, open(path, "wb") as f:
        f.write(r.read())
    return path


def period_table(xlsx, sheet):
    """Long table (year, age, qx) from an ONS period qx sheet: ages down the rows, years across the columns."""
    d = pd.read_excel(xlsx, sheet_name=sheet, header=None)
    header_row = d.index[d.iloc[:, 0].astype(str).str.startswith("Exact age")][0]
    years = [int(str(v).replace("Year", "").strip().split(".")[0]) for v in d.iloc[header_row, 1:]]
    body = d.iloc[header_row + 1:].dropna(subset=[0])
    rows = []
    for _, r in body.iterrows():
        age = int(r.iloc[0])
        for year, v in zip(years, r.iloc[1:]):
            rows.append((year, age, float(v) / 100_000))
    return pd.DataFrame(rows, columns=["year", "age", "qx"])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--cache", default=os.path.join(os.path.dirname(__file__), ".cache"))
    args = ap.parse_args()
    os.makedirs(args.cache, exist_ok=True)
    xlsx = download(URL, os.path.join(args.cache, "ukppp24qx.xlsx"))

    male = period_table(xlsx, "males period qx").rename(columns={"qx": "male"})
    female = period_table(xlsx, "females period qx").rename(columns={"qx": "female"})
    table = male.merge(female, on=["year", "age"])
    table = table[table.age >= MIN_AGE].sort_values(["year", "age"])
    assert table.male.between(0, 1).all() and table.female.between(0, 1).all()

    out = os.path.join(os.path.dirname(__file__), "..", "backend", "Finance.Api", "SeedData", "mortality_uk.csv")
    with open(out, "w", newline="") as f:
        f.write("# Source: Office for National Statistics licensed under the Open Government Licence v.3.0\n")
        f.write("# Mortality rates (qx), principal projection, United Kingdom, 2024-based; period tables\n")
        table.to_csv(f, index=False, float_format="%.6f")
    print(f"Wrote {len(table)} rows ({table.year.min()}-{table.year.max()}, ages {table.age.min()}-{table.age.max()}) to {out}")


if __name__ == "__main__":
    main()
