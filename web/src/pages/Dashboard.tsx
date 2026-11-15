import { useQuery } from "@tanstack/react-query";
import { AlertTriangle, CheckCircle2 } from "lucide-react";
import { Link } from "react-router-dom";
import { get } from "@/lib/api";
import { useCurrency } from "@/lib/auth";
import { percent } from "@/lib/format";
import { Card, ErrorText, Kpi, OccupancyBar, Spinner, Table, Num, NumHead } from "@/components/ui";

interface Dash {
  units: number; occupied: number; vacant: number; occupancyPercent: number; tenants: number; activeLeases: number;
  bcFailed: number; bcPending: number; expiringIn90: number;
  properties: { id: string; name: string; code: string; units: number; occupied: number; occupancyPercent: number }[];
}

export function DashboardPage() {
  const { currency } = useCurrency();
  const { data, error, isLoading } = useQuery({ queryKey: ["dashboard"], queryFn: () => get<Dash>("/api/dashboard") });
  if (isLoading) return <Spinner />;
  if (error || !data) return <ErrorText error={error} />;

  const actions = [
    data.bcFailed > 0 && { text: `${data.bcFailed} BC posting(s) failed`, to: "/bc-sync" },
    data.expiringIn90 > 0 && { text: `${data.expiringIn90} lease(s) end within 90 days`, to: "/tenants" },
    data.properties.some((p) => p.occupancyPercent < 85) && { text: "Some properties are under 85% occupancy", to: "/properties" },
  ].filter(Boolean) as { text: string; to: string }[];

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Kpi label="Occupancy" value={percent(data.occupancyPercent)} sub={`${data.occupied} of ${data.units} units let`} tone={data.occupancyPercent < 85 ? "warn" : undefined} />
        <Kpi label={`Billed this month (${currency})`} value="—" sub="Billing runs arrive in stage 3" />
        <Kpi label="Active leases" value={data.activeLeases} sub={`${data.tenants} tenants`} />
        <Kpi label="BC queue" value={data.bcPending} sub={data.bcFailed ? `${data.bcFailed} failed` : "Nothing failed"} tone={data.bcFailed ? "bad" : undefined} />
      </div>
      <div className="grid gap-4 lg:grid-cols-3">
        <Card title="Occupancy by property" className="lg:col-span-2" padded={false}>
          <Table head={["Property", <NumHead key="u">Units</NumHead>, <NumHead key="o">Occupied</NumHead>, "Occupancy"]}>
            {data.properties.map((p) => (
              <tr key={p.id}>
                <td><Link to={`/properties/${p.id}`} className="font-medium text-slate-800 hover:text-teal">{p.name}</Link><div className="text-xs text-slate-400">{p.code}</div></td>
                <Num>{p.units}</Num><Num>{p.occupied}</Num>
                <td><OccupancyBar percent={p.occupancyPercent} /></td>
              </tr>
            ))}
          </Table>
        </Card>
        <Card title="Action needed">
          {actions.length === 0 ? (
            <p className="flex items-center gap-2 text-sm text-emerald-700"><CheckCircle2 className="h-4 w-4" /> Nothing needs attention.</p>
          ) : (
            <ul className="space-y-2">
              {actions.map((a) => (
                <li key={a.text}><Link to={a.to} className="flex items-start gap-2 text-sm text-slate-700 hover:text-teal"><AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-500" />{a.text}</Link></li>
              ))}
            </ul>
          )}
          <p className="mt-4 text-xs text-slate-400">Unpaid bills, escalations, water readings and collection charts join this dashboard as stages 2 to 6 land.</p>
        </Card>
      </div>
    </div>
  );
}

export function ComingSoon({ title, stage }: { title: string; stage: number }) {
  return (
    <Card title={title}>
      <p className="text-sm text-slate-600">This page is part of stage {stage} of the build plan. The tables behind it already exist in SQL Server.</p>
    </Card>
  );
}
