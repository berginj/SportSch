import { createHash } from "node:crypto";

import { AppConfig, OutputPaths } from "./types";

export function sha256Hex(input: string): string {
  return createHash("sha256").update(input).digest("hex");
}

export function slugifyId(input: string): string {
  const slug = input
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .replace(/-{2,}/g, "-");

  return slug || "feed";
}

export function normalizeBlobPath(input: string): string {
  const value = input.trim().replace(/^\/+/, "");
  if (!value) {
    throw new Error("Blob paths must not be empty.");
  }

  return value;
}

export function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export function errorMessage(error: unknown): string {
  if (error instanceof Error) {
    return error.message;
  }

  return String(error);
}

export function parseContentLength(value: string | null): number | undefined {
  if (!value?.trim()) {
    return undefined;
  }

  const parsed = Number.parseInt(value, 10);
  return Number.isInteger(parsed) && parsed >= 0 ? parsed : undefined;
}

export function assertContentLengthWithinLimit(
  contentLength: string | null,
  maxBytes: number,
  label: string,
): void {
  const parsed = parseContentLength(contentLength);
  if (parsed !== undefined && parsed > maxBytes) {
    throw new Error(`${label} exceeds the configured limit of ${maxBytes} bytes.`);
  }
}

export async function readTextStreamWithLimit(
  stream: ReadableStream<Uint8Array> | null,
  maxBytes: number,
  label: string,
): Promise<string> {
  if (!stream) {
    return "";
  }

  const reader = stream.getReader();
  const decoder = new TextDecoder("utf-8");
  let totalBytes = 0;
  let text = "";

  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) {
        break;
      }

      totalBytes += value.byteLength;
      if (totalBytes > maxBytes) {
        try {
          await reader.cancel();
        } catch {
          // Ignore cancellation failures while aborting oversized reads.
        }

        throw new Error(`${label} exceeds the configured limit of ${maxBytes} bytes.`);
      }

      text += decoder.decode(value, { stream: true });
    }

    text += decoder.decode();
    return text;
  } finally {
    reader.releaseLock();
  }
}

export function buildOutputPaths(config: AppConfig): OutputPaths {
  const baseUrl = `https://${config.outputStorageAccount}.blob.core.windows.net`;

  return {
    storageAccount: config.outputStorageAccount,
    container: config.outputContainer,
    calendarBlobPath: config.outputBlobPath,
    publicCalendarBlobPath: config.publicCalendarBlobPath,
    publicGamesCalendarBlobPath: config.publicGamesCalendarBlobPath,
    scheduleXFullBlobPath: config.scheduleXFullBlobPath,
    scheduleXGamesBlobPath: config.scheduleXGamesBlobPath,
    statusBlobPath: config.statusBlobPath,
    blobBaseUrl: baseUrl,
    blobCalendarUrl: `${baseUrl}/${config.outputContainer}/${config.outputBlobPath}`,
    blobPublicCalendarUrl: `${baseUrl}/${config.outputContainer}/${config.publicCalendarBlobPath}`,
    blobPublicGamesCalendarUrl: `${baseUrl}/${config.outputContainer}/${config.publicGamesCalendarBlobPath}`,
    blobScheduleXFullUrl: `${baseUrl}/${config.outputContainer}/${config.scheduleXFullBlobPath}`,
    blobScheduleXGamesUrl: `${baseUrl}/${config.outputContainer}/${config.scheduleXGamesBlobPath}`,
    blobStatusUrl: `${baseUrl}/${config.outputContainer}/${config.statusBlobPath}`,
  };
}
