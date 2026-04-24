import { FeedRunResult, ParsedEvent } from "./types";

export function mergeFeedEvents(results: FeedRunResult[]): ParsedEvent[] {
  const deduped = new Map<string, ParsedEvent>();

  for (const result of results) {
    for (const event of result.events) {
      const existing = deduped.get(event.identityKey);
      if (!existing || comparePriority(event, existing) > 0) {
        deduped.set(event.identityKey, event);
      }
    }
  }

  return Array.from(deduped.values()).sort(compareEventOrder);
}

function comparePriority(left: ParsedEvent, right: ParsedEvent): number {
  if (left.sequence !== right.sequence) {
    return left.sequence - right.sequence;
  }

  if ((left.updatedSortValue ?? 0) !== (right.updatedSortValue ?? 0)) {
    return (left.updatedSortValue ?? 0) - (right.updatedSortValue ?? 0);
  }

  if (left.cancelled !== right.cancelled) {
    return left.cancelled ? 1 : -1;
  }

  return left.properties.length - right.properties.length;
}

function compareEventOrder(left: ParsedEvent, right: ParsedEvent): number {
  if (left.start.sortValue !== right.start.sortValue) {
    return left.start.sortValue - right.start.sortValue;
  }

  if (left.start.kind !== right.start.kind) {
    return left.start.kind === "date" ? -1 : 1;
  }

  const leftEnd = left.end?.sortValue ?? Number.MAX_SAFE_INTEGER;
  const rightEnd = right.end?.sortValue ?? Number.MAX_SAFE_INTEGER;
  if (leftEnd !== rightEnd) {
    return leftEnd - rightEnd;
  }

  const summaryCompare = left.summary.localeCompare(right.summary);
  if (summaryCompare !== 0) {
    return summaryCompare;
  }

  return left.mergedUid.localeCompare(right.mergedUid);
}
