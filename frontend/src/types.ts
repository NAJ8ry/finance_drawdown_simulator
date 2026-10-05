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
  | 'FixedAmounts'

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

/** From this age on, take this amount a year from the pot (today's money). */
export interface SpendingStep {
  age: number
  amount: number
}

/** From this age on, draw from the pot at this rate instead of the initial rate. */
export interface RateChange {
  age: number
  rate: number
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
  /** Income that is invested rather than spent, e.g. an inheritance or house sale. */
  intoPot?: boolean
  /** Kept in the list but left out of the results, so it can be switched back on. */
  disabled?: boolean
}

export interface SpendingRule {
  metric: RuleMetric
  comparison: RuleComparison
  threshold: number
  action: RuleAction
  value: number
}

/** What a spending plan was drafted for, kept so the app can tell when it has gone stale. Not used by the engine. */
export interface PlanBasis {
  /** The research pattern and target it was fitted to; null when the amounts were set or changed by hand. */
  fit: { pattern: string; targetSuccess: number; maxLifetimeRuin: number | null } | null
  retirementAge: number
  /** Pensions and other income to spend in each year of retirement, as they were when the plan was drafted. */
  income: number[]
}

/** Base strategy plus any number of adjustments (use* flags), applied in a fixed order each year. */
export interface SpendingParameters {
  /** Base: ConstantInflationAdjusted, ConstantPercentage or RemainingLife (other values are legacy). */
  type: SpendingStrategyType
  initialRate: number
  rateChanges: RateChange[]
  /** Fixed amounts base: yearly amount from the pot from retirement, then each step from its age. */
  fixedAmount: number
  amountSteps: SpendingStep[]
  assumedRealReturn: number
  /** Legacy "good year / bad year" (removed): converted to custom rules plus floor and ceiling when loaded. */
  useGoodBadYear?: boolean
  goodThreshold?: number
  badThreshold?: number
  raiseStep?: number
  cutStep?: number
  maxRaise?: number
  maxCut?: number
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
  /** Set when the Fixed amounts list comes from the Spending plan tab. */
  planBasis?: PlanBasis | null
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
  /** Age today; null = retiring now. Future (nominal) pounds grow with inflation from this age. */
  currentAge: number | null
  /** Also judge the plan against UK life tables (ONS); 'Couple' = a man and a woman of the same age. */
  lifeTable: LifeTable
  inflationRate: number
  /** Change to every year's share return (-0.01 = 1% a year lower than history). */
  equityReturnAdjustment: number
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
  /** Years with an unplanned spending cut of 10%+, and the largest such cut (fraction). */
  cuts: number
  worstCut: number
  yearsBelowFloor: number
  yearsWithoutPot: number
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

export type LifeTable = 'None' | 'Male' | 'Female' | 'Couple'

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
  /** % of paths with an unplanned one-year spending cut of 10%+, and the largest such cut (fraction). */
  cutRate: number | null
  worstCut: number | null
  /** Of the paths that ran out: median and longest years lived on other income alone. */
  medianYearsWithoutPot: number | null
  maxYearsWithoutPot: number | null
  /** Years below the spending minimum in the worst 1 in 10 paths. */
  p90YearsBelowFloor: number | null
  /** With a life table: % chance of running out while alive, % chance of outliving the age of death, and survival by age. */
  lifetimeRuinRate: number | null
  outliveHorizonRate: number | null
  survival: number[] | null
  bestIndex: number | null
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
