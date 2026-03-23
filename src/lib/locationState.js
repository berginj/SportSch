const FALLBACK_ORIGIN = "http://localhost";

function resolveOrigin() {
  if (typeof window === "undefined") return FALLBACK_ORIGIN;
  const origin = window.location.origin || "";
  return origin && origin !== "null" ? origin : FALLBACK_ORIGIN;
}

function normalizeHash(hash, fallbackHash = "") {
  if (hash === undefined) return fallbackHash;
  const next = String(hash || "").trim();
  if (!next) return "";
  return next.startsWith("#") ? next : `#${next}`;
}

function buildRelativeUrl(url, hash) {
  const search = url.searchParams.toString();
  const nextHash = normalizeHash(hash, url.hash);
  return `${url.pathname}${search ? `?${search}` : ""}${nextHash}`;
}

export function getCurrentUrl() {
  if (typeof window === "undefined") {
    return new URL("/", FALLBACK_ORIGIN);
  }
  return new URL(window.location.href || "/", resolveOrigin());
}

export function readLocationSearchParams(search) {
  if (typeof search === "string") {
    return new URLSearchParams(search);
  }
  return new URLSearchParams(getCurrentUrl().search);
}

export function readHashValue({ fallback = "", validValues } = {}) {
  if (typeof window === "undefined") return fallback;
  const next = (window.location.hash || "").replace(/^#/, "").trim();
  if (!next) return fallback;
  if (validValues && !validValues.has(next)) return fallback;
  return next;
}

export function updateLocationSearch(mutateParams, { hash } = {}) {
  if (typeof window === "undefined") return "";

  const url = getCurrentUrl();
  const params = new URLSearchParams(url.search);
  mutateParams(params, url);
  url.search = params.toString();

  const next = buildRelativeUrl(url, hash);
  window.history.replaceState({}, "", next);
  return next;
}

export function replaceLocation({ setParams = {}, clearParams = [], hash } = {}) {
  return updateLocationSearch((params) => {
    clearParams.forEach((key) => params.delete(key));
    Object.entries(setParams).forEach(([key, value]) => {
      if (value == null || value === "") {
        params.delete(key);
        return;
      }
      params.set(key, String(value));
    });
  }, { hash });
}

export function subscribeToLocationChanges(listener, { hashchange = false, popstate = true } = {}) {
  if (typeof window === "undefined") return () => {};

  if (popstate) window.addEventListener("popstate", listener);
  if (hashchange) window.addEventListener("hashchange", listener);

  return () => {
    if (popstate) window.removeEventListener("popstate", listener);
    if (hashchange) window.removeEventListener("hashchange", listener);
  };
}
