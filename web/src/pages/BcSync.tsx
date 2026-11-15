import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Play, RefreshCw, Wifi } from "lucide-react";
import { useState } from "react";
import { get, post } from "@/lib/api";
import { dateTime, label } from "@/lib/format";
import { Button, Card, ErrorText, Kpi, PageHeader, Select, Spinner, StatusPill, Table } from "@/components/ui";

interface QueueRow { id: string; entityType: string; functionName: string; externalRef?: string; status: string; attempts: number; lastError?: string; bcDocumentNo?: string; nextAttemptAt?: string; processedAt?: string; updatedAt: string }
interface Queue { kpi: { postedToday: number; firstTimeToday: number; retrying: number; failed: number; pending: number }; rows: QueueRow[] }

export function BcSyncPage() {
  const qc = useQueryClient();
  const [status, setStatus] = useState("");
  const [fn, setFn] = useState("");
  const { data, isLoading, error } = useQuery({
    queryKey: ["bc-queue", status, fn],
    queryFn: () => get<Queue>(`/api/bc/queue?status=${status}&function=${fn}`),
    refetchInterval: 15_000,
  });
  const refresh = () => { qc.invalidateQueries({ queryKey: ["bc-queue"] }); qc.invalidateQueries({ queryKey: ["bc-status"] }); };
  const retry = useMutation({ mutationFn: (id?: string) => post(`/api/bc/queue/retry${id ? `?id=${id}` : ""}`), onSuccess: refresh });
  const run = useMutation({ mutationFn: () => post("/api/bc/queue/run"), onSuccess: () => setTimeout(refresh, 3000) });
  const ping = useMutation({ mutationFn: () => post<{ ok: boolean; message?: string }>("/api/bc/ping"), onSuccess: refresh });

  const k = data?.kpi;
  const firstTime = k && k.postedToday ? Math.round((100 * k.firstTimeToday) / k.postedToday) : null;

  const exportCsv = () => {
    const rows = data?.rows ?? [];
    const head = ["time", "function", "navestate_ref", "bc_document", "status", "attempts", "error"];
    const csv = [head.join(","), ...rows.map((r) => [r.processedAt ?? r.updatedAt, r.functionName, r.externalRef ?? "", r.bcDocumentNo ?? "", r.status, r.attempts, (r.lastError ?? "").replace(/"/g, "'")].map((v) => `"${v}"`).join(","))].join("\n");
    const a = document.createElement("a");
    a.href = URL.createObjectURL(new Blob([csv], { type: "text/csv" }));
    a.download = "bc-sync-log.csv";
    a.click();
  };

  return (
    <div className="space-y-4">
      <PageHeader title="BC Sync Log" actions={<>
        <Button variant="secondary" busy={ping.isPending} onClick={() => ping.mutate()}><Wifi className="h-4 w-4" />Ping BC</Button>
        <Button variant="secondary" busy={run.isPending} onClick={() => run.mutate()}><Play className="h-4 w-4" />Process queue now</Button>
        <Button variant="secondary" onClick={exportCsv}>Export</Button>
        <Button busy={retry.isPending} disabled={!k?.failed} onClick={() => retry.mutate(undefined)}><RefreshCw className="h-4 w-4" />Retry failed</Button>
      </>}>
        Every posting goes through this queue to the NavEstate SOAP service (codeunit 52140613). Failed calls retry after 1, 5, 15 and 60 minutes, 5 attempts in all.
      </PageHeader>
      {ping.data && <div className={`rounded-lg px-3 py-2 text-sm ${ping.data.ok ? "bg-emerald-50 text-emerald-800" : "bg-red-50 text-red-700"}`}>Ping: {ping.data.ok ? "OK" : "failed"} {ping.data.message}</div>}
      <ErrorText error={error ?? run.error ?? retry.error} />
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Kpi label="Posted today" value={k?.postedToday ?? "—"} sub={firstTime !== null ? `${firstTime}% first time` : undefined} />
        <Kpi label="Waiting" value={k?.pending ?? "—"} />
        <Kpi label="Retrying" value={k?.retrying ?? "—"} tone={k?.retrying ? "warn" : undefined} />
        <Kpi label="Failed" value={k?.failed ?? "—"} tone={k?.failed ? "bad" : undefined} />
      </div>
      <Card padded={false} title="Log" actions={<>
        <Select value={status} onChange={(e) => setStatus(e.target.value)} placeholder="All statuses" options={["pending", "processing", "success", "failed"]} className="w-36" />
        <Select value={fn} onChange={(e) => setFn(e.target.value)} placeholder="All functions" options={["UpsertTenant", "PostInvoice", "PostCreditMemo", "PostReceipt", "ReverseReceipt", "PostDeposit", "TransferTenantUnit"].map((f) => ({ value: f, label: f }))} className="w-44" />
      </>}>
        {isLoading ? <Spinner /> : (
          <Table head={["Time", "Function", "NavEstate ref", "BC document", "Status", "Attempts", ""]}>
            {data!.rows.map((r) => (
              <tr key={r.id}>
                <td className="whitespace-nowrap text-xs">{dateTime(r.processedAt ?? r.updatedAt)}</td>
                <td>{r.functionName}<span className="block text-xs text-slate-400">{label(r.entityType)}</span></td>
                <td className="font-mono text-xs">{r.externalRef}</td>
                <td className="font-mono text-xs">{r.bcDocumentNo ?? "—"}</td>
                <td>
                  <StatusPill status={r.status} />
                  {r.lastError && <div className="mt-1 max-w-xs text-xs text-red-600">{r.lastError}</div>}
                  {r.status === "failed" && r.nextAttemptAt && <div className="text-xs text-slate-400">Next try {dateTime(r.nextAttemptAt)}</div>}
                </td>
                <td className="tabular-nums">{r.attempts}</td>
                <td className="text-right">{r.status === "failed" && <Button variant="ghost" onClick={() => retry.mutate(r.id)}>Retry</Button>}</td>
              </tr>
            ))}
          </Table>
        )}
      </Card>
    </div>
  );
}
