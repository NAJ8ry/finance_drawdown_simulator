import { activeFlows, fmt, investmentTitle, spendingTitle, startUnit } from '../defaults'
import type { SimulationInput, SimulationResult } from '../types'
import type { LegendState } from './PathChart'

const LIFE_TABLE_WHO: Record<string, string> = { Male: 'a man', Female: 'a woman', Couple: 'a couple, while either is alive' }

export function Headline({ result, input }: { result: SimulationResult; input: SimulationInput }) {
  const ruin = result.lifetimeRuinRate
  if (ruin != null) {
    // With life tables the chance of running out while alive is the main answer; the success rate becomes the detail
    const tone = ruin <= 5 ? 'good' : ruin <= 15 ? 'warn' : 'bad'
    return (
      <div className={`card headline ${tone}`}>
        <div className="big">{`${ruin.toFixed(ruin < 10 ? 1 : 0)}%`}</div>
        <div className="headline-text">
          chance of running out of money while alive
          <span className="muted small">
            {' '}(UK life tables, {LIFE_TABLE_WHO[input.lifeTable]}) · lasted to {input.deathAge} in{' '}
            {result.successRate == null ? '–' : `${result.successRate.toFixed(0)}%`} of start {startUnit(input)}s ·{' '}
            {fmt.pct((result.outliveHorizonRate ?? 0) / 100, 0)} chance of living past {input.deathAge}
          </span>
        </div>
      </div>
    )
  }
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
  const flows = activeFlows(input)
  const mix = [
    a.equity > 0 && `${Math.round(a.equity * 100)}% Global shares`,
    a.bond > 0 && `${Math.round(a.bond * 100)}% Bonds`,
    a.cash > 0 && `${Math.round(a.cash * 100)}% Cash`,
  ].filter(Boolean).join(' & ')
  return (
    <>
      {input.spending.type === 'FixedAmounts' ? (
        <div className="card stat">
          <div className="stat-value">{fmt.gbp(input.spending.fixedAmount)}</div>
          <div className="stat-label">Taken from the pot a year</div>
          <div className="muted small">
            {input.spending.amountSteps?.length
              ? input.spending.amountSteps.map((c) => `${fmt.gbp(c.amount)} from ${c.age}`).join(', ')
              : 'Same every year'}
          </div>
        </div>
      ) : showRate && (
        <div className="card stat">
          <div className="stat-value">{fmt.pctTrim(input.spending.initialRate)}</div>
          <div className="stat-label">{flows.length > 0 ? 'Year-one spending rate' : 'Starting withdrawal rate'}</div>
          <div className="muted small">{fmt.gbp(input.startingBalance * input.spending.initialRate)} a year</div>
        </div>
      )}
      <div className="card assumptions">
        <div className="assumptions-title">Portfolio assumptions</div>
        <ul>
          <li>{input.investment.type === 'DecliningGlidePath' || input.investment.type === 'RisingGlidePath'
            ? `Shares ${Math.round(input.investment.startEquity * 100)}% → ${Math.round(input.investment.endEquity * 100)}%`
            : mix}</li>
          <li>{fmt.pctTrim(input.feeRate)} fees · {fmt.pctTrim(input.inflationRate)} inflation</li>
          {input.equityReturnAdjustment ? <li>Share returns {fmt.pctTrim(-input.equityReturnAdjustment)} a year below history</li> : null}
          <li>No tax · {investmentTitle(input.investment)}</li>
        </ul>
      </div>
      {flows.length > 0 && (
        <div className="card assumptions">
          <div className="assumptions-title">Income &amp; outgoings</div>
          <ul>
            {flows.map((f, i) => (
              <li key={i} className={f.kind === 'Income' ? '' : 'bad'}>
                {f.kind === 'Income' ? '+' : '−'}{fmt.gbp(f.annualAmount)}/yr {f.label || (f.kind === 'Income' ? 'income' : 'outgoing')}{' '}
                <span className="muted">
                  {f.endAge ? `${f.startAge}–${f.endAge}` : `from ${f.startAge}`}{f.inflationLinked ? '' : ', fixed'}{f.kind === 'Income' && f.intoPot ? ', into the pot' : ''}
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
    ...(result.outliveHorizonRate != null
      ? [{
          label: `Chance of living past ${input.deathAge}`,
          value: fmt.pct(result.outliveHorizonRate / 100, 0),
          sub: result.outliveHorizonRate > 5 ? 'consider planning to a later age' : 'ONS life tables',
        }]
      : []),
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
  if (result.cutRate != null)
    items.push({
      label: 'Spending cut 10%+ in a year',
      value: fmt.pct(result.cutRate / 100, 0),
      sub: result.cutRate > 0 ? `of paths · biggest cut ${fmt.pct(result.worstCut, 0)} · planned changes not counted` : 'of paths · planned changes not counted',
    })
  if (result.maxYearsWithoutPot != null)
    items.push({
      label: 'If it runs out: years on other income only',
      value: `${Math.round(result.medianYearsWithoutPot ?? 0)}`,
      sub: `typical · up to ${Math.round(result.maxYearsWithoutPot)} years`,
    })
  if (input.spendingFloor)
    items.push({
      label: 'Dropped below minimum income',
      value: fmt.pct((result.belowFloorRate ?? 0) / 100, 0),
      sub: `of paths${result.p90YearsBelowFloor ? ` · 1 in 10 spend ${Math.round(result.p90YearsBelowFloor)}+ years below it` : ''}`,
    })
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
  { key: 'paths', label: 'All start dates', swatch: 'line paths' },
  { key: 'best', label: 'Best case', swatch: 'line best' },
  { key: 'median', label: 'Median', swatch: 'line median' },
  { key: 'worst', label: 'Worst case', swatch: 'line worst' },
  { key: 'likely', label: 'Likely', swatch: 'dot likely' },
  { key: 'lessLikely', label: 'Less likely', swatch: 'dot less' },
  { key: 'rare', label: 'Rare', swatch: 'dot rare' },
  { key: 'calendar', label: 'Highlight line on hover', swatch: 'line hover' },
  { key: 'oneOffs', label: 'One-offs, income & outgoings', swatch: 'markers' },
  { key: 'partial', label: 'Recent (partial)', swatch: 'line partial' },
]

/** The chart's markers: one-off (diamond), income starting (up triangle), outgoing starting (down triangle). */
function MarkerSwatch() {
  return (
    <svg className="swatch-markers" width="38" height="12" viewBox="0 0 38 12" aria-hidden="true">
      <path d="M6,0 L12,6 L6,12 L0,6Z" className="oneoff-out" />
      <path d="M13,11 L25,11 L19,1Z" className="oneoff-in" />
      <path d="M26,1 L38,1 L32,11Z" className="oneoff-out" />
    </svg>
  )
}

/** The spending chart's legend only offers one-offs; everything else is fixed (lines and hover on, ranges off). */
export const spendingHiddenLegend: (keyof LegendState)[] =
  ['paths', 'calendar', 'best', 'median', 'worst', 'likely', 'lessLikely', 'rare', 'partial']

export function Legend({ legend, onChange, hide = [] }: {
  legend: LegendState
  onChange: (l: LegendState) => void
  /** Items to leave out of this chart's legend. */
  hide?: (keyof LegendState)[]
}) {
  return (
    <div className="legend">
      {legendItems.filter((i) => !hide.includes(i.key)).map((i) => {
        // Highlighting picks one of the start-date lines, so it has nothing to act on while they're hidden
        const disabled = i.key === 'calendar' && !legend.paths
        return (
          <button key={i.key} className={`legend-item${legend[i.key] ? ' on' : ''}`} aria-pressed={legend[i.key]}
            disabled={disabled} title={disabled ? 'Show "All start dates" to highlight a line' : undefined}
            onClick={() => {
              const next = { ...legend, [i.key]: !legend[i.key] }
              // Hiding the lines with no ranges showing would leave an empty chart, so show the ranges
              if (i.key === 'paths' && !next.paths && !next.likely && !next.lessLikely && !next.rare)
                Object.assign(next, { likely: true, lessLikely: true, rare: true })
              onChange(next)
            }}>
            {i.swatch === 'markers' ? <MarkerSwatch /> : <span className={`swatch ${i.swatch}`} />}
            {i.label}
          </button>
        )
      })}
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
