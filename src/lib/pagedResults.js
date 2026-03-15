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

export async function fetchAllPagedItems(fetchPage) {
  const items = [];
  const seenTokens = new Set();
  let continuationToken = "";

  while (true) {
    const payload = await fetchPage(continuationToken);
    items.push(...readPagedItems(payload));

    const nextToken = readContinuationToken(payload);
    if (!nextToken || seenTokens.has(nextToken)) {
      break;
    }

    seenTokens.add(nextToken);
    continuationToken = nextToken;
  }

  return items;
}
