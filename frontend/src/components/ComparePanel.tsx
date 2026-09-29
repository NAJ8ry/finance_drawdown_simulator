import { useState } from 'react'
import { api } from '../api'
import { fmt, investmentTitle, spendingTitle } from '../defaults'
import type { Scenario, SimulationInput, SimulationResult } from '../types'

interface Props {
  current: SimulationInput
  currentResult: SimulationResult | null
  scenarios: Scenario[]
}

interface Row {
  name: string
  input: SimulationInput
  result: SimulationResult | null
  error?: string
}

export function ComparePanel({ current, currentResult, scenarios }: Props) {
  const [picked, setPicked] = useState<string[]>([])
  const [rows, setRows] = useState<Row[]>([])
  const [busy, setBusy] = useState(false)

  const toggle = (id: string) =>
    setPicked((p) => (p.includes(id) ? p.filter((x) => x !== id) : p.length >= 3 ? p : [...p, id]))

  const run = async () => {
    setBusy(true)
    const chosen = scenarios.filter((s) => picked.includes(s.id))
    const results = await Promise.all(
      chosen.map(async (s): Promise<Row> => {
        try {
          return { name: s.name, input: s.input, result: await api.simulate(s.input) }
        } catch (e) {
          return { name: s.name, input: s.input, result: null, error: (e as Error).message }
        }
      }),
    )
    setRows([{ name: 'Current inputs', input: current, result: currentResult }, ...results])
    setBusy(false)
  }

  const metrics: [string, (r: SimulationResult, i: SimulationInput) => string][] = [
    ['Success rate', (r) => (r.successRate == null ? '–' : `${r.successRate.toFixed(1)}%`)],
    ['Spending strategy', (_, i) => spendingTitle(i.spending)],
    ['Starting withdrawal', (_, i) => (i.spending.type === 'RemainingLife'
      ? '–'
      : i.spending.type === 'FixedAmounts'
        ? [fmt.gbp(i.spending.fixedAmount), ...(i.spending.amountSteps ?? []).map((c) => `${fmt.gbp(c.amount)} from ${c.age}`)].join(', ')
      : [fmt.pctTrim(i.spending.initialRate), ...(i.spending.rateChanges ?? []).map((c) => `${fmt.pctTrim(c.rate)} from ${c.age}`)].join(', '))],
    ['Investment strategy', (_, i) => investmentTitle(i.investment)],
    ['Mix (shares/bonds/cash)', (_, i) => `${Math.round(i.allocation.equity * 100)}/${Math.round(i.allocation.bond * 100)}/${Math.round(i.allocation.cash * 100)}`],
    ['Ages', (_, i) => `${i.retirementAge} → ${i.deathAge}`],
    ['Share returns', (_, i) => (i.equityReturnAdjustment ? `${fmt.pctTrim(-i.equityReturnAdjustment)} a year below history` : 'As history')],
    ['Median balance at death', (r) => fmt.gbp(r.medianEndBalance)],
    ['Worst 10% at death', (r) => fmt.gbp(r.p10EndBalance)],
    ['Earliest run-out age', (r) => (r.worstDepletionAge == null ? 'Never' : String(Math.floor(r.worstDepletionAge)))],
    ['Median average spending', (r) => fmt.gbp(r.medianAverageSpending)],
    ['Lowest yearly spending', (r) => fmt.gbp(r.minimumSpending)],
  ]

  return (
    <div className="card compare">
      <h3>Compare strategies</h3>
      {scenarios.length === 0 ? (
        <p className="muted">Save a scenario first (Actions → Save scenario), then compare it with your current inputs here.</p>
      ) : (
        <>
          <p className="muted small">Pick up to 3 saved scenarios to compare with your current inputs.</p>
          <div className="compare-picks">
            {scenarios.map((s) => (
              <label key={s.id} className="check">
                <input type="checkbox" checked={picked.includes(s.id)} onChange={() => toggle(s.id)} />
                <span>{s.name}</span>
              </label>
            ))}
          </div>
          <button className="btn primary" onClick={run} disabled={busy || picked.length === 0}>
            {busy ? 'Running…' : 'Compare'}
          </button>
        </>
      )}
      {rows.length > 0 && (
        <div className="table-scroll">
          <table className="compare-table">
            <thead>
              <tr>
                <th />
                {rows.map((r) => <th key={r.name}>{r.name}</th>)}
              </tr>
            </thead>
            <tbody>
              {metrics.map(([label, f]) => (
                <tr key={label}>
                  <td>{label}</td>
                  {rows.map((r) => (
                    <td key={r.name} className={label === 'Success rate' ? 'strong' : ''}>
                      {r.result ? f(r.result, r.input) : r.error ?? '–'}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
