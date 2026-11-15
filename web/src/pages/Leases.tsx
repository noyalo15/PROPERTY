import { useQuery } from "@tanstack/react-query";
import { BellRing, Plus } from "lucide-react";
import { useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { get } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { date, label, money } from "@/lib/format";
import { Button, Card, ErrorText, Input, Num, NumHead, PageHeader, Pill, Select, Spinner, StatusPill, Table } from "@/components/ui";

interface Row {
  id: string; leaseNo: string; status: string; startDate: string; endDate?: string; currencyCode: string; frequency: string;
  tenantId: string; tenant: string; tenantNo: string; unitId: string; unit: string; property: string; monthlyRent: number;
  nextChange?: { effectiveFrom: string; monthlyRent?: number; status: string }; noticeDue: boolean;
}
interface Summary { active: number; drafts: number; expiring30: number; expiring60: number; expiring90: number; escalationsDue: number }

export const scheduleTone = (s: string) => (s === "pending_approval" ? "amber" : s === "approved" ? "blue" : s === "current" ? "green" : "grey") as "amber" | "blue" | "green" | "grey";
export const scheduleLabel = (s: string) => (s === "pending_approval" ? "Notice due" : label(s));

export function daysUntil(d?: string) {
  if (!d) return null;
  const today = new Date(new Date().toLocaleDateString("en-CA", { timeZone: "Africa/Nairobi" }));
  return Math.round((new Date(d).getTime() - today.getTime()) / 86_400_000);
}

export function LeasesPage() {
  const [sp, setSp] = useSearchParams();
  const nav = useNavigate();
  const { can } = useAuth();
  const [q, setQ] = useState("");
  const [page, setPage] = useState(1);
  const status = sp.get("status") ?? "active";
  const expiring = sp.get("expiring") ?? "";
  const escalations = sp.get("escalations") === "due";
  const currency = sp.get("currency") ?? "";
  const propertyId = sp.get("propertyId") ?? "";

  const set = (k: string, v: string) => {
    const n = new URLSearchParams(sp);
    if (v) n.set(k, v); else n.delete(k);
    setSp(n, { replace: true });
    setPage(1);
  };

  const params = new URLSearchParams({ q, page: String(page), pageSize: "50" });
  if (status) params.set("status", status);
  if (expiring) params.set("expiring", expiring);
  if (escalations) params.set("escalationsDue", "true");
  if (currency) params.set("currency", currency);
  if (propertyId) params.set("propertyId", propertyId);

  const summary = useQuery({ queryKey: ["leases-summary"], queryFn: () => get<Summary>("/api/leases/summary") });
  const props = useQuery({ queryKey: ["properties"], queryFn: () => get<{ id: string; name: string }[]>("/api/properties") });
  const { data, isLoading, error } = useQuery({ queryKey: ["leases", params.toString()], queryFn: () => get<{ total: number; rows: Row[] }>(`/api/leases?${params}`) });
  const pages = data ? Math.max(1, Math.ceil(data.total / 50)) : 1;
  const s = summary.data;

  const chip = (active: boolean, text: string, count: number | undefined, onClick: () => void, tone?: "amber") => (
    <button onClick={onClick} className={`rounded-full border px-3 py-1 text-xs font-medium transition ${active ? "border-teal bg-teal text-white" : tone === "amber" && count ? "border-amber-300 bg-amber-50 text-amber-800 hover:border-amber-400" : "border-line bg-white text-slate-600 hover:border-teal/50"}`}>
      {text}{count !== undefined && <span className={`ml-1.5 tabular-nums ${active ? "text-white/80" : "text-slate-400"}`}>{count}</span>}
    </button>
  );

  return (
    <div>
      <PageHeader title="Leases & Rent Schedules" actions={can("property_manager") && <Button onClick={() => nav("/leases/new")}><Plus className="h-4 w-4" />New lease</Button>}>
        {s ? `${s.active} active leases${s.drafts ? `, ${s.drafts} drafts` : ""}` : ""}
      </PageHeader>

      <div className="mb-3 flex flex-wrap gap-2">
        {chip(!expiring && !escalations, "All", undefined, () => { set("expiring", ""); set("escalations", ""); })}
        {chip(escalations, "Escalations due", s?.escalationsDue, () => { const n = new URLSearchParams(sp); n.delete("expiring"); escalations ? n.delete("escalations") : n.set("escalations", "due"); n.set("status", "active"); setSp(n, { replace: true }); setPage(1); }, "amber")}
        {[30, 60, 90].map((d) => chip(expiring === String(d), `Expiring in ${d} days`, s?.[`expiring${d}` as keyof Summary] as number, () => { const n = new URLSearchParams(sp); n.delete("escalations"); expiring === String(d) ? n.delete("expiring") : n.set("expiring", String(d)); n.set("status", "active"); setSp(n, { replace: true }); setPage(1); }))}
      </div>

      <Card padded={false} title={
        <div className="flex flex-wrap gap-2">
          <Input placeholder="Lease no., tenant or unit" value={q} onChange={(e) => { setQ(e.target.value); setPage(1); }} className="w-56" />
          <Select value={status} onChange={(e) => set("status", e.target.value)} options={[{ value: "", label: "Any status" }, "active", "draft", "ended", "terminated", "transferred"].map((o) => (typeof o === "string" ? { value: o, label: label(o) } : o))} className="w-36" />
          <Select value={propertyId} onChange={(e) => set("propertyId", e.target.value)} placeholder="All properties" options={(props.data ?? []).map((p) => ({ value: p.id, label: p.name }))} className="w-56" />
          <Select value={currency} onChange={(e) => set("currency", e.target.value)} placeholder="KES and USD" options={["KES", "USD"]} className="w-40" />
        </div>
      }>
        {isLoading ? <Spinner /> : error ? <div className="p-4"><ErrorText error={error} /></div> : (
          <>
            <Table head={["Lease", "Tenant", "Unit", "Status", "Term", <NumHead key="r">Monthly rent</NumHead>, "Next change"]}>
              {data!.rows.map((l) => {
                const left = daysUntil(l.endDate);
                return (
                  <tr key={l.id} className="cursor-pointer" onClick={() => nav(`/leases/${l.id}`)}>
                    <td><Link to={`/leases/${l.id}`} className="font-mono text-xs font-medium text-slate-800 hover:text-teal" onClick={(e) => e.stopPropagation()}>{l.leaseNo}</Link></td>
                    <td><span className="font-medium text-slate-800">{l.tenant}</span><span className="block text-xs text-slate-400">{l.tenantNo}</span></td>
                    <td>{l.unit}<span className="block text-xs text-slate-400">{l.property}</span></td>
                    <td><StatusPill status={l.status} /></td>
                    <td className="whitespace-nowrap text-xs">
                      {date(l.startDate)} to {date(l.endDate)}
                      {l.status === "active" && left !== null && left <= 90 && <span className={`block ${left <= 30 ? "text-red-600" : "text-amber-600"}`}>{left < 0 ? `Ended ${-left} days ago` : `${left} days left`}</span>}
                    </td>
                    <Num>{money(l.monthlyRent, l.currencyCode)}</Num>
                    <td className="whitespace-nowrap text-xs">
                      {l.nextChange ? (
                        <span className="flex items-center gap-1.5">
                          {l.noticeDue && <BellRing className="h-3.5 w-3.5 text-amber-500" />}
                          {date(l.nextChange.effectiveFrom)}: {l.nextChange.monthlyRent != null ? money(l.nextChange.monthlyRent, l.currencyCode) : "market review"}
                          <Pill tone={scheduleTone(l.nextChange.status)}>{scheduleLabel(l.nextChange.status)}</Pill>
                        </span>
                      ) : <span className="text-slate-400">None</span>}
                    </td>
                  </tr>
                );
              })}
            </Table>
            {data!.rows.length === 0 && <p className="py-8 text-center text-sm text-slate-400">No leases match.</p>}
            <div className="flex items-center justify-end gap-2 border-t border-line px-4 py-2 text-sm text-slate-500">
              {data!.total} leases · Page {page} of {pages}
              <Button variant="secondary" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</Button>
              <Button variant="secondary" disabled={page >= pages} onClick={() => setPage(page + 1)}>Next</Button>
            </div>
          </>
        )}
      </Card>
    </div>
  );
}
