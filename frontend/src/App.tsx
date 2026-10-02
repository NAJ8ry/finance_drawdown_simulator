import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from './api'
import { AskPanel } from './components/AskPanel'
import { ComparePanel } from './components/ComparePanel'
import { DataPanel } from './components/DataPanel'
import { Explanation } from './components/Explanation'
import { InputPanel } from './components/InputPanel'
import { SpendingPlanner } from './components/SpendingPlanner'
import { chartToPng, defaultLegend, PathChart, type LegendState, type Money, type Series } from './components/PathChart'
import { AssumptionCards, Headline, KeyFigures, Legend, spendingHiddenLegend, strategyTitle, Tables } from './components/ResultParts'
import { defaultInput, fmt, incomeByYear, mergeInput, planIsStale, startUnit, toNominal, yearsFromToday } from './defaults'
import type { MarketSummary, Scenario, SimulationInput, SimulationResult, SpendingParameters } from './types'

type Tab = 'balance' | 'income' | 'plan' | 'tables' | 'compare' | 'data'

const STORAGE_KEY = 'drawdown-sim-input'

function loadStoredInput(): SimulationInput {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw) return mergeInput(JSON.parse(raw))
  } catch {
    /* storage unavailable */
  }
  return defaultInput
}

export default function App() {
  const [input, setInput] = useState<SimulationInput>(loadStoredInput)
  const [result, setResult] = useState<SimulationResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [tab, setTab] = useState<Tab>('balance')
  const [money, setMoney] = useState<Money>('Real')
  const [spendingView, setSpendingView] = useState<Series>('income')
  const [zoom, setZoom] = useState(1)
  const [legend, setLegend] = useState<LegendState>(defaultLegend)
  // The spending chart only lets you toggle one-offs; the rest is fixed (see spendingHiddenLegend)
  const [spendingLegend, setSpendingLegend] = useState<LegendState>(defaultLegend)
  const [selected, setSelected] = useState<number | null>(null)
  const [summary, setSummary] = useState<MarketSummary | null>(null)
  const [scenarios, setScenarios] = useState<Scenario[]>([])
  const [scenario, setScenario] = useState<Scenario | null>(null)
  const [showExplain, setShowExplain] = useState(false)
  const [showScenarios, setShowScenarios] = useState(false)
  const [menuOpen, setMenuOpen] = useState(false)
  const [toast, setToast] = useState<string | null>(null)
  const [inputsOpen, setInputsOpen] = useState(() => window.innerWidth > 900)
  const chartRef = useRef<HTMLDivElement>(null)

  const loadSummary = useCallback(() => api.marketSummary().then(setSummary).catch(() => undefined), [])
  const loadScenarios = useCallback(
    () => api.listScenarios().then((list) => setScenarios(list.map((s) => ({ ...s, input: mergeInput(s.input) })))).catch(() => undefined),
    [],
  )
  useEffect(() => {
    loadSummary()
    loadScenarios()
  }, [loadSummary, loadScenarios])

  // Re-run the simulation shortly after inputs stop changing
  const [runKey, setRunKey] = useState(0)
  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(input))
    } catch {
      /* storage unavailable */
    }
    if (inputProblem(input)) return
    const ctrl = new AbortController()
    const t = setTimeout(async () => {
      setLoading(true)
      try {
        const r = await api.simulate(input, ctrl.signal)
        setResult(r)
        setError(null)
        setSelected(null)
      } catch (e) {
        if ((e as Error).name !== 'AbortError') setError((e as Error).message)
      } finally {
        if (!ctrl.signal.aborted) setLoading(false)
      }
    }, 350)
    return () => {
      clearTimeout(t)
      ctrl.abort()
    }
  }, [input, runKey])
  const shownError = inputProblem(input) ?? error

  useEffect(() => {
    if (!toast) return
    const t = setTimeout(() => setToast(null), 3000)
    return () => clearTimeout(t)
  }, [toast])

  const download = (name: string, href: string) => {
    const a = document.createElement('a')
    a.href = href
    a.download = name
    a.click()
  }

  const exportCsv = () => {
    if (!result) return
    const years = input.deathAge - input.retirementAge
    const header = ['start', 'status', 'fail_age', ...Array.from({ length: years + 1 }, (_, k) => `balance_age_${input.retirementAge + k}`)]
    const lines = result.paths.map((p) => {
      const status = p.failed ? 'ran_out' : p.partial ? 'partial' : p.succeeded ? 'lasted' : 'below_legacy'
      const vals = p.balances.map((v, k) => Math.round(money === 'Nominal' ? toNominal(v, yearsFromToday(input, k), input.inflationRate) : v))
      return [p.start, status, p.failAge ?? '', ...vals].join(',')
    })
    const summaryLines = [
      `# success_rate,${result.successRate?.toFixed(2) ?? ''}`,
      `# complete_paths,${result.completeCount}`,
      `# money,${money}`,
      `# data_to,${result.dataLastMonth}`,
    ]
    const blob = new Blob([[...summaryLines, header.join(','), ...lines].join('\n')], { type: 'text/csv' })
    const url = URL.createObjectURL(blob)
    download('drawdown-paths.csv', url)
    setTimeout(() => URL.revokeObjectURL(url), 1000)
  }

  const exportPng = async () => {
    if (!chartRef.current) return
    const png = await chartToPng(chartRef.current)
    if (png) download('drawdown-chart.png', png)
  }

  const saveScenario = async (asNew: boolean) => {
    const name = asNew || !scenario ? window.prompt('Scenario name', scenario?.name ?? strategyTitle(input)) : scenario.name
    if (!name) return
    try {
      const saved = !asNew && scenario ? await api.updateScenario(scenario.id, name, input) : await api.createScenario(name, input)
      setScenario(saved)
      setToast(`Saved "${saved.name}"`)
      loadScenarios()
    } catch (e) {
      setToast(`Could not save: ${(e as Error).message}`)
    }
  }

  // One-click re-draft of a spending plan whose pensions and other income have changed since it was drafted
  const [redrafting, setRedrafting] = useState(false)
  const [redraft, setRedraft] = useState<{ applied: SpendingParameters; previous: SpendingParameters } | null>(null)
  const redraftPlan = async () => {
    const fit = input.spending.planBasis?.fit
    if (!fit) return
    setRedrafting(true)
    try {
      const r = await api.fitSpending(input, fit.pattern, fit.targetSuccess, fit.maxLifetimeRuin)
      if (!r.feasible) {
        setToast('No spending plan meets the target with these incomes and outgoings. Open Spending plan to review it.')
        return
      }
      const applied: SpendingParameters = {
        ...input.spending,
        fixedAmount: r.spending.fixedAmount,
        amountSteps: r.spending.amountSteps,
        planBasis: { fit, retirementAge: input.retirementAge, income: incomeByYear(input) },
      }
      setRedraft({ applied, previous: input.spending })
      setInput({ ...input, spending: applied })
    } catch (e) {
      setToast(`Could not re-draft: ${(e as Error).message}`)
    } finally {
      setRedrafting(false)
    }
  }

  const menu: { title: string; items: [string, () => void, boolean?][] }[] = [
    {
      title: 'Scenario',
      items: [
        // The defaults: a £100k pot with 4% a year taken from it, paid monthly
        ['New scenario', () => { setInput(defaultInput); setScenario(null) }],
        [scenario ? `Save "${scenario.name}"` : 'Save scenario…', () => saveScenario(false)],
        ['Save as new scenario…', () => saveScenario(true), !scenario],
        ['Open saved scenario…', () => setShowScenarios(true)],
      ],
    },
    {
      title: 'Export',
      items: [
        ['Paths as CSV', exportCsv],
        ['Chart as PNG', () => { setTab('balance'); setTimeout(exportPng, 50) }],
      ],
    },
  ]

  useEffect(() => {
    if (!menuOpen) return
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setMenuOpen(false)
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [menuOpen])

  const isChart = tab === 'balance' || tab === 'income'

  return (
    <div className="app">
      <header className="topbar">
        <div className="brand">
          <span className="wordmark">Retirement Drawdown</span>
          <span className="brand-sep" aria-hidden="true" />
          <span className="subtitle">{strategyTitle(input)}</span>
          {scenario && <span className="pill">{scenario.name}</span>}
        </div>
        <span className="poc-note">Remeody · Proof of concept · Not for commercial use</span>
        <button className="burger" onClick={() => setMenuOpen(true)} aria-label="Open menu" aria-expanded={menuOpen}>
          <span /><span /><span />
        </button>
      </header>
      {menuOpen && (
        <>
          <div className="drawer-backdrop" onClick={() => setMenuOpen(false)} />
          <nav className="drawer" aria-label="Menu">
            <div className="drawer-head">
              <span className="eyebrow">Menu</span>
              <button className="icon-btn" onClick={() => setMenuOpen(false)} aria-label="Close menu">×</button>
            </div>
            {menu.map(({ title, items }, g) => (
              <div className="drawer-group" key={g}>
                {title && <div className="eyebrow">{title}</div>}
                {items.filter(([, , hidden]) => !hidden).map(([label, fn]) => (
                  <button key={label} onClick={() => { setMenuOpen(false); fn() }}>{label}</button>
                ))}
              </div>
            ))}
          </nav>
        </>
      )}

      <div className="layout">
        <aside className={`sidebar${inputsOpen ? '' : ' collapsed'}`}>
          <button className="sidebar-toggle" onClick={() => setInputsOpen((o) => !o)}>
            {inputsOpen ? 'Hide inputs' : 'Show inputs'}
          </button>
          {inputsOpen && <InputPanel input={input} onChange={(i) => setInput(i)} />}
        </aside>

        <main className="main">
          <div className="summary-row">
            {isChart && (
              <div className="segmented" role="group" aria-label="Money basis">
                {(['Nominal', 'Real'] as Money[]).map((m) => (
                  <button key={m} className={money === m ? 'on' : ''} onClick={() => setMoney(m)}>{m}</button>
                ))}
              </div>
            )}
            {result && <Headline result={result} input={input} />}
            <AssumptionCards input={input} />
          </div>

          <nav className="tabs" role="tablist">
            {([
              ['balance', 'Balance'],
              ['income', 'Spending'],
              ['plan', 'Spending plan'],
              ['tables', 'Tables'],
              ['compare', 'Compare'],
              ['data', 'Market data'],
            ] as [Tab, string][]).map(([t, label]) => (
              <button key={t} role="tab" aria-selected={tab === t} className={tab === t ? 'on' : ''} onClick={() => setTab(t)}>
                {label}
              </button>
            ))}
            {loading && <span className="spinner" aria-label="Running" />}
            <button className="method-link" onClick={() => setShowExplain(true)}>Methodology &amp; data</button>
          </nav>

          {shownError && (
            <div className="banner error">
              {shownError}{' '}
              <button className="link" onClick={() => setRunKey((k) => k + 1)}>Retry</button>
            </div>
          )}

          {tab === 'income' && planIsStale(input) && (
            <div className="banner warn">
              Your Fixed amounts list came from a spending plan drafted for different pensions and other income than you
              have now, so total spending may jump or dip where an income changed.{' '}
              {input.spending.planBasis?.fit && (
                <>
                  <button className="link" disabled={redrafting} onClick={redraftPlan}>
                    {redrafting ? 'Re-drafting…' : 'Re-draft for current incomes'}
                  </button>
                  {' · '}
                </>
              )}
              <button className="link" onClick={() => setTab('plan')}>Review in Spending plan</button>
            </div>
          )}
          {tab === 'income' && redraft && input.spending === redraft.applied && (
            <div className="banner info">
              Spending plan re-drafted for your current incomes.{' '}
              <button className="link" onClick={() => { setInput({ ...input, spending: redraft.previous }); setRedraft(null) }}>Undo</button>
            </div>
          )}

          {isChart && result && (
            <div className="card chart-card" ref={chartRef}>
              <div className="zoom">
                <button className="icon-btn round" title="Zoom in" aria-label="Zoom in" onClick={() => setZoom((z) => Math.min(z * 1.5, 20))}>+</button>
                <button className="icon-btn round" title="Zoom out" aria-label="Zoom out" onClick={() => setZoom((z) => Math.max(z / 1.5, 0.3))}>−</button>
              </div>
              {tab === 'income' && (
                <div className="segmented chart-switch" role="group" aria-label="Spending view">
                  <button className={spendingView === 'income' ? 'on' : ''} onClick={() => setSpendingView('income')}>Total spending</button>
                  <button className={spendingView === 'withdrawal' ? 'on' : ''} onClick={() => setSpendingView('withdrawal')}>Taken from pot</button>
                </div>
              )}
              <PathChart result={result} input={input} series={tab === 'balance' ? 'balance' : spendingView} money={money} zoom={zoom}
                legend={tab === 'balance' ? legend : { ...defaultLegend, oneOffs: spendingLegend.oneOffs }} selected={selected} onSelect={setSelected} />
              {tab === 'balance'
                ? <Legend legend={legend} onChange={setLegend} />
                : <Legend legend={spendingLegend} onChange={setSpendingLegend} hide={spendingHiddenLegend} />}
              <p className="chart-note">
                {tab === 'balance'
                  ? `This page shows what your portfolio could have been worth at each birthday if you had retired in each ${startUnit(input)} of history. The success rate, bands and figures use the same start dates. Some market conditions are better than others. Ideally, your plan should be able to survive severe market conditions.`
                  : spendingView === 'income'
                    ? 'What you live on each year (from the pot plus pensions and other income) for every historical start date. Flat lines mean steady income; strategies that react to markets trade steadier pots for more variable income.'
                    : 'What is actually taken from the pot each year, after pensions and other income. Below zero means income exceeded spending and the surplus was invested. Money paid into the pot, such as an inheritance, is not counted.'}
              </p>
              <p className="disclaimer">For illustrative purposes only</p>
            </div>
          )}
          {isChart && result && (
            <KeyFigures result={result} input={input} onSelect={(i) => { setSelected(i); setTab('balance'); chartRef.current?.scrollIntoView({ behavior: 'smooth' }) }} />
          )}
          {tab === 'tables' && result && (
            <Tables result={result} input={input} onSelect={(i) => { setSelected(i); setTab('balance') }} />
          )}
          {tab === 'plan' && <SpendingPlanner input={input} onApply={(spending) => setInput({ ...input, spending })} />}
          {tab === 'compare' && <ComparePanel current={input} currentResult={result} scenarios={scenarios} />}
          {tab === 'data' && <DataPanel summary={summary} onRefreshed={() => { loadSummary(); setRunKey((k) => k + 1) }} />}
          {!result && !shownError && isChart && <div className="card placeholder">Running simulation…</div>}

          <AskPanel input={input} />

          <footer className="footer muted small">
            <div className="poc-footer">Remeody · Proof of concept · Not for commercial use</div>
            {summary?.lastMonth && <>Market data up to {fmt.month(summary.lastMonth)} · </>}
            {result && <>{result.paths.length.toLocaleString()} start {startUnit(input)}s from {result.firstStart && fmt.month(result.firstStart)} · </>}
            Not financial advice. Past performance is not a guide to future returns.
          </footer>
        </main>
      </div>

      {showExplain && <Explanation summary={summary} onClose={() => setShowExplain(false)} />}
      {showScenarios && (
        <ScenarioPicker
          scenarios={scenarios}
          onOpen={(s) => { setInput(mergeInput(s.input)); setScenario(s); setShowScenarios(false) }}
          onDelete={async (s) => {
            if (!window.confirm(`Delete "${s.name}"?`)) return
            await api.deleteScenario(s.id)
            if (scenario?.id === s.id) setScenario(null)
            loadScenarios()
          }}
          onClose={() => setShowScenarios(false)}
        />
      )}
      {toast && <div className="toast">{toast}</div>}
    </div>
  )
}

function inputProblem(input: SimulationInput): string | null {
  const a = input.allocation
  if (Math.abs(a.equity + a.bond + a.cash - 1) >= 0.001) return 'Asset mix must add up to 100%.'
  if (input.deathAge <= input.retirementAge) return 'Age of death must be after retirement age.'
  return null
}

function ScenarioPicker({ scenarios, onOpen, onDelete, onClose }: {
  scenarios: Scenario[]
  onOpen: (s: Scenario) => void
  onDelete: (s: Scenario) => void
  onClose: () => void
}) {
  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal narrow" role="dialog" aria-modal="true" onClick={(e) => e.stopPropagation()}>
        <div className="modal-head">
          <h2>Saved scenarios</h2>
          <button className="icon-btn" onClick={onClose} aria-label="Close">×</button>
        </div>
        <div className="modal-body">
          {scenarios.length === 0 && <p className="muted">No saved scenarios yet.</p>}
          {scenarios.map((s) => (
            <div className="scenario-row" key={s.id}>
              <button className="link strong" onClick={() => onOpen(s)}>{s.name}</button>
              <span className="muted small">{new Date(s.updatedAt).toLocaleDateString('en-GB')}</span>
              <button className="icon-btn" title="Delete" onClick={() => onDelete(s)}>×</button>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
