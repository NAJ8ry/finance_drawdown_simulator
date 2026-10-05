import { useEffect, useMemo, useRef, useState } from 'react'
import { activeFlows, flowsAt, fmt, toNominal, yearsFromToday } from '../defaults'
import type { PathResult, SimulationInput, SimulationResult } from '../types'

export type Series = 'balance' | 'income' | 'withdrawal'
export type Money = 'Real' | 'Nominal'

export interface LegendState {
  /** One faint line per historical start date. */
  paths: boolean
  best: boolean
  median: boolean
  worst: boolean
  likely: boolean
  lessLikely: boolean
  rare: boolean
  calendar: boolean
  oneOffs: boolean
  partial: boolean
}

export const defaultLegend: LegendState = {
  paths: true,
  best: false,
  median: false,
  worst: false,
  likely: false,
  lessLikely: false,
  rare: false,
  calendar: true,
  oneOffs: true,
  partial: true,
}

interface Props {
  result: SimulationResult
  input: SimulationInput
  series: Series
  money: Money
  zoom: number
  legend: LegendState
  selected: number | null
  onSelect: (index: number | null) => void
}

const HEIGHT = 440
const M = { top: 16, right: 20, bottom: 46, left: 64 }
/** How close (px) the pointer must be to a line to pick that start date rather than show the year summary. */
const PICK_DISTANCE = 6
const FALLBACK_COLORS = { best: '#2f6b4f', median: '#1d2d48', worst: '#9b3232', hover: '#111a2b', band: '#54688a' }
type ChartColors = typeof FALLBACK_COLORS

/** The line colours come from the theme (--chart-*), resolved to real colours so the PNG export keeps them. */
function readChartColors(): ChartColors {
  const css = getComputedStyle(document.documentElement)
  const pick = (k: keyof ChartColors) => css.getPropertyValue(`--chart-${k}`).trim() || FALLBACK_COLORS[k]
  return { best: pick('best'), median: pick('median'), worst: pick('worst'), hover: pick('hover'), band: pick('band') }
}

function useChartColors(): ChartColors {
  const [colors, setColors] = useState(readChartColors)
  useEffect(() => {
    const mq = window.matchMedia('(prefers-color-scheme: dark)')
    const update = () => setColors(readChartColors())
    mq.addEventListener('change', update)
    return () => mq.removeEventListener('change', update)
  }, [])
  return colors
}

function percentile(sorted: number[], p: number) {
  if (sorted.length === 0) return 0
  const r = (p / 100) * (sorted.length - 1)
  const lo = Math.floor(r)
  const hi = Math.ceil(r)
  return sorted[lo] + (sorted[hi] - sorted[lo]) * (r - lo)
}

export function PathChart({ result, input, series, money, zoom, legend, selected, onSelect }: Props) {
  const wrapRef = useRef<HTMLDivElement>(null)
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const COLORS = useChartColors()
  const [width, setWidth] = useState(800)
  // index = the start date under the pointer, or null for the summary of year k across all start dates
  const [hover, setHover] = useState<{ index: number | null; k: number; x: number; y: number } | null>(null)

  useEffect(() => {
    const el = wrapRef.current
    if (!el) return
    const ro = new ResizeObserver(([e]) => setWidth(Math.max(320, Math.floor(e.contentRect.width))))
    ro.observe(el)
    return () => ro.disconnect()
  }, [])

  const years = input.deathAge - input.retirementAge
  const infl = input.inflationRate

  // Values per path per year, converted to the chosen money basis
  const data = useMemo(() => {
    return result.paths.map((p) => {
      const raw = series === 'balance' ? p.balances : series === 'income' ? p.spending : p.withdrawals
      return money === 'Nominal' ? raw.map((v, k) => toNominal(v, yearsFromToday(input, k), infl)) : raw
    })
  }, [result, series, money, infl, input])

  const bands = useMemo(() => {
    const complete = result.paths.map((p, i) => (p.partial ? -1 : i)).filter((i) => i >= 0)
    const n = series === 'balance' ? years + 1 : years
    return Array.from({ length: n }, (_, k) => {
      const vals = complete.map((i) => data[i][k]).sort((a, b) => a - b)
      return [5, 10, 25, 50, 75, 90, 95].map((p) => percentile(vals, p))
    })
  }, [data, result, series, years])

  // Fit the scale to the likely range, not the extreme winners: 1.3 × the highest 75th percentile at any age,
  // never below 1.5 × the starting pot, rounded up to a tidy number. Lines above the top are clipped.
  const yMaxFit = useMemo(() => {
    const likelyTop = Math.max(1, ...bands.map((b) => b[4])) * 1.3
    const floor = series === 'balance' ? input.startingBalance * 1.5 : Math.max(...bands.map((b) => b[3])) * 1.2
    return niceCeil(Math.max(likelyTop, floor))
  }, [bands, series, input.startingBalance])
  const yMax = yMaxFit / zoom
  // Withdrawals go negative when other income exceeds spending and the surplus is invested
  const yMin = useMemo(() => {
    const bottom = Math.min(0, ...bands.map((b) => b[0]))
    return bottom < 0 ? (bottom * 1.25) / zoom : 0
  }, [bands, zoom])

  const innerW = width - M.left - M.right
  const innerH = HEIGHT - M.top - M.bottom
  const xOf = (k: number) => M.left + (k / years) * innerW
  const yOf = (v: number) => M.top + innerH - ((Math.max(Math.min(v, yMax * 1.5), yMin * 1.5) - yMin) / (yMax - yMin)) * innerH

  const drawnCount = result.paths.length
  const visible = (i: number) => legend.paths && (legend.partial || !result.paths[i].partial)
  const aboveTop = useMemo(
    () => (legend.paths ? data.filter((vals, i) => (legend.partial || !result.paths[i].partial) && vals.some((v) => v > yMax)).length : 0),
    [data, legend.paths, legend.partial, result, yMax],
  )

  // Balances are points at each birthday; spending is flat through each year, so draw it as steps
  const step = series !== 'balance'
  const points = (vals: number[]): [number, number][] => {
    if (!step) return vals.map((v, k) => [k, v])
    const out: [number, number][] = []
    vals.forEach((v, k) => out.push([k, v], [k + 1, v]))
    return out
  }

  // Spaghetti lines on canvas (fast for ~2,000 paths)
  useEffect(() => {
    const c = canvasRef.current
    if (!c) return
    const dpr = window.devicePixelRatio || 1
    c.width = width * dpr
    c.height = HEIGHT * dpr
    const ctx = c.getContext('2d')!
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
    ctx.clearRect(0, 0, width, HEIGHT)
    ctx.save()
    ctx.beginPath()
    ctx.rect(M.left, M.top, innerW, innerH)
    ctx.clip()
    const dark = window.matchMedia?.('(prefers-color-scheme: dark)').matches
    data.forEach((vals, i) => {
      const p = result.paths[i]
      if (!visible(i)) return
      // Vary the shade per path like the reference design
      const shade = (i * 37) % 100
      const light = dark ? 45 + shade * 0.35 : 30 + shade * 0.45
      // ~2,000 overlapping paths: keep each faint so density shows where outcomes cluster
      const alpha = Math.min(0.6, 45 / drawnCount + 0.05)
      ctx.strokeStyle = `hsla(${215 + (shade % 20)}, ${12 + (shade % 25)}%, ${light}%, ${p.partial ? alpha * 0.7 : alpha})`
      ctx.lineWidth = 0.9
      ctx.setLineDash(p.partial ? [3, 3] : [])
      ctx.beginPath()
      points(vals).forEach(([k, v], j) => (j === 0 ? ctx.moveTo(xOf(k), yOf(v)) : ctx.lineTo(xOf(k), yOf(v))))
      ctx.stroke()
    })
    ctx.restore()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data, width, yMax, yMin, legend.paths, legend.partial, result, step, drawnCount])

  const linePath = (vals: number[]) =>
    points(vals).map(([k, v], j) => `${j === 0 ? 'M' : 'L'}${xOf(k).toFixed(1)},${yOf(v).toFixed(1)}`).join('')
  const areaPath = (lo: number, hi: number) => {
    const top = points(bands.map((b) => b[hi]))
    const bottom = points(bands.map((b) => b[lo])).reverse()
    return [...top, ...bottom].map(([k, v], j) => `${j === 0 ? 'M' : 'L'}${xOf(k).toFixed(1)},${yOf(v).toFixed(1)}`).join('') + 'Z'
  }

  const yTicks = useMemo(() => {
    const raw = (yMax - yMin) / 6
    const mag = Math.pow(10, Math.floor(Math.log10(raw)))
    const step = [1, 2, 2.5, 5, 10].map((s) => s * mag).find((s) => s >= raw) ?? raw
    const ticks = []
    for (let v = Math.ceil(yMin / step) * step; v <= yMax + 1e-9; v += step) ticks.push(v)
    return ticks
  }, [yMax, yMin])

  const xEvery = innerW / years < 18 ? (innerW / years < 9 ? 5 : 2) : 1

  const onMove = (e: React.MouseEvent<SVGSVGElement>) => {
    const rect = e.currentTarget.getBoundingClientRect()
    const mx = e.clientX - rect.left
    const my = e.clientY - rect.top
    if (mx < M.left || mx > M.left + innerW) {
      setHover(null)
      return
    }
    const kMax = step ? years - 1 : years
    const pos = ((mx - M.left) / innerW) * years
    const k = Math.max(0, Math.min(kMax, step ? Math.floor(pos) : Math.round(pos)))
    // Only pick a line from inside the plot and right on top of it; the age axis and gaps show the year summary
    let best: number | null = null
    if (legend.paths && legend.calendar && my >= M.top && my <= M.top + innerH) {
      let bestDist = PICK_DISTANCE
      data.forEach((vals, i) => {
        if (!visible(i) || k >= vals.length) return
        const d = Math.abs(yOf(vals[k]) - my)
        if (d < bestDist) {
          bestDist = d
          best = i
        }
      })
      // Lines crowd together, so the pinned line wins whenever it is in reach – otherwise clicking it can't unpin it
      if (selected != null && visible(selected) && k < data[selected].length && Math.abs(yOf(data[selected][k]) - my) < PICK_DISTANCE)
        best = selected
    }
    setHover({ index: best, k, x: mx, y: my })
  }

  // Escape unpins: with ~2,000 lines nearly every click lands on one, so clicking a gap is unreliable
  useEffect(() => {
    if (selected == null) return
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onSelect(null)
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [selected, onSelect])

  const picked = hover?.index ?? null
  const active = picked ?? selected
  const activePath = active != null ? result.paths[active] : null

  // Best and worst are real start dates; the median is the middle value at each age, not any one start date's route
  const pathValues = (index: number | null) => (index != null ? data[index] : null)
  const named: { key: keyof LegendState; values: number[] | null; color: string }[] = [
    { key: 'best', values: pathValues(result.bestIndex), color: COLORS.best },
    { key: 'median', values: bands.length ? bands.map((b) => b[3]) : null, color: COLORS.median },
    { key: 'worst', values: pathValues(result.worstIndex), color: COLORS.worst },
  ]

  return (
    <div className="chart-wrap" ref={wrapRef}>
      <canvas ref={canvasRef} style={{ width, height: HEIGHT }} className="chart-canvas" />
      <svg
        width={width}
        height={HEIGHT}
        className="chart-svg"
        onMouseMove={onMove}
        onMouseLeave={() => setHover(null)}
        onClick={() => onSelect(picked != null ? (picked === selected ? null : picked) : null)}
        role="img"
        aria-label={`${series === 'balance' ? 'Portfolio balance' : series === 'income' ? 'Annual spending' : 'Yearly withdrawals from the pot'} for every historical start date`}
      >
        <defs>
          <clipPath id="plot-clip">
            <rect x={M.left} y={M.top} width={innerW} height={innerH} />
          </clipPath>
        </defs>

        {yTicks.map((v) => (
          <g key={v}>
            <line x1={M.left} x2={M.left + innerW} y1={yOf(v)} y2={yOf(v)} className="grid" />
            <text x={M.left - 8} y={yOf(v)} className="tick" textAnchor="end" dominantBaseline="middle">
              {fmt.gbpShort(v)}
            </text>
          </g>
        ))}

        {Array.from({ length: years + 1 }, (_, k) => k)
          .filter((k) => k % xEvery === 0 || k === years)
          .map((k) => (
            <text key={k} x={xOf(k)} y={M.top + innerH + 18} className="tick" textAnchor="middle">
              {input.retirementAge + k}
            </text>
          ))}
        <text x={M.left + innerW / 2} y={HEIGHT - 6} className="axis-title" textAnchor="middle">
          Age
        </text>

        <line x1={xOf(0)} x2={xOf(0)} y1={M.top} y2={M.top + innerH} className="retire-line" />
        <text transform={`translate(${xOf(0) + 12},${M.top + 6}) rotate(-90)`} className="retire-label" textAnchor="end">
          Retirement age
        </text>

        {aboveTop > 0 && (
          <text x={M.left + 26} y={M.top + 12} className="clip-note">
            ▲ {aboveTop} {aboveTop === 1 ? 'line goes' : 'lines go'} above {fmt.gbpShort(yMax)} – zoom out (−) to see
          </text>
        )}

        <g clipPath="url(#plot-clip)">
          {legend.rare && <path d={areaPath(0, 6)} fill={COLORS.band} opacity={0.22} />}
          {legend.lessLikely && <path d={areaPath(1, 5)} fill={COLORS.band} opacity={0.3} />}
          {legend.likely && <path d={areaPath(2, 4)} fill={COLORS.band} opacity={0.42} />}
          {named.map(({ key, values, color }) =>
            legend[key] && values ? (
              <path key={key} d={linePath(values)} stroke={color} strokeWidth={2.4} fill="none" />
            ) : null,
          )}
          {activePath && active != null && (
            <path d={linePath(data[active])} stroke={COLORS.hover} strokeWidth={2.4} fill="none"
              strokeDasharray={activePath.partial ? '5 4' : undefined} />
          )}
        </g>

        <line x1={M.left} x2={M.left + innerW} y1={yOf(0)} y2={yOf(0)} className="axis" />

        {legend.oneOffs &&
          input.oneOffs
            .filter((o) => o.age >= input.retirementAge && o.age < input.deathAge)
            .map((o, i) => {
              const x = xOf(o.age - input.retirementAge)
              const y = M.top + innerH
              return (
                <g key={i}>
                  <path d={`M${x},${y - 6} L${x + 6},${y} L${x},${y + 6} L${x - 6},${y}Z`} className={o.amount >= 0 ? 'oneoff-out' : 'oneoff-in'} />
                  <title>{`${o.label || 'One-off'} at ${o.age}: ${o.amount >= 0 ? 'spend' : 'add'} ${fmt.gbp(Math.abs(o.amount))}`}</title>
                </g>
              )
            })}
        {legend.oneOffs &&
          activeFlows(input)
            .filter((f) => f.startAge > input.retirementAge && f.startAge < input.deathAge)
            .map((f, i) => {
              const x = xOf(f.startAge - input.retirementAge)
              const y = M.top + innerH
              const up = f.kind === 'Income'
              return (
                <g key={`f${i}`}>
                  <path d={up ? `M${x - 6},${y + 5} L${x + 6},${y + 5} L${x},${y - 6}Z` : `M${x - 6},${y - 5} L${x + 6},${y - 5} L${x},${y + 6}Z`}
                    className={up ? 'oneoff-in' : 'oneoff-out'} />
                  <title>{`${f.label || (up ? 'Income' : 'Outgoing')} from ${f.startAge}${f.endAge ? ` to ${f.endAge}` : ''}: ${fmt.gbp(f.annualAmount)} a year`}</title>
                </g>
              )
            })}

        {hover && picked == null && (
          step
            ? <rect x={xOf(hover.k)} y={M.top} width={xOf(hover.k + 1) - xOf(hover.k)} height={innerH} className="year-hover" />
            : <line x1={xOf(hover.k)} x2={xOf(hover.k)} y1={M.top} y2={M.top + innerH} className="year-hover-line" />
        )}
        {hover && picked != null && activePath && (
          <circle cx={xOf(hover.k + (step ? 0.5 : 0))} cy={yOf(data[picked][hover.k])} r={4} fill={COLORS.hover} />
        )}
      </svg>

      {selected != null && result.paths[selected] && (
        <div className="pinned-tag">
          <strong>Pinned: retired {fmt.month(result.paths[selected].start)}</strong>
          {' · '}
          {result.paths[selected].failed
            ? `ran out at ${Math.floor(result.paths[selected].failAge ?? 0)}`
            : result.paths[selected].partial ? 'still running' : `${fmt.gbp(result.paths[selected].endBalance)} left`}
          <button className="link" onClick={() => onSelect(null)} title="Or press Esc">clear</button>
        </div>
      )}
      {hover && picked == null && (
        <YearSummary result={result} input={input} k={hover.k} money={money} x={hover.x} y={hover.y} width={width} />
      )}
      {hover && picked != null && activePath && (
        <div className="tooltip" style={{ left: Math.min(hover.x + 14, width - 230), top: Math.max(8, hover.y - 90) }}>
          <strong>Retired {fmt.month(activePath.start)}</strong>
          <div>
            Age {input.retirementAge + hover.k}: {fmt.gbp(data[picked][hover.k])}
            {series === 'balance' && hover.k < activePath.spending.length && (
              <> · spending {fmt.gbp(money === 'Nominal' ? toNominal(activePath.spending[hover.k], yearsFromToday(input, hover.k), infl) : activePath.spending[hover.k])}/yr</>
            )}
          </div>
          <div className={activePath.failed ? 'bad' : activePath.partial ? 'muted' : 'good'}>
            {activePath.failed
              ? `Ran out at age ${Math.floor(activePath.failAge ?? 0)}`
              : activePath.partial
                ? `Still running – history ends after ${Math.floor(activePath.months / 12)} years`
                : `Lasted – ${fmt.gbp(activePath.endBalance)} left (today's money)`}
          </div>
          <div className="muted small">Click to {selected === picked ? 'unpin' : 'pin'}</div>
        </div>
      )}
    </div>
  )
}

const SUMMARY_WIDTH = 270

/**
 * What happens in one year of retirement: the plan's own income, outgoings and one-offs, plus the typical (median)
 * spending, withdrawal and balance across all complete start dates, with the 10th–90th percentile range.
 */
function YearSummary({ result, input, k, money, x, y, width }: {
  result: SimulationResult
  input: SimulationInput
  k: number
  money: Money
  x: number
  y: number
  width: number
}) {
  const age = input.retirementAge + k
  const years = input.deathAge - input.retirementAge
  const cash = (v: number) => fmt.gbp(money === 'Nominal' ? toNominal(v, yearsFromToday(input, k), input.inflationRate) : v)
  const complete = result.paths.filter((p) => !p.partial)

  const flows = flowsAt(input, age).map((f) => ({ ...f, label: f.label + (f.intoPot ? ' (into the pot)' : '') }))
  const incomes = flows.filter((f) => f.kind === 'Income')
  const outgoings = flows.filter((f) => f.kind === 'Expense')
  const oneOffs = input.oneOffs.filter((o) => o.age === age)

  const stats = (pick: (p: PathResult) => number | undefined) => {
    const v = complete.map(pick).filter((n): n is number => n != null).sort((a, b) => a - b)
    return v.length ? { lo: percentile(v, 10), mid: percentile(v, 50), hi: percentile(v, 90) } : null
  }
  const inYear = k < years
  const spending = inYear ? stats((p) => p.spending[k]) : null
  const fromPot = inYear ? stats((p) => p.withdrawals[k]) : null
  const balance = stats((p) => p.balances[k])
  const ranOut = complete.filter((p) => p.failed && (p.failAge ?? Infinity) < age + 1).length

  const left = x > width / 2 ? x - 14 - SUMMARY_WIDTH : x + 14
  const below = y < HEIGHT / 2
  return (
    <div className="tooltip year-summary"
      style={{ left: Math.max(4, left), top: below ? y + 14 : y - 14, width: SUMMARY_WIDTH, transform: below ? undefined : 'translateY(-100%)' }}>
      <strong>Age {age}</strong>
      <span className="muted small"> · year {k + 1} of retirement{money === 'Nominal' ? ' · future £' : " · today's £"}</span>

      {inYear && (
        <div className="ys-section">
          {incomes.length === 0 && outgoings.length === 0 && oneOffs.length === 0 && (
            <div className="muted">No other income or outgoings this year</div>
          )}
          {incomes.map((f, i) => <Row key={`i${i}`} label={f.label} value={`+${cash(f.amount)}`} className="good" />)}
          {outgoings.map((f, i) => <Row key={`o${i}`} label={f.label} value={`−${cash(f.amount)}`} className="bad" />)}
          {oneOffs.map((o, i) => (
            <Row key={`x${i}`} label={`${o.label || 'One-off'} (one-off)`}
              value={o.amount >= 0 ? `−${cash(o.amount)}` : `+${cash(-o.amount)}`} className={o.amount >= 0 ? 'bad' : 'good'} />
          ))}
        </div>
      )}

      {result.survival && result.survival[k] != null && (
        <div className="muted small">{fmt.pct(result.survival[k], 0)} chance of being alive at {age} (ONS life tables)</div>
      )}
      <div className="ys-section">
        <div className="muted small">Typical (median) across {complete.length} start dates, with 10–90% range</div>
        <Stat label="Spending" cash={cash} s={spending} />
        <Stat label="Taken from the pot" cash={cash} s={fromPot} />
        <Stat label={k === 0 ? 'Balance at retirement' : `Balance at ${age}`} cash={cash} s={balance} />
      </div>
      {ranOut > 0 && <div className="bad small">Ran out by {age + 1} in {ranOut} of {complete.length} start dates</div>}
    </div>
  )
}

function Row({ label, value, className }: { label: string; value: string; className?: string }) {
  return <div className={`ys-row ${className ?? ''}`}><span>{label}</span><span>{value}</span></div>
}

function Stat({ label, s, cash }: { label: string; s: { lo: number; mid: number; hi: number } | null; cash: (v: number) => string }) {
  if (!s) return null
  return (
    <div>
      <Row label={label} value={cash(s.mid)} />
      <div className="ys-range">{cash(s.lo)} – {cash(s.hi)}</div>
    </div>
  )
}

/** Rounds up to 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8 × a power of ten. */
function niceCeil(v: number) {
  const mag = Math.pow(10, Math.floor(Math.log10(v)))
  const step = [1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10].find((s) => s * mag >= v) ?? 10
  return step * mag
}

/** Renders the chart's canvas + SVG into a PNG data URL. */
export async function chartToPng(container: HTMLElement): Promise<string | null> {
  const canvas = container.querySelector('canvas')
  const svg = container.querySelector('svg')
  if (!canvas || !svg) return null
  const w = svg.width.baseVal.value
  const h = svg.height.baseVal.value
  const out = document.createElement('canvas')
  out.width = w * 2
  out.height = h * 2
  const ctx = out.getContext('2d')!
  ctx.scale(2, 2)
  ctx.fillStyle = getComputedStyle(document.body).getPropertyValue('--surface') || '#fff'
  ctx.fillRect(0, 0, w, h)
  ctx.drawImage(canvas, 0, 0, w, h)
  const clone = svg.cloneNode(true) as SVGSVGElement
  clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg')
  const style = document.createElement('style')
  style.textContent = `text{font:12px Inter,system-ui,sans-serif;fill:#5b6475}.axis-title{font-weight:600;fill:#1f2937}.grid{stroke:#e5e7eb}.axis{stroke:#9ca3af}.retire-line{stroke:#9ca3af;stroke-dasharray:3 3}.oneoff-out{fill:#9b3232}.oneoff-in{fill:#2f6b4f}`
  clone.prepend(style)
  const url = URL.createObjectURL(new Blob([new XMLSerializer().serializeToString(clone)], { type: 'image/svg+xml' }))
  try {
    const img = new Image()
    await new Promise((res, rej) => {
      img.onload = res
      img.onerror = rej
      img.src = url
    })
    ctx.drawImage(img, 0, 0, w, h)
  } finally {
    URL.revokeObjectURL(url)
  }
  return out.toDataURL('image/png')
}
