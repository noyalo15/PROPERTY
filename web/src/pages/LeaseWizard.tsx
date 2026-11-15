import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertOctagon, Check, ChevronLeft, ChevronRight, Plus, Search, Trash2 } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import clsx from "clsx";
import { get, post, put } from "@/lib/api";
import { date, label, money } from "@/lib/format";
import { Button, Card, Checkbox, ErrorText, Field, Input, Num, NumHead, PageHeader, Pill, Select, Spinner, Table } from "@/components/ui";

export interface ChargeLine { chargeCode: string; description: string; amount: number; frequency: string; active: boolean }
export interface Escalation { type: string; value: number; everyMonths: number; compounding: boolean; noticeDays: number; requiresApproval: boolean; steps?: { from: string; monthlyRent: number }[] }
export interface LeaseInput {
  tenantId: string; unitId: string; startDate: string; endDate: string | null; currencyCode: string; billingDay: number; frequency: string;
  charges: ChargeLine[]; depositAmount: number; waterDepositAmount: number; graceDays: number; penaltyRuleId: string | null;
  vatApplicable: boolean; whtAgent: boolean; escalation: Escalation | null; overrideBlacklist: boolean;
}
interface TenantPick { id: string; tenantNo: string; fullName: string; phone: string; blacklisted: boolean; blacklistReason?: string | null; unit?: string }
interface UnitPick {
  id: string; code: string; unitType: string; status: string; defaultRent: number; defaultCurrency: string; propertyId: string; property: string; propertyType: string;
  charges: { chargeCode: string; amount: number; currencyCode: string; frequency: string }[];
}
export interface ChargeCode { code: string; description: string; isRent: boolean; active: boolean }
interface PreviewRow { from: string; to?: string; monthlyRent?: number; label: string }

const commercialTypes = ["shop", "office", "warehouse", "kiosk"];
const steps = ["Tenant", "Unit", "Term", "Charges", "Deposit & terms", "Escalation", "Review"] as const;

export const isoToday = () => new Date().toLocaleDateString("en-CA", { timeZone: "Africa/Nairobi" });
export const addYears = (iso: string, years: number) => {
  const d = new Date(iso + "T00:00:00Z");
  d.setUTCFullYear(d.getUTCFullYear() + years);
  d.setUTCDate(d.getUTCDate() - 1);
  return d.toISOString().slice(0, 10);
};
const firstOfNextMonth = () => {
  const [y, m] = isoToday().split("-").map(Number);
  return new Date(Date.UTC(m === 12 ? y + 1 : y, m === 12 ? 0 : m, 1)).toISOString().slice(0, 10);
};

export const defaultEscalation: Escalation = { type: "fixed_percent", value: 5, everyMonths: 12, compounding: true, noticeDays: 90, requiresApproval: true };

export function LeaseWizardPage() {
  const [sp] = useSearchParams();
  const nav = useNavigate();
  const qc = useQueryClient();
  const draftId = sp.get("draft");
  const [step, setStep] = useState(0);
  const [tenant, setTenant] = useState<TenantPick | null>(null);
  const [unit, setUnit] = useState<UnitPick | null>(null);
  const [f, setF] = useState<LeaseInput>({
    tenantId: "", unitId: "", startDate: firstOfNextMonth(), endDate: addYears(firstOfNextMonth(), 1), currencyCode: "KES", billingDay: 1, frequency: "monthly",
    charges: [], depositAmount: 0, waterDepositAmount: 0, graceDays: 5, penaltyRuleId: null, vatApplicable: false, whtAgent: false,
    escalation: { ...defaultEscalation }, overrideBlacklist: false,
  });
  const codes = useQuery({ queryKey: ["charge-codes"], queryFn: () => get<ChargeCode[]>("/api/setup/charge-codes") });
  const penalties = useQuery({ queryKey: ["penalty-rules"], queryFn: () => get<{ id: string; name: string }[]>("/api/setup/penalty-rules") });

  // Deep links: ?tenantId= from Tenant 360, ?unitId= from the property page, ?draft= to finish a saved draft.
  const preTenant = sp.get("tenantId");
  const preUnit = sp.get("unitId");
  useEffect(() => {
    if (preTenant && !draftId) get<{ tenant: TenantPick }>(`/api/tenants/${preTenant}`).then((r) => { setTenant(r.tenant); setStep((s) => Math.max(s, 1)); }).catch(() => {});
  }, [preTenant, draftId]);
  useEffect(() => {
    if (preUnit && !draftId) get<UnitPick[]>(`/api/leases/vacant-units?unitId=${preUnit}`).then((r) => r[0] && pickUnit(r[0], false)).catch(() => {});
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [preUnit, draftId]);
  useEffect(() => {
    if (!draftId) return;
    get<LeaseDetailForEdit>(`/api/leases/${draftId}`).then((d) => {
      setTenant({ id: d.tenant.id, tenantNo: d.tenant.tenantNo, fullName: d.tenant.fullName, phone: d.tenant.phone, blacklisted: d.tenant.blacklisted });
      setUnit({ id: d.unit.id, code: d.unit.code, unitType: d.unit.unitType, status: d.unit.status, defaultRent: d.lease.monthlyRent, defaultCurrency: d.lease.currencyCode, propertyId: d.unit.propertyId, property: d.unit.property, propertyType: "", charges: [] });
      setF(inputFromDetail(d, true));
      setStep(6);
    });
  }, [draftId]);

  function pickUnit(u: UnitPick, advance = true) {
    setUnit(u);
    const commercial = commercialTypes.includes(u.unitType);
    const rentCode = commercial ? "RENTCOM" : "RENT";
    setF((x) => ({
      ...x, unitId: u.id, currencyCode: u.defaultCurrency, vatApplicable: commercial,
      depositAmount: u.defaultRent * (commercial ? 3 : 1), waterDepositAmount: commercial || u.defaultCurrency === "USD" ? 0 : 2500,
      escalation: x.escalation ? { ...x.escalation, value: x.escalation.type === "fixed_percent" ? (commercial ? 7.5 : 5) : x.escalation.value } : null,
      charges: [
        { chargeCode: rentCode, description: commercial ? "Commercial rent" : "Rent", amount: u.defaultRent, frequency: "monthly", active: true },
        ...u.charges.map((c) => ({ chargeCode: c.chargeCode, description: codes.data?.find((cc) => cc.code === c.chargeCode)?.description ?? c.chargeCode, amount: c.amount, frequency: c.frequency, active: true })),
      ],
    }));
    if (advance) setStep(2);
  }

  // Unit charges picked before the charge codes loaded show the code; fill in the description once they arrive.
  useEffect(() => {
    if (!codes.data) return;
    setF((x) => x.charges.some((c) => c.description === c.chargeCode)
      ? { ...x, charges: x.charges.map((c) => (c.description === c.chargeCode ? { ...c, description: codes.data!.find((cc) => cc.code === c.chargeCode)?.description ?? c.description } : c)) }
      : x);
  }, [codes.data, f.charges.length]);

  const rentCodes = useMemo(() => new Set((codes.data ?? []).filter((c) => c.isRent).map((c) => c.code)), [codes.data]);
  const rent = f.charges.find((c) => c.active && rentCodes.has(c.chargeCode))?.amount ?? 0;
  const input: LeaseInput = { ...f, tenantId: tenant?.id ?? "", unitId: unit?.id ?? "" };

  const preview = useQuery({
    queryKey: ["schedule-preview", rent, f.startDate, f.endDate, f.escalation],
    queryFn: () => post<PreviewRow[]>("/api/leases/schedule-preview", { baseRent: rent, startDate: f.startDate, endDate: f.endDate || null, escalation: f.escalation }),
    enabled: step >= 5 && rent > 0, retry: false,
  });

  const save = useMutation({
    mutationFn: (activate: boolean) => draftId
      ? put(`/api/leases/${draftId}`, input).then(() => (activate ? post<{ id: string }>(`/api/leases/${draftId}/activate`, { overrideBlacklist: f.overrideBlacklist }) : { id: draftId }))
      : post<{ id: string }>(`/api/leases${activate ? "?activate=true" : ""}`, input),
    onSuccess: (r) => {
      qc.invalidateQueries({ queryKey: ["leases"] });
      qc.invalidateQueries({ queryKey: ["leases-summary"] });
      qc.invalidateQueries({ queryKey: ["dashboard"] });
      nav(`/leases/${(r as { id: string }).id}`);
    },
  });

  const stepError = (): string | null => {
    if (step === 0 && !tenant) return "Choose a tenant";
    if (step === 1 && !unit) return "Choose a vacant unit";
    if (step === 2) {
      if (!f.startDate) return "Enter the start date";
      if (f.endDate && f.endDate <= f.startDate) return "End date must be after the start date";
      if (f.billingDay < 1 || f.billingDay > 28) return "Billing day must be between 1 and 28";
    }
    if (step === 3 && f.charges.filter((c) => c.active && rentCodes.has(c.chargeCode)).length !== 1) return "Keep exactly one rent line (RENT or RENTCOM)";
    if (step === 3 && rent <= 0) return "Enter the monthly rent";
    if (step === 5 && preview.error) return (preview.error as Error).message;
    return null;
  };
  const err = stepError();

  return (
    <div className="space-y-4">
      <PageHeader title={draftId ? "Finish draft lease" : "New lease"}><Link to="/leases" className="hover:text-teal">Leases</Link> / {draftId ? "Draft" : "Wizard"}</PageHeader>
      <ol className="flex flex-wrap gap-1.5">
        {steps.map((s, i) => (
          <li key={s}>
            <button disabled={i > step && !!err} onClick={() => (i <= step || !err) && setStep(i)}
              className={clsx("flex items-center gap-1.5 rounded-full px-3 py-1 text-xs font-medium", i === step ? "bg-navy text-white" : i < step ? "bg-teal/10 text-teal" : "bg-white text-slate-400 ring-1 ring-line")}>
              <span className={clsx("flex h-4 w-4 items-center justify-center rounded-full text-[10px]", i === step ? "bg-white/20" : i < step ? "bg-teal text-white" : "bg-slate-100")}>{i < step ? <Check className="h-3 w-3" /> : i + 1}</span>{s}
            </button>
          </li>
        ))}
      </ol>

      <Card title={steps[step]}>
        {step === 0 && <TenantStep value={tenant} onPick={(t) => { setTenant(t); setStep(1); }} />}
        {step === 1 && <UnitStep value={unit} onPick={(u) => pickUnit(u)} />}
        {step === 2 && (
          <div className="grid gap-3 sm:grid-cols-3">
            <Field label="Start date"><Input type="date" value={f.startDate} onChange={(e) => setF({ ...f, startDate: e.target.value })} /></Field>
            <Field label="End date" hint="Leave empty for an open-ended (periodic) lease">
              <Input type="date" value={f.endDate ?? ""} onChange={(e) => setF({ ...f, endDate: e.target.value || null })} />
            </Field>
            <Field label="Term">
              <div className="flex flex-wrap gap-1">{[1, 2, 3, 5, 6].map((y) => <Button key={y} variant="secondary" className="px-2" onClick={() => setF({ ...f, endDate: addYears(f.startDate, y) })}>{y} yr</Button>)}</div>
            </Field>
            <Field label="Currency" hint={unit ? `The unit's default is ${unit.defaultCurrency}` : undefined}><Select value={f.currencyCode} onChange={(e) => setF({ ...f, currencyCode: e.target.value })} options={["KES", "USD"]} /></Field>
            <Field label="Billing day" hint="1 to 28"><Input type="number" min={1} max={28} value={f.billingDay} onChange={(e) => setF({ ...f, billingDay: Number(e.target.value) })} /></Field>
            <Field label="Billing frequency"><Select value={f.frequency} onChange={(e) => setF({ ...f, frequency: e.target.value })} options={["monthly", "quarterly", "semi_annual", "annual"]} /></Field>
          </div>
        )}
        {step === 3 && <ChargesEditor charges={f.charges} codes={codes.data ?? []} currency={f.currencyCode} onChange={(charges) => setF({ ...f, charges })} />}
        {step === 4 && (
          <div className="grid gap-3 sm:grid-cols-3">
            <Field label={`Rent deposit (${f.currencyCode})`} hint={rent ? `${(f.depositAmount / rent).toFixed(1)} × monthly rent` : undefined}><Input type="number" min={0} value={f.depositAmount} onChange={(e) => setF({ ...f, depositAmount: Number(e.target.value) })} /></Field>
            <Field label={`Water deposit (${f.currencyCode})`}><Input type="number" min={0} value={f.waterDepositAmount} onChange={(e) => setF({ ...f, waterDepositAmount: Number(e.target.value) })} /></Field>
            <Field label="Grace days" hint="Days after the due date before a penalty"><Input type="number" min={0} max={60} value={f.graceDays} onChange={(e) => setF({ ...f, graceDays: Number(e.target.value) })} /></Field>
            <Field label="Penalty rule" className="sm:col-span-2"><Select value={f.penaltyRuleId ?? ""} onChange={(e) => setF({ ...f, penaltyRuleId: e.target.value || null })} placeholder="No penalty" options={(penalties.data ?? []).map((p) => ({ value: p.id, label: p.name }))} /></Field>
            <div className="space-y-2 sm:col-span-3">
              <Checkbox label="VAT applicable (16% added to bills)" checked={f.vatApplicable} onChange={(v) => setF({ ...f, vatApplicable: v })} />
              <Checkbox label="Tenant is a WHT agent (withholds 10% on rent)" checked={f.whtAgent} onChange={(v) => setF({ ...f, whtAgent: v })} />
            </div>
          </div>
        )}
        {step === 5 && <EscalationEditor value={f.escalation} startDate={f.startDate} onChange={(escalation) => setF({ ...f, escalation })} />}
        {step === 6 && tenant && unit && (
          <div className="space-y-4">
            {tenant.blacklisted && (
              <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800">
                <div className="mb-2 flex items-center gap-2 font-semibold"><AlertOctagon className="h-4 w-4" />{tenant.fullName} is blacklisted{tenant.blacklistReason ? `: ${tenant.blacklistReason}` : ""}</div>
                <Checkbox label="I have checked this and want to let the unit to them anyway" checked={f.overrideBlacklist} onChange={(v) => setF({ ...f, overrideBlacklist: v })} />
              </div>
            )}
            <dl className="grid gap-x-6 gap-y-1.5 text-sm sm:grid-cols-[auto_1fr_auto_1fr]">
              <dt className="text-slate-500">Tenant</dt><dd>{tenant.fullName} <span className="font-mono text-xs text-slate-400">{tenant.tenantNo}</span></dd>
              <dt className="text-slate-500">Unit</dt><dd>{unit.code}, {unit.property}</dd>
              <dt className="text-slate-500">Term</dt><dd>{date(f.startDate)} to {f.endDate ? date(f.endDate) : "open-ended"}</dd>
              <dt className="text-slate-500">Billing</dt><dd>{label(f.frequency)}, day {f.billingDay}, {f.currencyCode}</dd>
              <dt className="text-slate-500">Deposits</dt><dd>{money(f.depositAmount, f.currencyCode)} rent{f.waterDepositAmount ? `, ${money(f.waterDepositAmount, f.currencyCode)} water` : ""}</dd>
              <dt className="text-slate-500">Terms</dt><dd>{f.graceDays} grace days{f.vatApplicable ? ", VAT" : ""}{f.whtAgent ? ", WHT agent" : ""}{f.penaltyRuleId ? `, ${penalties.data?.find((p) => p.id === f.penaltyRuleId)?.name}` : ", no penalty"}</dd>
            </dl>
            <Table head={["Charge", "Description", "Frequency", <NumHead key="a">Amount</NumHead>]}>
              {f.charges.filter((c) => c.active).map((c) => <tr key={c.chargeCode}><td className="font-mono text-xs">{c.chargeCode}</td><td>{c.description}</td><td>{label(c.frequency)}</td><Num>{money(c.amount, f.currencyCode)}</Num></tr>)}
            </Table>
            <SchedulePreview rows={preview.data} loading={preview.isLoading} currency={f.currencyCode} escalation={f.escalation} />
            <p className="text-xs text-slate-500">Activating sets {unit.code} to occupied, creates the deposit records (held, awaiting receipt) and sends the tenant's new PROPERTY and UNIT dimensions to Business Central.</p>
          </div>
        )}
        <div className="mt-4"><ErrorText error={save.error} /></div>
        <div className="mt-4 flex items-center justify-between border-t border-line pt-3">
          <Button variant="ghost" disabled={step === 0} onClick={() => setStep(step - 1)}><ChevronLeft className="h-4 w-4" />Back</Button>
          <div className="flex items-center gap-2">
            {err && step > 1 && <span className="text-xs text-red-600">{err}</span>}
            {step < 6 ? (
              <Button disabled={!!err} onClick={() => setStep(step + 1)}>Next<ChevronRight className="h-4 w-4" /></Button>
            ) : (
              <>
                <Button variant="secondary" busy={save.isPending && save.variables === false} onClick={() => save.mutate(false)}>Save as draft</Button>
                <Button busy={save.isPending && save.variables === true} disabled={tenant?.blacklisted && !f.overrideBlacklist} onClick={() => save.mutate(true)}>Activate lease</Button>
              </>
            )}
          </div>
        </div>
      </Card>
    </div>
  );
}

function TenantStep({ value, onPick }: { value: TenantPick | null; onPick: (t: TenantPick) => void }) {
  const [q, setQ] = useState("");
  const { data, isLoading } = useQuery({ queryKey: ["tenant-pick", q], queryFn: () => get<{ rows: TenantPick[] }>(`/api/tenants?pageSize=20&q=${encodeURIComponent(q)}`) });
  return (
    <div className="space-y-3">
      <div className="flex gap-2">
        <div className="relative flex-1"><Search className="absolute left-2.5 top-2.5 h-4 w-4 text-slate-400" /><Input autoFocus className="pl-8" placeholder="Search name, tenant no., phone or ID" value={q} onChange={(e) => setQ(e.target.value)} /></div>
        <Link to="/tenants"><Button variant="secondary"><Plus className="h-4 w-4" />New tenant</Button></Link>
      </div>
      {isLoading ? <Spinner /> : (
        <div className="divide-y divide-line rounded-lg border border-line">
          {(data?.rows ?? []).map((t) => (
            <button key={t.id} onClick={() => onPick(t)} className={clsx("flex w-full items-center gap-3 px-3 py-2 text-left text-sm hover:bg-slate-50", value?.id === t.id && "bg-teal/5")}>
              <span className="font-mono text-xs text-slate-400">{t.tenantNo}</span>
              <span className="font-medium">{t.fullName}</span>
              {t.blacklisted && <Pill tone="red">Blacklisted</Pill>}
              <span className="ml-auto text-xs text-slate-400">{t.unit ? `Has ${t.unit}` : t.phone}</span>
              {value?.id === t.id && <Check className="h-4 w-4 text-teal" />}
            </button>
          ))}
          {data?.rows.length === 0 && <p className="p-4 text-center text-sm text-slate-400">No tenant matches. Create the tenant first.</p>}
        </div>
      )}
    </div>
  );
}

function UnitStep({ value, onPick }: { value: UnitPick | null; onPick: (u: UnitPick) => void }) {
  const [q, setQ] = useState("");
  const [propertyId, setPropertyId] = useState("");
  const props = useQuery({ queryKey: ["properties"], queryFn: () => get<{ id: string; name: string }[]>("/api/properties") });
  const { data, isLoading } = useQuery({ queryKey: ["vacant-units", q, propertyId], queryFn: () => get<UnitPick[]>(`/api/leases/vacant-units?q=${encodeURIComponent(q)}${propertyId ? `&propertyId=${propertyId}` : ""}`) });
  return (
    <div className="space-y-3">
      <div className="flex flex-wrap gap-2">
        <Select value={propertyId} onChange={(e) => setPropertyId(e.target.value)} placeholder="All properties" options={(props.data ?? []).map((p) => ({ value: p.id, label: p.name }))} className="w-60" />
        <Input placeholder="Unit code" value={q} onChange={(e) => setQ(e.target.value)} className="w-40" />
        <span className="self-center text-xs text-slate-400">Only vacant and reserved units are offered.</span>
      </div>
      {isLoading ? <Spinner /> : (
        <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
          {(data ?? []).map((u) => (
            <button key={u.id} onClick={() => onPick(u)} className={clsx("rounded-lg border p-3 text-left text-sm transition hover:border-teal", value?.id === u.id ? "border-teal bg-teal/5" : "border-line")}>
              <div className="flex items-center justify-between"><span className="font-semibold">{u.code}</span>{u.status === "reserved" ? <Pill tone="amber">Reserved</Pill> : <Pill tone="blue">Vacant</Pill>}</div>
              <div className="text-xs text-slate-500">{u.property} · {label(u.unitType)}</div>
              <div className="mt-1 font-medium tabular-nums">{money(u.defaultRent, u.defaultCurrency)}<span className="font-normal text-slate-400"> / month</span></div>
            </button>
          ))}
          {data?.length === 0 && <p className="p-4 text-sm text-slate-400">No vacant units match.</p>}
        </div>
      )}
    </div>
  );
}

export function ChargesEditor({ charges, codes, currency, onChange, rentLocked }: { charges: ChargeLine[]; codes: ChargeCode[]; currency: string; onChange: (c: ChargeLine[]) => void; rentLocked?: boolean }) {
  const isRent = (code: string) => codes.find((c) => c.code === code)?.isRent ?? false;
  const upd = (i: number, patch: Partial<ChargeLine>) => onChange(charges.map((c, j) => (j === i ? { ...c, ...patch } : c)));
  const unused = codes.filter((c) => c.active && !charges.some((x) => x.chargeCode === c.code) && !["PENALTY", "WATER", "REPAIR"].includes(c.code));
  return (
    <div className="space-y-2">
      <div className="hidden grid-cols-[150px_1fr_130px_140px_32px] gap-2 text-[11px] font-semibold uppercase tracking-wide text-slate-400 sm:grid">
        <span>Charge code</span><span>Description</span><span>Frequency</span><span className="text-right">Amount ({currency})</span><span />
      </div>
      {charges.map((c, i) => {
        const locked = rentLocked && isRent(c.chargeCode);
        return (
          <div key={i} className="grid grid-cols-2 gap-2 sm:grid-cols-[150px_1fr_130px_140px_32px]">
            <Select disabled={locked} value={c.chargeCode} onChange={(e) => upd(i, { chargeCode: e.target.value, description: codes.find((x) => x.code === e.target.value)?.description ?? e.target.value })}
              options={codes.filter((x) => x.active || x.code === c.chargeCode).map((x) => ({ value: x.code, label: x.code }))} />
            <Input value={c.description} maxLength={50} onChange={(e) => upd(i, { description: e.target.value })} />
            <Select value={c.frequency} onChange={(e) => upd(i, { frequency: e.target.value })} options={["monthly", "quarterly", "semi_annual", "annual", "once"]} />
            <Input type="number" min={0} step="0.01" disabled={locked} title={locked ? "Rent changes go through the rent schedule" : undefined} className="text-right tabular-nums" value={c.amount} onChange={(e) => upd(i, { amount: Number(e.target.value) })} />
            <button disabled={locked} className="rounded p-1 text-slate-400 hover:bg-red-50 hover:text-red-600 disabled:opacity-30" onClick={() => onChange(charges.filter((_, j) => j !== i))} aria-label="Remove"><Trash2 className="h-4 w-4" /></button>
          </div>
        );
      })}
      {unused.length > 0 && (
        <Button variant="ghost" onClick={() => onChange([...charges, { chargeCode: unused[0].code, description: unused[0].description, amount: 0, frequency: "monthly", active: true }])}><Plus className="h-4 w-4" />Add charge</Button>
      )}
      <p className="text-xs text-slate-400">Copied from the unit's default charges. Water is billed from meter readings; penalties come from the penalty rule.</p>
    </div>
  );
}

function EscalationEditor({ value, startDate, onChange }: { value: Escalation | null; startDate: string; onChange: (e: Escalation | null) => void }) {
  const type = value?.type ?? "none";
  const e = value ?? defaultEscalation;
  const set = (patch: Partial<Escalation>) => onChange({ ...e, ...patch });
  return (
    <div className="space-y-3">
      <div className="flex flex-wrap gap-1.5">
        {[["none", "No escalation"], ["fixed_percent", "Fixed %"], ["fixed_amount", "Fixed amount"], ["stepped", "Stepped"], ["market_review", "Market review"]].map(([t, l]) => (
          <button key={t} onClick={() => onChange(t === "none" ? null : { ...e, type: t, value: t === "fixed_percent" ? 5 : t === "fixed_amount" ? 1000 : 0, steps: t === "stepped" ? [{ from: nextDay(addYears(startDate, 1)), monthlyRent: 0 }] : undefined })}
            className={clsx("rounded-full px-3 py-1 text-xs font-medium ring-1", type === t ? "bg-teal text-white ring-teal" : "bg-white text-slate-600 ring-line hover:ring-teal/50")}>{l}</button>
        ))}
      </div>
      {value && (
        <div className="grid gap-3 sm:grid-cols-4">
          {e.type === "fixed_percent" && <Field label="Increase (%)"><Input type="number" min={0} step="0.5" value={e.value} onChange={(x) => set({ value: Number(x.target.value) })} /></Field>}
          {e.type === "fixed_amount" && <Field label="Increase (amount)"><Input type="number" min={0} value={e.value} onChange={(x) => set({ value: Number(x.target.value) })} /></Field>}
          {e.type !== "stepped" && <Field label="Every (months)"><Input type="number" min={1} max={120} value={e.everyMonths} onChange={(x) => set({ everyMonths: Number(x.target.value) })} /></Field>}
          <Field label="Notice (days before)" hint={e.type === "market_review" ? "Market reviews alert 60 days before" : undefined}><Input type="number" min={0} max={365} value={e.noticeDays} onChange={(x) => set({ noticeDays: Number(x.target.value) })} /></Field>
          <div className="space-y-2 pt-5 sm:col-span-1">
            {e.type === "fixed_percent" && <Checkbox label="Compounding" checked={e.compounding} onChange={(v) => set({ compounding: v })} />}
            <Checkbox label="Needs accountant approval" checked={e.requiresApproval} onChange={(v) => set({ requiresApproval: v })} />
          </div>
          {e.type === "stepped" && (
            <div className="space-y-2 sm:col-span-4">
              {(e.steps ?? []).map((s, i) => (
                <div key={i} className="flex gap-2">
                  <Input type="date" className="w-44" value={s.from} onChange={(x) => set({ steps: e.steps!.map((y, j) => (j === i ? { ...y, from: x.target.value } : y)) })} />
                  <Input type="number" min={0} className="w-40 text-right" placeholder="New monthly rent" value={s.monthlyRent || ""} onChange={(x) => set({ steps: e.steps!.map((y, j) => (j === i ? { ...y, monthlyRent: Number(x.target.value) } : y)) })} />
                  <button className="rounded p-1 text-slate-400 hover:text-red-600" onClick={() => set({ steps: e.steps!.filter((_, j) => j !== i) })} aria-label="Remove step"><Trash2 className="h-4 w-4" /></button>
                </div>
              ))}
              <Button variant="ghost" onClick={() => set({ steps: [...(e.steps ?? []), { from: nextDay(addYears(e.steps?.length ? e.steps[e.steps.length - 1].from : startDate, 1)), monthlyRent: 0 }] })}><Plus className="h-4 w-4" />Add step</Button>
            </div>
          )}
        </div>
      )}
      <p className="text-xs text-slate-500">
        Each change is flagged "notice due" {e.type === "market_review" ? "60" : e.noticeDays} days before it starts. {e.requiresApproval ? "The accountant approves it and NavEstate sends the tenant an SMS notice; unapproved changes never take effect." : "It is approved automatically and the SMS notice goes out on that day."}
      </p>
    </div>
  );
}

const nextDay = (iso: string) => {
  const d = new Date(iso + "T00:00:00Z");
  d.setUTCDate(d.getUTCDate() + 1);
  return d.toISOString().slice(0, 10);
};

export function SchedulePreview({ rows, loading, currency, escalation }: { rows?: PreviewRow[]; loading: boolean; currency: string; escalation: Escalation | null }) {
  return (
    <div>
      <h4 className="mb-2 text-sm font-semibold">Rent schedule {escalation ? <span className="font-normal text-slate-400">({label(escalation.type)}{escalation.type === "fixed_percent" ? ` ${escalation.value}%${escalation.compounding ? " compounding" : ""}` : ""})</span> : <span className="font-normal text-slate-400">(no escalation)</span>}</h4>
      {loading ? <Spinner /> : (
        <Table head={["From", "To", <NumHead key="r">Monthly rent</NumHead>, "Change"]}>
          {(rows ?? []).map((r) => (
            <tr key={r.from}><td>{date(r.from)}</td><td>{r.to ? date(r.to) : "Open"}</td><Num>{r.monthlyRent != null ? money(r.monthlyRent, currency) : <span className="text-slate-400">To be agreed</span>}</Num><td className="text-xs text-slate-500">{r.label}</td></tr>
          ))}
        </Table>
      )}
    </div>
  );
}

/** The parts of GET /api/leases/{id} the edit forms need. */
export interface LeaseDetailForEdit {
  lease: { id: string; leaseNo: string; status: string; startDate: string; endDate?: string; currencyCode: string; billingDay: number; frequency: string; depositAmount: number; waterDepositAmount: number; graceDays: number; penaltyRuleId?: string; vatApplicable: boolean; whtAgent: boolean; monthlyRent: number };
  tenant: { id: string; tenantNo: string; fullName: string; phone: string; blacklisted: boolean };
  unit: { id: string; code: string; unitType: string; status: string; propertyId: string; property: string };
  charges: { chargeCode: string; description: string; amount: number; frequency: string; active: boolean }[];
  escalation?: { type: string; value: number; everyMonths: number; compounding: boolean; noticeDays: number; requiresApproval: boolean } | null;
  schedule: { effectiveFrom: string; monthlyRent?: number }[];
}

export function inputFromDetail(d: LeaseDetailForEdit, withEscalation: boolean): LeaseInput {
  return {
    tenantId: d.tenant.id, unitId: d.unit.id, startDate: d.lease.startDate, endDate: d.lease.endDate ?? null, currencyCode: d.lease.currencyCode,
    billingDay: d.lease.billingDay, frequency: d.lease.frequency, depositAmount: d.lease.depositAmount, waterDepositAmount: d.lease.waterDepositAmount,
    graceDays: d.lease.graceDays, penaltyRuleId: d.lease.penaltyRuleId ?? null, vatApplicable: d.lease.vatApplicable, whtAgent: d.lease.whtAgent,
    charges: d.charges.map((c) => ({ chargeCode: c.chargeCode, description: c.description, amount: c.amount, frequency: c.frequency, active: c.active })),
    escalation: withEscalation && d.escalation ? {
      ...d.escalation,
      steps: d.escalation.type === "stepped" ? d.schedule.slice(1).filter((r) => r.monthlyRent != null).map((r) => ({ from: r.effectiveFrom, monthlyRent: r.monthlyRent! })) : undefined,
    } : null,
    overrideBlacklist: d.tenant.blacklisted,
  };
}
