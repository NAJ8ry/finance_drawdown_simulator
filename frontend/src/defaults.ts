import type {
  InvestmentParameters,
  InvestmentStrategyType,
  RuleAction,
  RuleMetric,
  SimulationInput,
  SpendingParameters,
  SpendingStrategyType,
} from './types'

export const defaultInput: SimulationInput = {
  startingBalance: 100_000,
  retirementAge: 60,
  deathAge: 94,
  inflationRate: 0.025,
  feeRate: 0.005,
  allocation: { equity: 0.6, bond: 0.4, cash: 0 },
  oneOffs: [],
  flows: [],
  spendingFloor: null,
  legacyTarget: 0,
  withdrawalTiming: 'Monthly',
  spending: {
    type: 'ConstantInflationAdjusted',
    initialRate: 0.04,
    assumedRealReturn: 0.02,
    useGoodBadYear: false,
    goodThreshold: 0.1,
    badThreshold: -0.1,
    raiseStep: 0.1,
    cutStep: 0.1,
    maxRaise: 0.5,
    maxCut: 0.3,
    useGuytonKlinger: false,
    upperGuardrail: 0.2,
    lowerGuardrail: 0.2,
    guardrailRaise: 0.1,
    guardrailCut: 0.1,
    freezeAfterLoss: true,
    useInflationSkip: false,
    useRatchet: false,
    ratchetTrigger: 0.5,
    ratchetIncrease: 0.1,
    useFloorCeiling: false,
    floor: -0.15,
    ceiling: 0.25,
    useCustomRules: false,
    rules: [
      { metric: 'TrailingReturn', comparison: 'GreaterThan', threshold: 0.1, action: 'AdjustPercent', value: 0.1 },
      { metric: 'TrailingReturn', comparison: 'LessThan', threshold: -0.1, action: 'AdjustPercent', value: -0.1 },
    ],
    applyAllMatches: false,
  },
  investment: {
    type: 'FixedRebalance',
    frequency: 'Annually',
    band: 0.05,
    startEquity: 0.6,
    endEquity: 0.4,
    glideYears: 20,
    useCashBuffer: false,
    bufferYears: 2,
  },
}

/** Converts old single-strategy selections (e.g. type 'GuytonKlinger') to base + adjustments. */
function normaliseSpending(p: SpendingParameters): SpendingParameters {
  const adjusted = (patch: Partial<SpendingParameters>): SpendingParameters => ({ ...p, type: 'ConstantInflationAdjusted', ...patch })
  switch (p.type) {
    case 'SimpleGuardrails': return adjusted({ useGoodBadYear: true })
    case 'GuytonKlinger': return adjusted({ useGuytonKlinger: true })
    case 'InflationSkipAfterLoss': return adjusted({ useInflationSkip: true })
    case 'Ratchet': return adjusted({ useRatchet: true })
    case 'FloorAndCeiling': return { ...p, type: 'ConstantPercentage', useFloorCeiling: true }
    case 'CustomRules':
      return { ...p, type: p.baseType === 'ConstantPercentage' ? 'ConstantPercentage' : 'ConstantInflationAdjusted', useCustomRules: true }
    default: return p
  }
}

function normaliseInvestment(p: InvestmentParameters): InvestmentParameters {
  return p.type === 'CashBuffer' ? { ...p, type: 'FixedRebalance', frequency: 'Annually', useCashBuffer: true } : p
}

/** Fills in any fields missing from older saved inputs and converts old strategy selections. */
export const mergeInput = (raw: Partial<SimulationInput>): SimulationInput => ({
  ...defaultInput,
  ...raw,
  allocation: { ...defaultInput.allocation, ...raw.allocation },
  spending: normaliseSpending({ ...defaultInput.spending, ...raw.spending }),
  investment: normaliseInvestment({ ...defaultInput.investment, ...raw.investment }),
})

export type SpendingBase = 'ConstantInflationAdjusted' | 'ConstantPercentage' | 'RemainingLife'

export const spendingBases: Record<SpendingBase, { label: string; description: string }> = {
  ConstantInflationAdjusted: {
    label: 'Constant inflation-adjusted',
    description:
      'The classic "4% rule": set your year-one spending, then keep the same spending power every year. Adjustments below can change it.',
  },
  ConstantPercentage: {
    label: 'Percentage of the pot',
    description: 'Take a fixed percentage of whatever the pot is worth each year. Never runs out, but income moves with the market.',
  },
  RemainingLife: {
    label: 'Spend down over remaining life',
    description: 'Each year, spend the level amount that would exactly use up the pot by the age of death at an assumed real return.',
  },
}

export type SpendingAdjustment = 'useGoodBadYear' | 'useGuytonKlinger' | 'useInflationSkip' | 'useRatchet' | 'useCustomRules' | 'useFloorCeiling'

/** In the order the engine applies them. */
export const spendingAdjustments: Record<SpendingAdjustment, { label: string; description: string }> = {
  useInflationSkip: {
    label: 'Skip inflation rise after a loss',
    description: 'No increase for inflation in a year after the portfolio lost money.',
  },
  useGoodBadYear: {
    label: 'Good year / bad year',
    description: 'After a year where the portfolio rose by at least the "good" threshold, raise spending; after a year it fell by at least the "bad" threshold, cut it.',
  },
  useGuytonKlinger: {
    label: 'Guyton-Klinger guardrails',
    description: 'Cut spending when what you take from the pot drifts too far above your starting rate; raise it when it drifts far below.',
  },
  useRatchet: {
    label: 'Ratchet up',
    description: 'Step spending up permanently whenever the pot grows well past its last high-water mark.',
  },
  useCustomRules: {
    label: 'Custom rules',
    description: 'Your own "IF … THEN …" rules checked every year.',
  },
  useFloorCeiling: {
    label: 'Floor and ceiling',
    description: 'Whatever the other rules do, keep spending within these limits relative to year one. Applied last.',
  },
}

/** Legacy labels, used only to describe old saved scenarios. */
export const spendingStrategies: Record<SpendingStrategyType, { label: string }> = {
  ConstantInflationAdjusted: { label: spendingBases.ConstantInflationAdjusted.label },
  ConstantPercentage: { label: spendingBases.ConstantPercentage.label },
  RemainingLife: { label: spendingBases.RemainingLife.label },
  SimpleGuardrails: { label: spendingAdjustments.useGoodBadYear.label },
  GuytonKlinger: { label: spendingAdjustments.useGuytonKlinger.label },
  FloorAndCeiling: { label: spendingAdjustments.useFloorCeiling.label },
  InflationSkipAfterLoss: { label: spendingAdjustments.useInflationSkip.label },
  Ratchet: { label: spendingAdjustments.useRatchet.label },
  CustomRules: { label: spendingAdjustments.useCustomRules.label },
}

/** Short description of the whole spending plan, e.g. "Constant inflation-adjusted + Guyton-Klinger guardrails". */
export function spendingTitle(p: SpendingParameters) {
  const base = spendingBases[p.type as SpendingBase]?.label ?? spendingStrategies[p.type].label
  const adj = (Object.keys(spendingAdjustments) as SpendingAdjustment[]).filter((k) => p[k]).map((k) => spendingAdjustments[k].label)
  return [base, ...adj].join(' + ')
}

export type InvestmentBase = Exclude<InvestmentStrategyType, 'CashBuffer'>

export const investmentStrategies: Record<InvestmentBase, { label: string; description: string }> = {
  FixedRebalance: { label: 'Fixed mix, rebalanced', description: 'Hold the target mix and rebalance back to it on a schedule.' },
  ThresholdRebalance: { label: 'Rebalance when drifted', description: 'Only rebalance when an asset drifts more than the band from its target.' },
  BuyAndHold: { label: 'Buy and hold', description: 'Never rebalance; the mix drifts with the markets.' },
  DecliningGlidePath: { label: 'Declining equity glide path', description: 'Shares fall steadily from the start % to the end % over the glide period.' },
  RisingGlidePath: { label: 'Rising equity glide path', description: 'Shares rise over time (the "bond tent") to reduce early sequence risk.' },
  WithdrawFromWinner: { label: 'Withdraw from the winner', description: 'Take withdrawals from whichever asset is most over its target weight.' },
}

export function investmentTitle(p: InvestmentParameters) {
  const base = investmentStrategies[p.type as InvestmentBase]?.label ?? 'Fixed mix, rebalanced'
  return p.useCashBuffer ? `${base} + ${+p.bufferYears.toFixed(1)}-year cash buffer` : base
}

export const ruleMetrics: Record<RuleMetric, { label: string; percent: boolean }> = {
  TrailingReturn: { label: 'Last 12 months return', percent: true },
  DrawdownFromPeak: { label: 'Fall from peak', percent: true },
  WithdrawalRate: { label: 'Current withdrawal rate', percent: true },
  BalanceVsInitial: { label: 'Pot vs starting pot', percent: true },
  Age: { label: 'Age', percent: false },
}

export const ruleActions: Record<RuleAction, { label: string; unit: 'percent' | 'multiple' | 'none' }> = {
  AdjustPercent: { label: 'Change spending by', unit: 'percent' },
  SetPercentOfBalance: { label: 'Set spending to % of pot', unit: 'percent' },
  FreezeInflation: { label: 'Skip inflation rise', unit: 'none' },
  ClampMin: { label: 'At least × first-year spending', unit: 'multiple' },
  ClampMax: { label: 'At most × first-year spending', unit: 'multiple' },
}

const gbp0 = new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP', maximumFractionDigits: 0 })

export const fmt = {
  gbp: (v: number | null | undefined) => (v == null || Number.isNaN(v) ? '–' : gbp0.format(v)),
  gbpShort: (v: number) => {
    const a = Math.abs(v)
    if (a >= 1e6) return `£${(v / 1e6).toFixed(a >= 1e7 ? 0 : 1)}m`
    if (a >= 1e3) return `£${Math.round(v / 1e3)}k`
    return `£${Math.round(v)}`
  },
  pct: (v: number | null | undefined, digits = 1) => (v == null || Number.isNaN(v) ? '–' : `${(v * 100).toFixed(digits)}%`),
  /** Percentage without trailing zeros: 0.04 → "4%", 0.035 → "3.5%". */
  pctTrim: (v: number) => `${+(v * 100).toFixed(2)}%`,
  month: (label: string) => {
    const [y, m] = label.split('-').map(Number)
    return new Date(y, m - 1, 1).toLocaleDateString('en-GB', { month: 'short', year: 'numeric' })
  },
}

/** Converts a real (today's money) value at year k into nominal money using the planned inflation. */
export const toNominal = (value: number, years: number, inflation: number) => value * Math.pow(1 + inflation, years)
