const amount = new Intl.NumberFormat("en-KE", { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/** "KES 45,000.00" / "USD 4,410.00" (spec). */
export function money(value: number | null | undefined, currency = "KES"): string {
  if (value === null || value === undefined || Number.isNaN(value)) return "—";
  return `${currency} ${amount.format(value)}`;
}

const dateFmt = new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "short", year: "numeric", timeZone: "Africa/Nairobi" });
const dateTimeFmt = new Intl.DateTimeFormat("en-GB", {
  day: "2-digit", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit", timeZone: "Africa/Nairobi",
});

/** Dates are shown in Africa/Nairobi time. Accepts "yyyy-mm-dd" or an ISO timestamp. */
export function date(value: string | null | undefined): string {
  if (!value) return "—";
  const d = /^\d{4}-\d{2}-\d{2}$/.test(value) ? new Date(`${value}T00:00:00+03:00`) : new Date(value);
  return Number.isNaN(d.getTime()) ? "—" : dateFmt.format(d);
}

export function dateTime(value: string | null | undefined): string {
  if (!value) return "—";
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? "—" : dateTimeFmt.format(d);
}

export function label(value: string | null | undefined): string {
  if (!value) return "—";
  const special: Record<string, string> = { "1br": "1 BR", "2br": "2 BR", "3br": "3 BR", "4br": "4 BR", kra_pin: "KRA PIN", id: "ID" };
  if (special[value]) return special[value];
  return value.replace(/_/g, " ").replace(/^\w/, (c) => c.toUpperCase());
}

export function percent(value: number | null | undefined): string {
  return value === null || value === undefined ? "—" : `${value.toFixed(1)}%`;
}
