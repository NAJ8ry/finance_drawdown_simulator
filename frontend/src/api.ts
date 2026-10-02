import type { DataUpdateLog, MarketMonth, MarketSummary, Scenario, SimulationInput, SimulationResult, SpendingParameters } from './types'

export interface SpendingPatternInfo {
  id: string
  label: string
  description: string
  source: string | null
}

export interface FitResponse {
  feasible: boolean
  spending: SpendingParameters
  successRate: number | null
  medianEndBalance: number | null
  p10EndBalance: number | null
  cutRate: number | null
  lifetimeRuinRate: number | null
  years: { age: number; fromPot: number; spending: number }[]
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  })
  if (!res.ok) {
    let message = `${res.status} ${res.statusText}`
    try {
      const body = await res.json()
      message = body.detail ?? body.title ?? message
    } catch {
      /* not JSON */
    }
    throw new Error(message)
  }
  return res.status === 204 ? (undefined as T) : res.json()
}

const SCENARIOS_STORAGE = 'finance.scenarios'

function readScenarios(): Scenario[] {
  try {
    const list = JSON.parse(localStorage.getItem(SCENARIOS_STORAGE) ?? '[]')
    return Array.isArray(list) ? list : []
  } catch {
    return []
  }
}

function writeScenarios(list: Scenario[]) {
  localStorage.setItem(SCENARIOS_STORAGE, JSON.stringify(list))
}

/** Updates the scenario with this id, or adds a new one if the id is null or no longer saved. */
function saveScenario(id: string | null, name: string, input: SimulationInput): Scenario {
  const list = readScenarios()
  const now = new Date().toISOString()
  const existing = list.find((s) => s.id === id)
  const saved: Scenario = {
    id: existing?.id ?? crypto.randomUUID(),
    name: name.trim(),
    input,
    createdAt: existing?.createdAt ?? now,
    updatedAt: now,
  }
  writeScenarios([...list.filter((s) => s.id !== saved.id), saved])
  return saved
}

export const api = {
  simulate: (input: SimulationInput, signal?: AbortSignal) =>
    request<SimulationResult>('/api/simulate', { method: 'POST', body: JSON.stringify(input), signal }),
  spendingPatterns: () => request<SpendingPatternInfo[]>('/api/simulate/patterns'),
  /** Either a minimum success rate, or (with life tables) a maximum chance of running out while alive. */
  fitSpending: (input: SimulationInput, pattern: string, targetSuccess: number, maxLifetimeRuin: number | null = null) =>
    request<FitResponse>('/api/simulate/fit', { method: 'POST', body: JSON.stringify({ input, pattern, targetSuccess, maxLifetimeRuin }) }),
  marketSummary: () => request<MarketSummary>('/api/market/summary'),
  marketMonths: () => request<MarketMonth[]>('/api/market/months'),
  marketUpdates: () => request<DataUpdateLog[]>('/api/market/updates'),
  refreshMarket: () => request<DataUpdateLog>('/api/market/refresh', { method: 'POST' }),
  // Saved scenarios live in this browser only: the proof of concept has no database or user accounts
  listScenarios: async () => readScenarios().sort((a, b) => a.name.localeCompare(b.name)),
  createScenario: async (name: string, input: SimulationInput) => saveScenario(null, name, input),
  updateScenario: async (id: string, name: string, input: SimulationInput) => saveScenario(id, name, input),
  deleteScenario: async (id: string) => writeScenarios(readScenarios().filter((s) => s.id !== id)),
  askStatus: () => request<{ serverKey: boolean; model: string }>('/api/ask/status'),
}

export interface AskTurn {
  role: 'user' | 'assistant'
  text: string
}

export type AskEvent =
  | { type: 'status'; message: string }
  | { type: 'tool'; label: string; error: boolean }
  | { type: 'answer'; text: string }
  | { type: 'error'; message: string; badKey?: boolean }
  | { type: 'done' }

/** Asks Claude about the plan. Progress arrives as server-sent events. The key is sent only in this request's header. */
export async function ask(
  question: string,
  input: SimulationInput,
  history: AskTurn[],
  apiKey: string | null,
  onEvent: (e: AskEvent) => void,
  signal?: AbortSignal,
) {
  const res = await fetch('/api/ask', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...(apiKey ? { 'X-Anthropic-Key': apiKey } : {}) },
    body: JSON.stringify({ question, input, history }),
    signal,
  })
  if (res.status === 404 || res.status === 405)
    throw new Error('the API is running an older version without Ask Claude. Restart the API and try again')
  if (!res.ok || !res.body) throw new Error(`${res.status} ${res.statusText}`)
  const reader = res.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''
  for (;;) {
    const { value, done } = await reader.read()
    if (done) break
    buffer += decoder.decode(value, { stream: true })
    let sep
    while ((sep = buffer.indexOf('\n\n')) >= 0) {
      const chunk = buffer.slice(0, sep)
      buffer = buffer.slice(sep + 2)
      const name = /^event: (.*)$/m.exec(chunk)?.[1]
      const data = /^data: (.*)$/m.exec(chunk)?.[1]
      if (name) onEvent({ type: name, ...(data ? JSON.parse(data) : {}) } as AskEvent)
    }
  }
}
