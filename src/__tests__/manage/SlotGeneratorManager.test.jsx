import { describe, it, expect, beforeEach, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import SlotGeneratorManager from "../../manage/SlotGeneratorManager";
import * as api from "../../lib/api";

vi.mock("../../lib/api", () => ({
  apiFetch: vi.fn(),
}));

vi.mock("../../lib/telemetry", () => ({
  trackEvent: vi.fn(),
}));

const DIVISIONS = [
  { code: "10U", name: "10U" },
];

const FIELDS = [
  { fieldKey: "PARK1/F1", displayName: "Park 1 > Field 1" },
  { fieldKey: "PARK1/F2", displayName: "Park 1 > Field 2" },
];

const LEAGUE = {
  season: {
    springStart: "2026-03-01",
    springEnd: "2026-06-30",
  },
};

const AVAILABILITY_SLOTS = [
  {
    slotId: "slot-1",
    division: "10U",
    gameDate: "2026-04-06",
    startTime: "18:00",
    endTime: "20:00",
    fieldKey: "PARK1/F1",
    displayName: "Park 1 > Field 1",
    isAvailability: true,
  },
  {
    slotId: "slot-2",
    division: "10U",
    gameDate: "2026-04-20",
    startTime: "18:00",
    endTime: "20:00",
    fieldKey: "PARK1/F1",
    displayName: "Park 1 > Field 1",
    isAvailability: true,
  },
  {
    slotId: "slot-3",
    division: "10U",
    gameDate: "2026-04-08",
    startTime: "17:00",
    endTime: "19:00",
    fieldKey: "PARK1/F2",
    displayName: "Park 1 > Field 2",
    isAvailability: true,
  },
];

function installApiMock() {
  api.apiFetch.mockImplementation((url, options = {}) => {
    if (url === "/api/divisions") return Promise.resolve(DIVISIONS);
    if (url === "/api/fields") return Promise.resolve(FIELDS);
    if (url === "/api/league") return Promise.resolve(LEAGUE);
    if (String(url).startsWith("/api/availability-slots?")) {
      return Promise.resolve({ items: AVAILABILITY_SLOTS });
    }
    if (String(url).startsWith("/api/slots/") && options?.method === "PATCH") {
      return Promise.resolve({ ok: true });
    }
    throw new Error(`Unexpected apiFetch call: ${url}`);
  });
}

async function renderManager() {
  render(<SlotGeneratorManager leagueId="league-1" />);
  await screen.findByText("Recurring availability view");
}

describe("SlotGeneratorManager", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    installApiMock();
  });

  it("groups availability slots into recurring patterns and shows interruptions", async () => {
    await renderManager();

    expect(screen.getByText("Count: 2")).toBeInTheDocument();
    expect(screen.getByText("Interruptions: 1")).toBeInTheDocument();

    fireEvent.click(screen.getAllByRole("button", { name: "Occurrences & interruptions" })[0]);

    expect(screen.getByText("2026-04-13")).toBeInTheDocument();
  });

  it("applies recurring edits to every slot in the pattern", async () => {
    await renderManager();

    fireEvent.change(screen.getByLabelText("Start time for Mon PARK1/F1 10U"), {
      target: { value: "18:30" },
    });
    fireEvent.click(screen.getAllByRole("button", { name: "Save recurring changes" })[0]);

    await waitFor(() => {
      const patchCalls = api.apiFetch.mock.calls.filter(
        ([url, options]) => String(url).startsWith("/api/slots/10U/") && options?.method === "PATCH"
      );
      expect(patchCalls).toHaveLength(2);
    });

    const patchBodies = api.apiFetch.mock.calls
      .filter(([url, options]) => String(url).startsWith("/api/slots/10U/") && options?.method === "PATCH")
      .map(([, options]) => JSON.parse(options.body));

    expect(patchBodies.every((body) => body.startTime === "18:30")).toBe(true);
    expect(screen.getAllByText("Updated 2 recurring availability slot(s).").length).toBeGreaterThanOrEqual(1);
  });

  it("removes every occurrence in a recurring pattern", async () => {
    const confirmSpy = vi.spyOn(window, "confirm").mockReturnValue(true);
    try {
      await renderManager();

      fireEvent.click(screen.getAllByRole("button", { name: "Remove recurring pattern" })[0]);

      await waitFor(() => {
        const cancelCalls = api.apiFetch.mock.calls.filter(
          ([url, options]) => String(url).includes("/cancel") && options?.method === "PATCH"
        );
        expect(cancelCalls).toHaveLength(2);
      });

      expect(screen.getAllByText("Removed 2 recurring availability slot(s).").length).toBeGreaterThanOrEqual(1);
    } finally {
      confirmSpy.mockRestore();
    }
  });
});
