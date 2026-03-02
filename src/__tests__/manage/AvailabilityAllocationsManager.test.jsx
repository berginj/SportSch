import { describe, it, expect, beforeEach, vi } from "vitest";
import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import AvailabilityAllocationsManager from "../../manage/AvailabilityAllocationsManager";
import * as api from "../../lib/api";

vi.mock("../../lib/api", () => ({
  apiFetch: vi.fn(),
}));

vi.mock("../../lib/telemetry", () => ({
  trackEvent: vi.fn(),
}));

const DIVISIONS = [{ code: "10U", name: "10U" }];
const FIELDS = [{ fieldKey: "PARK1/F1", displayName: "Park 1 > Field 1" }];
const LEAGUE = {
  season: {
    springStart: "2026-03-01",
    springEnd: "2026-06-30",
  },
};

function installApiMock() {
  let currentAllocations = [
    {
      allocationId: "alloc-1",
      scope: "10U",
      fieldKey: "PARK1/F1",
      division: "10U",
      startsOn: "2026-03-14",
      endsOn: "2026-06-10",
      daysOfWeek: ["Mon", "Wed"],
      startTimeLocal: "18:00",
      endTimeLocal: "20:00",
      slotType: "game",
      priorityRank: 2,
      notes: "League note",
      isActive: true,
    },
  ];

  api.apiFetch.mockImplementation((url, options = {}) => {
    if (url === "/api/divisions") return Promise.resolve(DIVISIONS);
    if (url === "/api/fields") return Promise.resolve(FIELDS);
    if (url === "/api/league") return Promise.resolve(LEAGUE);
    if (String(url).startsWith("/api/availability/allocations?")) {
      return Promise.resolve(currentAllocations);
    }
    if (url === "/api/availability/allocations/alloc-1" && options?.method === "PATCH") {
      const body = JSON.parse(options.body);
      currentAllocations = currentAllocations.map((row) =>
        row.allocationId === "alloc-1"
          ? {
              ...row,
              scope: body.scope,
              division: body.scope,
              fieldKey: body.fieldKey,
              startsOn: body.startsOn,
              endsOn: body.endsOn,
              daysOfWeek: body.daysOfWeek,
              startTimeLocal: body.startTimeLocal,
              endTimeLocal: body.endTimeLocal,
              slotType: body.slotType,
              priorityRank: body.priorityRank ? Number(body.priorityRank) : null,
              notes: body.notes,
              isActive: !!body.isActive,
            }
          : row
      );
      return Promise.resolve(currentAllocations[0]);
    }
    throw new Error(`Unexpected apiFetch call: ${url}`);
  });
}

describe("AvailabilityAllocationsManager", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    installApiMock();
  });

  it("edits an allocation row from the allocation table", async () => {
    render(<AvailabilityAllocationsManager leagueId="league-1" />);

    await screen.findByText("Allocation list");
    fireEvent.click(screen.getByRole("button", { name: "Load allocations" }));

    await screen.findByText("League note");
    fireEvent.click(screen.getByRole("button", { name: "Edit" }));

    const editor = screen.getByText("Edit allocation").closest(".callout");
    expect(editor).not.toBeNull();

    fireEvent.change(within(editor).getByLabelText("Start time"), {
      target: { value: "18:30" },
    });
    fireEvent.change(within(editor).getByLabelText("Notes"), {
      target: { value: "Updated note" },
    });
    fireEvent.click(within(editor).getByRole("button", { name: "Save allocation" }));

    await waitFor(() => {
      expect(api.apiFetch).toHaveBeenCalledWith(
        "/api/availability/allocations/alloc-1",
        expect.objectContaining({ method: "PATCH" })
      );
    });

    const patchCall = api.apiFetch.mock.calls.find(
      ([url, options]) => url === "/api/availability/allocations/alloc-1" && options?.method === "PATCH"
    );
    const patchBody = JSON.parse(patchCall[1].body);

    expect(patchBody.startTimeLocal).toBe("18:30");
    expect(patchBody.notes).toBe("Updated note");
    expect(await screen.findByText("Updated note")).toBeInTheDocument();
    expect(screen.getAllByText("Allocation updated.").length).toBeGreaterThanOrEqual(1);
  });
});
