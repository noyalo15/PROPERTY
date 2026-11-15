import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeftRight, BellRing, CalendarPlus, FileText, Pencil, Play, Send, Trash2, X } from "lucide-react";
import { useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { del, get, post, put } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { date, dateTime, label, money } from "@/lib/format";
import { Button, Card, Checkbox, Empty, ErrorText, Field, Input, Modal, Num, NumHead, PageHeader, Pill, Select, Spinner, StatusPill, Table } from "@/components/ui";
import { BcTick } from "./Tenants";
import { daysUntil, scheduleLabel, scheduleTone } from "./Leases";
import { addYears, ChargesEditor, inputFromDetail, type ChargeCode, type LeaseDetailForEdit, type LeaseInput } from "./LeaseWizard";

interface ScheduleRow {
  id: string; effectiveFrom: string; effectiveTo?: string; monthlyRent?: number; changeLabel?: string; status: string;
  noticeSentAt?: string; approvedBy?: string; noticeDate?: string; overdue: boolean;
}
interface Detail extends LeaseDetailForEdit {
  lease: LeaseDetailForEdit["lease"] & { penaltyRule?: string; endReason?: string; endedAt?: string; createdAt: string };
  tenant: LeaseDetailForEdit["tenant"] & { email?: string; bcSyncedAt?: string; bcStatus?: string };
  unit: LeaseDetailForEdit["unit"] & { propertyCode: string };
  charges: { id: string; chargeCode: string; description: string; amount: number; currencyCode: string; frequency: string; active: boolean; isRent: boolean; bcAccount?: string }[];
  schedule: ScheduleRow[];
  deposits: { id: string; depositType: string; currencyCode: string; amountHeld: number; status: string }[];
  notices: { id: string; channel: string; subject?: string; body: string; status: string; sentAt?: string; createdAt: string }[];
}

export function LeaseDetailPage() {
  const { id } = useParams();
  const nav = useNavigate();
  const qc = useQueryClient();
  const { can } = useAuth();
  const [modal, setModal] = useState<null | "edit" | "renew" | "end" | { approve: ScheduleRow } | { amend: ScheduleRow }>(null);
  const { data, isLoading, error } = useQuery({ queryKey: ["lease", id], queryFn: () => get<Detail>(`/api/leases/${id}`) });
  const refresh = () => {
    qc.invalidateQueries({ queryKey: ["lease", id] });
    qc.invalidateQueries({ queryKey: ["leases"] });
    qc.invalidateQueries({ queryKey: ["leases-summary"] });
    qc.invalidateQueries({ queryKey: ["dashboard"] });
  };
  const activate = useMutation({ mutationFn: () => post(`/api/leases/${id}/activate`, { overrideBlacklist: data?.tenant.blacklisted }), onSuccess: refresh });
  const remove = useMutation({ mutationFn: () => del(`/api/leases/${id}`), onSuccess: () => { refresh(); nav("/leases"); } });
  const decline = useMutation({ mutationFn: (rowId: string) => del(`/api/leases/${id}/schedule/${rowId}`), onSuccess: refresh });

  if (isLoading) return <Spinner />;
  if (error || !data) return <ErrorText error={error} />;
  const l = data.lease;
  const ops = can("property_manager");
  const approver = can("accountant");
  const cur = l.currencyCode;
  const due = data.schedule.filter((r) => r.status === "pending_approval");
  const left = daysUntil(l.endDate);
  const prevRent = (row: ScheduleRow) => [...data.schedule].filter((r) => r.effectiveFrom < row.effectiveFrom && r.monthlyRent != null).pop()?.monthlyRent;

  return (
    <div className="space-y-4">
      <PageHeader title={`Lease ${l.leaseNo}`} actions={<StatusPill status={l.status} />}>
        <Link to="/leases" className="hover:text-teal">Leases</Link> / <Link to={`/tenants/${data.tenant.id}`} className="hover:text-teal">{data.tenant.fullName}</Link> · {data.unit.code}, {data.unit.property}
      </PageHeader>

      {l.status === "draft" && (
        <div className="flex flex-wrap items-center gap-3 rounded-card border border-line bg-white px-4 py-3 text-sm">
          <span className="text-slate-600">This lease is a draft. Activating it lets {data.unit.code} to {data.tenant.fullName}.</span>
          {ops && <div className="ml-auto flex gap-2">
            <Button variant="ghost" busy={remove.isPending} onClick={() => remove.mutate()}><Trash2 className="h-4 w-4" />Delete draft</Button>
            <Button variant="secondary" onClick={() => nav(`/leases/new?draft=${l.id}`)}><Pencil className="h-4 w-4" />Edit in wizard</Button>
            <Button busy={activate.isPending} onClick={() => activate.mutate()}><Play className="h-4 w-4" />Activate</Button>
          </div>}
          <div className="w-full"><ErrorText error={activate.error ?? remove.error} /></div>
        </div>
      )}

      <div className="grid gap-4 lg:grid-cols-5">
        <Card title="Lease details" className="min-w-0 lg:col-span-2" actions={l.status === "active" && ops && <Button variant="ghost" onClick={() => setModal("edit")}><Pencil className="h-4 w-4" />Edit</Button>}>
          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 text-sm">
            <dt className="text-slate-500">Tenant</dt><dd><Link to={`/tenants/${data.tenant.id}`} className="font-medium text-teal">{data.tenant.fullName}</Link> <span className="font-mono text-xs text-slate-400">{data.tenant.tenantNo}</span></dd>
            <dt className="text-slate-500">Unit</dt><dd><Link to={`/properties/${data.unit.propertyId}`} className="hover:text-teal">{data.unit.code}, {data.unit.property}</Link> <span className="text-xs text-slate-400">{label(data.unit.unitType)}</span></dd>
            <dt className="text-slate-500">Term</dt>
            <dd>{date(l.startDate)} to {l.endDate ? date(l.endDate) : "open-ended"}
              {l.status === "active" && left !== null && left <= 90 && <span className={`ml-2 text-xs ${left <= 30 ? "text-red-600" : "text-amber-600"}`}>{left} days left</span>}</dd>
            <dt className="text-slate-500">Monthly rent</dt><dd className="font-semibold">{money(l.monthlyRent, cur)}</dd>
            <dt className="text-slate-500">Billing</dt><dd>{label(l.frequency)}, day {l.billingDay}, {cur}</dd>
            <dt className="text-slate-500">Deposits</dt><dd>{money(l.depositAmount, cur)} rent{l.waterDepositAmount ? ` · ${money(l.waterDepositAmount, cur)} water` : ""}</dd>
            <dt className="text-slate-500">Late payment</dt><dd>{l.graceDays} grace days · {l.penaltyRule ?? "no penalty rule"}</dd>
            <dt className="text-slate-500">Tax</dt><dd>{l.vatApplicable ? "VAT 16%" : "No VAT"}{l.whtAgent ? " · tenant withholds 10% WHT" : ""}</dd>
            {l.endReason && <><dt className="text-slate-500">Ended</dt><dd>{dateTime(l.endedAt)}: {l.endReason}</dd></>}
            <dt className="text-slate-500">BC</dt><dd><BcTick syncedAt={data.tenant.bcSyncedAt} status={data.tenant.bcStatus} /> <span className="text-xs text-slate-400">PROPERTY {data.unit.propertyCode}, UNIT {data.unit.code}</span></dd>
          </dl>
          <div className="mt-4 flex flex-wrap gap-2 border-t border-line pt-3">
            <Link to={`/tenants/${data.tenant.id}`}><Button variant="secondary"><FileText className="h-4 w-4" />Statement</Button></Link>
            <Button variant="secondary" disabled title="Transfers arrive in stage 5"><ArrowLeftRight className="h-4 w-4" />Transfer unit</Button>
            {l.status === "active" && ops && <>
              <Button variant="secondary" onClick={() => setModal("renew")}><CalendarPlus className="h-4 w-4" />Renew</Button>
              <Button variant="ghost" className="text-red-600" onClick={() => setModal("end")}><X className="h-4 w-4" />End lease</Button>
            </>}
          </div>
        </Card>

        <Card title="Rent schedule and escalation" className="min-w-0 lg:col-span-3" padded={false}
          actions={data.escalation && <span className="text-xs text-slate-500">{label(data.escalation.type)}{data.escalation.type === "fixed_percent" ? ` ${data.escalation.value}%${data.escalation.compounding ? " compounding" : " simple"}` : data.escalation.type === "fixed_amount" ? ` +${money(data.escalation.value, cur)}` : ""}{data.escalation.type !== "stepped" ? ` every ${data.escalation.everyMonths} months` : ""} · {data.escalation.noticeDays} days notice</span>}>
          {due.map((r) => (
            <div key={r.id} className="flex flex-wrap items-center gap-3 border-b border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
              <BellRing className="h-4 w-4 shrink-0 text-amber-600" />
              <span className="flex-1">
                <b>Escalation notice due.</b> Rent changes from {money(prevRent(r), cur)} to {r.monthlyRent != null ? <b>{money(r.monthlyRent, cur)}</b> : "an amount to be agreed (market review)"} on {date(r.effectiveFrom)}.
                {r.overdue && " The date has passed: the old rent applies until this is approved."}
              </span>
              {approver ? <Button onClick={() => setModal({ approve: r })}><Send className="h-4 w-4" />Approve &amp; send notice</Button>
                : <span className="text-xs">Waiting for the accountant to approve.</span>}
            </div>
          ))}
          <Table head={["From", "To", <NumHead key="r">Monthly rent</NumHead>, "Change", "Status", ""]}>
            {data.schedule.map((r) => (
              <tr key={r.id}>
                <td className="whitespace-nowrap">{date(r.effectiveFrom)}</td>
                <td className="whitespace-nowrap">{r.effectiveTo ? date(r.effectiveTo) : "Open"}</td>
                <Num>{r.monthlyRent != null ? money(r.monthlyRent, cur) : <span className="text-slate-400">To be agreed</span>}</Num>
                <td className="whitespace-nowrap text-xs text-slate-500">{r.changeLabel}</td>
                <td>
                  <Pill tone={scheduleTone(r.status)}>{scheduleLabel(r.status)}</Pill>
                  {r.status === "scheduled" && r.noticeDate && <span className="block text-[11px] text-slate-400">Notice from {date(r.noticeDate)}</span>}
                  {r.noticeSentAt && r.status !== "completed" && <span className="block text-[11px] text-slate-400">Notice sent {date(r.noticeSentAt)}{r.approvedBy ? ` · ${r.approvedBy}` : ""}</span>}
                </td>
                <td className="whitespace-nowrap text-right">
                  {approver && l.status === "active" && (r.status === "scheduled" || r.status === "pending_approval") && r !== data.schedule[0] && <>
                    <button className="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-teal" title="Change amount" onClick={() => setModal({ amend: r })}><Pencil className="h-3.5 w-3.5" /></button>
                    <button className="rounded p-1 text-slate-400 hover:bg-red-50 hover:text-red-600" title="Decline this escalation" onClick={() => decline.mutate(r.id)}><Trash2 className="h-3.5 w-3.5" /></button>
                  </>}
                </td>
              </tr>
            ))}
          </Table>
          {data.schedule.length === 0 && <Empty>No schedule.</Empty>}
          <div className="px-4 pb-3"><ErrorText error={decline.error} /></div>
        </Card>
      </div>

      <Card title="Recurring charges" padded={false}>
        <Table head={["Charge", "Description", "BC G/L account (Charge Mapping)", "Frequency", <NumHead key="a">Amount</NumHead>, ""]}>
          {data.charges.map((c) => (
            <tr key={c.id} className={c.active ? "" : "opacity-50"}>
              <td className="font-mono text-xs">{c.chargeCode}</td>
              <td>{c.description}</td>
              <td className="text-slate-600">{c.bcAccount}</td>
              <td>{label(c.frequency)}</td>
              <Num>{money(c.amount, c.currencyCode)}</Num>
              <td>{c.isRent ? <Pill tone="teal">Follows schedule</Pill> : !c.active && <Pill>Inactive</Pill>}</td>
            </tr>
          ))}
        </Table>
      </Card>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="Deposits" className="min-w-0" padded={false}>
          {data.deposits.length === 0 ? <Empty>{l.status === "draft" ? "Deposit records are created when the lease is activated." : "No deposits."}</Empty> : (
            <Table head={["Type", "Expected", <NumHead key="h">Received / held</NumHead>, "Status"]}>
              {data.deposits.map((d) => (
                <tr key={d.id}>
                  <td>{label(d.depositType)}</td>
                  <td>{money(d.depositType === "rent" ? l.depositAmount : l.waterDepositAmount, d.currencyCode)}</td>
                  <Num>{money(d.amountHeld, d.currencyCode)}</Num>
                  <td><StatusPill status={d.status} />{d.amountHeld === 0 && <span className="ml-1 text-xs text-amber-600">awaiting receipt</span>}</td>
                </tr>
              ))}
            </Table>
          )}
        </Card>
        <Card title="Escalation notices" className="min-w-0" padded={false}>
          {data.notices.length === 0 ? <Empty>No notices sent for this lease yet.</Empty> : (
            <ul className="divide-y divide-line">
              {data.notices.map((n) => (
                <li key={n.id} className="px-4 py-2.5 text-sm">
                  <div className="flex items-center gap-2"><Pill tone={n.status === "sent" ? "green" : n.status === "failed" ? "red" : "amber"}>{n.channel === "sms" ? "SMS" : label(n.channel)} {n.status}</Pill><span className="text-xs text-slate-400">{dateTime(n.sentAt ?? n.createdAt)}</span></div>
                  <p className="mt-1 text-slate-600">{n.body}</p>
                  {n.status === "failed" && <p className="mt-1 text-xs text-red-600">Not delivered. Check the Africa's Talking settings (AfricasTalking__ApiKey) on the server.</p>}
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>

      {modal === "edit" && <EditLeaseModal d={data} onClose={() => setModal(null)} onSaved={refresh} />}
      {modal === "renew" && <RenewModal d={data} onClose={() => setModal(null)} onSaved={refresh} />}
      {modal === "end" && <EndModal d={data} onClose={() => setModal(null)} onSaved={refresh} />}
      {modal && typeof modal === "object" && "approve" in modal && <ApproveModal d={data} row={modal.approve} previous={prevRent(modal.approve)} onClose={() => setModal(null)} onSaved={refresh} />}
      {modal && typeof modal === "object" && "amend" in modal && <AmendModal d={data} row={modal.amend} onClose={() => setModal(null)} onSaved={refresh} />}
    </div>
  );
}

type ModalProps = { d: Detail; onClose: () => void; onSaved: () => void };

function EditLeaseModal({ d, onClose, onSaved }: ModalProps) {
  const [f, setF] = useState<LeaseInput>(inputFromDetail(d, false));
  const codes = useQuery({ queryKey: ["charge-codes"], queryFn: () => get<ChargeCode[]>("/api/setup/charge-codes") });
  const penalties = useQuery({ queryKey: ["penalty-rules"], queryFn: () => get<{ id: string; name: string }[]>("/api/setup/penalty-rules") });
  const save = useMutation({ mutationFn: () => put(`/api/leases/${d.lease.id}`, f), onSuccess: () => { onSaved(); onClose(); } });
  return (
    <Modal open wide title={`Edit ${d.lease.leaseNo}`} onClose={onClose}
      footer={<><Button variant="secondary" onClick={onClose}>Cancel</Button><Button busy={save.isPending} onClick={() => save.mutate()}>Save</Button></>}>
      <div className="space-y-4">
        <ErrorText error={save.error} />
        <p className="text-xs text-slate-500">Tenant, unit, dates, currency and rent are fixed on an active lease: rent changes through the schedule, the end date through Renew or End lease.</p>
        <div className="grid gap-3 sm:grid-cols-3">
          <Field label="Billing day"><Input type="number" min={1} max={28} value={f.billingDay} onChange={(e) => setF({ ...f, billingDay: Number(e.target.value) })} /></Field>
          <Field label="Billing frequency"><Select value={f.frequency} onChange={(e) => setF({ ...f, frequency: e.target.value })} options={["monthly", "quarterly", "semi_annual", "annual"]} /></Field>
          <Field label="Grace days"><Input type="number" min={0} max={60} value={f.graceDays} onChange={(e) => setF({ ...f, graceDays: Number(e.target.value) })} /></Field>
          <Field label="Rent deposit"><Input type="number" min={0} value={f.depositAmount} onChange={(e) => setF({ ...f, depositAmount: Number(e.target.value) })} /></Field>
          <Field label="Water deposit"><Input type="number" min={0} value={f.waterDepositAmount} onChange={(e) => setF({ ...f, waterDepositAmount: Number(e.target.value) })} /></Field>
          <Field label="Penalty rule"><Select value={f.penaltyRuleId ?? ""} onChange={(e) => setF({ ...f, penaltyRuleId: e.target.value || null })} placeholder="No penalty" options={(penalties.data ?? []).map((p) => ({ value: p.id, label: p.name }))} /></Field>
          <div className="space-y-2 sm:col-span-3">
            <Checkbox label="VAT applicable (16%)" checked={f.vatApplicable} onChange={(v) => setF({ ...f, vatApplicable: v })} />
            <Checkbox label="Tenant is a WHT agent (withholds 10% on rent)" checked={f.whtAgent} onChange={(v) => setF({ ...f, whtAgent: v })} />
          </div>
        </div>
        <ChargesEditor rentLocked charges={f.charges} codes={codes.data ?? []} currency={f.currencyCode} onChange={(charges) => setF({ ...f, charges })} />
      </div>
    </Modal>
  );
}

function RenewModal({ d, onClose, onSaved }: ModalProps) {
  const from = d.lease.endDate ?? d.lease.startDate;
  const [end, setEnd] = useState(addYears(nextDayIso(from), 1));
  const save = useMutation({ mutationFn: () => post<{ added: number }>(`/api/leases/${d.lease.id}/renew`, { newEndDate: end }), onSuccess: () => { onSaved(); onClose(); } });
  return (
    <Modal open title={`Renew ${d.lease.leaseNo}`} onClose={onClose}
      footer={<><Button variant="secondary" onClick={onClose}>Cancel</Button><Button busy={save.isPending} onClick={() => save.mutate()}>Renew lease</Button></>}>
      <div className="space-y-3">
        <ErrorText error={save.error} />
        <p className="text-sm text-slate-600">The lease currently ends {d.lease.endDate ? date(d.lease.endDate) : "open-ended"}. The rent schedule is kept{d.escalation && d.escalation.type !== "stepped" ? " and the escalation rule adds rows for the extra term" : ""}.</p>
        <Field label="New end date"><Input type="date" value={end} onChange={(e) => setEnd(e.target.value)} /></Field>
        <div className="flex gap-1">{[1, 2, 3, 5].map((y) => <Button key={y} variant="secondary" className="px-2" onClick={() => setEnd(addYears(nextDayIso(from), y))}>+{y} yr</Button>)}</div>
      </div>
    </Modal>
  );
}

function EndModal({ d, onClose, onSaved }: ModalProps) {
  const [end, setEnd] = useState(new Date().toLocaleDateString("en-CA", { timeZone: "Africa/Nairobi" }));
  const [reason, setReason] = useState("");
  const early = !!d.lease.endDate && end < d.lease.endDate;
  const save = useMutation({ mutationFn: () => post(`/api/leases/${d.lease.id}/terminate`, { endDate: end, reason }), onSuccess: () => { onSaved(); onClose(); } });
  return (
    <Modal open title={`End ${d.lease.leaseNo}`} onClose={onClose}
      footer={<><Button variant="secondary" onClick={onClose}>Cancel</Button><Button busy={save.isPending} className="bg-red-600 hover:bg-red-700" onClick={() => save.mutate()}>{early ? "Terminate lease" : "End lease"}</Button></>}>
      <div className="space-y-3">
        <ErrorText error={save.error} />
        <Field label="Last day of the lease"><Input type="date" value={end} onChange={(e) => setEnd(e.target.value)} /></Field>
        <Field label="Reason"><Input value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} placeholder="e.g. Tenant relocating" /></Field>
        <p className="text-xs text-slate-500">
          {early ? "This is before the agreed end date, so the lease is marked terminated." : "The lease is marked ended."} {d.unit.code} becomes vacant and rent schedule rows after this date are dropped.
          Deposits stay held until the move-out (stage 5) refunds or applies them.
        </p>
      </div>
    </Modal>
  );
}

function ApproveModal({ d, row, previous, onClose, onSaved }: ModalProps & { row: ScheduleRow; previous?: number }) {
  const [rent, setRent] = useState(row.monthlyRent ?? 0);
  const cur = d.lease.currencyCode;
  const save = useMutation({ mutationFn: () => post(`/api/leases/${d.lease.id}/schedule/${row.id}/approve`, { monthlyRent: row.monthlyRent == null ? rent : null }), onSuccess: () => { onSaved(); onClose(); } });
  const sms = `Dear ${d.tenant.fullName}, as per your lease ${d.lease.leaseNo}, rent for ${d.unit.code} changes from ${money(previous, cur)} to ${money(row.monthlyRent ?? rent, cur)} from ${date(row.effectiveFrom)}.`;
  return (
    <Modal open title="Approve escalation and send notice" onClose={onClose}
      footer={<><Button variant="secondary" onClick={onClose}>Cancel</Button><Button busy={save.isPending} onClick={() => save.mutate()}><Send className="h-4 w-4" />Approve &amp; send</Button></>}>
      <div className="space-y-3 text-sm">
        <ErrorText error={save.error} />
        {row.monthlyRent == null && <Field label={`Agreed rent after the market review (${cur})`}><Input type="number" min={0} value={rent || ""} onChange={(e) => setRent(Number(e.target.value))} /></Field>}
        <p className="text-slate-600">From {date(row.effectiveFrom)} the rent becomes {money(row.monthlyRent ?? rent, cur)} (was {money(previous, cur)}). The RENT charge changes on that date and billing uses the new amount.</p>
        <div>
          <div className="mb-1 text-xs font-medium text-slate-500">SMS to {d.tenant.phone}</div>
          <div className="rounded-lg bg-page p-3 text-slate-700">{sms}</div>
        </div>
      </div>
    </Modal>
  );
}

function AmendModal({ d, row, onClose, onSaved }: ModalProps & { row: ScheduleRow }) {
  const [rent, setRent] = useState(row.monthlyRent ?? 0);
  const save = useMutation({ mutationFn: () => put(`/api/leases/${d.lease.id}/schedule/${row.id}`, { monthlyRent: rent }), onSuccess: () => { onSaved(); onClose(); } });
  return (
    <Modal open title={`Change rent from ${date(row.effectiveFrom)}`} onClose={onClose}
      footer={<><Button variant="secondary" onClick={onClose}>Cancel</Button><Button busy={save.isPending} onClick={() => save.mutate()}>Save</Button></>}>
      <div className="space-y-3">
        <ErrorText error={save.error} />
        <Field label={`Monthly rent (${d.lease.currencyCode})`}><Input type="number" min={0} step="0.01" value={rent || ""} onChange={(e) => setRent(Number(e.target.value))} /></Field>
        <p className="text-xs text-slate-500">Later rows keep their amounts; change them one by one if needed.</p>
      </div>
    </Modal>
  );
}

function nextDayIso(iso: string) {
  const d = new Date(iso + "T00:00:00Z");
  d.setUTCDate(d.getUTCDate() + 1);
  return d.toISOString().slice(0, 10);
}
