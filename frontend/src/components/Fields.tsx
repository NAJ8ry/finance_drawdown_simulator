import { useEffect, useId, useState, type ReactNode } from 'react'

interface NumberFieldProps {
  label: string
  value: number | null
  onChange: (value: number | null) => void
  /** Show and edit the value as a percentage (value is stored as a fraction). */
  percent?: boolean
  prefix?: string
  suffix?: string
  step?: number
  min?: number
  max?: number
  allowEmpty?: boolean
  hint?: string
}

export function NumberField({ label, value, onChange, percent, prefix, suffix, step, min, max, allowEmpty, hint }: NumberFieldProps) {
  const id = useId()
  const toText = (v: number | null) => (v == null ? '' : String(+(percent ? v * 100 : v).toFixed(6)))
  const [text, setText] = useState(toText(value))
  const [focused, setFocused] = useState(false)

  useEffect(() => {
    if (!focused) setText(toText(value))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value, focused])

  const shown = percent ? (v: number) => v / 100 : (v: number) => v
  const parsed = text.trim() === '' ? null : Number(text)
  const invalid =
    (parsed == null && !allowEmpty) ||
    (parsed != null && (Number.isNaN(parsed) || (min != null && parsed < min) || (max != null && parsed > max)))

  return (
    <label className="field" htmlFor={id}>
      <span className="field-label">{label}</span>
      <span className={`input-wrap${invalid ? ' invalid' : ''}`}>
        {prefix && <span className="affix">{prefix}</span>}
        <input
          id={id}
          type="number"
          inputMode="decimal"
          value={text}
          step={step}
          min={min}
          max={max}
          onFocus={() => setFocused(true)}
          onBlur={() => setFocused(false)}
          onChange={(e) => {
            setText(e.target.value)
            const t = e.target.value.trim()
            if (t === '' && allowEmpty) return onChange(null)
            const n = Number(t)
            if (t !== '' && !Number.isNaN(n) && (min == null || n >= min) && (max == null || n <= max)) onChange(shown(n))
          }}
        />
        {(suffix ?? (percent ? '%' : undefined)) && <span className="affix">{suffix ?? '%'}</span>}
      </span>
      {hint && <span className="field-hint">{hint}</span>}
    </label>
  )
}

interface SelectFieldProps<T extends string> {
  label: string
  value: T
  options: Record<T, { label: string }> | { value: T; label: string }[]
  onChange: (value: T) => void
}

export function SelectField<T extends string>({ label, value, options, onChange }: SelectFieldProps<T>) {
  const id = useId()
  const list = Array.isArray(options)
    ? options
    : (Object.entries(options) as [T, { label: string }][]).map(([v, o]) => ({ value: v, label: o.label }))
  return (
    <label className="field" htmlFor={id}>
      <span className="field-label">{label}</span>
      <select id={id} value={value} onChange={(e) => onChange(e.target.value as T)}>
        {list.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
    </label>
  )
}

export function CheckField({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <label className="check">
      <input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} />
      <span>{label}</span>
    </label>
  )
}

export function Section({ title, children, defaultOpen = true }: { title: string; children: ReactNode; defaultOpen?: boolean }) {
  return (
    <details className="section" open={defaultOpen}>
      <summary>{title}</summary>
      <div className="section-body">{children}</div>
    </details>
  )
}
