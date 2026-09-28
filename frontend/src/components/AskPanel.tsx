import { useEffect, useRef, useState } from 'react'
import Markdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { api, ask, type AskTurn } from '../api'
import type { SimulationInput } from '../types'

const KEY_STORAGE = 'anthropic-api-key'

interface Message {
  role: 'user' | 'assistant'
  text: string
  tools?: { label: string; error: boolean }[]
  error?: boolean
}

const suggestions = [
  'When my pensions start, should I spend more?',
  'Which historical start years are the most dangerous for my plan, and why?',
  'Would a bigger or smaller cash buffer help?',
  'How much could I spend in year one and still succeed 95% of the time?',
]

function readKey(): { key: string | null; remembered: boolean } {
  try {
    const local = localStorage.getItem(KEY_STORAGE)
    if (local) return { key: local, remembered: true }
    return { key: sessionStorage.getItem(KEY_STORAGE), remembered: false }
  } catch {
    return { key: null, remembered: false }
  }
}

function storeKey(key: string | null, remember: boolean) {
  try {
    localStorage.removeItem(KEY_STORAGE)
    sessionStorage.removeItem(KEY_STORAGE)
    if (key) (remember ? localStorage : sessionStorage).setItem(KEY_STORAGE, key)
  } catch {
    /* storage unavailable: the key lives only in memory for this page */
  }
}

/**
 * Chat with Claude about the current plan. Visitors supply their own Anthropic API key, which stays in their browser
 * and is sent only with their questions. The chat itself only appears once a key is available.
 */
export function AskPanel({ input }: { input: SimulationInput }) {
  const [{ key, remembered }, setKeyState] = useState(readKey)
  const [serverKey, setServerKey] = useState(false)
  const [editingKey, setEditingKey] = useState(false)
  const [draftKey, setDraftKey] = useState('')
  const [remember, setRemember] = useState(remembered)
  const [messages, setMessages] = useState<Message[]>([])
  const [question, setQuestion] = useState('')
  const [status, setStatus] = useState<string | null>(null)
  const [keyError, setKeyError] = useState<string | null>(null)
  const abortRef = useRef<AbortController | null>(null)
  const endRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    api.askStatus().then((s) => setServerKey(s.serverKey)).catch(() => setServerKey(false))
  }, [])

  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: 'smooth', block: 'nearest' })
  }, [messages, status])

  const hasKey = !!key || serverKey
  const busy = status !== null

  const saveKey = () => {
    const k = draftKey.trim()
    if (!k) return
    storeKey(k, remember)
    setKeyState({ key: k, remembered: remember })
    setDraftKey('')
    setEditingKey(false)
    setKeyError(null)
  }

  const removeKey = () => {
    storeKey(null, false)
    setKeyState({ key: null, remembered: false })
    setEditingKey(false)
  }

  const send = async (text: string) => {
    const q = text.trim()
    if (!q || busy) return
    const history: AskTurn[] = messages.filter((m) => !m.error).map((m) => ({ role: m.role, text: m.text }))
    setMessages((m) => [...m, { role: 'user', text: q }, { role: 'assistant', text: '', tools: [] }])
    setQuestion('')
    setStatus('Thinking…')
    const ctrl = new AbortController()
    abortRef.current = ctrl

    const updateLast = (patch: (m: Message) => Message) =>
      setMessages((all) => [...all.slice(0, -1), patch(all[all.length - 1])])

    try {
      await ask(q, input, history, key, (e) => {
        if (e.type === 'status') setStatus(e.message)
        else if (e.type === 'tool') updateLast((m) => ({ ...m, tools: [...(m.tools ?? []), { label: e.label, error: e.error }] }))
        else if (e.type === 'answer') updateLast((m) => ({ ...m, text: e.text }))
        else if (e.type === 'error') {
          updateLast((m) => ({ ...m, text: e.message, error: true }))
          if (e.badKey) {
            removeKey()
            setKeyError('Anthropic rejected that key. Check it and paste it again.')
          }
        }
      }, ctrl.signal)
    } catch (err) {
      if ((err as Error).name !== 'AbortError') updateLast((m) => ({ ...m, text: `Could not reach the server: ${(err as Error).message}`, error: true }))
      else updateLast((m) => ({ ...m, text: m.text || 'Stopped.', error: !m.text }))
    } finally {
      setStatus(null)
      abortRef.current = null
    }
  }

  const keyForm = (
    <div className="key-form">
      <input type="password" autoComplete="off" spellCheck={false} placeholder="sk-ant-…" value={draftKey}
        onChange={(e) => setDraftKey(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && saveKey()}
        aria-label="Anthropic API key" />
      <label className="check small">
        <input type="checkbox" checked={remember} onChange={(e) => setRemember(e.target.checked)} />
        <span>Remember on this device</span>
      </label>
      <button className="btn primary small" onClick={saveKey} disabled={!draftKey.trim()}>Use key</button>
      {key && <button className="btn small" onClick={() => setEditingKey(false)}>Cancel</button>}
    </div>
  )

  // No key yet: only the key field is shown
  if (!hasKey) {
    return (
      <div className="card ask-setup">
        <div className="ask-setup-text">
          <strong>Ask Claude about your plan</strong>
          <span className="muted small">
            Paste your Anthropic API key to chat about these results. Get one at{' '}
            <a href="https://console.anthropic.com/settings/keys" target="_blank" rel="noreferrer">console.anthropic.com</a>.
            Your key stays in this browser and is only sent, with your questions, to be passed straight to Anthropic. It is never stored on the server.
          </span>
        </div>
        {keyForm}
        {keyError && <p className="error ask-key-error">{keyError}</p>}
      </div>
    )
  }

  return (
    <div className="card ask-panel">
      <div className="ask-head">
        <h3>Ask Claude</h3>
        <div className="ask-key">
          {editingKey ? keyForm : key ? (
            <>
              <span className="muted small">Using your key ••••{key.slice(-4)}{remembered ? ' (remembered)' : ''}</span>
              <button className="link small" onClick={() => setEditingKey(true)}>Change</button>
              <button className="link small" onClick={removeKey}>Remove</button>
            </>
          ) : (
            <>
              <span className="muted small">Using the server's key</span>
              <button className="link small" onClick={() => setEditingKey(true)}>Use my own key</button>
            </>
          )}
          {messages.length > 0 && !busy && <button className="link small" onClick={() => setMessages([])}>Clear chat</button>}
        </div>
      </div>

      <div className="ask-log">
        {messages.length === 0 && (
          <div className="ask-empty">
            <p className="muted small">Claude sees your current inputs and results, and can re-run the simulation to test changes.</p>
            <div className="ask-suggestions">
              {suggestions.map((s) => (
                <button key={s} className="chip" onClick={() => send(s)}>{s}</button>
              ))}
            </div>
          </div>
        )}
        {messages.map((m, i) => (
          <div key={i} className={`ask-msg ${m.role}${m.error ? ' error' : ''}`}>
            {m.tools && m.tools.length > 0 && (
              <div className="ask-tools">
                {m.tools.map((t, j) => (
                  <span key={j} className={`tool-chip${t.error ? ' bad' : ''}`}>▸ Simulated: {t.label}</span>
                ))}
              </div>
            )}
            {m.role === 'assistant' && !m.error ? (
              m.text && <div className="prose-md"><Markdown remarkPlugins={[remarkGfm]}>{m.text}</Markdown></div>
            ) : (
              <div>{m.text}</div>
            )}
          </div>
        ))}
        {status && <div className="ask-status"><span className="spinner" /> {status}</div>}
        <div ref={endRef} />
      </div>

      <form className="ask-input" onSubmit={(e) => { e.preventDefault(); send(question) }}>
        <textarea rows={2} placeholder="Ask about your plan, e.g. should I spend more once my pensions start?"
          value={question} onChange={(e) => setQuestion(e.target.value)}
          onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send(question) } }} />
        {busy ? (
          <button type="button" className="btn" onClick={() => abortRef.current?.abort()}>Stop</button>
        ) : (
          <button type="submit" className="btn primary" disabled={!question.trim()}>Ask</button>
        )}
      </form>
      <p className="muted small ask-note">Answers come from Claude using this historical backtest. They are not regulated financial advice. Each question uses your API credit.</p>
    </div>
  )
}
