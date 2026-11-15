import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { useEffect, useState, type ReactNode } from "react";
import { del, get, post, put } from "@/lib/api";
import { useAuth, type Role } from "@/lib/auth";
import { date, label, money } from "@/lib/format";
import { Button, Card, Checkbox, ErrorText, Field, Input, Modal, PageHeader, Pill, Select, Spinner, Table, Tabs, Textarea } from "@/components/ui";
import type { PropertyRow, Landlord } from "./Properties";

type TabId = "company" | "charges" | "penalties" | "water" | "channels" | "currencies" | "users" | "templates";

export function SetupPage() {
  const { can } = useAuth();
  const [tab, setTab] = useState<TabId>("company");
  const tabs: { id: TabId; label: string }[] = [
    { id: "company", label: "Company" }, { id: "charges", label: "Charge codes" }, { id: "penalties", label: "Penalty rules" },
    { id: "water", label: "Water tariffs" }, { id: "channels", label: "Payment channels" }, { id: "currencies", label: "Currencies" },
    ...(can() ? [{ id: "users" as TabId, label: "Users & roles" }] : []), { id: "templates", label: "SMS & email templates" },
  ];
  return (
    <div>
      <PageHeader title="Setup">Super admins and accountants can change setup. Charge codes must match the BC "NavEstate Charge Mapping" table.</PageHeader>
      <Tabs tabs={tabs} value={tab} onChange={setTab} />
      <div className="mt-4">
        {tab === "company" && <CompanyTab />}
        {tab === "charges" && (
          <CrudTab<ChargeCode> resource="charge-codes" title="Charge codes" keyOf={(r) => r.code} blank={{ code: "", description: "", isRent: false, allocationPriority: 3, active: true }}
            columns={[["Code", (r) => <span className="font-mono">{r.code}</span>], ["Description", (r) => r.description], ["Rent", (r) => (r.isRent ? "Yes" : "")],
              ["Allocation priority", (r) => r.allocationPriority], ["Active", (r) => (r.active ? <Pill tone="green">Active</Pill> : <Pill>Off</Pill>)]]}
            form={(v, set, isNew) => <>
              <Field label="Code" hint="Same code as in BC Charge Mapping"><Input disabled={!isNew} value={v.code} maxLength={20} onChange={(e) => set({ code: e.target.value.toUpperCase() })} /></Field>
              <Field label="Description"><Input value={v.description} maxLength={50} onChange={(e) => set({ description: e.target.value })} /></Field>
              <Field label="Allocation priority" hint="Payments clear lower numbers first (PENALTY 1, WATER 2, SERVICE 3, RENT 4)"><Input type="number" value={v.allocationPriority} onChange={(e) => set({ allocationPriority: Number(e.target.value) })} /></Field>
              <div className="flex gap-4"><Checkbox label="Is rent" checked={v.isRent} onChange={(b) => set({ isRent: b })} /><Checkbox label="Active" checked={v.active} onChange={(b) => set({ active: b })} /></div>
            </>} />
        )}
        {tab === "penalties" && (
          <CrudTab<PenaltyRule> resource="penalty-rules" title="Penalty rules" keyOf={(r) => r.id!} blank={{ name: "", basis: "percent_overdue_rent", value: 10, frequency: "once", capType: "none", capValue: null, graceDays: 5 }}
            columns={[["Name", (r) => r.name], ["Basis", (r) => label(r.basis)], ["Value", (r) => (r.basis === "fixed" ? money(r.value) : `${r.value}%`)],
              ["Frequency", (r) => label(r.frequency)], ["Cap", (r) => (r.capType === "none" ? "None" : r.capType === "amount" ? money(r.capValue ?? 0) : `${r.capValue}% of rent`)], ["Grace days", (r) => r.graceDays]]}
            form={(v, set) => <>
              <Field label="Name"><Input value={v.name} onChange={(e) => set({ name: e.target.value })} /></Field>
              <Field label="Basis"><Select value={v.basis} onChange={(e) => set({ basis: e.target.value })} options={["fixed", "percent_overdue_rent", "percent_overdue_balance"]} /></Field>
              <Field label={v.basis === "fixed" ? "Amount" : "Percent"}><Input type="number" step="0.01" value={v.value} onChange={(e) => set({ value: Number(e.target.value) })} /></Field>
              <Field label="Frequency"><Select value={v.frequency} onChange={(e) => set({ frequency: e.target.value })} options={["once", "monthly"]} /></Field>
              <Field label="Cap"><Select value={v.capType} onChange={(e) => set({ capType: e.target.value })} options={["none", "amount", "percent_of_rent"]} /></Field>
              {v.capType !== "none" && <Field label="Cap value"><Input type="number" step="0.01" value={v.capValue ?? ""} onChange={(e) => set({ capValue: Number(e.target.value) })} /></Field>}
              <Field label="Grace days"><Input type="number" value={v.graceDays} onChange={(e) => set({ graceDays: Number(e.target.value) })} /></Field>
            </>} />
        )}
        {tab === "water" && <WaterTab />}
        {tab === "channels" && (
          <CrudTab<Channel> resource="payment-channels" title="Payment channels" keyOf={(r) => r.id!} blank={{ code: "", name: "", bcChannelCode: "MPESA", accountNo: "", currencyCode: "KES", active: true }}
            columns={[["Code", (r) => <span className="font-mono">{r.code}</span>], ["Name", (r) => r.name], ["BC channel", (r) => r.bcChannelCode], ["Account", (r) => r.accountNo || "—"], ["Currency", (r) => r.currencyCode], ["Active", (r) => (r.active ? <Pill tone="green">Active</Pill> : <Pill>Off</Pill>)]]}
            form={(v, set) => <>
              <Field label="Code"><Input value={v.code} onChange={(e) => set({ code: e.target.value.toUpperCase() })} /></Field>
              <Field label="Name"><Input value={v.name} onChange={(e) => set({ name: e.target.value })} /></Field>
              <Field label="BC channel code" hint="Matches BC NavEstate Payment Channel"><Select value={v.bcChannelCode} onChange={(e) => set({ bcChannelCode: e.target.value })} options={["MPESA", "BANK", "CHEQUE", "CASH"]} /></Field>
              <Field label="Account no. / Paybill"><Input value={v.accountNo ?? ""} onChange={(e) => set({ accountNo: e.target.value })} /></Field>
              <Field label="Currency"><Select value={v.currencyCode} onChange={(e) => set({ currencyCode: e.target.value })} options={["KES", "USD"]} /></Field>
              <Checkbox label="Active" checked={v.active} onChange={(b) => set({ active: b })} />
            </>} />
        )}
        {tab === "currencies" && <CurrenciesTab />}
        {tab === "users" && <UsersTab />}
        {tab === "templates" && (
          <CrudTab<Template> resource="templates" title="SMS & email templates" keyOf={(r) => r.id!} blank={{ code: "", channel: "sms", subject: "", body: "", active: true }}
            columns={[["Code", (r) => <span className="font-mono text-xs">{r.code}</span>], ["Channel", (r) => label(r.channel)], ["Text", (r) => <span className="line-clamp-2 text-xs text-slate-600">{r.body}</span>]]}
            form={(v, set) => <>
              <Field label="Code"><Input value={v.code} onChange={(e) => set({ code: e.target.value })} /></Field>
              <Field label="Channel"><Select value={v.channel} onChange={(e) => set({ channel: e.target.value })} options={["sms", "email", "portal"]} /></Field>
              {v.channel === "email" && <Field label="Subject"><Input value={v.subject ?? ""} onChange={(e) => set({ subject: e.target.value })} /></Field>}
              <Field label="Text" hint={`Placeholders like {{tenant_name}}, {{amount}}, {{unit}}.${v.channel === "sms" ? ` ${v.body.length}/480 characters.` : ""}`}><Textarea rows={5} value={v.body} onChange={(e) => set({ body: e.target.value })} /></Field>
              <Checkbox label="Active" checked={v.active} onChange={(b) => set({ active: b })} />
            </>} />
        )}
      </div>
    </div>
  );
}

interface ChargeCode { code: string; description: string; isRent: boolean; allocationPriority: number; active: boolean }
interface PenaltyRule { id?: string; name: string; basis: string; value: number; frequency: string; capType: string; capValue: number | null; graceDays: number }
interface Channel { id?: string; code: string; name: string; bcChannelCode: string; accountNo?: string; currencyCode: string; active: boolean }
interface Template { id?: string; code: string; channel: string; subject?: string; body: string; active: boolean }

function CrudTab<T extends object>({ resource, title, columns, form, blank, keyOf }: {
  resource: string; title: string; blank: T; keyOf: (r: T) => string;
  columns: [string, (r: T) => ReactNode][];
  form: (v: T, set: (p: Partial<T>) => void, isNew: boolean) => ReactNode;
}) {
  const { can } = useAuth();
  const write = can("accountant");
  const qc = useQueryClient();
  const url = `/api/setup/${resource}`;
  const { data, isLoading, error } = useQuery({ queryKey: [resource], queryFn: () => get<T[]>(url) });
  const [edit, setEdit] = useState<{ value: T; isNew: boolean } | null>(null);
  const save = useMutation({
    mutationFn: () => (edit!.isNew ? post(url, edit!.value) : put(`${url}/${encodeURIComponent(keyOf(edit!.value))}`, edit!.value)),
    onSuccess: () => { qc.invalidateQueries({ queryKey: [resource] }); setEdit(null); },
  });
  const remove = useMutation({ mutationFn: (k: string) => del(`${url}/${encodeURIComponent(k)}`), onSuccess: () => qc.invalidateQueries({ queryKey: [resource] }) });
  return (
    <Card title={title} padded={false} actions={write && <Button onClick={() => setEdit({ value: { ...blank }, isNew: true })}><Plus className="h-4 w-4" />Add</Button>}>
      {isLoading ? <Spinner /> : error ? <div className="p-4"><ErrorText error={error} /></div> : (
        <Table head={[...columns.map((c) => c[0]), ""]}>
          {data!.map((r) => (
            <tr key={keyOf(r)}>
              {columns.map(([h, render]) => <td key={h}>{render(r)}</td>)}
              <td className="whitespace-nowrap text-right">
                {write && <>
                  <button aria-label="Edit" className="mr-2 text-slate-400 hover:text-teal" onClick={() => setEdit({ value: { ...r }, isNew: false })}><Pencil className="h-4 w-4" /></button>
                  <button aria-label="Delete" className="text-slate-300 hover:text-red-500" onClick={() => remove.mutate(keyOf(r))}><Trash2 className="h-4 w-4" /></button>
                </>}
              </td>
            </tr>
          ))}
        </Table>
      )}
      {remove.error && <div className="p-3"><ErrorText error={remove.error} /></div>}
      {edit && (
        <Modal open title={`${edit.isNew ? "Add" : "Edit"}: ${title.toLowerCase()}`} onClose={() => setEdit(null)}
          footer={<><Button variant="secondary" onClick={() => setEdit(null)}>Cancel</Button><Button busy={save.isPending} onClick={() => save.mutate()}>Save</Button></>}>
          <div className="space-y-3">
            <ErrorText error={save.error} />
            {form(edit.value, (p) => setEdit({ ...edit, value: { ...edit.value, ...p } }), edit.isNew)}
          </div>
        </Modal>
      )}
    </Card>
  );
}

const settingFields: { key: string; label: string; hint?: string; type?: "number" | "text" | "list" }[] = [
  { key: "company_name", label: "Company name" }, { key: "company_address", label: "Address" }, { key: "company_kra_pin", label: "KRA PIN" },
  { key: "company_logo", label: "Logo URL" }, { key: "paybill_number", label: "M-PESA Paybill number" },
  { key: "allocation_order", label: "Allocation order", hint: "Comma-separated charge codes, cleared in this order", type: "list" },
  { key: "vat_rate", label: "VAT rate %", type: "number" }, { key: "wht_rate", label: "WHT rate %", type: "number" }, { key: "mri_rate", label: "MRI rate %", type: "number" },
  { key: "penalty_run_time", label: "Penalty run time", hint: "Nightly, Nairobi time (HH:MM)" }, { key: "reminder_days", label: "Reminder days before due", type: "number" },
  { key: "sms_sender_id", label: "SMS sender ID" },
];

function CompanyTab() {
  const { can } = useAuth();
  const qc = useQueryClient();
  const { data, isLoading } = useQuery({ queryKey: ["settings"], queryFn: () => get<Record<string, unknown>>("/api/setup/settings") });
  const [f, setF] = useState<Record<string, string>>({});
  useEffect(() => {
    if (data) setF(Object.fromEntries(settingFields.map((s) => [s.key, Array.isArray(data[s.key]) ? (data[s.key] as string[]).join(", ") : String(data[s.key] ?? "")])));
  }, [data]);
  const save = useMutation({
    mutationFn: () => put("/api/setup/settings", Object.fromEntries(settingFields.map((s) => [s.key,
      s.type === "number" ? Number(f[s.key] || 0) : s.type === "list" ? f[s.key].split(",").map((x) => x.trim().toUpperCase()).filter(Boolean) : f[s.key]]))),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["settings"] }),
  });
  if (isLoading) return <Spinner />;
  return (
    <Card title="Company settings" actions={can("accountant") && <Button busy={save.isPending} onClick={() => save.mutate()}>Save</Button>}>
      <ErrorText error={save.error} />
      {save.isSuccess && <p className="mb-3 text-sm text-emerald-700">Saved.</p>}
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {settingFields.map((s) => (
          <Field key={s.key} label={s.label} hint={s.hint}>
            <Input type={s.type === "number" ? "number" : "text"} step="0.01" disabled={!can("accountant")} value={f[s.key] ?? ""} onChange={(e) => setF({ ...f, [s.key]: e.target.value })} />
          </Field>
        ))}
      </div>
    </Card>
  );
}

interface Tariff { id: string; propertyId?: string; name: string; standingCharge: number; effectiveFrom: string; bands: { fromM3: number; toM3?: number | null; rate: number }[] }

function WaterTab() {
  const { can } = useAuth();
  const qc = useQueryClient();
  const { data, isLoading } = useQuery({ queryKey: ["water-tariffs"], queryFn: () => get<Tariff[]>("/api/setup/water-tariffs") });
  const props = useQuery({ queryKey: ["properties", ""], queryFn: () => get<PropertyRow[]>("/api/properties?q=") });
  const [edit, setEdit] = useState<Partial<Tariff> | null>(null);
  const save = useMutation({
    mutationFn: () => {
      const body = { ...edit, propertyId: edit!.propertyId || null, bands: edit!.bands!.map((b) => ({ fromM3: Number(b.fromM3), toM3: b.toM3 === null || b.toM3 === undefined || String(b.toM3) === "" ? null : Number(b.toM3), rate: Number(b.rate) })) };
      return edit!.id ? put(`/api/setup/water-tariffs/${edit!.id}`, body) : post("/api/setup/water-tariffs", body);
    },
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["water-tariffs"] }); setEdit(null); },
  });
  const propName = (id?: string) => props.data?.find((p) => p.id === id)?.name ?? "All properties";
  if (isLoading) return <Spinner />;
  return (
    <Card title="Water tariffs" padded={false} actions={can("accountant") && <Button onClick={() => setEdit({ name: "", standingCharge: 0, effectiveFrom: new Date().toISOString().slice(0, 10), bands: [{ fromM3: 0, toM3: null, rate: 0 }] })}><Plus className="h-4 w-4" />Add</Button>}>
      <Table head={["Name", "Property", "Effective from", "Standing charge", "Bands", ""]}>
        {data!.map((t) => (
          <tr key={t.id}>
            <td className="font-medium">{t.name}</td><td>{propName(t.propertyId)}</td><td>{date(t.effectiveFrom)}</td><td>{money(t.standingCharge)}</td>
            <td className="text-xs text-slate-600">{t.bands.map((b) => `${b.fromM3}–${b.toM3 ?? "∞"} m³ @ ${b.rate}`).join(" · ")}</td>
            <td className="text-right">{can("accountant") && <button aria-label="Edit" className="text-slate-400 hover:text-teal" onClick={() => setEdit(t)}><Pencil className="h-4 w-4" /></button>}</td>
          </tr>
        ))}
      </Table>
      {edit && (
        <Modal open wide title={edit.id ? "Edit water tariff" : "New water tariff"} onClose={() => setEdit(null)}
          footer={<><Button variant="secondary" onClick={() => setEdit(null)}>Cancel</Button><Button busy={save.isPending} onClick={() => save.mutate()}>Save</Button></>}>
          <div className="space-y-3">
            <ErrorText error={save.error} />
            <div className="grid gap-3 sm:grid-cols-2">
              <Field label="Name"><Input value={edit.name} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></Field>
              <Field label="Property"><Select value={edit.propertyId ?? ""} onChange={(e) => setEdit({ ...edit, propertyId: e.target.value })} placeholder="All properties" options={(props.data ?? []).map((p) => ({ value: p.id, label: p.name }))} /></Field>
              <Field label="Standing charge (KES)"><Input type="number" value={edit.standingCharge} onChange={(e) => setEdit({ ...edit, standingCharge: Number(e.target.value) })} /></Field>
              <Field label="Effective from"><Input type="date" value={edit.effectiveFrom} onChange={(e) => setEdit({ ...edit, effectiveFrom: e.target.value })} /></Field>
            </div>
            <div>
              <div className="mb-1 text-xs font-medium text-slate-600">Bands (from m³, to m³ – leave the last one empty, rate per m³)</div>
              {edit.bands!.map((b, i) => (
                <div key={i} className="mb-2 grid grid-cols-[1fr_1fr_1fr_32px] gap-2">
                  <Input type="number" value={b.fromM3} onChange={(e) => setEdit({ ...edit, bands: edit.bands!.map((x, j) => (j === i ? { ...x, fromM3: Number(e.target.value) } : x)) })} />
                  <Input type="number" placeholder="∞" value={b.toM3 ?? ""} onChange={(e) => setEdit({ ...edit, bands: edit.bands!.map((x, j) => (j === i ? { ...x, toM3: e.target.value === "" ? null : Number(e.target.value) } : x)) })} />
                  <Input type="number" step="0.01" value={b.rate} onChange={(e) => setEdit({ ...edit, bands: edit.bands!.map((x, j) => (j === i ? { ...x, rate: Number(e.target.value) } : x)) })} />
                  <button aria-label="Remove band" className="text-slate-400 hover:text-red-500" onClick={() => setEdit({ ...edit, bands: edit.bands!.filter((_, j) => j !== i) })}><Trash2 className="h-4 w-4" /></button>
                </div>
              ))}
              <Button variant="secondary" onClick={() => { const last = edit.bands![edit.bands!.length - 1]; setEdit({ ...edit, bands: [...edit.bands!, { fromM3: last?.toM3 ?? 0, toM3: null, rate: 0 }] }); }}><Plus className="h-4 w-4" />Add band</Button>
            </div>
          </div>
        </Modal>
      )}
    </Card>
  );
}

function CurrenciesTab() {
  const { can } = useAuth();
  const qc = useQueryClient();
  const cur = useQuery({ queryKey: ["currencies"], queryFn: () => get<{ code: string; name: string; bcCode: string }[]>("/api/setup/currencies") });
  const rates = useQuery({ queryKey: ["rates"], queryFn: () => get<{ id: string; currencyCode: string; rateDate: string; kesPerUnit: number; source: string }[]>("/api/setup/exchange-rates") });
  const [rate, setRate] = useState({ rateDate: new Date().toISOString().slice(0, 10), kesPerUnit: "" });
  const add = useMutation({ mutationFn: () => post("/api/setup/exchange-rates", { currencyCode: "USD", rateDate: rate.rateDate, kesPerUnit: Number(rate.kesPerUnit) }), onSuccess: () => qc.invalidateQueries({ queryKey: ["rates"] }) });
  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <Card title="Currencies" padded={false}>
        <Table head={["Code", "Name", "Sent to BC as"]}>
          {(cur.data ?? []).map((c) => <tr key={c.code}><td className="font-mono">{c.code}</td><td>{c.name}</td><td className="font-mono">{c.bcCode === "" ? "'' (local currency)" : c.bcCode}</td></tr>)}
        </Table>
      </Card>
      <Card title="USD exchange rates" padded={false}>
        <p className="px-4 pt-3 text-xs text-slate-500">A daily job reads the rate from BC (GetExchangeRate) at 06:00. Add a manual rate only when BC is unavailable.</p>
        {can("accountant") && (
          <form className="flex flex-wrap gap-2 p-4" onSubmit={(e) => { e.preventDefault(); add.mutate(); }}>
            <Input type="date" className="w-40" value={rate.rateDate} onChange={(e) => setRate({ ...rate, rateDate: e.target.value })} />
            <Input type="number" step="0.000001" className="w-40" placeholder="KES per USD" value={rate.kesPerUnit} onChange={(e) => setRate({ ...rate, kesPerUnit: e.target.value })} />
            <Button variant="secondary" busy={add.isPending} disabled={!rate.kesPerUnit}>Save rate</Button>
            <ErrorText error={add.error} />
          </form>
        )}
        <Table head={["Date", "KES per USD", "Source"]}>
          {(rates.data ?? []).map((r) => <tr key={r.id}><td>{date(r.rateDate)}</td><td className="tabular-nums">{r.kesPerUnit.toFixed(4)}</td><td><Pill tone={r.source === "BC" ? "teal" : "grey"}>{r.source}</Pill></td></tr>)}
        </Table>
      </Card>
    </div>
  );
}

const ROLES: Role[] = ["super_admin", "property_manager", "accountant", "cashier", "caretaker", "landlord", "tenant"];
interface UserRow { id: string; email: string; fullName: string; phone?: string; active: boolean; twoFactorEnabled: boolean; roles: Role[]; propertyIds: string[]; landlordIds: string[]; tenantId?: string; landlordId?: string; lockedOut: boolean }

function UsersTab() {
  const qc = useQueryClient();
  const { data, isLoading } = useQuery({ queryKey: ["users"], queryFn: () => get<UserRow[]>("/api/setup/users") });
  const props = useQuery({ queryKey: ["properties", ""], queryFn: () => get<PropertyRow[]>("/api/properties?q=") });
  const landlords = useQuery({ queryKey: ["landlords"], queryFn: () => get<Landlord[]>("/api/landlords") });
  const [edit, setEdit] = useState<(Partial<UserRow> & { password?: string }) | null>(null);
  const save = useMutation({
    mutationFn: () => {
      const body = { email: edit!.email, fullName: edit!.fullName, phone: edit!.phone, roles: edit!.roles ?? [], active: edit!.active ?? true,
        propertyIds: edit!.propertyIds ?? [], landlordIds: edit!.landlordIds ?? [], tenantId: edit!.tenantId || null, landlordId: edit!.landlordId || null, password: edit!.password || null };
      return edit!.id ? put(`/api/setup/users/${edit!.id}`, body) : post("/api/setup/users", body);
    },
    onSuccess: () => { qc.invalidateQueries({ queryKey: ["users"] }); setEdit(null); },
  });
  const reset2fa = useMutation({ mutationFn: (id: string) => post(`/api/setup/users/${id}/reset-2fa`), onSuccess: () => qc.invalidateQueries({ queryKey: ["users"] }) });
  const toggle = <K extends "roles" | "propertyIds" | "landlordIds">(k: K, v: string) => {
    const list = (edit![k] ?? []) as string[];
    setEdit({ ...edit!, [k]: list.includes(v) ? list.filter((x) => x !== v) : [...list, v] });
  };
  if (isLoading) return <Spinner />;
  return (
    <Card title="Users & roles" padded={false} actions={<Button onClick={() => setEdit({ roles: ["property_manager"], active: true, propertyIds: [], landlordIds: [] })}><Plus className="h-4 w-4" />Add user</Button>}>
      <Table head={["Name", "Email", "Roles", "Access", "2FA", "Status", ""]}>
        {data!.map((u) => (
          <tr key={u.id}>
            <td className="font-medium">{u.fullName}</td><td className="text-slate-600">{u.email}</td>
            <td><div className="flex flex-wrap gap-1">{u.roles.map((r) => <Pill key={r} tone="teal">{label(r)}</Pill>)}</div></td>
            <td className="text-xs text-slate-500">{u.roles.includes("super_admin") ? "Everything" : `${u.landlordIds.length} landlord(s), ${u.propertyIds.length} property(ies)`}</td>
            <td>{u.twoFactorEnabled ? <Pill tone="green">On</Pill> : <Pill>Off</Pill>}</td>
            <td>{!u.active ? <Pill tone="red">Disabled</Pill> : u.lockedOut ? <Pill tone="amber">Locked</Pill> : <Pill tone="green">Active</Pill>}</td>
            <td className="whitespace-nowrap text-right">
              {u.twoFactorEnabled && <Button variant="ghost" onClick={() => reset2fa.mutate(u.id)}>Reset 2FA</Button>}
              <button aria-label="Edit" className="text-slate-400 hover:text-teal" onClick={() => setEdit(u)}><Pencil className="h-4 w-4" /></button>
            </td>
          </tr>
        ))}
      </Table>
      {edit && (
        <Modal open wide title={edit.id ? `Edit ${edit.fullName}` : "Add user"} onClose={() => setEdit(null)}
          footer={<><Button variant="secondary" onClick={() => setEdit(null)}>Cancel</Button><Button busy={save.isPending} onClick={() => save.mutate()}>Save</Button></>}>
          <div className="space-y-4">
            <ErrorText error={save.error} />
            <div className="grid gap-3 sm:grid-cols-2">
              <Field label="Full name"><Input value={edit.fullName ?? ""} onChange={(e) => setEdit({ ...edit, fullName: e.target.value })} /></Field>
              <Field label="Email"><Input type="email" value={edit.email ?? ""} onChange={(e) => setEdit({ ...edit, email: e.target.value })} /></Field>
              <Field label="Phone" hint="Tenants sign in with this number"><Input value={edit.phone ?? ""} onChange={(e) => setEdit({ ...edit, phone: e.target.value })} /></Field>
              <Field label={edit.id ? "New password (optional)" : "Password"} hint="At least 10 characters with upper, lower case and a digit"><Input type="password" value={edit.password ?? ""} onChange={(e) => setEdit({ ...edit, password: e.target.value })} /></Field>
            </div>
            <div>
              <div className="mb-1 text-xs font-medium text-slate-600">Roles</div>
              <div className="flex flex-wrap gap-3">{ROLES.map((r) => <Checkbox key={r} label={label(r)} checked={edit.roles?.includes(r) ?? false} onChange={() => toggle("roles", r)} />)}</div>
            </div>
            {edit.roles?.includes("landlord") && (
              <Field label="Landlord record"><Select value={edit.landlordId ?? ""} onChange={(e) => setEdit({ ...edit, landlordId: e.target.value })} placeholder="Choose…" options={(landlords.data ?? []).map((l) => ({ value: l.id, label: l.name }))} /></Field>
            )}
            {edit.roles?.includes("tenant") && <Field label="Tenant record id" hint="Open the tenant and copy the id from the address bar"><Input value={edit.tenantId ?? ""} onChange={(e) => setEdit({ ...edit, tenantId: e.target.value })} /></Field>}
            {!edit.roles?.includes("super_admin") && !edit.roles?.includes("tenant") && (
              <div className="grid gap-4 sm:grid-cols-2">
                <div>
                  <div className="mb-1 text-xs font-medium text-slate-600">Landlords (all their properties)</div>
                  <div className="space-y-1">{(landlords.data ?? []).map((l) => <div key={l.id}><Checkbox label={l.name} checked={edit.landlordIds?.includes(l.id) ?? false} onChange={() => toggle("landlordIds", l.id)} /></div>)}</div>
                </div>
                <div>
                  <div className="mb-1 text-xs font-medium text-slate-600">Or single properties</div>
                  <div className="space-y-1">{(props.data ?? []).map((p) => <div key={p.id}><Checkbox label={p.name} checked={edit.propertyIds?.includes(p.id) ?? false} onChange={() => toggle("propertyIds", p.id)} /></div>)}</div>
                </div>
              </div>
            )}
            {edit.id && <Checkbox label="Active (untick to block sign-in)" checked={edit.active ?? true} onChange={(b) => setEdit({ ...edit, active: b })} />}
          </div>
        </Modal>
      )}
    </Card>
  );
}
