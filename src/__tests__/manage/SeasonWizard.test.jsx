import { describe, it, expect, beforeEach, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import SeasonWizard from "../../manage/SeasonWizard";
import * as api from "../../lib/api";

vi.mock("../../lib/api", () => ({
  apiFetch: vi.fn(),
}));

vi.mock("../../lib/telemetry", () => ({
  trackEvent: vi.fn(),
}));

const BASE_PREVIEW = {
  summary: {
    teamCount: 2,
    totalSlots: 1,
    regularSeason: {
      phase: "Regular Season",
      slotsTotal: 1,
      matchupsTotal: 1,
      slotsAssigned: 1,
      unassignedMatchups: 0,
    },
    poolPlay: {
      phase: "Pool Play",
      slotsTotal: 0,
      matchupsTotal: 0,
      slotsAssigned: 0,
      unassignedMatchups: 0,
    },
    bracket: {
      phase: "Bracket",
      slotsTotal: 0,
      matchupsTotal: 0,
      slotsAssigned: 0,
      unassignedMatchups: 0,
    },
  },
  assignments: [
    {
      phase: "Regular Season",
      slotId: "slot-1",
      gameDate: "2026-04-07",
      startTime: "18:00",
      endTime: "20:00",
      fieldKey: "FIELD-1",
      homeTeamId: "TEAM-1",
      awayTeamId: "TEAM-2",
    },
  ],
  warnings: [],
  issues: [],
  totalIssues: 0,
  repairProposals: [],
  applyBlocked: false,
  ruleHealth: {
    status: "green",
    hardViolationCount: 0,
    softViolationCount: 0,
    softScore: 0,
    groups: [],
  },
  constructionStrategy: "test_strategy_v1",
  seed: 7,
};

function installLocalStorageMock() {
  const store = new Map();
  localStorage.getItem.mockImplementation((key) => (store.has(key) ? store.get(key) : null));
  localStorage.setItem.mockImplementation((key, value) => {
    store.set(key, String(value));
  });
  localStorage.removeItem.mockImplementation((key) => {
    store.delete(key);
  });
  localStorage.clear.mockImplementation(() => {
    store.clear();
  });
  return store;
}

function installApiMock({ previewResponse = BASE_PREVIEW } = {}) {
  api.apiFetch.mockImplementation((path) => {
    const url = String(path || "");
    if (url === "/api/divisions") {
      return Promise.resolve([{ code: "U12", name: "Under 12" }]);
    }
    if (url === "/api/league") {
      return Promise.resolve({
        season: {
          springStart: "2026-04-01",
          springEnd: "2026-06-30",
        },
      });
    }
    if (url.startsWith("/api/teams?")) {
      return Promise.resolve([
        { teamId: "TEAM-1", name: "Team 1" },
        { teamId: "TEAM-2", name: "Team 2" },
      ]);
    }
    if (url.startsWith("/api/slots?")) {
      return Promise.resolve([
        {
          slotId: "avail-1",
          isAvailability: true,
          gameDate: "2026-04-07",
          startTime: "18:00",
          endTime: "20:00",
          fieldKey: "FIELD-1",
          allocationSlotType: "game",
          allocationPriorityRank: 1,
        },
      ]);
    }
    if (url === "/api/schedule/wizard/feasibility") {
      return Promise.resolve({
        conflicts: [],
        recommendations: {
          message: "Capacity fits current settings.",
          utilizationStatus: "Balanced",
          minGamesPerTeam: 1,
          maxGamesPerTeam: 1,
          optimalGuestGamesPerWeek: 0,
        },
        capacity: {
          requiredRegularSlots: 1,
          availableRegularSlots: 1,
          guestSlotsReserved: 0,
          effectiveSlotsRemaining: 1,
        },
      });
    }
    if (url === "/api/schedule/wizard/preview") {
      return Promise.resolve(previewResponse);
    }
    throw new Error(`Unexpected apiFetch call: ${url}`);
  });
}

async function renderWizard() {
  render(<SeasonWizard leagueId="league-1" />);
  const seasonStartInput = await screen.findByLabelText(/Season start/i);
  await waitFor(() => expect(seasonStartInput).toHaveValue("2026-04-01"));
  return { seasonStartInput };
}

async function advanceToPreview() {
  await renderWizard();

  fireEvent.click(screen.getByRole("button", { name: "Next" }));
  await waitFor(() => expect(screen.getByText("Postseason windows")).toBeInTheDocument());

  fireEvent.click(screen.getByRole("button", { name: "Next" }));
  await waitFor(() => expect(screen.getByText("Weekly availability view")).toBeInTheDocument());
  await waitFor(() => expect(screen.getByRole("button", { name: "Next" })).not.toBeDisabled());

  fireEvent.click(screen.getByRole("button", { name: "Next" }));
  await waitFor(() => expect(screen.getByText("Game targets & weekly limits")).toBeInTheDocument());
  await waitFor(() => expect(screen.getByRole("button", { name: "Preview schedule" })).not.toBeDisabled());

  fireEvent.click(screen.getByRole("button", { name: "Preview schedule" }));
  await waitFor(() => expect(screen.getByText("Preview overview")).toBeInTheDocument());
}

describe("SeasonWizard", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    installLocalStorageMock();
    installApiMock();
  });

  it("blocks forward step navigation until the current step is valid", async () => {
    const { seasonStartInput } = await renderWizard();

    fireEvent.change(seasonStartInput, { target: { value: "" } });
    fireEvent.click(screen.getByRole("button", { name: "2. Postseason" }));

    expect(screen.getByText("Season basics")).toBeInTheDocument();
    expect(screen.getByText(/Finish the current requirements before moving ahead\./)).toBeInTheDocument();
    expect(screen.getByText(/Basics: Season start\/end must be YYYY-MM-DD\./)).toBeInTheDocument();
  });

  it("supports expanding and collapsing all preview review sections", async () => {
    await advanceToPreview();

    expect(screen.getByText("Scheduling context")).toBeInTheDocument();
    expect(screen.queryByText("Rule Health")).not.toBeInTheDocument();
    expect(screen.queryByText("Game explainability (preview)")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Collapse all" }));

    await waitFor(() => {
      expect(screen.queryByText("Scheduling context")).not.toBeInTheDocument();
      expect(screen.queryByText("Rule Health")).not.toBeInTheDocument();
      expect(screen.queryByText("Game explainability (preview)")).not.toBeInTheDocument();
    });

    expect(localStorage.getItem("collapsible-season-wizard-preview-health")).toBe("false");
    expect(localStorage.getItem("collapsible-season-wizard-preview-coverage")).toBe("false");
    expect(localStorage.getItem("collapsible-season-wizard-preview-assignments")).toBe("false");

    fireEvent.click(screen.getByRole("button", { name: "Expand all" }));

    await waitFor(() => {
      expect(screen.getByText("Rule Health")).toBeInTheDocument();
      expect(screen.getByText("Scheduling context")).toBeInTheDocument();
      expect(screen.getByText("Game explainability (preview)")).toBeInTheDocument();
    });

    expect(localStorage.getItem("collapsible-season-wizard-preview-health")).toBe("true");
    expect(localStorage.getItem("collapsible-season-wizard-preview-coverage")).toBe("true");
    expect(localStorage.getItem("collapsible-season-wizard-preview-assignments")).toBe("true");
  });
});
