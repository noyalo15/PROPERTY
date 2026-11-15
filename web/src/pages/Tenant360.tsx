import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertOctagon, Download, Pencil, Plus, RefreshCw, Trash2, Upload } from "lucide-react";
import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import { del, get, post, upload } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { date, dateTime, label, money } from "@/lib/format";
import { Button, Card, Empty, ErrorText, Field, Input, Modal, PageHeader, Pill, Select, Spinner, StatusPill, Table, Tabs, Num, NumHead } from "@/components/ui";
import { BcTick, TenantForm, type TenantRecord } from "./Tenants";

interface Lease {
  id: string; leaseNo: string; status: string; startDate: string; endDate?: string; currencyCode: string; billingDay: number; frequency: string;
  depositAmount: number; unitId: string; unit: string; property: string; propertyCode: string; monthlyRent: number;
  charges: { chargeCode: string; description: string; amount: number; currencyCode: string; frequency: string }[];
}
interface Detail {
  tenant: TenantRecord & { id: string; tenantNo: string; createdAt: string };
  documents: { id: string; docType: string; fileName: string; expiryDate?: string; createdAt: string; expired: boolean }[];
  leases: Lease[];
  bcSync?: { status: string; attempts: number; lastError?: string; processedAt?: string };
}

type TabId = "leases" | "bills" | "payments" | "statement" | "deposits" | "maintenance" | "notices" | "documents" | "audit";

export function Tenant360Page() {
  const { id } = useParams();
  const { can } = useAuth();
  const qc = useQueryClient();
  const [tab, setTab] = useState<TabId>("leases");
  const [editing, setEditing] = useState(false);
  const { data, isLoading, error } = useQuery({ queryKey: ["tenant", id], queryFn: () => get<Detail>(`/api/tenants/${id}`), refetchInterval: (q) => (q.state.data?.bcSync?.status === "pending" ? 15_000 : false) });
  const resync = useMutation({ mutationFn: () => post(`/api/tenants/${id}/sync`), onSuccess: () => qc.invalidateQueries({ queryKey: ["tenant", id] }) });
  if (isLoading) return <Spinner />;
  if (error || !data) return <ErrorText error={error} />;
  const t = data.tenant;
  const active = data.leases.find((l) => l.status === "active");

  return (
    <div className="space-y-4">
      {t.blacklisted && (
        <div className="flex items-start gap-3 rounded-card border border-red-200 bg-red-600 px-4 py-3 text-white">
          <AlertOctagon className="mt-0.5 h-5 w-5 shrink-0" />
          <div><div className="font-semibold">Blacklisted tenant</div><div className="text-sm text-red-100">{t.blacklistReason}</div></div>
        </div>
      )}
      <PageHeader title={t.fullName} actions={can("property_manager") && <>
        <Button variant="secondary" busy={resync.isPending} onClick={() => resync.mutate()}><RefreshCw className="h-4 w-4" />Send to BC again</Button>
        <Button onClick={() => setEditing(true)}><Pencil className="h-4 w-4" />Edit</Button>
      </>}>
        <Link to="/tenants" className="hover:text-teal">Tenants</Link> / <span className="font-mono">{t.tenantNo}</span> · {label(t.type)} · {active ? `${active.unit}, ${active.property}` : "No active lease"}
      </PageHeader>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card title="Contact and KYC">
          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1.5 text-sm">
            <dt className="text-slate-500">Phone</dt><dd>{t.phone}{t.altPhone ? ` / ${t.altPhone}` : ""}</dd>
            <dt className="text-slate-500">Email</dt><dd className="break-all">{t.email ?? "—"}</dd>
            {t.type === "company" ? <>
              <dt className="text-slate-500">Reg. no.</dt><dd>{t.companyRegNo ?? "—"}</dd>
              <dt className="text-slate-500">Contact</dt><dd>{t.contactPerson ?? "—"}</dd>
            </> : <>
              <dt className="text-slate-500">ID / passport</dt><dd>{t.idOrPassportNo ?? "—"}</dd>
              <dt className="text-slate-500">Next of kin</dt><dd>{t.nextOfKinName ? `${t.nextOfKinName}, ${t.nextOfKinPhone ?? ""}` : "—"}</dd>
            </>}
            <dt className="text-slate-500">KRA PIN</dt><dd>{t.kraPin ?? "—"}</dd>
          </dl>
        </Card>
        <Card title="Business Central">
          <div className="space-y-2 text-sm">
            <BcTick syncedAt={t.bcSyncedAt} status={data.bcSync?.status} />
            <div className="text-slate-600">Customer no. <span className="font-mono">{t.tenantNo}</span></div>
            {active && <div className="text-slate-600">Dimensions PROPERTY <span className="font-mono">{active.propertyCode}</span>, UNIT <span className="font-mono">{active.unit}</span></div>}
            {data.bcSync?.lastError && <div className="rounded bg-red-50 px-2 py-1 text-xs text-red-700">{data.bcSync.lastError} (attempt {data.bcSync.attempts})</div>}
          </div>
        </Card>
        <Card title="Current lease">
          {active ? (
            <div className="space-y-1.5 text-sm">
              <div className="flex justify-between"><span className="text-slate-500">Lease</span><Link to={`/leases/${active.id}`} className="font-mono text-teal hover:underline">{active.leaseNo}</Link></div>
              <div className="flex justify-between"><span className="text-slate-500">Term</span><span>{date(active.startDate)} to {date(active.endDate)}</span></div>
              <div className="flex justify-between"><span className="text-slate-500">Rent</span><span className="font-medium">{money(active.monthlyRent, active.currencyCode)}</span></div>
              <div className="flex justify-between"><span className="text-slate-500">Deposit</span><span>{money(active.depositAmount, active.currencyCode)}</span></div>
            </div>
          ) : can("property_manager") ? (
            <div className="space-y-2 text-sm text-slate-500"><p>No active lease.</p><Link to={`/leases/new?tenantId=${t.id}`}><Button><Plus className="h-4 w-4" />New lease</Button></Link></div>
          ) : <p className="text-sm text-slate-400">No active lease.</p>}
        </Card>
      </div>

      <Card padded={false}>
        <div className="px-2 pt-1">
          <Tabs value={tab} onChange={setTab} tabs={[
            { id: "leases", label: "Leases", count: data.leases.length }, { id: "bills", label: "Bills" }, { id: "payments", label: "Payments" },
            { id: "statement", label: "Statement" }, { id: "deposits", label: "Deposits" }, { id: "maintenance", label: "Maintenance" },
            { id: "notices", label: "Notices" }, { id: "documents", label: "Documents", count: data.documents.length }, { id: "audit", label: "Audit" },
          ]} />
        </div>
        <div className="p-2">
          {tab === "leases" && <LeasesTab leases={data.leases} />}
          {tab === "documents" && <DocumentsTab tenantId={t.id} docs={data.documents} />}
          {tab === "statement" && <Empty>The tenant statement (with the BC GetCustomerBalance check) arrives with billing and payments in stages 3 and 4.</Empty>}
          {(["bills", "payments", "deposits", "maintenance", "notices", "audit"] as TabId[]).includes(tab) && <ListTab tenantId={t.id} tab={tab} />}
        </div>
      </Card>
      {editing && <TenantForm tenant={t} onClose={() => setEditing(false)} />}
    </div>
  );
}

function LeasesTab({ leases }: { leases: Lease[] }) {
  if (leases.length === 0) return <Empty>No leases yet.</Empty>;
  return (
    <Table head={["Lease", "Unit", "Status", "Start", "End", "Billing", <NumHead key="r">Rent</NumHead>, <NumHead key="d">Deposit</NumHead>]}>
      {leases.map((l) => (
        <tr key={l.id}>
          <td><Link to={`/leases/${l.id}`} className="font-mono text-xs text-teal hover:underline">{l.leaseNo}</Link></td>
          <td>{l.unit}<span className="block text-xs text-slate-400">{l.property}</span></td>
          <td><StatusPill status={l.status} /></td>
          <td>{date(l.startDate)}</td><td>{date(l.endDate)}</td>
          <td className="text-xs text-slate-600">{label(l.frequency)}, day {l.billingDay}</td>
          <Num>{money(l.monthlyRent, l.currencyCode)}</Num>
          <Num>{money(l.depositAmount, l.currencyCode)}</Num>
        </tr>
      ))}
    </Table>
  );
}

type AnyRow = Record<string, unknown> & { id: string | number };
const columns: Record<string, { head: string; render: (r: AnyRow) => React.ReactNode }[]> = {
  bills: [
    { head: "Bill", render: (r) => <span className="font-mono text-xs">{String(r.billNo)}</span> },
    { head: "Type", render: (r) => label(r.billType as string) }, { head: "Date", render: (r) => date(r.billDate as string) },
    { head: "Due", render: (r) => date(r.dueDate as string) }, { head: "Total", render: (r) => money(r.totalAmount as number, r.currencyCode as string) },
    { head: "Balance", render: (r) => money(r.balance as number, r.currencyCode as string) }, { head: "Status", render: (r) => <StatusPill status={r.status as string} /> },
  ],
  payments: [
    { head: "Receipt", render: (r) => <span className="font-mono text-xs">{String(r.receiptNo)}</span> }, { head: "Channel", render: (r) => label(r.channel as string) },
    { head: "Reference", render: (r) => String(r.reference ?? "—") }, { head: "Date", render: (r) => date(r.paymentDate as string) },
    { head: "Amount", render: (r) => money(r.amount as number, r.currencyCode as string) }, { head: "Status", render: (r) => <StatusPill status={r.status as string} /> },
  ],
  deposits: [
    { head: "Type", render: (r) => label(r.depositType as string) }, { head: "Held", render: (r) => money(r.amountHeld as number, r.currencyCode as string) },
    { head: "Status", render: (r) => <StatusPill status={r.status as string} /> },
  ],
  maintenance: [
    { head: "Request", render: (r) => String(r.requestNo) }, { head: "Category", render: (r) => label(r.category as string) },
    { head: "Priority", render: (r) => label(r.priority as string) }, { head: "Status", render: (r) => <StatusPill status={r.status as string} /> },
    { head: "Logged", render: (r) => dateTime(r.createdAt as string) },
  ],
  notices: [
    { head: "Channel", render: (r) => label(r.channel as string) }, { head: "Message", render: (r) => <span className="line-clamp-2">{String(r.body)}</span> },
    { head: "Status", render: (r) => <StatusPill status={r.status as string} /> }, { head: "Sent", render: (r) => dateTime(r.sentAt as string) },
  ],
  audit: [
    { head: "When", render: (r) => dateTime(r.createdAt as string) }, { head: "Who", render: (r) => String(r.user ?? "system") },
    { head: "Action", render: (r) => <Pill tone={r.action === "insert" ? "green" : r.action === "delete" ? "red" : "blue"}>{String(r.action)}</Pill> },
    { head: "Changes", render: (r) => <AuditDiff oldValues={r.oldValues as string} newValues={r.newValues as string} /> },
  ],
};
const emptyText: Record<string, string> = {
  bills: "No bills yet. Billing runs arrive in stage 3.", payments: "No payments yet. Receipts and M-PESA arrive in stage 4.",
  deposits: "No deposits recorded.", maintenance: "No repair requests. Maintenance arrives in stage 5.", notices: "No notices sent.", audit: "No changes recorded.",
};

function AuditDiff({ oldValues, newValues }: { oldValues?: string; newValues?: string }) {
  const o = oldValues ? (JSON.parse(oldValues) as Record<string, unknown>) : {};
  const n = newValues ? (JSON.parse(newValues) as Record<string, unknown>) : {};
  const keys = Object.keys(n).filter((k) => !["id", "created_at", "created_by", "updated_at", "updated_by"].includes(k));
  if (!oldValues) return <span className="text-xs text-slate-500">Created</span>;
  return (
    <ul className="text-xs text-slate-600">
      {keys.map((k) => <li key={k}><span className="font-medium">{label(k)}</span>: {String(o[k] ?? "—")} → {String(n[k] ?? "—")}</li>)}
    </ul>
  );
}

function ListTab({ tenantId, tab }: { tenantId: string; tab: string }) {
  const { data, isLoading, error } = useQuery({ queryKey: ["tenant-tab", tenantId, tab], queryFn: () => get<AnyRow[]>(`/api/tenants/${tenantId}/${tab}`) });
  if (isLoading) return <Spinner />;
  if (error) return <ErrorText error={error} />;
  if (!data?.length) return <Empty>{emptyText[tab]}</Empty>;
  const cols = columns[tab];
  return (
    <Table head={cols.map((c) => c.head)}>
      {data.map((r) => <tr key={String(r.id)}>{cols.map((c) => <td key={c.head}>{c.render(r)}</td>)}</tr>)}
    </Table>
  );
}

function DocumentsTab({ tenantId, docs }: { tenantId: string; docs: Detail["documents"] }) {
  const { can } = useAuth();
  const qc = useQueryClient();
  const [open, setOpen] = useState(false);
  const [docType, setDocType] = useState("id");
  const [expiry, setExpiry] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const send = useMutation({
    mutationFn: () => {
      const fd = new FormData(); fd.append("docType", docType); if (expiry) fd.append("expiryDate", expiry); fd.append("file", file!);
      return upload(`/api/tenants/${tenantId}/documents`, fd);
    },
    onSuccess: () => { setOpen(false); setFile(null); setExpiry(""); qc.invalidateQueries({ queryKey: ["tenant", tenantId] }); },
  });
  const remove = useMutation({ mutationFn: (d: string) => del(`/api/tenants/${tenantId}/documents/${d}`), onSuccess: () => qc.invalidateQueries({ queryKey: ["tenant", tenantId] }) });
  return (
    <div>
      {can("property_manager") && <div className="flex justify-end p-2"><Button variant="secondary" onClick={() => setOpen(true)}><Upload className="h-4 w-4" />Upload document</Button></div>}
      {docs.length === 0 ? <Empty>No documents uploaded.</Empty> : (
        <Table head={["Type", "File", "Expiry", "Uploaded", ""]}>
          {docs.map((d) => (
            <tr key={d.id}>
              <td>{label(d.docType)}</td>
              <td><a className="inline-flex items-center gap-1 text-teal hover:underline" href={`/api/tenants/${tenantId}/documents/${d.id}`}><Download className="h-3.5 w-3.5" />{d.fileName}</a></td>
              <td>{d.expiryDate ? <span className={d.expired ? "font-medium text-red-600" : ""}>{date(d.expiryDate)}{d.expired ? " (expired)" : ""}</span> : "—"}</td>
              <td>{dateTime(d.createdAt)}</td>
              <td className="text-right">{can("property_manager") && <button aria-label="Delete document" className="text-slate-300 hover:text-red-500" onClick={() => remove.mutate(d.id)}><Trash2 className="h-4 w-4" /></button>}</td>
            </tr>
          ))}
        </Table>
      )}
      <Modal open={open} title="Upload document" onClose={() => setOpen(false)}
        footer={<><Button variant="secondary" onClick={() => setOpen(false)}>Cancel</Button><Button disabled={!file} busy={send.isPending} onClick={() => send.mutate()}>Upload</Button></>}>
        <div className="space-y-3">
          <ErrorText error={send.error} />
          <Field label="Document type"><Select value={docType} onChange={(e) => setDocType(e.target.value)} options={["id", "kra_pin", "lease", "guarantor", "other"]} /></Field>
          <Field label="Expiry date (optional)"><Input type="date" value={expiry} onChange={(e) => setExpiry(e.target.value)} /></Field>
          <Field label="File" hint="PDF, image, Word or Excel, up to 10 MB"><Input type="file" accept=".pdf,.jpg,.jpeg,.png,.webp,.docx,.xlsx" onChange={(e) => setFile(e.target.files?.[0] ?? null)} /></Field>
        </div>
      </Modal>
    </div>
  );
}
