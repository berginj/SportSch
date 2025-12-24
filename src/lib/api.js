// src/lib/api.js
import { LEAGUE_HEADER_NAME, LEAGUE_STORAGE_KEY } from "./constants";

export function apiBase() {
  if (import.meta.env.DEV) {
    const b = import.meta.env.VITE_API_BASE_URL;
    return b && b.trim() ? b.trim().replace(/\/+$/, "") : "";
  }
  return "";
}

function isNonJsonBody(body) {
  return (
    (typeof FormData !== "undefined" && body instanceof FormData) ||
    (typeof Blob !== "undefined" && body instanceof Blob) ||
    (typeof ArrayBuffer !== "undefined" && body instanceof ArrayBuffer) ||
    (typeof URLSearchParams !== "undefined" && body instanceof URLSearchParams)
  );
}

export async function apiFetch(path, options = {}) {
  const base = apiBase();
  const url = base ? `${base}${path}` : path;

  const headers = new Headers(options.headers || {});

  // Always attach active league id
  const leagueId = (localStorage.getItem(LEAGUE_STORAGE_KEY) || "").trim();
  if (leagueId && !headers.has(LEAGUE_HEADER_NAME)) {
    headers.set(LEAGUE_HEADER_NAME, leagueId);
  }

  // Only default Content-Type for JSON-ish requests.
  // DO NOT set Content-Type for FormData; browser must set boundary.
  const body = options.body;
  if (body != null && !headers.has("Content-Type") && !isNonJsonBody(body)) {
    headers.set("Content-Type", "application/json");
  }

  const res = await fetch(url, {
    ...options,
    headers,
    credentials: "include",
  });

  const text = await res.text();
  let data = null;
  try {
    data = text ? JSON.parse(text) : null;
  } catch {
    data = text;
  }

  if (!res.ok) {
    const err = data?.error;
    const msg =
      (typeof err === "string" ? err : err?.message) ||
      (typeof data === "string" ? data : "Request failed");
    const code = typeof err === "object" && err?.code ? `${err.code}: ` : "";
    throw new Error(`${res.status} ${code}${msg}`);
  }

  if (data && typeof data === "object" && "data" in data) return data.data;
  return data;
}
