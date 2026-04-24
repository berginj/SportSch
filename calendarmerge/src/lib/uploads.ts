import { UploadAction, UploadCalendarInput } from "./types";
import { slugifyId } from "./util";

export interface NormalizedUploadCalendarInput extends UploadCalendarInput {
  name: string;
}

export function normalizeUploadCalendarInput(
  input: Partial<UploadCalendarInput>,
): NormalizedUploadCalendarInput {
  const calendarText = normalizeCalendarText(input.calendarText);
  const id = normalizeCalendarId(input.id ?? input.name);
  const name = input.name?.trim() || input.id?.trim() || id;

  return {
    id,
    name,
    calendarText,
  };
}

export function normalizeCalendarId(value: string | undefined): string {
  const rawIdentity = value?.trim() || "";
  if (!rawIdentity) {
    throw new Error("Calendar uploads must include an id or name.");
  }

  return slugifyId(rawIdentity);
}

export function parseBooleanInput(value: boolean | string | undefined, fallback: boolean): boolean {
  if (typeof value === "boolean") {
    return value;
  }

  if (typeof value !== "string" || !value.trim()) {
    return fallback;
  }

  const normalized = value.trim().toLowerCase();
  if (["1", "true", "yes", "on"].includes(normalized)) {
    return true;
  }

  if (["0", "false", "no", "off"].includes(normalized)) {
    return false;
  }

  throw new Error(`Invalid boolean value: ${value}`);
}

export function parseUploadAction(value: string | undefined, fallback: UploadAction = "upsert"): UploadAction {
  if (!value?.trim()) {
    return fallback;
  }

  const normalized = value.trim().toLowerCase();
  if (normalized === "create" || normalized === "replace" || normalized === "upsert") {
    return normalized;
  }

  throw new Error(`Invalid upload action: ${value}`);
}

function normalizeCalendarText(value: string | undefined): string {
  const normalized = value?.replace(/^\uFEFF/, "").trim();
  if (!normalized) {
    throw new Error("Calendar uploads must include ICS content.");
  }

  return normalized;
}
