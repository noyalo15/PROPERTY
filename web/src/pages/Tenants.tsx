import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, Clock, Plus, XCircle } from "lucide-react";
import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { get, post, put } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { label } from "@/lib/format";
import { Button, Card, Checkbox, ErrorText, Field, Input, Modal, PageHeader, Pill, Select, Spinner, Table } from "@/components/ui";

export interface TenantRecord {
  id?: string; tenantNo?: string; type: string; fullName: string; idOrPassportNo?: string | null; kraPin?: string | null; phone: string; altPhone?: string | null;
  email?: string | null; nextOfKinName?: string | null; nextOfKinPhone?: string | null; companyRegNo?: string | null; contactPerson?: string | null;
  blacklisted: boolean; blacklistReason?: string | null; bcSyncedAt?: string | null;
}
interface Row { id: string; tenantNo: string; type: string; fullName: string; phone: string; email?: string; blacklisted: boolean; bcSyncedAt?: string; unit?: string; property?: string; bcStatus?: string }

export function BcTick({ syncedAt, status }: { syncedAt?: string | null; status?: string | null }) {
  if (syncedAt && status !== "failed" && status !== "pending")
    return <span className="inline-flex items-center gap-1 text-xs text-emerald-700" title={`Synced to BC ${new Date(syncedAt).toLocaleString()}`}><CheckCircle2 className="h-4 w-4" />Synced to BC</span>;
  if (status === "failed") return <span className="inline-flex items-center gap-1 text-xs text-red-600"><XCircle className="h-4 w-4" />BC failed</span>;
  return <span className="inline-flex items-center gap-1 text-xs text-amber-600"><Clock className="h-4 w-4" />Waiting for BC</span>;
}

export function TenantsPage() {
  const [q, setQ] = useState("");
  const [type, setType] = useState("");
  const [bc, setBc] = useState("");
  const [black, setBlack] = useState("");
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
  const { can } = useAuth();
  const params = new URLSearchParams({ q, page: String(page), pageSize: "50" });
  if (type) params.set("type", type);
  if (bc) params.set("bc", bc);
  if (black) params.set("blacklisted", black);
  const { data, isLoading, error } = useQuery({ queryKey: ["tenants", params.toString()], queryFn: () => get<{ total: number; rows: Row[] }>(`/api/tenants?${params}`) });
  const pages = data ? Math.max(1, Math.ceil(data.total / 50)) : 1;

  return (
    <div>
      <PageHeader title="Tenants" actions={can("property_manager") && <Button onClick={() => setCreating(true)}><Plus className="h-4 w-4" />New tenant</Button>}>
        {data ? `${data.total} tenants` : ""} · The tenant number is also the BC customer number.
      </PageHeader>
      <Card padded={false} title={
        <div className="flex flex-wrap gap-2">
          <Input placeholder="Name, tenant no., phone, ID or unit" value={q} onChange={(e) => { setQ(e.target.value); setPage(1); }} className="w-64" />
          <Select value={type} onChange={(e) => { setType(e.target.value); setPage(1); }} placeholder="All types" options={["individual", "company"]} className="w-36" />
          <Select value={bc} onChange={(e) => { setBc(e.target.value); setPage(1); }} placeholder="BC: any" options={[{ value: "synced", label: "Synced to BC" }, { value: "not_synced", label: "Not synced" }]} className="w-36" />
          <Select value={black} onChange={(e) => { setBlack(e.target.value); setPage(1); }} placeholder="Blacklist: any" options={[{ value: "true", label: "Blacklisted" }, { value: "false", label: "Not blacklisted" }]} className="w-40" />
        </div>
      }>
        {isLoading ? <Spinner /> : error ? <div className="p-4"><ErrorText error={error} /></div> : (
          <>
            <Table head={["Tenant no.", "Name", "Type", "Unit", "Phone", "BC"]}>
              {data!.rows.map((t) => (
                <tr key={t.id}>
                  <td className="font-mono text-xs">{t.tenantNo}</td>
                  <td><Link to={`/tenants/${t.id}`} className="font-medium text-slate-800 hover:text-teal">{t.fullName}</Link>{t.blacklisted && <span className="ml-2"><Pill tone="red">Blacklisted</Pill></span>}</td>
                  <td>{label(t.type)}</td>
                  <td>{t.unit ? <span>{t.unit}<span className="block text-xs text-slate-400">{t.property}</span></span> : <span className="text-slate-400">No active lease</span>}</td>
                  <td className="whitespace-nowrap">{t.phone}</td>
                  <td><BcTick syncedAt={t.bcSyncedAt} status={t.bcStatus} /></td>
                </tr>
              ))}
            </Table>
            {data!.rows.length === 0 && <p className="py-8 text-center text-sm text-slate-400">No tenants match.</p>}
            <div className="flex items-center justify-end gap-2 border-t border-line px-4 py-2 text-sm text-slate-500">
              Page {page} of {pages}
              <Button variant="secondary" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</Button>
              <Button variant="secondary" disabled={page >= pages} onClick={() => setPage(page + 1)}>Next</Button>
            </div>
          </>
        )}
      </Card>
      {creating && <TenantForm onClose={() => setCreating(false)} />}
    </div>
  );
}

const blank: TenantRecord = { type: "individual", fullName: "", phone: "", blacklisted: false };

export function TenantForm({ tenant, onClose }: { tenant?: TenantRecord; onClose: () => void }) {
  const qc = useQueryClient();
  const nav = useNavigate();
  const [f, setF] = useState<TenantRecord>(tenant ?? blank);
  const save = useMutation({
    mutationFn: () => (tenant?.id ? put<{ id: string }>(`/api/tenants/${tenant.id}`, f) : post<{ id: string }>("/api/tenants", f)),
    onSuccess: (r) => {
      qc.invalidateQueries({ queryKey: ["tenants"] });
      qc.invalidateQueries({ queryKey: ["tenant", r.id] });
      onClose();
      if (!tenant?.id) nav(`/tenants/${r.id}`);
    },
  });
  const set = (k: keyof TenantRecord) => (e: { target: { value: string } }) => setF({ ...f, [k]: e.target.value });
  const company = f.type === "company";
  return (
    <Modal open wide title={tenant?.id ? `Edit ${tenant.tenantNo}` : "New tenant"} onClose={onClose}
      footer={<><Button variant="secondary" onClick={onClose}>Cancel</Button><Button busy={save.isPending} onClick={() => save.mutate()}>Save{tenant?.id ? "" : " and send to BC"}</Button></>}>
      <div className="grid gap-3 sm:grid-cols-2">
        <div className="sm:col-span-2"><ErrorText error={save.error} /></div>
        <Field label="Type"><Select value={f.type} onChange={set("type")} options={["individual", "company"]} /></Field>
        <Field label={company ? "Company name" : "Full name"}><Input value={f.fullName} maxLength={100} onChange={set("fullName")} /></Field>
        {company ? (
          <>
            <Field label="Company registration no."><Input value={f.companyRegNo ?? ""} onChange={set("companyRegNo")} /></Field>
            <Field label="Contact person"><Input value={f.contactPerson ?? ""} onChange={set("contactPerson")} /></Field>
          </>
        ) : (
          <Field label="ID or passport no."><Input value={f.idOrPassportNo ?? ""} onChange={set("idOrPassportNo")} /></Field>
        )}
        <Field label="KRA PIN" hint="e.g. A123456789B"><Input value={f.kraPin ?? ""} maxLength={11} onChange={set("kraPin")} /></Field>
        <Field label="Phone" hint="Used for SMS and M-PESA matching"><Input type="tel" value={f.phone} onChange={set("phone")} /></Field>
        <Field label="Alternative phone"><Input type="tel" value={f.altPhone ?? ""} onChange={set("altPhone")} /></Field>
        <Field label="Email"><Input type="email" value={f.email ?? ""} onChange={set("email")} /></Field>
        {!company && (
          <>
            <Field label="Next of kin"><Input value={f.nextOfKinName ?? ""} onChange={set("nextOfKinName")} /></Field>
            <Field label="Next of kin phone"><Input type="tel" value={f.nextOfKinPhone ?? ""} onChange={set("nextOfKinPhone")} /></Field>
          </>
        )}
        <div className="sm:col-span-2 rounded-lg border border-line p-3">
          <Checkbox label="Blacklisted" checked={f.blacklisted} onChange={(v) => setF({ ...f, blacklisted: v })} />
          {f.blacklisted && <Field label="Reason" className="mt-2"><Input value={f.blacklistReason ?? ""} onChange={set("blacklistReason")} /></Field>}
        </div>
      </div>
    </Modal>
  );
}
