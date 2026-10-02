import { useEffect, useMemo, useState } from 'react'
import { api } from '../api'
import { fmt } from '../defaults'
import type { DataUpdateLog, MarketMonth, MarketSummary } from '../types'

interface Props {
  summary: MarketSummary | null
  onRefreshed: () => void
}

export function DataPanel({ summary, onRefreshed }: Props) {
  const [months, setMonths] = useState<MarketMonth[] | null>(null)
  const [updates, setUpdates] = useState<DataUpdateLog[]>([])
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)

  const load = () => {
    api.marketMonths().then(setMonths).catch(() => setMonths([]))
    api.marketUpdates().then(setUpdates).catch(() => setUpdates([]))
  }
  useEffect(load, [])

  const refresh = async () => {
    setBusy(true)
    setMessage(null)
    try {
      const log = await api.refreshMarket()
      setMessage(log.message)
      load()
      onRefreshed()
    } catch (e) {
      setMessage((e as Error).message)
    } finally {
      setBusy(false)
    }
  }

  const recent = useMemo(() => {
    if (!months || months.length < 12) return null
    const last = months.slice(-12)
    const comp = (k: keyof Omit<MarketMonth, 'month'>) => last.reduce((acc, m) => acc * (1 + m[k]), 1) - 1
    return { equity: comp('equity'), bond: comp('bond'), cash: comp('cash'), inflation: comp('inflation') }
  }, [months])

  if (!summary) return <div className="card">Loading market data…</div>

  return (
    <div className="data-panel">
      <div className="card">
        <div className="data-head">
          <div>
            <h3>Market history</h3>
            <p className="muted">
              {summary.months.toLocaleString()} months from {summary.firstMonth && fmt.month(summary.firstMonth)} to{' '}
              {summary.lastMonth && fmt.month(summary.lastMonth)} · {summary.historyMonths} from the historical dataset,{' '}
              {summary.liveMonths} fetched live
            </p>
            {summary.lastSuccessfulUpdate && (
              <p className="muted small">
                Last successful update {new Date(summary.lastSuccessfulUpdate.runAt).toLocaleString('en-GB')}. The
                server checks for a new month every day.
              </p>
            )}
          </div>
          <button className="btn" onClick={refresh} disabled={busy}>
            {busy ? 'Checking…' : 'Check for new data'}
          </button>
        </div>
        {message && <p className="note">{message}</p>}

        <table className="stats-table">
          <thead>
            <tr><th /><th>Average a year</th><th>After inflation</th><th>Worst month</th><th>Best month</th><th>Last 12 months</th></tr>
          </thead>
          <tbody>
            {summary.assets.map((a) => (
              <tr key={a.asset}>
                <td>{a.asset}</td>
                <td>{fmt.pct(a.nominalReturn)}</td>
                <td>{fmt.pct(a.realReturn)}</td>
                <td>{fmt.pct(a.worstMonth)}</td>
                <td>{fmt.pct(a.bestMonth)}</td>
                <td>{recent ? fmt.pct(recent[a.asset === 'Equities' ? 'equity' : a.asset === 'Bonds' ? 'bond' : 'cash']) : '–'}</td>
              </tr>
            ))}
            <tr>
              <td>UK inflation</td>
              <td>{fmt.pct(summary.annualInflation)}</td>
              <td /><td /><td />
              <td>{recent ? fmt.pct(recent.inflation) : '–'}</td>
            </tr>
          </tbody>
        </table>
      </div>

      {months && months.length > 0 && (
        <div className="card">
          <h3>Growth of £1 after inflation <span className="muted small">(log scale)</span></h3>
          <GrowthChart months={months} />
        </div>
      )}

      <div className="card">
        <h3>Sources</h3>
        <table className="sources">
          <tbody>
            {summary.sources.map((s, i) => (
              <tr key={i}>
                <td className="nowrap"><strong>{s.series}</strong></td>
                <td className="nowrap">{s.period}</td>
                <td>{s.source}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {updates.length > 0 && (
        <div className="card">
          <h3>Update log</h3>
          <table>
            <tbody>
              {updates.map((u) => (
                <tr key={u.id}>
                  <td className="nowrap">{new Date(u.runAt).toLocaleString('en-GB')}</td>
                  <td className={u.success ? 'good' : 'bad'}>{u.success ? 'OK' : 'Failed'}</td>
                  <td>{u.message}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

function GrowthChart({ months }: { months: MarketMonth[] }) {
  const W = 900
  const H = 280
  const m = { l: 56, r: 130, t: 10, b: 28 }
  const series = useMemo(() => {
    const keys = ['equity', 'bond', 'cash'] as const
    return keys.map((k) => {
      let v = 1
      return months.map((mm) => (v *= (1 + mm[k]) / (1 + mm.inflation)))
    })
  }, [months])
  const all = series.flat()
  const lo = Math.log10(Math.min(...all))
  const hi = Math.log10(Math.max(...all))
  const x = (i: number) => m.l + (i / (months.length - 1)) * (W - m.l - m.r)
  const y = (v: number) => m.t + (1 - (Math.log10(v) - lo) / (hi - lo)) * (H - m.t - m.b)
  const colors = ['#1d2d48', '#a4844c', '#6b7280']
  const names = ['Shares', 'Bonds', 'Cash']
  const decades = months.map((mm, i) => ({ i, y: +mm.month.slice(0, 4), mo: mm.month.slice(5) }))
    .filter((d) => d.mo === '01' && d.y % 20 === 0)
  const ticks = []
  for (let p = Math.ceil(lo); p <= Math.floor(hi); p++) ticks.push(p)
  return (
    <svg viewBox={`0 0 ${W} ${H}`} className="growth-chart" role="img" aria-label="Growth of £1 after inflation">
      {ticks.map((p) => (
        <g key={p}>
          <line x1={m.l} x2={W - m.r} y1={y(10 ** p)} y2={y(10 ** p)} className="grid" />
          <text x={m.l - 6} y={y(10 ** p)} className="tick" textAnchor="end" dominantBaseline="middle">
            £{(10 ** p).toLocaleString('en-GB', { maximumFractionDigits: 2 })}
          </text>
        </g>
      ))}
      {decades.map((d) => (
        <text key={d.y} x={x(d.i)} y={H - 8} className="tick" textAnchor="middle">{d.y}</text>
      ))}
      {series.map((s, si) => {
        const step = Math.max(1, Math.floor(s.length / 600))
        const d = s.filter((_, i) => i % step === 0 || i === s.length - 1)
          .map((v, j, arr) => `${j === 0 ? 'M' : 'L'}${x(j === arr.length - 1 ? s.length - 1 : j * step).toFixed(1)},${y(v).toFixed(1)}`).join('')
        return (
          <g key={si}>
            <path d={d} stroke={colors[si]} strokeWidth={1.6} fill="none" />
            <text x={W - m.r + 6} y={y(s[s.length - 1])} className="series-label" fill={colors[si]} dominantBaseline="middle">
              {names[si]} £{s[s.length - 1].toLocaleString('en-GB', { maximumFractionDigits: 0 })}
            </text>
          </g>
        )
      })}
    </svg>
  )
}
