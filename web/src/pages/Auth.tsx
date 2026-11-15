import { useEffect, useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import QRCode from "qrcode";
import { ApiError, post } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { Button, Card, ErrorText, Field, Input, Tabs } from "@/components/ui";

export function LoginPage() {
  const [mode, setMode] = useState<"staff" | "tenant">("staff");
  const [step, setStep] = useState<"password" | "2fa" | "otp">("password");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [phone, setPhone] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const { refresh } = useAuth();
  const nav = useNavigate();

  async function run(fn: () => Promise<void>) {
    setBusy(true); setError(null);
    try { await fn(); } catch (e) {
      const status = e instanceof ApiError ? (e.body as { status?: string })?.status : undefined;
      setError(new Error(status === "locked" ? "Too many attempts. Try again in 15 minutes." : status === "invalid" ? "Those details are not right." : (e as Error).message));
    } finally { setBusy(false); }
  }

  const done = async () => { await refresh(); nav("/", { replace: true }); };

  const submitPassword = (e: FormEvent) => { e.preventDefault(); run(async () => {
    const r = await post<{ status: string }>("/api/auth/login", { email, password });
    if (r.status === "requires_2fa") setStep("2fa"); else await done();
  }); };
  const submit2fa = (e: FormEvent) => { e.preventDefault(); run(async () => { await post("/api/auth/login-2fa", { code }); await done(); }); };
  const requestOtp = (e: FormEvent) => { e.preventDefault(); run(async () => { await post("/api/auth/otp/request", { phone }); setStep("otp"); }); };
  const verifyOtp = (e: FormEvent) => { e.preventDefault(); run(async () => { await post("/api/auth/otp/verify", { phone, code }); await done(); }); };

  return (
    <div className="flex min-h-screen items-center justify-center bg-page p-4">
      <div className="w-full max-w-sm">
        <div className="mb-6 flex items-center justify-center gap-3">
          <span className="h-10 w-10 rounded-xl bg-gradient-to-br from-accent to-teal" />
          <div><div className="text-lg font-semibold text-navy">NavEstate</div><div className="text-xs text-slate-500">by Dynamicnav</div></div>
        </div>
        <Card>
          <Tabs tabs={[{ id: "staff", label: "Staff & landlords" }, { id: "tenant", label: "Tenants" }]} value={mode}
            onChange={(m) => { setMode(m); setStep("password"); setError(null); setCode(""); }} />
          <div className="mt-4 space-y-3">
            <ErrorText error={error} />
            {mode === "staff" && step === "password" && (
              <form onSubmit={submitPassword} className="space-y-3">
                <Field label="Email"><Input type="email" autoComplete="username" required value={email} onChange={(e) => setEmail(e.target.value)} /></Field>
                <Field label="Password"><Input type="password" autoComplete="current-password" required value={password} onChange={(e) => setPassword(e.target.value)} /></Field>
                <Button className="w-full" busy={busy}>Sign in</Button>
              </form>
            )}
            {mode === "staff" && step === "2fa" && (
              <form onSubmit={submit2fa} className="space-y-3">
                <p className="text-sm text-slate-600">Enter the 6-digit code from your authenticator app.</p>
                <Field label="Code"><Input inputMode="numeric" autoComplete="one-time-code" autoFocus required value={code} onChange={(e) => setCode(e.target.value)} /></Field>
                <Button className="w-full" busy={busy}>Verify</Button>
              </form>
            )}
            {mode === "tenant" && step === "password" && (
              <form onSubmit={requestOtp} className="space-y-3">
                <Field label="Phone number" hint="We will SMS you a one-time code."><Input type="tel" autoComplete="tel" placeholder="0712 345 678" required value={phone} onChange={(e) => setPhone(e.target.value)} /></Field>
                <Button className="w-full" busy={busy}>Send code</Button>
              </form>
            )}
            {mode === "tenant" && step === "otp" && (
              <form onSubmit={verifyOtp} className="space-y-3">
                <p className="text-sm text-slate-600">If {phone} is registered, a code is on its way.</p>
                <Field label="Code"><Input inputMode="numeric" autoComplete="one-time-code" autoFocus required value={code} onChange={(e) => setCode(e.target.value)} /></Field>
                <Button className="w-full" busy={busy}>Sign in</Button>
                <button type="button" className="w-full text-xs text-teal" onClick={() => setStep("password")}>Use another number</button>
              </form>
            )}
          </div>
        </Card>
      </div>
    </div>
  );
}

export function TwoFactorPage() {
  const { me, refresh } = useAuth();
  const [setup, setSetup] = useState<{ sharedKey: string; otpauthUri: string } | null>(null);
  const [qr, setQr] = useState("");
  const [code, setCode] = useState("");
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const nav = useNavigate();

  useEffect(() => {
    if (me && !me.twoFactorEnabled && !setup)
      post<{ sharedKey: string; otpauthUri: string }>("/api/auth/2fa/setup").then(setSetup).catch(setError);
  }, [me, setup]);
  useEffect(() => { if (setup) QRCode.toDataURL(setup.otpauthUri, { margin: 1, width: 200 }).then(setQr); }, [setup]);

  async function enable(e: FormEvent) {
    e.preventDefault(); setBusy(true); setError(null);
    try { await post("/api/auth/2fa/enable", { code }); await refresh(); nav("/"); } catch (err) { setError(err); } finally { setBusy(false); }
  }

  if (me?.twoFactorEnabled) return <Card title="Two-factor sign-in"><p className="text-sm text-slate-600">Two-factor sign-in is on for your account. Ask a super admin to reset it if you lose your phone.</p></Card>;

  return (
    <div className="mx-auto max-w-lg">
      <Card title="Set up two-factor sign-in">
        {me?.mustEnrolTwoFactor && <p className="mb-3 rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800">Staff accounts need two-factor sign-in before using NavEstate.</p>}
        <ol className="mb-4 list-decimal space-y-1 pl-5 text-sm text-slate-600">
          <li>Install Google Authenticator, Microsoft Authenticator or a similar app.</li>
          <li>Scan this code, or type the key into the app.</li>
          <li>Enter the 6-digit code the app shows.</li>
        </ol>
        {qr && <img src={qr} alt="QR code for your authenticator app" className="mx-auto mb-3 h-[200px] w-[200px]" />}
        {setup && <p className="mb-4 break-all text-center font-mono text-sm text-slate-700">{setup.sharedKey.match(/.{1,4}/g)?.join(" ")}</p>}
        <form onSubmit={enable} className="space-y-3">
          <ErrorText error={error} />
          <Field label="Code from the app"><Input inputMode="numeric" required value={code} onChange={(e) => setCode(e.target.value)} /></Field>
          <Button busy={busy} className="w-full">Turn on</Button>
        </form>
      </Card>
    </div>
  );
}
