import type { MarketSummary } from '../types'
import { fmt } from '../defaults'

export function Explanation({ summary, onClose }: { summary: MarketSummary | null; onClose: () => void }) {
  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal" role="dialog" aria-modal="true" aria-labelledby="explain-title" onClick={(e) => e.stopPropagation()}>
        <div className="modal-head">
          <h2 id="explain-title">How this works</h2>
          <button className="icon-btn" onClick={onClose} aria-label="Close">×</button>
        </div>
        <div className="modal-body prose">
          <p>
            Your plan is replayed as if you had retired at the start of <strong>every year in recorded market history</strong>
            {summary?.firstMonth && <> since {fmt.month(summary.firstMonth)}</>}. Each line on the chart is one of those
            start dates (January each year; you can switch to every month under Assumptions). If the plan would have survived the worst times in history — 1929, the 1970s, 2000, 2008 — it is
            more likely to survive whatever comes next.
          </p>
          <h3>Success rate</h3>
          <p>
            The percentage of start dates where your money lasted until the age of death (and left at least the amount you
            set in "Leave at least"). Only start dates with enough history to cover your <em>whole</em> retirement count.
            More recent start dates are still shown on the chart as dashed "partial" lines, but they are left out of the
            percentage because we don't know how they end yet.
          </p>
          <h3>Beyond the success rate</h3>
          <p>
            Two plans with the same success rate can feel very different to live through, so the results also show how
            often spending had to be cut by 10% or more from one year to the next (changes you planned, such as a new
            amount from an age or a pension starting, are not counted), how long you would live on other income alone if
            the money ran out, and how many years spending sat below your minimum.
          </p>
          <h3>Lifespan</h3>
          <p>
            Nobody knows their age of death, so a plan that lasts to 94 can still run out while you are alive, or you
            may not live to see the shortfall. Choose UK life tables under "Your pot" to weigh each run-out by the chance
            of still being alive at that age: the result is the chance of running out while alive (for a couple, while
            either of you is). It uses the Office for National Statistics' 2024-based projections for people your age,
            following your own generation through the tables. Source: Office for National Statistics licensed under the
            Open Government Licence v.3.0.
          </p>
          <h3>Real and nominal</h3>
          <p>
            <strong>Real</strong> shows everything in today's money. Each historical month's return is adjusted by that
            month's actual UK inflation, so the ups and downs of history are kept in the right order. <strong>Nominal</strong>{' '}
            shows the same results in future pounds, grown by your planned constant inflation rate from today: your
            current age if you have entered one, otherwise the day you retire.
          </p>
          <h3>What each month does</h3>
          <ol>
            <li>On each retirement anniversary, the spending strategy sets the year's spending.</li>
            <li>That month's spending, plus any regular outgoings and one-offs, less any pension or other income, is taken from the pot. If income is more than you spend, the surplus is invested. Income marked "Pay into the pot" (an inheritance or house sale, say) is always invested, never spent directly. Deposits and surplus income count as new capital: with a constant inflation-adjusted base, spending rises by your initial rate on them, and they don't count as growth for the ratchet or falls from the peak. If you set a new rate from a later age, spending restarts at that rate on the whole pot (including money added that year) and the guardrails and ratchet are measured from there.</li>
            <li>If there isn't enough money to pay it, the plan has run out at that age.</li>
            <li>Shares, bonds and cash grow or shrink by that historical month's return.</li>
            <li>Fees are deducted, then the investment strategy rebalances if it's due.</li>
          </ol>
          <h3>Pensions and other income</h3>
          <p>
            Your spending strategy sets what you live on. Regular income such as the State Pension pays part of that, so
            the pot only has to provide the rest. Strategies defined as a percentage of the pot (constant percentage,
            floor and ceiling, spend down) and fixed amounts set what the pot pays, and your other income is added on top. Guardrail
            strategies judge the withdrawal rate on what actually comes out of the pot. If the pot runs out, you live on
            your other income alone.
          </p>
          <h3>Last year's return</h3>
          <p>
            Rules that react to markets (skipping an inflation rise, custom rules on the trailing return) look at the
            portfolio's return over the previous 12 months, expressed in ordinary (nominal) terms using your planned
            inflation rate.
          </p>
          <h3>Data and its limits</h3>
          <ul>
            {summary?.sources.map((s, i) => (
              <li key={i}><strong>{s.series}</strong> ({s.period}): {s.source}</li>
            ))}
          </ul>
          <p>
            Before 2010, "global shares" are approximated by US shares converted to pounds, and bonds by UK government
            bonds. The US was one of the best-performing share markets of the last 150 years; across other developed
            markets returns were lower, and so were safe withdrawal rates (Pfau 2010; Anarkulova, Cederburg, O'Doherty
            &amp; Sias 2025). "Share returns" under Assumptions lowers every year's share return to test how much your
            plan relies on US-style history. Tax is not modelled. The server fetches new months automatically, so results can change a little as
            recent start dates become complete.
          </p>
          <p className="muted">
            For illustrative purposes only. Past performance is not a guide to future returns. This is not financial advice.
          </p>
        </div>
      </div>
    </div>
  )
}
