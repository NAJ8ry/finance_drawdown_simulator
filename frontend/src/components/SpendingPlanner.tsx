import { useEffect, useMemo, useRef, useState } from 'react'
import { api, type SpendingPatternInfo } from '../api'
import { fmt, otherIncomeAt } from '../defaults'
import type { SimulationInput, SimulationResult, SpendingParameters, SpendingStep } from '../types'
import { NumberField, SelectField } from './Fields'

const TARGETS = [90, 95, 99]
/** Sizes for the − / + buttons that scale every amount in the draft. */
const SCALE_STEPS = [0.01, 0.05, 0.1]
/** With life tables: the highest acceptable chance of running out while alive. */
const RUIN_TARGETS = [10, 5, 2, 1]

/** The draft as fixed amounts spending, with adjustments off so it runs exactly as drafted. */
function draftSpending(base: SpendingParameters, draft: SpendingStep[]): SpendingParameters {
  return {
    ...base,
    type: 'FixedAmounts',
    fixedAmount: draft[0]?.amount ?? 0,
    amountSteps: draft.slice(1),
    useGuytonKlinger: false,
    useCustomRules: false,
    useRatchet: false,
    useInflationSkip: false,
    useFloorCeiling: false,
  }
}

/** Keeps the first step at retirement, the rest in age order inside the retirement, one per age. */
function tidy(input: SimulationInput, draft: SpendingStep[]): SpendingStep[] {
  const [first, ...rest] = draft
  const seen = new Set<number>([input.retirementAge])
  const later = rest
    .filter((s) => s.age > input.retirementAge && s.age < input.deathAge && !seen.has(s.age) && seen.add(s.age))
    .sort((a, b) => a.age - b.age)
  return [{ age: input.retirementAge, amount: first?.amount ?? 0 }, ...later]
}

/**
 * Spending plan tab: draft a fixed amounts schedule (from research on how spending changes with age, from your
 * current list, or by hand), see what it means year by year with your pensions, check it against history, then put
 * it into the Fixed amounts list.
 */
export function SpendingPlanner({ input, onApply }: { input: SimulationInput; onApply: (spending: SpendingParameters) => void }) {
  const [patterns, setPatterns] = useState<SpendingPatternInfo[]>([])
  const [pattern, setPattern] = useState('')
  const [target, setTarget] = useState(95)
  const [maxRuin, setMaxRuin] = useState(5)
  const lifetime = input.lifeTable !== 'None'
  const [draft, setDraft] = useState<SpendingStep[] | null>(null)
  const [drafting, setDrafting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [check, setCheck] = useState<SimulationResult | null>(null)
  const [checking, setChecking] = useState(false)
  const [previous, setPrevious] = useState<SpendingParameters | null>(null)
  const [scaleStep, setScaleStep] = useState(0.05)

  useEffect(() => {
    api.spendingPatterns()
      .then((list) => {
        setPatterns(list)
        setPattern((p) => p || list[0]?.id || '')
      })
      .catch((e: Error) => setError(e.message))
  }, [])

  const current = input.spending.type === 'FixedAmounts'
    ? [{ age: input.retirementAge, amount: input.spending.fixedAmount }, ...(input.spending.amountSteps ?? [])]
    : null

  const draftFromResearch = async () => {
    setDrafting(true)
    setError(null)
    try {
      const fit = await api.fitSpending(input, pattern, target, lifetime ? maxRuin : null)
      if (!fit.feasible) {
        setError(lifetime
          ? `Even taking nothing from the pot, your outgoings and one-offs give more than a ${maxRuin}% chance of running out while alive. Look at the outgoings (care costs, for example) or the investment mix first.`
          : `Even taking nothing from the pot, your outgoings and one-offs run it out in more than ${100 - target}% of start dates. Look at the outgoings (care costs, for example) or the investment mix first.`)
        return
      }
      setDraft(tidy(input, [{ age: input.retirementAge, amount: fit.spending.fixedAmount }, ...fit.spending.amountSteps]))
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setDrafting(false)
    }
  }

  // Check the draft against history whenever it changes (debounced)
  const plan = useMemo(() => (draft ? { ...input, spending: draftSpending(input.spending, draft) } : null), [input, draft])
  useEffect(() => {
    if (!plan) return
    const ctrl = new AbortController()
    const t = setTimeout(() => {
      setChecking(true)
      api.simulate(plan, ctrl.signal)
        .then(setCheck)
        .catch((e: Error) => { if (e.name !== 'AbortError') setError(e.message) })
        .finally(() => setChecking(false))
    }, 400)
    return () => { clearTimeout(t); ctrl.abort() }
  }, [plan])

  const update = (i: number, patch: Partial<SpendingStep>) => draft && setDraft(draft.map((s, j) => (j === i ? { ...s, ...patch } : s)))
  /** Raise or lower every amount by the same share, rounded to £100; the history check reruns by itself. */
  const scale = (direction: 1 | -1) =>
    draft && setDraft(draft.map((s) => ({ ...s, amount: Math.max(0, Math.round((s.amount * (1 + direction * scaleStep)) / 100) * 100) })))
  const apply = () => {
    if (!plan) return
    setPrevious(input.spending)
    onApply(plan.spending)
  }
  const inUse = !!current && !!draft && JSON.stringify(current) === JSON.stringify(draft) && input.spending.type === 'FixedAmounts'
  const chosen = patterns.find((p) => p.id === pattern)

  return (
    <div className="planner">
      <div className="card planner-card">
        <h3>Draft a spending plan</h3>
        <p className="muted small">
          Start from research on how people's spending changes through retirement, sized to the most your money would
          have supported, or from your current list. Then adjust the amounts below and check them against history.
        </p>
        <div className="planner-controls">
          <SelectField label="Spending pattern" value={pattern}
            options={patterns.map((p) => ({ value: p.id, label: p.label }))} onChange={setPattern} />
          {lifetime ? (
            <SelectField label="Chance of running out while alive" value={String(maxRuin)}
              options={RUIN_TARGETS.map((t) => ({ value: String(t), label: `At most ${t}%` }))} onChange={(v) => setMaxRuin(Number(v))} />
          ) : (
            <SelectField label="Must last in at least" value={String(target)}
              options={TARGETS.map((t) => ({ value: String(t), label: `${t}% of start dates` }))} onChange={(v) => setTarget(Number(v))} />
          )}
          <button className="btn primary" disabled={drafting || !pattern} onClick={draftFromResearch}>
            {drafting ? 'Working it out…' : 'Draft from research'}
          </button>
          {current && (
            <button className="btn" onClick={() => setDraft(tidy(input, current))}>Start from my Fixed amounts</button>
          )}
          {!draft && (
            <button className="btn" onClick={() => setDraft([{ age: input.retirementAge, amount: 20_000 }])}>Start from scratch</button>
          )}
        </div>
        {chosen && (
          <p className="note">
            {chosen.description}
            {chosen.source && <><br /><span className="muted">Based on: {chosen.source}</span></>}
          </p>
        )}
        {error && <p className="error">{error}</p>}
      </div>

      {draft && (
        <>
          <div className="card planner-card">
            <div className="planner-head">
              <h3>Your draft <span className="muted small">(per year, today's money)</span></h3>
              <div className="planner-scale" role="group" aria-label="Change all amounts">
                <button className="icon-btn round" title={`Lower every amount by ${fmt.pctTrim(scaleStep)}`}
                  aria-label={`Lower every amount by ${fmt.pctTrim(scaleStep)}`} onClick={() => scale(-1)}>−</button>
                <select value={scaleStep} onChange={(e) => setScaleStep(Number(e.target.value))} aria-label="Step size">
                  {SCALE_STEPS.map((s) => <option key={s} value={s}>{fmt.pctTrim(s)}</option>)}
                </select>
                <button className="icon-btn round" title={`Raise every amount by ${fmt.pctTrim(scaleStep)}`}
                  aria-label={`Raise every amount by ${fmt.pctTrim(scaleStep)}`} onClick={() => scale(1)}>+</button>
                <span className="muted small">all amounts</span>
              </div>
              <div className="planner-check">
                {checking && <span className="spinner" aria-label="Checking" />}
                {check && (lifetime && check.lifetimeRuinRate != null ? (
                  <span className={check.lifetimeRuinRate <= maxRuin ? 'good' : 'bad'}>
                    <strong>{fmt.pct(check.lifetimeRuinRate / 100, 1)}</strong> chance of running out while alive
                  </span>
                ) : (
                  <span className={check.successRate != null && check.successRate >= target ? 'good' : 'bad'}>
                    Lasted in <strong>{fmt.pct((check.successRate ?? 0) / 100, 1)}</strong> of start dates
                  </span>
                ))}
              </div>
            </div>
            <DraftChart input={input} draft={draft} />
            <table className="planner-table">
              <thead>
                <tr><th>Ages</th><th>From the pot</th><th>Other income</th><th>You live on</th><th /></tr>
              </thead>
              <tbody>
                {draft.map((s, i) => {
                  const to = (draft[i + 1]?.age ?? input.deathAge) - 1
                  const incomes = Array.from({ length: to - s.age + 1 }, (_, k) => otherIncomeAt(input, s.age + k))
                  const lo = Math.min(...incomes)
                  const hi = Math.max(...incomes)
                  const range = (a: number, b: number) => (Math.round(a) === Math.round(b) ? fmt.gbp(a) : `${fmt.gbp(a)}–${fmt.gbp(b)}`)
                  return (
                    <tr key={i}>
                      <td className="planner-ages">
                        {i === 0 ? (
                          <span>{s.age}</span>
                        ) : (
                          <NumberField label="" value={s.age} min={input.retirementAge + 1} max={input.deathAge - 1} step={1}
                            onChange={(v) => update(i, { age: Math.round(v ?? s.age) })} />
                        )}
                        <span className="muted">– {to}</span>
                      </td>
                      <td>
                        <NumberField label="" prefix="£" value={s.amount} min={0} step={500} onChange={(v) => update(i, { amount: v ?? 0 })} />
                      </td>
                      <td>{range(lo, hi)}</td>
                      <td><strong>{range(s.amount + lo, s.amount + hi)}</strong></td>
                      <td>
                        {i > 0 && (
                          <button className="icon-btn" title="Remove" aria-label="Remove" onClick={() => setDraft(draft.filter((_, j) => j !== i))}>×</button>
                        )}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
            <div className="planner-actions">
              <button className="btn small"
                onClick={() => {
                  const last = draft[draft.length - 1]
                  const age = Math.min(last.age + 5, input.deathAge - 1)
                  if (age > last.age) setDraft([...draft, { age, amount: last.amount }])
                }}>
                + Add a step
              </button>
              <button className="btn small" onClick={() => setDraft(tidy(input, draft))} title="Sort the steps by age and drop duplicates">Tidy</button>
            </div>
          </div>

          {check && (
            <div className="card planner-card">
              <h3>How the draft did in history</h3>
              <div className="planner-stats">
                <Stat label="Lasted" value={fmt.pct((check.successRate ?? 0) / 100, 1)} sub={`of ${check.completeCount} start dates`} />
                <Stat label="Typically left at death" value={fmt.gbp(check.medianEndBalance)} sub={`age ${input.deathAge}`} />
                <Stat label="1 in 10 left less than" value={fmt.gbp(check.p10EndBalance)} />
                <Stat label="Earliest run-out" value={check.worstDepletionAge != null ? String(Math.floor(check.worstDepletionAge)) : 'Never'} />
                {check.lifetimeRuinRate != null && (
                  <Stat label="Chance of running out while alive" value={fmt.pct(check.lifetimeRuinRate / 100, 1)} sub="ONS life tables" />
                )}
              </div>
              <p className="muted small">
                Fixed amounts are taken whatever the markets do, so there are no cuts; the risk shows up as the pot running
                out instead. A historical backtest, not financial advice. Tax is not included.
              </p>
              <div className="planner-actions">
                <button className="btn primary" onClick={apply} disabled={inUse}>
                  {inUse ? 'This is your current Fixed amounts list' : 'Put into my Fixed amounts list'}
                </button>
                {previous && (
                  <button className="btn" onClick={() => { onApply(previous); setPrevious(null) }}>Undo</button>
                )}
              </div>
              <p className="muted small">
                This sets your spending strategy to Fixed amounts with these steps and switches its adjustments off, so
                the main results match what you see here.
              </p>
            </div>
          )}
        </>
      )}
    </div>
  )
}

function Stat({ label, value, sub }: { label: string; value: string; sub?: string }) {
  return (
    <div className="kf">
      <div className="kf-value">{value}</div>
      <div className="kf-label">{label}</div>
      {sub && <div className="muted small">{sub}</div>}
    </div>
  )
}

const H = 220
const M = { top: 12, right: 12, bottom: 28, left: 56 }

/** Stacked bars by age: what comes from the pot, and pensions and other income on top. */
function DraftChart({ input, draft }: { input: SimulationInput; draft: SpendingStep[] }) {
  const ref = useRef<HTMLDivElement>(null)
  const [width, setWidth] = useState(640)
  const [hover, setHover] = useState<number | null>(null)
  useEffect(() => {
    const el = ref.current
    if (!el) return
    const ro = new ResizeObserver(([e]) => setWidth(Math.max(280, Math.floor(e.contentRect.width))))
    ro.observe(el)
    return () => ro.disconnect()
  }, [])

  const years = input.deathAge - input.retirementAge
  const rows = Array.from({ length: years }, (_, k) => {
    const age = input.retirementAge + k
    const step = [...draft].reverse().find((s) => s.age <= age) ?? draft[0]
    return { age, pot: step.amount, income: otherIncomeAt(input, age) }
  })
  const max = Math.max(1, ...rows.map((r) => r.pot + r.income))
  const mag = Math.pow(10, Math.floor(Math.log10(max / 4)))
  const tick = [1, 2, 2.5, 5, 10].map((s) => s * mag).find((s) => max / s <= 5) ?? mag
  const top = Math.ceil(max / tick) * tick
  const innerW = width - M.left - M.right
  const innerH = H - M.top - M.bottom
  const band = innerW / years
  const barW = Math.max(2, band - 2)
  const y = (v: number) => M.top + innerH - (v / top) * innerH
  const every = band < 14 ? 5 : band < 24 ? 2 : 1
  const h = hover != null ? rows[hover] : null

  return (
    <div className="draft-chart" ref={ref}>
      <div className="draft-legend">
        <span><i className="swatch-pot" /> From the pot</span>
        <span><i className="swatch-income" /> Pensions and other income</span>
      </div>
      <svg width={width} height={H} role="img" aria-label="Planned spending by age: from the pot and from other income"
        onMouseLeave={() => setHover(null)}>
        {Array.from({ length: Math.round(top / tick) + 1 }, (_, i) => i * tick).map((v) => (
          <g key={v}>
            <line x1={M.left} x2={M.left + innerW} y1={y(v)} y2={y(v)} className="grid" />
            <text x={M.left - 6} y={y(v)} className="tick" textAnchor="end" dominantBaseline="middle">{fmt.gbpShort(v)}</text>
          </g>
        ))}
        {rows.map((r, k) => {
          const x = M.left + k * band + (band - barW) / 2
          // 2px surface gap between the two stacked segments
          const potTop = y(r.pot)
          const incomeTop = y(r.pot + r.income)
          return (
            <g key={r.age} onMouseEnter={() => setHover(k)}>
              <rect x={M.left + k * band} y={M.top} width={band} height={innerH} fill="transparent" />
              {r.pot > 0 && <rect x={x} y={potTop} width={barW} height={Math.max(0, y(0) - potTop)} className="bar-pot" rx={1.5} />}
              {r.income > 0 && (
                <rect x={x} y={incomeTop} width={barW} height={Math.max(0, potTop - incomeTop - 2)} className="bar-income" rx={1.5} />
              )}
              {(k % every === 0) && (
                <text x={M.left + k * band + band / 2} y={H - 10} className="tick" textAnchor="middle">{r.age}</text>
              )}
            </g>
          )
        })}
        {hover != null && (
          <rect x={M.left + hover * band} y={M.top} width={band} height={innerH} className="bar-hover" />
        )}
      </svg>
      {h && (
        <div className="tooltip" style={{ left: Math.min(M.left + hover! * band + band + 8, width - 200), top: 24, width: 190 }}>
          <strong>Age {h.age}</strong>
          <div className="ys-row"><span>From the pot</span><span>{fmt.gbp(h.pot)}</span></div>
          <div className="ys-row"><span>Other income</span><span>{fmt.gbp(h.income)}</span></div>
          <div className="ys-row"><span>You live on</span><span><strong>{fmt.gbp(h.pot + h.income)}</strong></span></div>
        </div>
      )}
    </div>
  )
}
