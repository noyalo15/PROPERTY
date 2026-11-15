import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { ApiError, get, post } from "./api";

export type Role = "super_admin" | "landlord" | "property_manager" | "accountant" | "cashier" | "caretaker" | "tenant";

export interface Me {
  id: string;
  email: string;
  fullName: string;
  phoneNumber?: string;
  roles: Role[];
  isStaff: boolean;
  twoFactorEnabled: boolean;
  mustEnrolTwoFactor: boolean;
  tenantId?: string;
  landlordId?: string;
}

interface AuthState {
  me: Me | null;
  loading: boolean;
  refresh: () => Promise<void>;
  logout: () => Promise<void>;
  can: (...roles: Role[]) => boolean;
}

const Ctx = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const qc = useQueryClient();
  const q = useQuery({
    queryKey: ["me"],
    queryFn: async () => {
      try {
        return await get<Me>("/api/auth/me");
      } catch (e) {
        if (e instanceof ApiError && e.status === 401) return null;
        throw e;
      }
    },
    staleTime: 60_000,
    retry: false,
  });
  const me = q.data ?? null;
  const value: AuthState = {
    me,
    loading: q.isLoading,
    refresh: async () => {
      await qc.invalidateQueries({ queryKey: ["me"] });
    },
    logout: async () => {
      await post("/api/auth/logout");
      qc.clear();
      window.location.href = "/login";
    },
    can: (...roles) => !!me && (me.roles.includes("super_admin") || roles.some((r) => me.roles.includes(r))),
  };
  return <Ctx.Provider value={value}>{children}</Ctx.Provider>;
}

export function useAuth() {
  const v = useContext(Ctx);
  if (!v) throw new Error("useAuth outside AuthProvider");
  return v;
}

/** Dashboard currency switch (KES/USD), kept per browser. */
const CurCtx = createContext<{ currency: "KES" | "USD"; setCurrency: (c: "KES" | "USD") => void }>({
  currency: "KES",
  setCurrency: () => {},
});

export function CurrencyProvider({ children }: { children: ReactNode }) {
  const [currency, setCurrency] = useState<"KES" | "USD">(() => {
    try {
      return (localStorage.getItem("navestate.currency") as "KES" | "USD") || "KES";
    } catch {
      return "KES";
    }
  });
  useEffect(() => {
    try {
      localStorage.setItem("navestate.currency", currency);
    } catch {
      /* private mode */
    }
  }, [currency]);
  return <CurCtx.Provider value={{ currency, setCurrency }}>{children}</CurCtx.Provider>;
}

export const useCurrency = () => useContext(CurCtx);
