import { fmt, investmentTitle, spendingTitle, startUnit } from '../defaults'
import type { SimulationInput, SimulationResult } from '../types'
import type { LegendState } from './PathChart'

export function Headline({ result, input }: { result: SimulationResult; input: SimulationInput }) {
  const rate = result.successRate
  const tone = rate == null ? '' : rate >= 90 ? 'good' : rate >= 75 ? 'warn' : 'bad'
  return (
    <div className={`card headline ${tone}`}>
      <div className="big">{rate == null ? '–' : `${rate.toFixed(rate >= 99.95 || rate < 10 ? 0 : 1)}%`}</div>
      <div className="headline-text">
        {rate == null ? (
          <>No start {startUnit(input)} has enough history to cover {input.deathAge - input.retirementAge} years.</>
        ) : (
          <>
            of historical start {startUnit(input)}s lasted to age {input.deathAge}
            <span className="muted small">
              {' '}({result.successCount.toLocaleString()} of {result.completeCount.toLocaleString()} complete paths
              {input.legacyTarget > 0 && <>, leaving at least {fmt.gbp(input.legacyTarget)}</>})
            </span>
          </>
        )}
      </div>
    </div>
  )
}

export function AssumptionCards({ input }: { input: SimulationInput }) {
  const a = input.allocation
  const showRate = input.spending.type !== 'RemainingLife'
  const mix = [
    a.equity > 0 && `${Math.round(a.equity * 100)}% Global shares`,
    a.bond > 0 && `${Math.round(a.bond * 100)}% Bonds`,
    a.cash > 0 && `${Math.round(a.cash * 100)}% Cash`,
  ].filter(Boolean).join(' & ')
  return (
    <>
      {showRate && (
        <div className="card stat">
          <div className="stat-value">{fmt.pctTrim(input.spending.initialRate)}</div>
          <div className="stat-label">{input.flows.length > 0 ? 'Year-one spending rate' : 'Starting withdrawal rate'}</div>
          <div className="muted small">{fmt.gbp(input.startingBalance * input.spending.initialRate)} a year</div>
        </div>
      )}
      <div className="card assumptions">
        <div className="assumptions-title">Portfolio Assumptions</div>
        <ul>
          <li>{input.investment.type === 'DecliningGlidePath' || input.investment.type === 'RisingGlidePath'
            ? `Shares ${Math.round(input.investment.startEquity * 100)}% → ${Math.round(input.investment.endEquity * 100)}%`
            : mix}</li>
          <li>{fmt.pctTrim(input.feeRate)} fees · {fmt.pctTrim(input.inflationRate)} inflation</li>
          <li>No tax · {investmentTitle(input.investment)}</li>
        </ul>
      </div>
      {input.flows.length > 0 && (
        <div className="card assumptions">
          <div className="assumptions-title">Income &amp; outgoings</div>
          <ul>
            {input.flows.map((f, i) => (
              <li key={i} className={f.kind === 'Income' ? '' : 'bad'}>
                {f.kind === 'Income' ? '+' : '−'}{fmt.gbp(f.annualAmount)}/yr {f.label || (f.kind === 'Income' ? 'income' : 'outgoing')}{' '}
                <span className="muted">
                  {f.endAge ? `${f.startAge}–${f.endAge}` : `from ${f.startAge}`}{f.inflationLinked ? '' : ', fixed'}
                </span>
              </li>
            ))}
          </ul>
        </div>
      )}
    </>
  )
}

export function KeyFigures({ result, input, onSelect }: {
  result: SimulationResult
  input: SimulationInput
  onSelect: (index: number) => void
}) {
  // The chart draws one line per year, but the figures use every start month, so name the month responsible
  let worst: number | null = null
  result.paths.forEach((p, i) => {
    if (p.failed && (worst == null || (p.failAge ?? 0) < (result.paths[worst].failAge ?? 0))) worst = i
  })
  const failedCount = result.paths.filter((p) => p.failed).length

  const items: { label: string; value: string; sub?: string; action?: () => void }[] = [
    { label: 'Median balance at death', value: fmt.gbp(result.medianEndBalance), sub: "today's money" },
    { label: '1 in 10 paths end below', value: fmt.gbp(result.p10EndBalance), sub: 'at death' },
    worst == null
      ? { label: 'Earliest run-out age', value: 'Never' }
      : {
          label: 'Earliest run-out age',
          value: String(Math.floor(result.paths[worst].failAge ?? 0)),
          sub: `if retired ${fmt.month(result.paths[worst].start)}${result.paths[worst].partial ? ' (partial)' : ''} · ${failedCount} of ${result.paths.length} start ${startUnit(input)}s ran out · show on chart`,
          action: () => onSelect(worst!),
        },
    { label: 'Median average spending', value: fmt.gbp(result.medianAverageSpending), sub: 'per year' },
    { label: 'Lowest yearly spending', value: fmt.gbp(result.minimumSpending) },
    { label: 'Median worst fall', value: fmt.pct(result.medianMaxDrawdown, 0), sub: 'peak to trough' },
  ]
  if (input.spendingFloor)
    items.push({ label: 'Dropped below minimum income', value: fmt.pct((result.belowFloorRate ?? 0) / 100, 0), sub: 'of paths' })
  if (result.partialCount > 0)
    items.push({
      label: `Recent start ${startUnit(input)}s (partial)`,
      value: `${result.partialCount}`,
      sub: result.partialFailedCount > 0 ? `${result.partialFailedCount} already ran out` : 'none have run out yet',
    })
  return (
    <div className="key-figures">
      {items.map(({ label, value, sub, action }) =>
        action ? (
          <button className="kf clickable" key={label} onClick={action} title="Show this start date on the chart">
            <div className="kf-value">{value}</div>
            <div className="kf-label">{label}</div>
            {sub && <div className="muted small">{sub}</div>}
          </button>
        ) : (
          <div className="kf" key={label}>
            <div className="kf-value">{value}</div>
            <div className="kf-label">{label}</div>
            {sub && <div className="muted small">{sub}</div>}
          </div>
        ),
      )}
    </div>
  )
}

const legendItems: { key: keyof LegendState; label: string; swatch: string }[] = [
  { key: 'best', label: 'Best case', swatch: 'line best' },
  { key: 'median', label: 'Median', swatch: 'line median' },
  { key: 'worst', label: 'Worst case', swatch: 'line worst' },
  { key: 'likely', label: 'Likely', swatch: 'dot likely' },
  { key: 'lessLikely', label: 'Less likely', swatch: 'dot less' },
  { key: 'rare', label: 'Rare', swatch: 'dot rare' },
  { key: 'calendar', label: 'Calendar year', swatch: 'line hover' },
  { key: 'oneOffs', label: 'One-offs / goals', swatch: 'dot oneoff' },
  { key: 'partial', label: 'Recent (partial)', swatch: 'line partial' },
]

export function Legend({ legend, onChange }: { legend: LegendState; onChange: (l: LegendState) => void }) {
  return (
    <div className="legend">
      {legendItems.map((i) => (
        <button key={i.key} className={`legend-item${legend[i.key] ? ' on' : ''}`} aria-pressed={legend[i.key]}
          onClick={() => onChange({ ...legend, [i.key]: !legend[i.key] })}>
          <span className={`swatch ${i.swatch}`} />
          {i.label}
        </button>
      ))}
    </div>
  )
}

export function Tables({ result, input, onSelect }: { result: SimulationResult; input: SimulationInput; onSelect: (i: number) => void }) {
  return (
    <div className="tables">
      <div className="card table-card">
        <h3>Balance by age <span className="muted small">(today's money, complete paths)</span></h3>
        <table>
          <thead>
            <tr><th>Age</th><th>10th</th><th>25th</th><th>Median</th><th>75th</th><th>90th</th></tr>
          </thead>
          <tbody>
            {result.ageTable.map((b) => (
              <tr key={b.age}>
                <td>{b.age}</td>
                <td>{fmt.gbp(b.p10)}</td>
                <td>{fmt.gbp(b.p25)}</td>
                <td><strong>{fmt.gbp(b.p50)}</strong></td>
                <td>{fmt.gbp(b.p75)}</td>
                <td>{fmt.gbp(b.p90)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <div className="card table-card">
        <h3>Worst start {startUnit(input)}s</h3>
        <table>
          <thead>
            <tr><th>Retired</th><th>Outcome</th><th>Lowest balance</th><th>At age {input.deathAge}</th></tr>
          </thead>
          <tbody>
            {result.worstStarts.map((i) => {
              const p = result.paths[i]
              return (
                <tr key={i} className="clickable" onClick={() => onSelect(i)} title="Show on chart">
                  <td>{fmt.month(p.start)}{p.partial && <span className="muted small"> (partial)</span>}</td>
                  <td className={p.failed ? 'bad' : ''}>{p.failed ? `Ran out at ${Math.floor(p.failAge ?? 0)}` : 'Lasted'}</td>
                  <td>{fmt.gbp(p.minBalance)}</td>
                  <td>{p.partial ? '–' : fmt.gbp(p.endBalance)}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </div>
  )
}

export function strategyTitle(input: SimulationInput) {
  return spendingTitle(input.spending)
}
