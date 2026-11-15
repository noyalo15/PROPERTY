export class ApiError extends Error {
  constructor(public status: number, message: string, public body?: unknown) {
    super(message);
  }
}

function messageFrom(body: unknown, status: number): string {
  if (body && typeof body === "object") {
    const b = body as Record<string, unknown>;
    if (typeof b.error === "string") return b.error;
    if (Array.isArray(b.errors)) return b.errors.join(" ");
    if (b.errors && typeof b.errors === "object") return Object.values(b.errors as Record<string, string[]>).flat().join(" ");
    if (typeof b.title === "string") return b.title;
  }
  if (status === 403) return "You do not have permission to do that.";
  if (status === 401) return "Please sign in.";
  return `Request failed (${status})`;
}

/** Fetch wrapper: same-origin cookie auth plus the X-NavEstate header the API requires on writes (CSRF). */
export async function api<T = unknown>(path: string, init: RequestInit & { json?: unknown } = {}): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set("X-NavEstate", "1");
  let body = init.body;
  if (init.json !== undefined) {
    headers.set("Content-Type", "application/json");
    body = JSON.stringify(init.json);
  }
  const res = await fetch(path, { ...init, body, headers, credentials: "same-origin" });
  const text = await res.text();
  const data = text ? safeJson(text) : undefined;
  if (!res.ok) {
    if (res.status === 403 && (data as { error?: string })?.error === "two_factor_enrolment_required") {
      window.dispatchEvent(new Event("navestate:enrol-2fa"));
    }
    throw new ApiError(res.status, messageFrom(data, res.status), data);
  }
  return data as T;
}

function safeJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

export const get = <T>(path: string) => api<T>(path);
export const post = <T>(path: string, json?: unknown) => api<T>(path, { method: "POST", json });
export const put = <T>(path: string, json?: unknown) => api<T>(path, { method: "PUT", json });
export const del = <T>(path: string) => api<T>(path, { method: "DELETE" });
export const upload = <T>(path: string, form: FormData) => api<T>(path, { method: "POST", body: form });
