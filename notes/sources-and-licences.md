# Sources, research and licences

The record of every data source and piece of research the simulator uses or builds on, with its licence and what
must happen before any **commercial** use. Keep it up to date whenever a source is added, changed or removed.

- Compiled: 29 September 2026. Licence terms were read on the publishers' own pages on that date; quotes are verbatim.
- This is a working record, not legal advice. Before launching commercially, have the "Action needed" items confirmed
  (ideally in writing from each publisher) and keep that correspondence with this file.
- **UNVERIFIED** marks anything that could not be confirmed at the source. Treat it as unknown.

## At a glance

| Source | Used for | Used now? | Commercial use | Action needed before commercial launch | Contact |
|---|---|---|---|---|---|
| Yahoo Finance chart API (SWDA.L, IGLT.L) | Live share and gilt prices from 2010 | Yes | **No** | **Replace** with a licensed price feed | none published |
| Bank of England, *A millennium of macroeconomic data* v3.1 | Gilts, bills, $/£, CPI before 2010 | Yes | **Unclear / No by default** | Ask the BoE for written permission; some series inside are third-party | enquiries@bankofengland.co.uk |
| Robert Shiller, `ie_data.xls` | US shares before 2010 | Yes | **Unclear** (no licence) | Ask Shiller / RSBB-I, LLC; check S&P Dow Jones Indices' position | none published |
| ONS CPI, series D7BT (MM23) | Live UK inflation | Yes | Yes, with attribution (OGL v3) | Show the attribution line | not needed |
| Bank of England IADB, Bank Rate IUMABEDR | Live cash returns | Yes | Yes, with attribution (OGL) | Show the attribution line | enquiries@bankofengland.co.uk |
| ONS mortality rates (qx), 2024-based principal projection | Lifespan (chance of running out while alive) | Yes | Yes, with attribution (OGL v3) | Show the attribution line | pop.info@ons.gov.uk |
| Pensions UK / Loughborough University Retirement Living Standards | Spending benchmarks (possible) | No | Yes, under their free licence, with conditions | Accept their terms (exact figures and name, link/logo, keep current, not-advice notice, indemnity) | livingstandards@pensionsuk.org.uk |
| JST Macrohistory Database R6 | International returns (considered) | No | **No** (CC BY-NC-SA 4.0) | Permission needed, or non-commercial version only | macrofinancelab@gmail.com |
| Kenneth French Data Library | International returns (considered) | No | **Unclear** | Written permission; MSCI/Bloomberg rights may apply | none published |
| MSCI World index history (GBP) | Global shares before 2010 (option) | No | **No** without a paid licence | Buy an index data licence | MSCI "Contact sales" form |
| FTSE All-World history (GBP) | Global shares before 2010 (option) | No | **No** without a paid licence | Buy an index data licence | LSEG licensing, +44 (0)20 7866 1810 |

Research papers are cited for their methods and findings. Methods and ideas are not copyright, so implementing them
needs no permission, but cite them, and do not copy their text, tables or figures into the product.

## Data sources used today

### Yahoo Finance chart API — **blocker for commercial use**
- Used in `backend/Finance.Api/Services/LiveMarketData.cs` for monthly prices of iShares Core MSCI World (SWDA.L) and
  iShares Core UK Gilts (IGLT.L), 2010 onwards.
- Undocumented endpoint (`query1.finance.yahoo.com/v8/finance/chart`), covered by the general Yahoo Terms of Service
  (https://legal.yahoo.com/us/en/yahoo/terms/otos/index.html):
  - "Unless otherwise expressly stated, you may not access or reuse the Services, or any portion thereof, for any
    commercial purpose."
  - "[You may not] access or collect data … from our Services using any automated means … for any purpose without our
    express, prior permission."
  - "Unless you have explicit written permission, you must not … exploit for any commercial purposes, any portion or
    use of, or access to, the Services (including content, advertisements, APIs, and software)."
- **Commercial use: No.** Strictly, even automated personal use is not permitted.
- **Action:** replace with a licensed feed (an LSEG/LSE data vendor, or a paid API whose terms allow display and
  redistribution). Yahoo publishes no licensing contact.
- MSCI (https://www.msci.com/legal/index-terms): index data is "for your informational, non-commercial purposes only"
  without MSCI's prior written approval, and "Reproduction, redistribution … without MSCI's prior written consent is
  strictly prohibited." The app uses ETF *prices*, not MSCI index levels. Do not present the series as "MSCI World index"
  returns. Whether naming the fund descriptively is acceptable is UNVERIFIED legally.

### Bank of England — *A millennium of macroeconomic data for the UK*, v3.1
- Used by `tools/build_history.py` to build `backend/Finance.Api/SeedData/history.csv`. Sheets: M15 ($/£), M10
  (consol and 10-year gilt yields), M9 (prime and Treasury bill rates), M6 and A47 (CPI).
- Download: https://www.bankofengland.co.uk/-/media/boe/files/statistics/research-datasets/a-millennium-of-macroeconomic-data-for-the-uk.xlsx
- No dataset-specific licence. It is not part of the IADB "Database", which is OGL. The general terms apply
  (https://www.bankofengland.co.uk/legal):
  - "You may … download, display or print the Resources for personal use or internal use within an individual
    organisation for non-commercial purposes. Requests for further authorisation regarding proposed use of the
    Resources should be addressed to: Head of Communications Division … enquiries@bankofengland.co.uk … +44 (0)20 3461 4878."
  - "Where Bank Resources include or are comprised of third party copyright materials … any re-use is subject to the
    separate approval of the relevant third party."
- Third-party sources named in the sheets used: M15 Craighead (2010), Federal Reserve Board, ONS; M10 Neal (1990),
  Odlyzko (2014), NBER macrohistory database, Capie & Webber, Klovland (1994), ONS/DMO, The Economist, BoE; M9 Weiller
  & Mirowski, Nishimura, Capie & Webber, ONS, BoE; M6 Gayer et al. (1953), Klovland (1993), Capie & Collins, ONS.
- **Commercial use: unclear, leaning no without permission.** Redistributing derived data is not granted.
- **Action:** written request to the BoE; ask whether the third-party series need separate clearance.
- Suggested attribution: "Source: Bank of England, A millennium of macroeconomic data for the UK, v3.1."

### Robert Shiller — `ie_data.xls` (U.S. Stock Markets 1871–Present and CAPE Ratio)
- Used by `tools/build_history.py` for the S&P Composite real total return and US CPI, 1871–2009.
- https://shillerdata.com/ (older copy: http://www.econ.yale.edu/~shiller/data.htm).
- No licence is published. The site carries a disclaimer only ("The user of this information assumes the entire risk
  of any use made of the information provided herein…"). Underlying data come from S&P, Cowles and the BLS; whether
  S&P Dow Jones Indices asserts rights over these values is UNVERIFIED.
- **Commercial use: unclear.** Silence is not a licence.
- **Action:** ask Robert Shiller / RSBB-I, LLC for permission (no contact published; approach via Yale Economics).
- Customary attribution: "Robert J. Shiller, Irrational Exuberance data (shillerdata.com)."

### ONS — CPI index (2015=100), series D7BT, dataset MM23
- Used in `LiveMarketData.cs` for monthly UK inflation from 2010.
- Open Government Licence v3.0. ONS: "if you wish to use or re-use ONS material, whether commercially or privately,
  you may do so freely without a specific application for a licence … you must include a source accreditation to ONS"
  (https://www.ons.gov.uk/methodology/geography/licences).
- **Commercial use: yes, with attribution.**
- Attribution: "Source: Office for National Statistics licensed under the Open Government Licence v.3.0".

### Bank of England IADB — Bank Rate, series IUMABEDR
- Used in `LiveMarketData.cs` for cash returns from 2010.
- "Reproduction of data in the Database is subject to the terms of the UK Open Government Licence"
  (https://www.bankofengland.co.uk/legal). Some exchange-rate series are excluded; Bank Rate is not one of them.
- **Commercial use: yes, with attribution.**
- Suggested attribution: "Bank of England Bank Rate (IUMABEDR), licensed under the Open Government Licence v3.0,
  © the Governor and Company of the Bank of England."

## Data we may add

### ONS mortality rates (qx), principal projection, UK, 2024-based (released 15 May 2026) — now used
- Built into `backend/Finance.Api/SeedData/mortality_uk.csv` by `tools/build_mortality.py` (period tables, ages
  16–100, years 1981–2074). The CSV and the app's Explanation and Market data pages carry the ONS/OGL attribution.
- https://www.ons.gov.uk/peoplepopulationandcommunity/birthsdeathsandmarriages/lifeexpectancies/datasets/mortalityratesqxprincipalprojectionunitedkingdom
- File: `…/2024based/ukppp24qx.xlsx`. Sheets: males/females period qx and cohort qx, per 100,000, ages 0–100 (to 125
  on request from pop.info@ons.gov.uk).
- The cohort sheets start at year of birth 1981, so for people now aged about 50–70 the cohort rates are built from
  the period sheets: qx(age a, born b) = period[year b + a][age a].
- "© Crown copyright 2026. You may re-use this document/publication (not including logos) free of charge in any
  format or medium, under the terms of the Open Government Licence v3.0."
- **Commercial use: yes, with attribution.**

### Pensions UK / Loughborough University Retirement Living Standards (2026 update, 3 June 2026)
- https://www.retirementlivingstandards.org.uk/details. Yearly costs after tax, excluding rent/mortgage and care:

  | | One person | One person, London | Two people | Two people, London |
  |---|---|---|---|---|
  | Minimum | £13,900 | £14,600 | £22,500 | £24,100 |
  | Moderate | £32,700 | £34,000 | £45,400 | £47,000 |
  | Comfortable | £45,400 | £47,200 | £62,700 | £64,800 |

- Terms (https://www.retirementlivingstandards.org.uk/terms-of-use): free, non-transferable licence; figures and names
  must be used unchanged and kept current; describe them as "Pensions UK/Loughborough University Retirement Living
  Standards" and link to the site where practicable; personalised targets must be clearly "for illustration and
  guidance only and do not constitute financial advice"; the user indemnifies the licensors; terminable on 30 days'
  notice. Adjusted or derived values need written approval.
- **Commercial use: yes, under those conditions.**

### JST Macrohistory Database, release R6
- https://www.macrohistory.net/database/ — annual data for 18 countries from 1870.
- CC BY-NC-SA 4.0: "Commercial data providers are thus strictly forbidden to integrate all or parts of the dataset into
  their services and/or resell the data."
- **Commercial use: no** without permission (macrofinancelab@gmail.com). Cite Jordà et al. (2019) for returns data.

### Kenneth R. French Data Library
- https://mba.tuck.dartmouth.edu/pages/faculty/ken.french/data_library.html — developed and international returns in
  US dollars (index portfolios from 1975). Raw data from MSCI (1975–2006) and Bloomberg (2007 onwards).
- No licence published. **Commercial use: unclear**; MSCI and Bloomberg rights may also apply.

### Global share indices in GBP before 2010
- No free series whose licence allows commercial use was found.
- MSCI World: paid licence (MSCI "Contact sales"). FTSE All-World/World: paid licence (LSEG General-License form,
  +44 (0)20 7866 1810). Start dates UNVERIFIED.

## Research the simulator draws on

| Paper | Used in the simulator for |
|---|---|
| Bengen (1994) | The historical backtest and fixed real spending ("constant inflation-adjusted") |
| Cooley, Hubbard & Walz (1998) | Success rates by withdrawal rate and asset mix |
| Guyton & Klinger (2006) | Guyton-Klinger guardrails adjustment |
| Pfau & Kitces (2014); Kitces & Pfau (2015) | Rising equity glide path |
| Pfau (2010); Anarkulova, Cederburg & O'Doherty (2022); Anarkulova, Cederburg, O'Doherty & Sias (2025); Jordà et al. (2019) | Why US history flatters results; the share-returns adjustment |
| Milevsky & Robinson (2000); ONS mortality | Lifespan-weighted chance of running out |
| Crawford, Karjalainen & Sturrock (2022, IFS) | "UK average (IFS)" spending pattern |
| Blanchett (2014) | "US research (Blanchett)" spending pattern |
| Scott, Sharpe & Watson (2009) | Measures beyond the success rate (cuts, shortfall) |
| Waring & Siegel (2015) | Possible upgrade to the spend-down base |
| Kitces (2008); Jeske, ERN series | Starting valuation (CAPE) and long horizons (not peer-reviewed) |

### Citations
1. Bengen, W. P. (1994). "Determining Withdrawal Rates Using Historical Data." *Journal of Financial Planning* 7(4),
   171–180. On US history, 4% raised with inflation lasted at least 33 years in every period.
2. Cooley, P. L., Hubbard, C. M. & Walz, D. T. (1998). "Retirement Savings: Choosing a Withdrawal Rate That Is
   Sustainable." *AAII Journal* 20(2), 16–21.
3. Guyton, J. T. & Klinger, W. J. (2006). "Decision Rules and Maximum Initial Withdrawal Rates." *Journal of Financial
   Planning* 19(3) (pages UNVERIFIED). With guardrails, 5.2–5.6% starting rates were sustainable at 99% confidence
   over 40 years with 65%+ equities.
4. Pfau, W. D. & Kitces, M. E. (2014). "Reducing Retirement Risk with a Rising Equity Glide Path." *Journal of
   Financial Planning* 27(1), 38–45. SSRN 2324930.
5. Kitces, M. E. & Pfau, W. D. (2015). "Retirement Risk, Rising Equity Glide Paths, and Valuation-Based Asset
   Allocation." *Journal of Financial Planning*, March 2015 (volume/pages UNVERIFIED). SSRN 2497053.
6. Pfau, W. D. (2010). "An International Perspective on Safe Withdrawal Rates: The Demise of the 4 Percent Rule?"
   *Journal of Financial Planning* 23(12), 52–61. Across 17 countries, 4% was "safe" in only 4.
7. Anarkulova, A., Cederburg, S. & O'Doherty, M. S. (2022). "Stocks for the Long Run? Evidence from a Broad Sample of
   Developed Markets." *Journal of Financial Economics* 143(1), 409–433. doi:10.1016/j.jfineco.2021.06.040.
8. Anarkulova, A., Cederburg, S., O'Doherty, M. S. & Sias, R. W. (2025). "The Safe Withdrawal Rate: Evidence from a
   Broad Sample of Developed Markets." *Journal of Pension Economics and Finance* 24(3), 464–500.
   doi:10.1017/S1474747225000010. "A 65-year-old couple willing to bear a 5 percent chance of financial ruin can
   withdraw just 2.31 percent per year."
9. Jordà, Ò., Knoll, K., Kuvshinov, D., Schularick, M. & Taylor, A. M. (2019). "The Rate of Return on Everything,
   1870–2015." *Quarterly Journal of Economics* 134(3), 1225–1298. doi:10.1093/qje/qjz012.
10. Milevsky, M. A. & Robinson, C. (2000). "Self-Annuitization and Ruin in Retirement." *North American Actuarial
    Journal* 4(4), 112–124. doi:10.1080/10920277.2000.10595940.
11. Blanchett, D. (2014). "Exploring the Retirement Consumption Puzzle." *Journal of Financial Planning* 27(5), 34–42.
    Equation 1 (p. 39): ΔAS = 0.00008·Age² − 0.0125·Age − 0.0066·ln(ExpTar) + 0.546, the annual real change in
    spending, with ExpTar = after-tax spending in US dollars. US data (HRS/CAMS), adjusted up slightly for medical costs.
    Average real change from 60 to 90 was −0.96% a year.
12. Crawford, R., Karjalainen, H. & Sturrock, D. (2022). *How does spending change through retirement?* IFS Report
    R209. "On average, retirees' total household spending per person remains relatively constant in real terms through
    retirement, increasing slightly at ages up to around age 80 and remaining flat or falling thereafter."
13. Urzi Brancati, C., Beach, B., Franklin, B. & Jones, M. (2015). *Understanding retirement journeys: Expectations vs
    reality.* ILC-UK. (Cross-sectional household data; weaker evidence than IFS.)
14. Waring, M. B. & Siegel, L. B. (2015). "The Only Spending Rule Article You Will Ever Need." *Financial Analysts
    Journal* 71(1), 91–107. doi:10.2469/faj.v71.n1.2.
15. Scott, J. S., Sharpe, W. F. & Watson, J. G. (2009). "The 4% Rule—At What Price?" *Journal of Investment
    Management* 7(3) (pages UNVERIFIED). SSRN 1115023.
16. Kitces, M. E. (2008). "Resolving the Paradox – Is the Safe Withdrawal Rate Sometimes Too Safe?" *The Kitces
    Report*, May 2008. Not peer-reviewed.
17. Jeske, K. "The Safe Withdrawal Rate Series." Early Retirement Now (blog, from December 2016). Not peer-reviewed.

## How the research is applied (our own modelling choices)

These are our interpretations, not the authors'. Say so wherever they are shown to users.

- **UK average (IFS) pattern:** level spending power to age 80, then −1% a year. A simplification of the IFS finding;
  it deliberately ignores the slight rise before 80 and excludes care, which users add as an outgoing.
- **US research (Blanchett) pattern:** Blanchett's Equation 1 evaluated at $50,000 of spending for every year, compounded
  from the retirement age. The formula is US-based and depends on the spending level in dollars. His data cover ages
  60–90; for retirements before 60 (spending rises about 1–2% a year in the late 50s) and after 90 the formula is
  extrapolated.
- **Spending plan fitter:** finds the highest level that follows the chosen pattern and succeeds in the chosen share of
  historical start dates, then rounds the schedule to £100 steps. It is a historical backtest, not advice.
- **Lifespan:** a person is followed along their own generation (born this year minus current age) through the ONS
  period tables; years after 2074 use 2074's rates. Beyond age 100 (the table's limit) the yearly chance of dying is
  assumed to keep rising 5% a year, capped at 50% — our assumption, not ONS data, and only used when planning past 100.
  "Couple" means a man and a woman of the same age, and the money must last while either is alive. Results are given
  for someone alive at retirement. Each run-out is weighted by the chance of being alive then (the idea behind
  Milevsky & Robinson's lifetime ruin, applied to the historical paths rather than their formula).
- **Share returns adjustment:** lowers every year's share return by a fixed amount (0.5–2% a year) as a stress test.
  The size is the user's choice; the research shows non-US markets did worse but does not give one "correct" figure.
- **Fitting to a lifetime target:** "at most X% chance of running out while alive" mirrors how Anarkulova et al. (2025)
  frame their 5% ruin standard, but uses this simulator's (US-flavoured) history and ONS UK mortality, so our numbers
  will differ from theirs.

