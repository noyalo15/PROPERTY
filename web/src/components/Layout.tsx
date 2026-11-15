import clsx from "clsx";
import {
  BarChart3, Building2, CalendarClock, ChevronRight, CreditCard, Droplets, FileText, Gauge, LogOut, Menu, RefreshCw,
  Search, Settings, ShieldAlert, Truck, Users, Wrench, X,
} from "lucide-react";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { Link, NavLink, useLocation, useNavigate } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { get } from "@/lib/api";
import { useAuth, useCurrency, type Role } from "@/lib/auth";

type Item = { to: string; label: string; icon: typeof Gauge; roles?: Role[]; stage?: number };
const groups: { title: string; items: Item[] }[] = [
  { title: "General", items: [
    { to: "/", label: "Dashboard", icon: Gauge },
    { to: "/properties", label: "Properties & Units", icon: Building2 },
    { to: "/tenants", label: "Tenants", icon: Users },
    { to: "/leases", label: "Leases & Rent Schedules", icon: CalendarClock, stage: 2 },
  ] },
  { title: "Billing", items: [
    { to: "/billing-runs", label: "Billing Runs", icon: FileText, stage: 3 },
    { to: "/water", label: "Water Readings", icon: Droplets, stage: 3 },
    { to: "/penalties", label: "Penalties", icon: ShieldAlert, stage: 3 },
    { to: "/payments", label: "Payments & Receipts", icon: CreditCard, stage: 4 },
  ] },
  { title: "Operations", items: [
    { to: "/maintenance", label: "Maintenance", icon: Wrench, stage: 5 },
    { to: "/transfers", label: "Transfers & Move-outs", icon: Truck, stage: 5 },
    { to: "/reports", label: "Reports", icon: BarChart3, stage: 6 },
  ] },
  { title: "System", items: [
    { to: "/bc-sync", label: "BC Sync Log", icon: RefreshCw, roles: ["accountant"] },
    { to: "/setup", label: "Setup", icon: Settings, roles: ["accountant"] },
  ] },
];

export const pageTitles: Record<string, string> = Object.fromEntries(groups.flatMap((g) => g.items.map((i) => [i.to, i.label])));

function Logo() {
  return (
    <Link to="/" className="flex items-center gap-2.5 px-4 py-4">
      <span className="h-8 w-8 rounded-lg bg-gradient-to-br from-accent to-teal shadow-inner" />
      <span className="leading-tight">
        <span className="block text-[15px] font-semibold text-white">NavEstate</span>
        <span className="block text-[11px] text-slate-400">by Dynamicnav</span>
      </span>
    </Link>
  );
}

function Sidebar({ onNavigate }: { onNavigate?: () => void }) {
  const { can } = useAuth();
  return (
    <nav className="flex h-full w-[232px] flex-col bg-navy">
      <Logo />
      <div className="flex-1 overflow-y-auto px-2 pb-4">
        {groups.map((g) => ({ ...g, items: g.items.filter((i) => !i.roles || can(...i.roles)) })).filter((g) => g.items.length > 0).map((g) => (
          <div key={g.title} className="mt-3">
            <div className="px-3 pb-1 text-[10px] font-semibold uppercase tracking-wider text-slate-500">{g.title}</div>
            {g.items.map((i) => (
              <NavLink
                key={i.to}
                to={i.to}
                end={i.to === "/"}
                onClick={onNavigate}
                className={({ isActive }) => clsx(
                  "flex items-center gap-2.5 rounded-lg px-3 py-2 text-[13px] transition",
                  isActive ? "bg-teal/90 text-white" : "text-slate-300 hover:bg-navy-600 hover:text-white",
                )}
              >
                <i.icon className="h-4 w-4 shrink-0" />
                <span className="flex-1 truncate">{i.label}</span>
                {i.stage && <span className="rounded bg-white/10 px-1 text-[9px] text-slate-400">S{i.stage}</span>}
              </NavLink>
            ))}
          </div>
        ))}
      </div>
    </nav>
  );
}

function BcPill() {
  const { data } = useQuery({
    queryKey: ["bc-status"],
    queryFn: () => get<{ connected: boolean; mock: boolean; lastPingAt?: string; message?: string }>("/api/bc/status"),
    refetchInterval: 60_000,
  });
  const ok = data?.connected;
  if (data?.mock)
    return <span title="BusinessCentral:UseMock is on: nothing is posted to BC" className="hidden items-center gap-1.5 rounded-full bg-amber-50 px-2.5 py-1 text-xs font-medium text-amber-700 sm:inline-flex"><span className="h-1.5 w-1.5 rounded-full bg-amber-500" />BC mock mode</span>;
  return (
    <span
      title={data?.lastPingAt ? `Last ping ${new Date(data.lastPingAt).toLocaleString()}: ${data.message ?? ""}` : "No ping yet"}
      className={clsx("hidden items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium sm:inline-flex",
        ok ? "bg-emerald-50 text-emerald-700" : "bg-red-50 text-red-700")}
    >
      <span className={clsx("h-1.5 w-1.5 rounded-full", ok ? "bg-emerald-500" : "bg-red-500")} />
      {ok ? "BC 13 connected" : "BC offline"}
    </span>
  );
}

type Hit = { kind: string; id: string; title: string; subtitle: string; link: string };
function GlobalSearch() {
  const [q, setQ] = useState("");
  const [open, setOpen] = useState(false);
  const nav = useNavigate();
  const box = useRef<HTMLDivElement>(null);
  const { data } = useQuery({
    queryKey: ["search", q],
    queryFn: () => get<Hit[]>(`/api/search?q=${encodeURIComponent(q)}`),
    enabled: q.trim().length >= 2,
  });
  useEffect(() => {
    const h = (e: MouseEvent) => box.current && !box.current.contains(e.target as Node) && setOpen(false);
    document.addEventListener("mousedown", h);
    return () => document.removeEventListener("mousedown", h);
  }, []);
  return (
    <div ref={box} className="relative w-full max-w-sm">
      <Search className="pointer-events-none absolute left-3 top-2.5 h-4 w-4 text-slate-400" />
      <input
        value={q}
        onChange={(e) => { setQ(e.target.value); setOpen(true); }}
        onFocus={() => setOpen(true)}
        placeholder="Search tenant, unit, receipt no."
        className="w-full rounded-lg border border-line bg-page py-2 pl-9 pr-3 text-sm outline-none focus:border-teal focus:bg-white"
      />
      {open && q.trim().length >= 2 && (
        <div className="absolute z-40 mt-1 w-full overflow-hidden rounded-lg border border-line bg-white shadow-lg">
          {(data ?? []).length === 0 && <div className="px-3 py-2 text-sm text-slate-400">No matches</div>}
          {(data ?? []).map((h) => (
            <button key={h.kind + h.id} className="flex w-full items-center justify-between px-3 py-2 text-left text-sm hover:bg-slate-50"
              onClick={() => { setOpen(false); setQ(""); nav(h.link); }}>
              <span>
                <span className="font-medium text-slate-800">{h.title}</span>
                <span className="ml-2 text-xs text-slate-400">{h.subtitle}</span>
              </span>
              <span className="text-[10px] uppercase text-slate-400">{h.kind}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

function UserMenu() {
  const { me, logout } = useAuth();
  const [open, setOpen] = useState(false);
  const initials = (me?.fullName ?? "?").split(" ").map((p) => p[0]).slice(0, 2).join("").toUpperCase();
  return (
    <div className="relative">
      <button onClick={() => setOpen((o) => !o)} className="flex h-9 w-9 items-center justify-center rounded-full bg-navy text-xs font-semibold text-white" aria-label="Account">
        {initials}
      </button>
      {open && (
        <div className="absolute right-0 z-40 mt-2 w-60 rounded-lg border border-line bg-white p-2 shadow-lg" onMouseLeave={() => setOpen(false)}>
          <div className="px-2 py-1.5">
            <div className="text-sm font-medium">{me?.fullName}</div>
            <div className="text-xs text-slate-500">{me?.email}</div>
            <div className="mt-1 text-[11px] text-slate-400">{me?.roles.join(", ")}</div>
          </div>
          <Link to="/account/2fa" className="block rounded px-2 py-1.5 text-sm hover:bg-slate-50" onClick={() => setOpen(false)}>
            Two-factor sign-in {me?.twoFactorEnabled ? "(on)" : "(off)"}
          </Link>
          <button onClick={logout} className="flex w-full items-center gap-2 rounded px-2 py-1.5 text-sm text-red-600 hover:bg-red-50">
            <LogOut className="h-4 w-4" /> Sign out
          </button>
        </div>
      )}
    </div>
  );
}

export function Layout({ children }: { children: ReactNode }) {
  const [mobileNav, setMobileNav] = useState(false);
  const { pathname } = useLocation();
  const { currency, setCurrency } = useCurrency();
  const root = "/" + (pathname.split("/")[1] ?? "");
  const title = pageTitles[root] ?? (pathname.startsWith("/account") ? "Account" : "NavEstate");

  return (
    <div className="flex min-h-screen bg-page">
      <aside className="sticky top-0 hidden h-screen shrink-0 lg:block"><Sidebar /></aside>
      {mobileNav && (
        <div className="fixed inset-0 z-50 flex lg:hidden" onClick={() => setMobileNav(false)}>
          <div onClick={(e) => e.stopPropagation()}><Sidebar onNavigate={() => setMobileNav(false)} /></div>
          <div className="flex-1 bg-slate-900/40" />
          <button className="absolute right-3 top-3 rounded bg-white p-1" aria-label="Close menu"><X className="h-5 w-5" /></button>
        </div>
      )}
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-30 flex items-center gap-3 border-b border-line bg-white px-4 py-2.5">
          <button className="rounded p-1.5 hover:bg-slate-100 lg:hidden" onClick={() => setMobileNav(true)} aria-label="Menu"><Menu className="h-5 w-5" /></button>
          <div className="hidden min-w-0 md:block">
            <div className="flex items-center gap-1 text-[11px] text-slate-400">
              <span>NavEstate</span><ChevronRight className="h-3 w-3" /><span>{title}</span>
            </div>
            <div className="truncate text-sm font-semibold text-slate-800">{title}</div>
          </div>
          <div className="flex flex-1 justify-center"><GlobalSearch /></div>
          <BcPill />
          <div className="hidden overflow-hidden rounded-lg border border-line text-xs sm:flex" role="group" aria-label="Dashboard currency">
            {(["KES", "USD"] as const).map((c) => (
              <button key={c} onClick={() => setCurrency(c)} className={clsx("px-2.5 py-1.5 font-medium", currency === c ? "bg-teal text-white" : "bg-white text-slate-600")}>{c}</button>
            ))}
          </div>
          <UserMenu />
        </header>
        <main className="mx-auto w-full max-w-[1400px] flex-1 p-4 md:p-6">{children}</main>
      </div>
    </div>
  );
}
