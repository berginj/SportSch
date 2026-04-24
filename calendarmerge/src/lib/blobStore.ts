import { randomUUID } from "node:crypto";

import { BlobLeaseClient, BlobServiceClient, ContainerClient } from "@azure/storage-blob";
import { DefaultAzureCredential } from "@azure/identity";

import {
  AppConfig,
  PublicServiceStatus,
  ServiceStatus,
  SourceFeedConfig,
  UploadAction,
  UploadedCalendarRecord,
} from "./types";
import { ConflictError, NotFoundError } from "./errors";
import { errorMessage } from "./util";
import { NormalizedUploadCalendarInput } from "./uploads";

interface UploadedCalendarManifest {
  id: string;
  name: string;
  kind: "uploaded";
  blobPath: string;
  uploadedAt: string;
}

interface StoredUploadedCalendarManifest extends UploadedCalendarManifest {
  etag: string;
}

export interface RefreshLease {
  release(): Promise<void>;
}

export class BlobStore {
  private readonly serviceClient: BlobServiceClient;

  constructor(private readonly config: AppConfig) {
    this.serviceClient = new BlobServiceClient(
      `https://${config.outputStorageAccount}.blob.core.windows.net`,
      new DefaultAzureCredential(),
    );
  }

  async calendarExists(): Promise<boolean> {
    return this.getOutputBlobClient(this.config.outputBlobPath).exists();
  }

  async readPublicStatus(): Promise<PublicServiceStatus | null> {
    return this.readJsonBlob<PublicServiceStatus>(this.getPublicStatusBlobClient());
  }

  async readOperatorStatus(): Promise<ServiceStatus | null> {
    return this.readJsonBlob<ServiceStatus>(this.getOperatorStatusBlobClient());
  }

  async writeCalendar(calendarText: string): Promise<void> {
    await this.writePublicTextBlob(this.config.outputBlobPath, calendarText, "text/calendar; charset=utf-8");
  }

  async writePublicCalendar(blobPath: string, calendarText: string): Promise<void> {
    await this.writePublicTextBlob(blobPath, calendarText, "text/calendar; charset=utf-8");
  }

  async writePublicJsonBlob(blobPath: string, value: unknown): Promise<void> {
    await this.writePublicTextBlob(blobPath, `${JSON.stringify(value, null, 2)}\n`, "application/json; charset=utf-8");
  }

  async writePublicStatus(status: PublicServiceStatus): Promise<void> {
    await this.writePublicJsonBlob(this.config.statusBlobPath, status);
  }

  async writeOperatorStatus(status: ServiceStatus): Promise<void> {
    await this.ensurePrivateContainer(this.config.operatorStatusContainer);
    await this.getOperatorStatusBlobClient().uploadData(Buffer.from(`${JSON.stringify(status, null, 2)}\n`, "utf8"), {
      blobHTTPHeaders: {
        blobContentType: "application/json; charset=utf-8",
      },
    });
  }

  async listUploadedCalendars(): Promise<UploadedCalendarRecord[]> {
    const containerClient = this.getUploadedContainerClient();
    if (!(await containerClient.exists())) {
      return [];
    }

    const records: UploadedCalendarRecord[] = [];
    const prefix = buildPrefix(this.config.uploadedSourcesPrefix);

    for await (const blob of containerClient.listBlobsFlat(prefix ? { prefix } : undefined)) {
      if (!blob.name.endsWith(".json")) {
        continue;
      }

      const blobClient = containerClient.getBlockBlobClient(blob.name);
      try {
        const response = await blobClient.download();
        const body = await streamToString(response.readableStreamBody);
        const manifest = parseUploadedCalendarManifest(JSON.parse(body), blob.name);
        records.push(this.toUploadedCalendarRecord(manifest));
      } catch (error) {
        throw new Error(
          `Failed to read uploaded calendar manifest ${blob.name}: ${errorMessage(error)}`,
        );
      }
    }

    return records.sort((left, right) => left.id.localeCompare(right.id));
  }

  async listUploadedSources(): Promise<SourceFeedConfig[]> {
    const calendars = await this.listUploadedCalendars();
    return calendars.map((calendar) => this.toUploadedSourceFeed(calendar));
  }

  async readUploadedCalendar(blobPath: string): Promise<string> {
    const blobClient = this.getUploadedContainerClient().getBlockBlobClient(blobPath);
    const response = await blobClient.download();
    return streamToString(response.readableStreamBody);
  }

  async writeUploadedCalendar(
    input: NormalizedUploadCalendarInput,
    action: UploadAction,
  ): Promise<{ action: UploadAction; created: boolean; record: UploadedCalendarRecord; source: SourceFeedConfig }> {
    await this.ensureUploadedSourcesContainer();

    const containerClient = this.getUploadedContainerClient();
    const manifestBlobPath = buildUploadedManifestPath(this.config.uploadedSourcesPrefix, input.id);
    const manifestClient = containerClient.getBlockBlobClient(manifestBlobPath);
    const existingManifest = await this.tryReadUploadedManifest(manifestBlobPath);

    if (action === "create" && existingManifest) {
      throw new ConflictError(`Uploaded calendar '${input.id}' already exists. Use action=replace to overwrite it.`);
    }

    if (action === "replace" && !existingManifest) {
      throw new NotFoundError(`Uploaded calendar '${input.id}' does not exist. Use action=create to add it.`);
    }

    const uploadedAt = new Date().toISOString();
    const calendarBlobPath = buildUploadedCalendarBlobPath(
      this.config.uploadedSourcesPrefix,
      input.id,
      buildVersionToken(uploadedAt),
    );
    const calendarBlobClient = containerClient.getBlockBlobClient(calendarBlobPath);

    await calendarBlobClient.uploadData(Buffer.from(input.calendarText, "utf8"), {
      blobHTTPHeaders: {
        blobContentType: "text/calendar; charset=utf-8",
      },
      conditions: {
        ifNoneMatch: "*",
      },
    });

    const manifest: UploadedCalendarManifest = {
      id: input.id,
      name: input.name,
      kind: "uploaded",
      blobPath: calendarBlobPath,
      uploadedAt,
    };

    const nextAction = existingManifest ? (action === "create" ? "create" : "replace") : "create";
    const created = nextAction === "create";

    try {
      await manifestClient.uploadData(Buffer.from(`${JSON.stringify(manifest, null, 2)}\n`, "utf8"), {
        blobHTTPHeaders: {
          blobContentType: "application/json; charset=utf-8",
        },
        conditions: created
          ? {
              ifNoneMatch: "*",
            }
          : {
              ifMatch: existingManifest?.etag,
            },
      });
    } catch (error) {
      await calendarBlobClient.deleteIfExists();

      if (isConflictStatus(error)) {
        throw new ConflictError(`Uploaded calendar '${input.id}' changed during write. Retry the request.`);
      }

      throw error;
    }

    if (existingManifest?.blobPath && existingManifest.blobPath !== calendarBlobPath) {
      try {
        await containerClient.getBlockBlobClient(existingManifest.blobPath).deleteIfExists();
      } catch {
        // Leave the previous version in place when cleanup fails.
      }
    }

    const record = this.toUploadedCalendarRecord(manifest);
    return {
      action: nextAction,
      created,
      record,
      source: this.toUploadedSourceFeed(record),
    };
  }

  async deleteUploadedCalendar(
    id: string,
  ): Promise<{ deleted: true; record: UploadedCalendarRecord; source: SourceFeedConfig }> {
    const manifestBlobPath = buildUploadedManifestPath(this.config.uploadedSourcesPrefix, id);
    const existingManifest = await this.tryReadUploadedManifest(manifestBlobPath);

    if (!existingManifest) {
      throw new NotFoundError(`Uploaded calendar '${id}' does not exist.`);
    }

    const containerClient = this.getUploadedContainerClient();
    const manifestClient = containerClient.getBlockBlobClient(manifestBlobPath);
    try {
      await manifestClient.delete({
        conditions: {
          ifMatch: existingManifest.etag,
        },
      });
    } catch (error) {
      if (isConflictStatus(error)) {
        throw new ConflictError(`Uploaded calendar '${id}' changed during delete. Retry the request.`);
      }

      if (isMissingStatus(error)) {
        throw new NotFoundError(`Uploaded calendar '${id}' no longer exists.`);
      }

      throw error;
    }

    try {
      await containerClient.getBlockBlobClient(existingManifest.blobPath).deleteIfExists();
    } catch {
      // Leave orphan cleanup to a later maintenance pass when blob deletion fails.
    }

    const record = this.toUploadedCalendarRecord(existingManifest);
    return {
      deleted: true,
      record,
      source: this.toUploadedSourceFeed(record),
    };
  }

  async tryAcquireRefreshLock(): Promise<RefreshLease | null> {
    await this.ensurePrivateContainer(this.config.refreshLockContainer);

    const lockBlobClient = this.getRefreshLockBlobClient();
    try {
      await lockBlobClient.uploadData(Buffer.alloc(0), {
        blobHTTPHeaders: {
          blobContentType: "application/octet-stream",
        },
        conditions: {
          ifNoneMatch: "*",
        },
      });
    } catch (error) {
      if (!isConflictStatus(error)) {
        throw error;
      }
    }

    const leaseClient = new BlobLeaseClient(lockBlobClient);
    try {
      await leaseClient.acquireLease(60);
    } catch (error) {
      if (isConflictStatus(error)) {
        return null;
      }

      throw error;
    }

    let released = false;
    const renewTimer = setInterval(() => {
      void leaseClient.renewLease().catch(() => {
        // Renewal failures surface later as competing writes; no extra recovery here.
      });
    }, 30_000);
    renewTimer.unref?.();

    return {
      release: async () => {
        if (released) {
          return;
        }

        released = true;
        clearInterval(renewTimer);

        try {
          await leaseClient.releaseLease();
        } catch (error) {
          if (!isMissingStatus(error) && !isConflictStatus(error)) {
            throw error;
          }
        }
      },
    };
  }

  private async readJsonBlob<T>(blobClient: ReturnType<BlobStore["getOutputBlobClient"]>): Promise<T | null> {
    if (!(await blobClient.exists())) {
      return null;
    }

    const response = await blobClient.download();
    const body = await streamToString(response.readableStreamBody);
    return JSON.parse(body) as T;
  }

  private async tryReadUploadedManifest(blobPath: string): Promise<StoredUploadedCalendarManifest | null> {
    const blobClient = this.getUploadedContainerClient().getBlockBlobClient(blobPath);
    if (!(await blobClient.exists())) {
      return null;
    }

    const [properties, response] = await Promise.all([blobClient.getProperties(), blobClient.download()]);
    const body = await streamToString(response.readableStreamBody);
    const manifest = parseUploadedCalendarManifest(JSON.parse(body), blobPath);
    if (!properties.etag) {
      throw new Error(`Uploaded calendar manifest ${blobPath} is missing an etag.`);
    }

    return {
      ...manifest,
      etag: properties.etag,
    };
  }

  private async ensureOutputContainer(): Promise<void> {
    await this.serviceClient.getContainerClient(this.config.outputContainer).createIfNotExists();
  }

  private async ensureUploadedSourcesContainer(): Promise<void> {
    await this.getUploadedContainerClient().createIfNotExists();
  }

  private async writePublicTextBlob(blobPath: string, content: string, contentType: string): Promise<void> {
    await this.ensureOutputContainer();
    await this.getOutputBlobClient(blobPath).uploadData(Buffer.from(content, "utf8"), {
      blobHTTPHeaders: {
        blobContentType: contentType,
      },
    });
  }

  private async ensurePrivateContainer(containerName: string): Promise<void> {
    await this.serviceClient.getContainerClient(containerName).createIfNotExists();
  }

  private getOutputBlobClient(blobPath: string) {
    return this.serviceClient
      .getContainerClient(this.config.outputContainer)
      .getBlockBlobClient(blobPath);
  }

  private getPublicStatusBlobClient() {
    return this.getOutputBlobClient(this.config.statusBlobPath);
  }

  private getOperatorStatusBlobClient() {
    return this.serviceClient
      .getContainerClient(this.config.operatorStatusContainer)
      .getBlockBlobClient(this.config.operatorStatusBlobPath);
  }

  private getRefreshLockBlobClient() {
    return this.serviceClient
      .getContainerClient(this.config.refreshLockContainer)
      .getBlockBlobClient(this.config.refreshLockBlobPath);
  }

  private getUploadedContainerClient(): ContainerClient {
    return this.serviceClient.getContainerClient(this.config.uploadedSourcesContainer);
  }

  private toUploadedCalendarRecord(manifest: UploadedCalendarManifest): UploadedCalendarRecord {
    return {
      id: manifest.id,
      name: manifest.name,
      kind: "uploaded",
      blobPath: manifest.blobPath,
      url: buildBlobUrl(this.config.outputStorageAccount, this.config.uploadedSourcesContainer, manifest.blobPath),
      uploadedAt: manifest.uploadedAt,
    };
  }

  private toUploadedSourceFeed(record: UploadedCalendarRecord): SourceFeedConfig {
    return {
      id: record.id,
      name: record.name,
      kind: "uploaded",
      url: record.url,
      blobPath: record.blobPath,
      uploadedAt: record.uploadedAt,
    };
  }
}

function parseUploadedCalendarManifest(value: unknown, blobName: string): UploadedCalendarManifest {
  if (!value || typeof value !== "object") {
    throw new Error(`Manifest ${blobName} is not a JSON object.`);
  }

  const manifest = value as Partial<UploadedCalendarManifest>;
  if (!manifest.id?.trim()) {
    throw new Error(`Manifest ${blobName} is missing id.`);
  }

  if (!manifest.name?.trim()) {
    throw new Error(`Manifest ${blobName} is missing name.`);
  }

  if (manifest.kind !== "uploaded") {
    throw new Error(`Manifest ${blobName} has unsupported kind.`);
  }

  if (!manifest.blobPath?.trim()) {
    throw new Error(`Manifest ${blobName} is missing blobPath.`);
  }

  if (!manifest.uploadedAt?.trim()) {
    throw new Error(`Manifest ${blobName} is missing uploadedAt.`);
  }

  return {
    id: manifest.id.trim(),
    name: manifest.name.trim(),
    kind: "uploaded",
    blobPath: manifest.blobPath.trim(),
    uploadedAt: manifest.uploadedAt.trim(),
  };
}

function buildUploadedManifestPath(prefix: string, id: string): string {
  return [prefix, `${id}.json`].filter(Boolean).join("/");
}

function buildUploadedCalendarBlobPath(prefix: string, id: string, versionToken: string): string {
  return [prefix, id, `${versionToken}.ics`].filter(Boolean).join("/");
}

function buildPrefix(prefix: string): string | undefined {
  return prefix ? `${prefix}/` : undefined;
}

function buildBlobUrl(storageAccount: string, container: string, blobPath: string): string {
  return `https://${storageAccount}.blob.core.windows.net/${container}/${blobPath}`;
}

function buildVersionToken(uploadedAt: string): string {
  const timestamp = uploadedAt.replace(/[-:.TZ]/g, "");
  const suffix = randomUUID().replace(/-/g, "").slice(0, 12);
  return `${timestamp}-${suffix}`;
}

function getAzureStatusCode(error: unknown): number | undefined {
  if (typeof error === "object" && error !== null && "statusCode" in error) {
    const statusCode = (error as { statusCode?: unknown }).statusCode;
    if (typeof statusCode === "number") {
      return statusCode;
    }
  }

  return undefined;
}

function isConflictStatus(error: unknown): boolean {
  const statusCode = getAzureStatusCode(error);
  return statusCode === 409 || statusCode === 412;
}

function isMissingStatus(error: unknown): boolean {
  return getAzureStatusCode(error) === 404;
}

async function streamToString(stream: NodeJS.ReadableStream | null | undefined): Promise<string> {
  if (!stream) {
    return "";
  }

  const chunks: Buffer[] = [];
  for await (const chunk of stream) {
    chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
  }

  return Buffer.concat(chunks).toString("utf8");
}
