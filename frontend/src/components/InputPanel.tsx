import type { ReactNode } from 'react'
import {
  activeFlows,
  adjustmentUsed,
  fmt,
  investmentStrategies,
  ruleActions,
  ruleMetrics,
  spendingAdjustments,
  spendingBases,
  type InvestmentBase,
  type SpendingAdjustment,
  type SpendingBase,
} from '../defaults'
import type {
  InvestmentParameters,
  LifeTable,
  RateChange,
  RecurringFlow,
  RuleAction,
  RuleComparison,
  RuleMetric,
  SimulationInput,
  SpendingParameters,
  SpendingStep,
  SpendingRule,
} from '../types'
import { CheckField, NumberField, Section, SelectField } from './Fields'

interface Props {
  input: SimulationInput
  onChange: (input: SimulationInput) => void
}

export function InputPanel({ input, onChange }: Props) {
  const set = <K extends keyof SimulationInput>(key: K, value: SimulationInput[K]) => onChange({ ...input, [key]: value })
  const setSpending = (patch: Partial<SpendingParameters>) => set('spending', { ...input.spending, ...patch })
  const setInvestment = (patch: Partial<InvestmentParameters>) => set('investment', { ...input.investment, ...patch })
  const a = input.allocation
  const allocationTotal = a.equity + a.bond + a.cash
  const retiringLater = input.currentAge != null && input.currentAge < input.retirementAge

  return (
    <div className="input-panel">
      <Section title="Your pot">
        <NumberField label={retiringLater ? 'Pot at retirement' : 'Starting balance'} prefix="£" value={input.startingBalance} min={1} step={1000}
          onChange={(v) => set('startingBalance', v ?? 0)} hint={retiringLater ? "In today's money" : undefined} />
        <div className="row-3">
          <NumberField label="Current age" value={input.currentAge} min={16} max={input.retirementAge} step={1} allowEmpty
            onChange={(v) => set('currentAge', v == null ? null : Math.round(v))} hint={input.currentAge == null ? 'Retiring now' : undefined} />
          <NumberField label="Retirement age" value={input.retirementAge} min={30} max={100} step={1}
            onChange={(v) => set('retirementAge', Math.round(v ?? 60))} />
          <NumberField label={input.lifeTable === 'None' ? 'Age of death' : 'Plan to age'} value={input.deathAge} min={input.retirementAge + 1} max={120} step={1}
            onChange={(v) => set('deathAge', Math.round(v ?? 94))} />
        </div>
        <SelectField<LifeTable> label="Lifespan" value={input.lifeTable}
          options={[
            { value: 'None', label: 'Use the age of death only' },
            { value: 'Male', label: 'UK life tables: a man' },
            { value: 'Female', label: 'UK life tables: a woman' },
            { value: 'Couple', label: 'UK life tables: a couple (man and woman, same age)' },
          ]}
          onChange={(v) => set('lifeTable', v)} />
        {input.lifeTable !== 'None' && (
          <p className="note">
            Adds the chance of running out while still alive{input.lifeTable === 'Couple' ? ' (while either of you is)' : ''},
            using ONS projections of how long people your age live. "Plan to age" is how far the simulation runs; 95–100
            covers most lifespans.
          </p>
        )}
        {retiringLater && (
          <p className="note">
            All amounts are in today's money, including the pot you expect to have when you retire
            {' '}{input.retirementAge - (input.currentAge ?? 0)} years from now.
          </p>
        )}
      </Section>

      <Section title="Assumptions">
        <div className="row-2">
          <NumberField label="Planned inflation" percent value={input.inflationRate} step={0.1} min={-5} max={20}
            onChange={(v) => set('inflationRate', v ?? 0)} hint="Constant, per year" />
          <NumberField label="Fees" percent value={input.feeRate} step={0.05} min={0} max={5}
            onChange={(v) => set('feeRate', v ?? 0)} hint="Per year" />
        </div>
        <SelectField label="Share returns" value={String(input.equityReturnAdjustment ?? 0)}
          options={[0, -0.005, -0.01, -0.015, -0.02].map((v) => ({
            value: String(v),
            label: v === 0 ? 'As history' : `${fmt.pctTrim(-v)} a year lower than history`,
          }))}
          onChange={(v) => set('equityReturnAdjustment', Number(v))} />
        <p className="note">
          Before 2010 the share history is US shares, among the best performers of any country. Studies of other
          developed markets find lower returns and lower safe withdrawal rates (Pfau 2010; Anarkulova, Cederburg,
          O'Doherty &amp; Sias 2025), so check your plan still works with shares doing a little worse.
        </p>
        <div className="row-2">
          <SelectField label="Withdrawals taken" value={input.withdrawalTiming}
            options={[{ value: 'Monthly', label: 'Monthly' }, { value: 'AnnualInAdvance', label: 'Yearly in advance' }]}
            onChange={(v) => set('withdrawalTiming', v)} />
          <SelectField label="Test retiring in" value={input.startFrequency}
            options={[{ value: 'Yearly', label: 'Every year (Jan)' }, { value: 'Monthly', label: 'Every month' }]}
            onChange={(v) => set('startFrequency', v)} />
        </div>
        <div className="row-2">
          <NumberField label="Leave at least" prefix="£" value={input.legacyTarget} min={0} step={1000}
            onChange={(v) => set('legacyTarget', v ?? 0)} hint="Needed for success" />
          <NumberField label="Minimum income" prefix="£" value={input.spendingFloor} min={0} step={500} allowEmpty
            onChange={(v) => set('spendingFloor', v)} hint="Per year (optional)" />
        </div>
        <p className="note">Tax is not modelled. All amounts are in today's money.</p>
      </Section>

      <Section title="Asset mix">
        <div className="row-3">
          <NumberField label="Global shares" percent value={a.equity} min={0} max={100} step={5}
            onChange={(v) => set('allocation', { ...a, equity: v ?? 0 })} />
          <NumberField label="Bonds" percent value={a.bond} min={0} max={100} step={5}
            onChange={(v) => set('allocation', { ...a, bond: v ?? 0 })} />
          <NumberField label="Cash" percent value={a.cash} min={0} max={100} step={5}
            onChange={(v) => set('allocation', { ...a, cash: v ?? 0 })} />
        </div>
        {Math.abs(allocationTotal - 1) > 0.001 && (
          <p className="error">Mix adds up to {(allocationTotal * 100).toFixed(0)}% — it must be 100%.</p>
        )}
      </Section>

      <Section title="Spending strategy">
        <SelectField<SpendingBase> label="Base" value={input.spending.type as SpendingBase} options={spendingBases}
          onChange={(type) => setSpending({ type })} />
        <p className="note">{spendingBases[input.spending.type as SpendingBase]?.description}</p>
        <SpendingBaseFields p={input.spending} set={setSpending} balance={input.startingBalance} hasIncome={activeFlows(input).length > 0} />
        {input.spending.type === 'FixedAmounts' && <AmountSteps input={input} set={setSpending} />}
        {input.spending.type !== 'RemainingLife' && input.spending.type !== 'FixedAmounts' && <RateChanges input={input} set={setSpending} />}
        <div className="adjustments-title">Adjustments <span className="muted small">(tick any combination)</span></div>
        <SpendingAdjustments p={input.spending} set={setSpending} />
      </Section>

      <Section title="Investment strategy">
        <SelectField<InvestmentBase> label="Base" value={input.investment.type as InvestmentBase} options={investmentStrategies}
          onChange={(type) => setInvestment({ type })} />
        <p className="note">{investmentStrategies[input.investment.type as InvestmentBase]?.description}</p>
        <InvestmentFields p={input.investment} set={setInvestment} />
        <Adjustment title="Keep a cash buffer" on={input.investment.useCashBuffer}
          description="Hold some years of withdrawals in cash. In a falling market, spend the cash instead of selling shares; in a rising market, spend from investments and top the cash back up at the year end."
          onToggle={(v) => setInvestment({ useCashBuffer: v })}>
          <NumberField label="Cash buffer" value={input.investment.bufferYears} min={0} max={10} step={0.5} suffix="years of withdrawals"
            onChange={(v) => setInvestment({ bufferYears: v ?? 0 })} />
          <p className="note">A "falling market" means shares are down over the last 12 months.</p>
        </Adjustment>
      </Section>

      <Section title="Regular income & outgoings" defaultOpen>
        <FlowList input={input} onChange={(flows) => set('flows', flows)} />
      </Section>

      <Section title="One-offs / goals" defaultOpen={input.oneOffs.length > 0}>
        {input.oneOffs.map((o, i) => (
          <div className="one-off" key={i}>
            <input className="one-off-label" placeholder="e.g. New car" value={o.label ?? ''}
              onChange={(e) => set('oneOffs', input.oneOffs.map((x, j) => (j === i ? { ...x, label: e.target.value } : x)))} />
            <NumberField label="Age" value={o.age} min={input.retirementAge} max={input.deathAge - 1} step={1}
              onChange={(v) => set('oneOffs', input.oneOffs.map((x, j) => (j === i ? { ...x, age: Math.round(v ?? x.age) } : x)))} />
            <NumberField label="Amount" prefix="£" value={o.amount} step={1000}
              onChange={(v) => set('oneOffs', input.oneOffs.map((x, j) => (j === i ? { ...x, amount: v ?? 0 } : x)))}
              hint="+ spend, − add" />
            <button className="icon-btn" title="Remove" onClick={() => set('oneOffs', input.oneOffs.filter((_, j) => j !== i))}>
              ×
            </button>
          </div>
        ))}
        <button className="btn small"
          onClick={() => set('oneOffs', [...input.oneOffs, { age: Math.min(input.retirementAge + 10, input.deathAge - 1), amount: 10_000, label: '' }])}>
          + Add one-off
        </button>
      </Section>
    </div>
  )
}

interface SpendingFieldsProps {
  p: SpendingParameters
  set: (patch: Partial<SpendingParameters>) => void
}

function SpendingBaseFields({ p, set, balance, hasIncome }: SpendingFieldsProps & { balance: number; hasIncome: boolean }) {
  if (p.type === 'RemainingLife')
    return (
      <NumberField label="Assumed real return" percent value={p.assumedRealReturn} min={-5} max={10} step={0.5}
        onChange={(v) => set({ assumedRealReturn: v ?? 0 })} />
    )
  if (p.type === 'FixedAmounts')
    return (
      <>
        <NumberField label="Take from the pot each year" prefix="£" value={p.fixedAmount} min={0} step={500}
          onChange={(v) => set({ fixedAmount: v ?? 0 })} hint="From retirement, today's money" />
        {hasIncome && <p className="note">Your other income is added on top of what the pot pays.</p>}
        <p className="note">Tip: the <strong>Spending plan</strong> tab can draft these amounts for you from research on how spending changes with age.</p>
      </>
    )
  if (p.type === 'ConstantPercentage')
    return (
      <>
        <NumberField label="Take from the pot each year" percent value={p.initialRate} min={0} max={50} step={0.1}
          onChange={(v) => set({ initialRate: v ?? 0 })} />
        {hasIncome && <p className="note">Your other income is added on top of what the pot pays.</p>}
      </>
    )
  return (
    <>
      <div className="row-2">
        <NumberField label="Spending in year one" prefix="£" value={Math.round(p.initialRate * balance)} min={0} step={500}
          onChange={(v) => set({ initialRate: balance > 0 ? (v ?? 0) / balance : 0 })} hint="Per year, today's money" />
        <NumberField label="= % of starting pot" percent value={p.initialRate} min={0} max={50} step={0.1}
          onChange={(v) => set({ initialRate: v ?? 0 })} />
      </div>
      {hasIncome && <p className="note">This is what you live on. Pensions and other income pay part of it, so the pot only provides the rest.</p>}
    </>
  )
}

function AmountSteps({ input, set }: { input: SimulationInput; set: SpendingFieldsProps['set'] }) {
  const steps = input.spending.amountSteps ?? []
  const update = (i: number, patch: Partial<SpendingStep>) => set({ amountSteps: steps.map((c, j) => (j === i ? { ...c, ...patch } : c)) })
  return (
    <div className="rate-changes">
      {steps.map((c, i) => (
        <div className="rate-change" key={i}>
          <NumberField label="From age" value={c.age} min={input.retirementAge + 1} max={input.deathAge - 1} step={1}
            onChange={(v) => update(i, { age: Math.round(v ?? c.age) })} />
          <NumberField label="Take from the pot" prefix="£" value={c.amount} min={0} step={500}
            onChange={(v) => update(i, { amount: v ?? 0 })} />
          <button className="icon-btn" title="Remove" onClick={() => set({ amountSteps: steps.filter((_, j) => j !== i) })}>
            ×
          </button>
        </div>
      ))}
      {steps.length > 0 && <p className="note">Each amount is taken every year until the next one starts.</p>}
      <button className="btn small"
        onClick={() => {
          const last = steps.length ? steps[steps.length - 1] : { age: input.retirementAge, amount: input.spending.fixedAmount }
          set({ amountSteps: [...steps, { age: Math.min(last.age + 5, input.deathAge - 1), amount: last.amount }] })
        }}>
        + Change the amount from an age
      </button>
    </div>
  )
}

function RateChanges({ input, set }: { input: SimulationInput; set: SpendingFieldsProps['set'] }) {
  const changes = input.spending.rateChanges ?? []
  const update = (i: number, patch: Partial<RateChange>) => set({ rateChanges: changes.map((c, j) => (j === i ? { ...c, ...patch } : c)) })
  return (
    <div className="rate-changes">
      {changes.map((c, i) => (
        <div className="rate-change" key={i}>
          <NumberField label="From age" value={c.age} min={input.retirementAge + 1} max={input.deathAge - 1} step={1}
            onChange={(v) => update(i, { age: Math.round(v ?? c.age) })} />
          <NumberField label="Take from the pot" percent value={c.rate} min={0} max={50} step={0.1}
            onChange={(v) => update(i, { rate: v ?? 0 })} />
          <button className="icon-btn" title="Remove" onClick={() => set({ rateChanges: changes.filter((_, j) => j !== i) })}>
            ×
          </button>
        </div>
      ))}
      {changes.length > 0 && input.spending.type === 'ConstantInflationAdjusted' && (
        <p className="note">
          At each age, spending restarts at the new rate on the pot, including money added that year (an inheritance, say).
          Guardrails and the ratchet are then measured from there.
        </p>
      )}
      <button className="btn small"
        onClick={() => set({
          rateChanges: [...changes, { age: Math.min(input.retirementAge + 5, input.deathAge - 1), rate: input.spending.initialRate }],
        })}>
        + Change the rate from an age
      </button>
    </div>
  )
}

function Adjustment({ title, description, on, onToggle, disabledReason, children }: {
  title: string
  description: string
  on: boolean
  onToggle: (on: boolean) => void
  /** Set when the chosen base has no use for this adjustment; it is shown off and can't be ticked. */
  disabledReason?: string
  children?: ReactNode
}) {
  const active = on && !disabledReason
  return (
    <div className={`adjustment${active ? ' on' : ''}${disabledReason ? ' disabled' : ''}`}>
      <label className="adjustment-head">
        <input type="checkbox" checked={active} disabled={!!disabledReason} onChange={(e) => onToggle(e.target.checked)} />
        <span className="adjustment-title">{title}</span>
      </label>
      <p className="adjustment-desc">{disabledReason ?? description}</p>
      {active && children && <div className="adjustment-body">{children}</div>}
    </div>
  )
}

function SpendingAdjustments({ p, set }: SpendingFieldsProps) {
  const fields: Record<SpendingAdjustment, ReactNode> = {
    useInflationSkip: null,
    useGuytonKlinger: (
      <>
        <div className="row-2">
          <NumberField label="Upper guardrail" percent value={p.upperGuardrail} min={0} step={5} onChange={(v) => set({ upperGuardrail: v ?? 0 })} />
          <NumberField label="Lower guardrail" percent value={p.lowerGuardrail} min={0} step={5} onChange={(v) => set({ lowerGuardrail: v ?? 0 })} />
        </div>
        <div className="row-2">
          <NumberField label="Below lower: raise by" percent value={p.guardrailRaise} min={0} max={100} step={1}
            onChange={(v) => set({ guardrailRaise: v ?? 0 })} hint="0% = never raise" />
          <NumberField label="Above upper: cut by" percent value={p.guardrailCut} min={0} max={100} step={1}
            onChange={(v) => set({ guardrailCut: v ?? 0 })} hint="0% = never cut" />
        </div>
        <CheckField label="Skip inflation rise after a losing year when above the starting rate" checked={p.freezeAfterLoss}
          onChange={(v) => set({ freezeAfterLoss: v })} />
      </>
    ),
    useRatchet: (
      <div className="row-2">
        <NumberField label="Ratchet when pot grows" percent value={p.ratchetTrigger} min={0} step={5} onChange={(v) => set({ ratchetTrigger: v ?? 0 })} />
        <NumberField label="Increase by" percent value={p.ratchetIncrease} min={0} step={1} onChange={(v) => set({ ratchetIncrease: v ?? 0 })} />
      </div>
    ),
    useCustomRules: (
      <>
        <RuleBuilder rules={p.rules} onChange={(rules) => set({ rules })} />
        <CheckField label="Apply every matching rule (otherwise only the first)" checked={p.applyAllMatches}
          onChange={(v) => set({ applyAllMatches: v })} />
      </>
    ),
    useFloorCeiling: (
      <div className="row-2">
        <NumberField label="Floor vs year one" percent value={p.floor} max={0} step={5} onChange={(v) => set({ floor: v ?? 0 })} />
        <NumberField label="Ceiling vs year one" percent value={p.ceiling} min={0} step={5} onChange={(v) => set({ ceiling: v ?? 0 })} />
      </div>
    ),
  }
  return (
    <div className="adjustments">
      {(Object.keys(spendingAdjustments) as SpendingAdjustment[]).map((key) => (
        <Adjustment key={key} title={spendingAdjustments[key].label} description={spendingAdjustments[key].description}
          on={p[key]} onToggle={(v) => set({ [key]: v } as Partial<SpendingParameters>)}
          disabledReason={adjustmentUsed(p, key) ? undefined : 'Not used with fixed amounts: your schedule sets every year, so there is nothing for this to adjust.'}>
          {fields[key]}
        </Adjustment>
      ))}
    </div>
  )
}

function RuleBuilder({ rules, onChange }: { rules: SpendingRule[]; onChange: (rules: SpendingRule[]) => void }) {
  const update = (i: number, patch: Partial<SpendingRule>) => onChange(rules.map((r, j) => (j === i ? { ...r, ...patch } : r)))
  return (
    <div className="rules">
      {rules.map((r, i) => {
        const metric = ruleMetrics[r.metric]
        const action = ruleActions[r.action]
        return (
          <div className="rule" key={i}>
            <div className="rule-row">
              <span className="rule-kw">IF</span>
              <select value={r.metric} onChange={(e) => update(i, { metric: e.target.value as RuleMetric })}>
                {Object.entries(ruleMetrics).map(([k, m]) => <option key={k} value={k}>{m.label}</option>)}
              </select>
              <select value={r.comparison} onChange={(e) => update(i, { comparison: e.target.value as RuleComparison })}>
                <option value="GreaterThan">&gt;</option>
                <option value="LessThan">&lt;</option>
              </select>
              <div className="rule-num">
                <NumberField label="" value={r.threshold} percent={metric.percent} step={metric.percent ? 1 : 1}
                  onChange={(v) => update(i, { threshold: v ?? 0 })} />
              </div>
            </div>
            <div className="rule-row">
              <span className="rule-kw">THEN</span>
              <select value={r.action} onChange={(e) => update(i, { action: e.target.value as RuleAction })}>
                {Object.entries(ruleActions).map(([k, x]) => <option key={k} value={k}>{x.label}</option>)}
              </select>
              {action.unit !== 'none' && (
                <div className="rule-num">
                  <NumberField label="" value={r.value} percent={action.unit === 'percent'} suffix={action.unit === 'multiple' ? '×' : undefined}
                    step={action.unit === 'multiple' ? 0.05 : 1} onChange={(v) => update(i, { value: v ?? 0 })} />
                </div>
              )}
              <button className="icon-btn" title="Remove rule" onClick={() => onChange(rules.filter((_, j) => j !== i))}>×</button>
            </div>
          </div>
        )
      })}
      <button className="btn small"
        onClick={() => onChange([...rules, { metric: 'TrailingReturn', comparison: 'LessThan', threshold: 0, action: 'FreezeInflation', value: 0 }])}>
        + Add rule
      </button>
    </div>
  )
}

function InvestmentFields({ p, set }: { p: InvestmentParameters; set: (patch: Partial<InvestmentParameters>) => void }) {
  switch (p.type) {
    case 'FixedRebalance':
      return (
        <SelectField label="Rebalance" value={p.frequency}
          options={[{ value: 'Monthly', label: 'Monthly' }, { value: 'Quarterly', label: 'Quarterly' }, { value: 'Annually', label: 'Yearly' }]}
          onChange={(frequency) => set({ frequency })} />
      )
    case 'ThresholdRebalance':
      return <NumberField label="Rebalance when any asset drifts by" percent value={p.band} min={0} max={50} step={1} onChange={(v) => set({ band: v ?? 0 })} suffix="pts" />
    case 'DecliningGlidePath':
    case 'RisingGlidePath':
      return (
        <>
          <div className="row-2">
            <NumberField label="Shares at start" percent value={p.startEquity} min={0} max={100} step={5} onChange={(v) => set({ startEquity: v ?? 0 })} />
            <NumberField label="Shares at end" percent value={p.endEquity} min={0} max={100} step={5} onChange={(v) => set({ endEquity: v ?? 0 })} />
          </div>
          <NumberField label="Over" value={p.glideYears} min={1} max={60} step={1} suffix="years" onChange={(v) => set({ glideYears: Math.round(v ?? 1) })} />
          <p className="note">The rest is split between bonds and cash in the proportions of your asset mix. Rebalanced yearly.</p>
        </>
      )
    default:
      return null
  }
}

/** A short, large income that is being treated as spending money, e.g. an inheritance entered as one year of income. */
function looksLikeLumpSum(f: RecurringFlow, startingBalance: number) {
  const years = f.endAge == null ? Infinity : f.endAge - f.startAge
  return f.kind === 'Income' && !f.intoPot && years <= 2 && f.annualAmount >= Math.max(50_000, 0.25 * startingBalance)
}

function FlowList({ input, onChange }: { input: SimulationInput; onChange: (flows: RecurringFlow[]) => void }) {
  const flows = input.flows
  const update = (i: number, patch: Partial<RecurringFlow>) => onChange(flows.map((f, j) => (j === i ? { ...f, ...patch } : f)))
  const add = (f: RecurringFlow) => onChange([...flows, f])
  return (
    <>
      {flows.map((f, i) => (
        <div className={`flow ${f.kind === 'Income' ? 'income' : 'expense'}${f.disabled ? ' off' : ''}`} key={i}>
          <div className="flow-head">
            <input className="flow-label" placeholder={f.kind === 'Income' ? 'e.g. Workplace pension' : 'e.g. Mortgage'}
              value={f.label ?? ''} onChange={(e) => update(i, { label: e.target.value })} aria-label="Name" />
            <select value={f.kind} onChange={(e) => update(i, { kind: e.target.value as RecurringFlow['kind'] })} aria-label="Type">
              <option value="Income">Income</option>
              <option value="Expense">Outgoing</option>
            </select>
            <button className="switch" role="switch" aria-checked={!f.disabled} aria-label="Include in the plan"
              title={f.disabled ? 'Left out of the results – click to include' : 'Included – click to leave out without deleting'}
              onClick={() => update(i, { disabled: !f.disabled })} />
            <button className="icon-btn" title="Remove" aria-label="Remove" onClick={() => onChange(flows.filter((_, j) => j !== i))}>×</button>
          </div>
          {f.disabled && <p className="flow-off-note">Left out of the results. Switch it back on to include it.</p>}
          <div className="row-3">
            <NumberField label="Per year" prefix="£" value={f.annualAmount} min={0} step={500}
              onChange={(v) => update(i, { annualAmount: v ?? 0 })} />
            <NumberField label="From age" value={f.startAge} min={0} max={120} step={1}
              onChange={(v) => update(i, { startAge: Math.round(v ?? f.startAge) })} />
            <NumberField label="Until age" value={f.endAge} min={f.startAge + 1} max={120} step={1} allowEmpty
              onChange={(v) => update(i, { endAge: v == null ? null : Math.round(v) })} hint={f.endAge == null ? 'For life' : undefined} />
          </div>
          <CheckField label="Rises with inflation" checked={f.inflationLinked} onChange={(v) => update(i, { inflationLinked: v })} />
          {f.kind === 'Income' && (
            <CheckField label="Pay into the pot (don't spend it)" checked={!!f.intoPot} onChange={(v) => update(i, { intoPot: v })} />
          )}
          {looksLikeLumpSum(f, input.startingBalance) && (
            <p className="note warn">
              This looks like a lump sum. As plain income it is spent in the years it arrives; tick "Pay into the pot" to invest it.
            </p>
          )}
        </div>
      ))}
      <div className="flow-add">
        {!flows.some((f) => f.label === 'State Pension') && (
          <button className="btn small" onClick={() => add({ label: 'State Pension', kind: 'Income', startAge: 67, endAge: null, annualAmount: 12_000, inflationLinked: true })}>
            + State Pension
          </button>
        )}
        <button className="btn small" onClick={() => add({ label: 'Pension', kind: 'Income', startAge: Math.max(input.retirementAge, 65), endAge: null, annualAmount: 5_000, inflationLinked: true })}>
          + Other income
        </button>
        <button className="btn small" onClick={() => add({ label: '', kind: 'Expense', startAge: input.retirementAge, endAge: input.retirementAge + 10, annualAmount: 2_000, inflationLinked: true })}>
          + Outgoing
        </button>
      </div>
      {flows.length > 0 && (
        <p className="note">Amounts are per year in today's money. Leave "Until age" empty for life. Income that isn't inflation-linked stays the same in pounds, so it buys less each year.</p>
      )}
    </>
  )
}
