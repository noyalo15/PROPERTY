import clsx from "clsx";
import { Loader2, X } from "lucide-react";
import { useEffect, type ButtonHTMLAttributes, type InputHTMLAttributes, type ReactNode, type SelectHTMLAttributes, type TextareaHTMLAttributes } from "react";
import { label as labelOf } from "@/lib/format";

export function Card({ title, actions, children, className, padded = true }: { title?: ReactNode; actions?: ReactNode; children: ReactNode; className?: string; padded?: boolean }) {
  return (
    <section className={clsx("rounded-card border border-line bg-white", className)}>
      {(title || actions) && (
        <header className="flex flex-wrap items-center justify-between gap-2 border-b border-line px-4 py-3">
          <h2 className="text-sm font-semibold text-slate-800">{title}</h2>
          <div className="flex flex-wrap items-center gap-2">{actions}</div>
        </header>
      )}
      <div className={clsx(padded && "p-4")}>{children}</div>
    </section>
  );
}

type Variant = "primary" | "secondary" | "danger" | "ghost";
export function Button({ variant = "primary", busy, className, children, ...rest }: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: Variant; busy?: boolean }) {
  return (
    <button
      {...rest}
      disabled={rest.disabled || busy}
      className={clsx(
        "inline-flex items-center justify-center gap-1.5 rounded-lg px-3 py-2 text-sm font-medium transition disabled:cursor-not-allowed disabled:opacity-60",
        variant === "primary" && "bg-teal text-white hover:bg-teal-600",
        variant === "secondary" && "border border-line bg-white text-slate-700 hover:bg-slate-50",
        variant === "danger" && "bg-red-600 text-white hover:bg-red-700",
        variant === "ghost" && "text-slate-600 hover:bg-slate-100",
        className,
      )}
    >
      {busy && <Loader2 className="h-4 w-4 animate-spin" />}
      {children}
    </button>
  );
}

const fieldBase = "rounded-lg border border-line bg-white px-3 py-2 text-sm text-slate-800 outline-none focus:border-teal focus:ring-2 focus:ring-teal/20 disabled:bg-slate-50";
/** Full width unless the caller sets its own width class. */
const field = (className?: string) => clsx(fieldBase, !/(^|\s)w-/.test(className ?? "") && "w-full", className);

export function Input(props: InputHTMLAttributes<HTMLInputElement>) {
  return <input {...props} className={field(props.className)} />;
}
export function Textarea(props: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <textarea {...props} className={field(props.className)} />;
}
export function Select({ options, placeholder, ...props }: SelectHTMLAttributes<HTMLSelectElement> & { options: (string | { value: string; label: string })[]; placeholder?: string }) {
  return (
    <select {...props} className={field(props.className)}>
      {placeholder !== undefined && <option value="">{placeholder}</option>}
      {options.map((o) => (typeof o === "string" ? <option key={o} value={o}>{labelOf(o)}</option> : <option key={o.value} value={o.value}>{o.label}</option>))}
    </select>
  );
}

export function Field({ label, hint, children, className }: { label: string; hint?: string; children: ReactNode; className?: string }) {
  return (
    <label className={clsx("block", className)}>
      <span className="mb-1 block text-xs font-medium text-slate-600">{label}</span>
      {children}
      {hint && <span className="mt-1 block text-xs text-slate-400">{hint}</span>}
    </label>
  );
}

export function Checkbox({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <label className="inline-flex cursor-pointer items-center gap-2 text-sm text-slate-700">
      <input type="checkbox" className="h-4 w-4 rounded border-line accent-teal" checked={checked} onChange={(e) => onChange(e.target.checked)} />
      {label}
    </label>
  );
}

type Tone = "green" | "amber" | "red" | "blue" | "grey" | "teal";
const tones: Record<Tone, string> = {
  green: "bg-emerald-50 text-emerald-700 ring-emerald-200",
  amber: "bg-amber-50 text-amber-700 ring-amber-200",
  red: "bg-red-50 text-red-700 ring-red-200",
  blue: "bg-sky-50 text-sky-700 ring-sky-200",
  grey: "bg-slate-100 text-slate-600 ring-slate-200",
  teal: "bg-teal-50 text-teal ring-teal/30",
};
export function Pill({ tone = "grey", children }: { tone?: Tone; children: ReactNode }) {
  return <span className={clsx("inline-flex items-center gap-1 whitespace-nowrap rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset", tones[tone])}>{children}</span>;
}

const statusTone: Record<string, Tone> = {
  occupied: "green", vacant: "blue", reserved: "amber", maintenance: "amber", off_market: "grey",
  active: "green", draft: "grey", ended: "grey", transferred: "blue", terminated: "red",
  success: "green", pending: "amber", processing: "blue", failed: "red",
  held: "teal", paid: "green", posted: "green", approved: "blue", cancelled: "grey", partially_paid: "amber",
};
export function StatusPill({ status }: { status: string | null | undefined }) {
  if (!status) return <Pill>—</Pill>;
  return <Pill tone={statusTone[status] ?? "grey"}>{labelOf(status)}</Pill>;
}

export function Table({ head, children, empty }: { head: ReactNode[]; children: ReactNode; empty?: ReactNode }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-line">
            {head.map((h, i) => (
              <th key={i} className="whitespace-nowrap px-3 py-2 text-left text-[11px] font-semibold uppercase tracking-wide text-slate-400">{h}</th>
            ))}
          </tr>
        </thead>
        <tbody className="[&>tr:hover]:bg-slate-50 [&>tr]:border-b [&>tr]:border-line/70 [&_td]:px-3 [&_td]:py-2.5">{children}</tbody>
      </table>
      {empty}
    </div>
  );
}
export const Num = ({ children }: { children: ReactNode }) => <td className="whitespace-nowrap text-right tabular-nums">{children}</td>;
export const NumHead = ({ children }: { children: ReactNode }) => <span className="block text-right">{children}</span>;

export function Empty({ children }: { children: ReactNode }) {
  return <div className="px-4 py-10 text-center text-sm text-slate-400">{children}</div>;
}

export function Spinner({ label = "Loading…" }: { label?: string }) {
  return (
    <div className="flex items-center justify-center gap-2 py-10 text-sm text-slate-400">
      <Loader2 className="h-4 w-4 animate-spin" /> {label}
    </div>
  );
}

export function ErrorText({ error }: { error: unknown }) {
  if (!error) return null;
  const msg = error instanceof Error ? error.message : String(error);
  return <div className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{msg}</div>;
}

export function Modal({ open, title, onClose, children, footer, wide }: { open: boolean; title: string; onClose: () => void; children: ReactNode; footer?: ReactNode; wide?: boolean }) {
  useEffect(() => {
    if (!open) return;
    const h = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", h);
    return () => window.removeEventListener("keydown", h);
  }, [open, onClose]);
  if (!open) return null;
  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-slate-900/40 p-0 sm:items-center sm:p-4" onMouseDown={onClose}>
      <div
        role="dialog"
        aria-label={title}
        className={clsx("flex max-h-[92vh] w-full flex-col rounded-t-card bg-white shadow-xl sm:rounded-card", wide ? "sm:max-w-3xl" : "sm:max-w-lg")}
        onMouseDown={(e) => e.stopPropagation()}
      >
        <header className="flex items-center justify-between border-b border-line px-4 py-3">
          <h3 className="text-sm font-semibold">{title}</h3>
          <button className="rounded p-1 text-slate-400 hover:bg-slate-100" onClick={onClose} aria-label="Close"><X className="h-4 w-4" /></button>
        </header>
        <div className="overflow-y-auto p-4">{children}</div>
        {footer && <footer className="flex justify-end gap-2 border-t border-line px-4 py-3">{footer}</footer>}
      </div>
    </div>
  );
}

export function Tabs<T extends string>({ tabs, value, onChange }: { tabs: { id: T; label: string; count?: number }[]; value: T; onChange: (t: T) => void }) {
  return (
    <div className="flex gap-1 overflow-x-auto border-b border-line">
      {tabs.map((t) => (
        <button
          key={t.id}
          onClick={() => onChange(t.id)}
          className={clsx("whitespace-nowrap border-b-2 px-3 py-2 text-sm font-medium", value === t.id ? "border-teal text-teal" : "border-transparent text-slate-500 hover:text-slate-700")}
        >
          {t.label}
          {t.count !== undefined && <span className="ml-1.5 rounded-full bg-slate-100 px-1.5 text-xs text-slate-500">{t.count}</span>}
        </button>
      ))}
    </div>
  );
}

export function Kpi({ label, value, sub, tone }: { label: string; value: ReactNode; sub?: ReactNode; tone?: "warn" | "bad" }) {
  return (
    <div className="rounded-card border border-line bg-white p-4">
      <div className="text-xs font-medium uppercase tracking-wide text-slate-400">{label}</div>
      <div className={clsx("mt-1 text-2xl font-semibold tabular-nums", tone === "bad" ? "text-red-600" : tone === "warn" ? "text-amber-600" : "text-slate-800")}>{value}</div>
      {sub && <div className="mt-1 text-xs text-slate-500">{sub}</div>}
    </div>
  );
}

export function OccupancyBar({ percent }: { percent: number }) {
  return (
    <div className="flex items-center gap-2">
      <div className="h-2 w-28 overflow-hidden rounded-full bg-slate-100">
        <div className={clsx("h-full rounded-full", percent < 85 ? "bg-amber-400" : "bg-accent")} style={{ width: `${Math.min(100, percent)}%` }} />
      </div>
      <span className="w-12 text-right text-xs tabular-nums text-slate-600">{percent.toFixed(1)}%</span>
    </div>
  );
}

export function PageHeader({ title, actions, children }: { title: string; actions?: ReactNode; children?: ReactNode }) {
  return (
    <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
      <div>
        <h1 className="text-xl font-semibold text-slate-800">{title}</h1>
        {children && <div className="mt-0.5 text-sm text-slate-500">{children}</div>}
      </div>
      {actions && <div className="flex flex-wrap gap-2">{actions}</div>}
    </div>
  );
}
