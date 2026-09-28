// Mirrors Finance.Engine models (camelCase JSON, enums as strings)

export type SpendingStrategyType =
  | 'ConstantInflationAdjusted'
  | 'ConstantPercentage'
  | 'SimpleGuardrails'
  | 'GuytonKlinger'
  | 'FloorAndCeiling'
  | 'InflationSkipAfterLoss'
  | 'Ratchet'
  | 'RemainingLife'
  | 'CustomRules'

export type InvestmentStrategyType =
  | 'FixedRebalance'
  | 'ThresholdRebalance'
  | 'BuyAndHold'
  | 'DecliningGlidePath'
  | 'RisingGlidePath'
  | 'CashBuffer'
  | 'WithdrawFromWinner'

export type RebalanceFrequency = 'Monthly' | 'Quarterly' | 'Annually'
export type WithdrawalTiming = 'Monthly' | 'AnnualInAdvance'
export type StartFrequency = 'Yearly' | 'Monthly'
export type RuleMetric = 'TrailingReturn' | 'DrawdownFromPeak' | 'WithdrawalRate' | 'BalanceVsInitial' | 'Age'
export type RuleComparison = 'GreaterThan' | 'LessThan'
export type RuleAction = 'AdjustPercent' | 'SetPercentOfBalance' | 'FreezeInflation' | 'ClampMin' | 'ClampMax'

export interface Allocation {
  equity: number
  bond: number
  cash: number
}

export interface OneOff {
  age: number
  amount: number
  label?: string | null
}

export type FlowKind = 'Income' | 'Expense'

export interface RecurringFlow {
  label?: string | null
  kind: FlowKind
  startAge: number
  endAge: number | null
  annualAmount: number
  inflationLinked: boolean
}

export interface SpendingRule {
  metric: RuleMetric
  comparison: RuleComparison
  threshold: number
  action: RuleAction
  value: number
}

/** Base strategy plus any number of adjustments (use* flags), applied in a fixed order each year. */
export interface SpendingParameters {
  /** Base: ConstantInflationAdjusted, ConstantPercentage or RemainingLife (other values are legacy). */
  type: SpendingStrategyType
  initialRate: number
  assumedRealReturn: number
  useGoodBadYear: boolean
  goodThreshold: number
  badThreshold: number
  raiseStep: number
  cutStep: number
  maxRaise: number
  maxCut: number
  useGuytonKlinger: boolean
  upperGuardrail: number
  lowerGuardrail: number
  guardrailRaise: number
  guardrailCut: number
  freezeAfterLoss: boolean
  useInflationSkip: boolean
  useRatchet: boolean
  ratchetTrigger: number
  ratchetIncrease: number
  useFloorCeiling: boolean
  floor: number
  ceiling: number
  useCustomRules: boolean
  rules: SpendingRule[]
  applyAllMatches: boolean
  /** Legacy base for the old CustomRules strategy. */
  baseType?: SpendingStrategyType | null
}

export interface InvestmentParameters {
  /** Base strategy; CashBuffer is legacy (now the useCashBuffer option). */
  type: InvestmentStrategyType
  frequency: RebalanceFrequency
  band: number
  startEquity: number
  endEquity: number
  glideYears: number
  useCashBuffer: boolean
  bufferYears: number
}

export interface SimulationInput {
  startingBalance: number
  retirementAge: number
  deathAge: number
  inflationRate: number
  feeRate: number
  allocation: Allocation
  oneOffs: OneOff[]
  flows: RecurringFlow[]
  spendingFloor: number | null
  legacyTarget: number
  withdrawalTiming: WithdrawalTiming
  startFrequency: StartFrequency
  spending: SpendingParameters
  investment: InvestmentParameters
}

export interface PathResult {
  start: string
  months: number
  partial: boolean
  failed: boolean
  failAge: number | null
  succeeded: boolean
  belowFloor: boolean
  endBalance: number
  minBalance: number
  maxDrawdown: number
  balances: number[]
  spending: number[]
  withdrawals: number[]
}

export interface PercentileBand {
  age: number
  p5: number
  p10: number
  p25: number
  p50: number
  p75: number
  p90: number
  p95: number
}

export interface SimulationResult {
  successRate: number | null
  completeCount: number
  successCount: number
  failedCount: number
  partialCount: number
  partialFailedCount: number
  firstStart: string | null
  lastCompleteStart: string | null
  dataLastMonth: string | null
  medianEndBalance: number | null
  p10EndBalance: number | null
  p90EndBalance: number | null
  worstDepletionAge: number | null
  belowFloorRate: number | null
  medianAverageSpending: number | null
  minimumSpending: number | null
  medianMaxDrawdown: number | null
  bestIndex: number | null
  medianIndex: number | null
  worstIndex: number | null
  bands: PercentileBand[]
  ageTable: PercentileBand[]
  worstStarts: number[]
  paths: PathResult[]
}

export interface DataUpdateLog {
  id: number
  runAt: string
  success: boolean
  monthsWritten: number
  lastMonth: string | null
  message: string
}

export interface MarketSummary {
  firstMonth: string | null
  lastMonth: string | null
  months: number
  historyMonths: number
  liveMonths: number
  lastUpdate: DataUpdateLog | null
  lastSuccessfulUpdate: DataUpdateLog | null
  annualInflation: number
  assets: { asset: string; nominalReturn: number; realReturn: number; worstMonth: number; bestMonth: number }[]
  sources: { series: string; period: string; source: string }[]
}

export interface MarketMonth {
  month: string
  equity: number
  bond: number
  cash: number
  inflation: number
}

export interface Scenario {
  id: string
  name: string
  input: SimulationInput
  createdAt: string
  updatedAt: string
}
