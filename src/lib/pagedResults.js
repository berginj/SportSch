export function readPagedItems(payload) {
  if (!payload || typeof payload !== "object") return [];
  return Array.isArray(payload.items) ? payload.items : [];
}

export function readContinuationToken(payload) {
  if (!payload || typeof payload !== "object") return "";
  return typeof payload.continuationToken === "string"
    ? payload.continuationToken.trim()
    : "";
}
