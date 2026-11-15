import { useEffect, type ReactNode } from "react";
import { Navigate, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import { LogOut } from "lucide-react";
import { useAuth, type Role } from "@/lib/auth";
import { Layout } from "@/components/Layout";
import { Button, Card, Spinner } from "@/components/ui";
import { LoginPage, TwoFactorPage } from "@/pages/Auth";
import { ComingSoon, DashboardPage } from "@/pages/Dashboard";
import { PropertiesPage } from "@/pages/Properties";
import { PropertyDetailPage } from "@/pages/PropertyDetail";
import { TenantsPage } from "@/pages/Tenants";
import { Tenant360Page } from "@/pages/Tenant360";
import { SetupPage } from "@/pages/Setup";
import { BcSyncPage } from "@/pages/BcSync";

function RequireStaff({ children, roles }: { children: ReactNode; roles?: Role[] }) {
  const { me, loading, can } = useAuth();
  const loc = useLocation();
  const nav = useNavigate();
  useEffect(() => {
    const h = () => nav("/account/2fa");
    window.addEventListener("navestate:enrol-2fa", h);
    return () => window.removeEventListener("navestate:enrol-2fa", h);
  }, [nav]);
  if (loading) return <Spinner />;
  if (!me) return <Navigate to="/login" replace state={{ from: loc.pathname }} />;
  if (!me.isStaff) return <Navigate to="/portal" replace />;
  if (me.mustEnrolTwoFactor && loc.pathname !== "/account/2fa") return <Navigate to="/account/2fa" replace />;
  if (roles && !can(...roles)) return <Layout><Card title="Not available"><p className="text-sm text-slate-600">Your role does not include this page.</p></Card></Layout>;
  return <Layout>{children}</Layout>;
}

/** Tenant and landlord portals: placeholders until stage 6. */
function Portal() {
  const { me, loading, logout } = useAuth();
  if (loading) return <Spinner />;
  if (!me) return <Navigate to="/login" replace />;
  if (me.isStaff) return <Navigate to="/" replace />;
  const tenant = me.roles.includes("tenant");
  return (
    <div className="min-h-screen bg-page p-4">
      <div className="mx-auto max-w-md space-y-4">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2"><span className="h-8 w-8 rounded-lg bg-gradient-to-br from-accent to-teal" /><span className="font-semibold text-navy">NavEstate</span></div>
          <Button variant="ghost" onClick={logout}><LogOut className="h-4 w-4" />Sign out</Button>
        </div>
        <Card title={`Karibu, ${me.fullName.split(" ")[0]}`}>
          <p className="text-sm text-slate-600">
            {tenant
              ? "Your tenant portal (bills, statement, Pay with M-PESA, repairs and notices) opens in stage 6. You are signed in."
              : "Your landlord portal (statements and reports for your properties) opens in stage 6. You are signed in."}
          </p>
        </Card>
      </div>
    </div>
  );
}

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/portal" element={<Portal />} />
      <Route path="/" element={<RequireStaff><DashboardPage /></RequireStaff>} />
      <Route path="/account/2fa" element={<RequireStaff><TwoFactorPage /></RequireStaff>} />
      <Route path="/properties" element={<RequireStaff><PropertiesPage /></RequireStaff>} />
      <Route path="/properties/:id" element={<RequireStaff><PropertyDetailPage /></RequireStaff>} />
      <Route path="/tenants" element={<RequireStaff><TenantsPage /></RequireStaff>} />
      <Route path="/tenants/:id" element={<RequireStaff><Tenant360Page /></RequireStaff>} />
      <Route path="/setup" element={<RequireStaff><SetupPage /></RequireStaff>} />
      <Route path="/bc-sync" element={<RequireStaff roles={["accountant"]}><BcSyncPage /></RequireStaff>} />
      <Route path="/leases" element={<RequireStaff><ComingSoon title="Leases & Rent Schedules" stage={2} /></RequireStaff>} />
      <Route path="/billing-runs" element={<RequireStaff><ComingSoon title="Billing Runs" stage={3} /></RequireStaff>} />
      <Route path="/water" element={<RequireStaff><ComingSoon title="Water Readings" stage={3} /></RequireStaff>} />
      <Route path="/penalties" element={<RequireStaff><ComingSoon title="Penalties" stage={3} /></RequireStaff>} />
      <Route path="/payments" element={<RequireStaff><ComingSoon title="Payments & Receipts" stage={4} /></RequireStaff>} />
      <Route path="/maintenance" element={<RequireStaff><ComingSoon title="Maintenance" stage={5} /></RequireStaff>} />
      <Route path="/transfers" element={<RequireStaff><ComingSoon title="Transfers & Move-outs" stage={5} /></RequireStaff>} />
      <Route path="/reports" element={<RequireStaff><ComingSoon title="Reports" stage={6} /></RequireStaff>} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
