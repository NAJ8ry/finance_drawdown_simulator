import type { DataUpdateLog, MarketMonth, MarketSummary, Scenario, SimulationInput, SimulationResult } from './types'

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

export const api = {
  simulate: (input: SimulationInput, signal?: AbortSignal) =>
    request<SimulationResult>('/api/simulate', { method: 'POST', body: JSON.stringify(input), signal }),
  marketSummary: () => request<MarketSummary>('/api/market/summary'),
  marketMonths: () => request<MarketMonth[]>('/api/market/months'),
  marketUpdates: () => request<DataUpdateLog[]>('/api/market/updates'),
  refreshMarket: () => request<DataUpdateLog>('/api/market/refresh', { method: 'POST' }),
  listScenarios: () => request<Scenario[]>('/api/scenarios'),
  createScenario: (name: string, input: SimulationInput) =>
    request<Scenario>('/api/scenarios', { method: 'POST', body: JSON.stringify({ name, input }) }),
  updateScenario: (id: string, name: string, input: SimulationInput) =>
    request<Scenario>(`/api/scenarios/${id}`, { method: 'PUT', body: JSON.stringify({ name, input }) }),
  deleteScenario: (id: string) => request<void>(`/api/scenarios/${id}`, { method: 'DELETE' }),
}
