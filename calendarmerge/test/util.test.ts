import { describe, expect, it } from "vitest";

import { readTextStreamWithLimit } from "../src/lib/util";

describe("stream limits", () => {
  it("reads text content when the stream stays within the configured limit", async () => {
    const stream = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(new TextEncoder().encode("BEGIN:VCALENDAR"));
        controller.close();
      },
    });

    await expect(readTextStreamWithLimit(stream, 64, "Request body")).resolves.toBe("BEGIN:VCALENDAR");
  });

  it("rejects text content that exceeds the configured limit", async () => {
    const stream = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(new TextEncoder().encode("123456"));
        controller.close();
      },
    });

    await expect(readTextStreamWithLimit(stream, 4, "Request body")).rejects.toThrow(/configured limit/);
  });
});
