import { describe, it, expect, beforeEach, vi } from "vitest";
import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
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
const MULTI_ASSIGNMENT_PREVIEW = {
  ...BASE_PREVIEW,
  summary: {
    teamCount: 2,
    totalSlots: 2,
    regularSeason: {
      phase: "Regular Season",
      slotsTotal: 1,
      matchupsTotal: 1,
      slotsAssigned: 1,
      unassignedMatchups: 0,
    },
    poolPlay: {
      phase: "Pool Play",
      slotsTotal: 1,
      matchupsTotal: 1,
      slotsAssigned: 1,
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
    ...BASE_PREVIEW.assignments,
    {
      phase: "Pool Play",
      slotId: "slot-2",
      gameDate: "2026-05-01",
      startTime: "17:30",
      endTime: "19:00",
      fieldKey: "FIELD-2",
      homeTeamId: "TEAM-2",
      awayTeamId: "TEAM-1",
    },
  ],
};
const SEQUENTIAL_SLOT_AVAILABILITY = [
  {
    slotId: "avail-1",
    isAvailability: true,
    gameDate: "2026-04-06",
    startTime: "18:00",
    endTime: "19:30",
    fieldKey: "FIELD-1",
    allocationSlotType: "practice",
    allocationPriorityRank: "",
  },
  {
    slotId: "avail-2",
    isAvailability: true,
    gameDate: "2026-04-06",
    startTime: "19:30",
    endTime: "21:00",
    fieldKey: "FIELD-1",
    allocationSlotType: "practice",
    allocationPriorityRank: "",
  },
];

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

function installApiMock({ previewResponse = BASE_PREVIEW, previewError = null, slotsResponse } = {}) {
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
      return Promise.resolve(slotsResponse || [
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
      if (previewError) {
        return Promise.reject(previewError);
      }
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

async function advanceToSlotPlan() {
  await renderWizard();

  fireEvent.click(screen.getByRole("button", { name: "Next" }));
  await waitFor(() => expect(screen.getByText("Postseason windows")).toBeInTheDocument());

  fireEvent.click(screen.getByRole("button", { name: "Next" }));
  await waitFor(() => expect(screen.getByText("Weekly availability view")).toBeInTheDocument());
}

async function advanceToRules() {
  await advanceToSlotPlan();

  await waitFor(() => expect(screen.getByRole("button", { name: "Next" })).not.toBeDisabled());
  fireEvent.click(screen.getByRole("button", { name: "Next" }));
  await waitFor(() => expect(screen.getByText("Game targets & weekly limits")).toBeInTheDocument());
  await waitFor(() => expect(screen.getByRole("button", { name: "Preview schedule" })).not.toBeDisabled());
}

async function advanceToPreview() {
  await advanceToRules();

  fireEvent.click(screen.getByRole("button", { name: "Preview schedule" }));
  await waitFor(() => expect(screen.getByText("Preview overview")).toBeInTheDocument());
}

describe("SeasonWizard", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.history.replaceState({}, "", "/");
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

  it("defaults postseason dates from the season range and uses date pickers", async () => {
    const { seasonStartInput } = await renderWizard();
    const seasonEndInput = screen.getByLabelText(/Season end/i);

    expect(seasonStartInput).toHaveAttribute("type", "date");
    expect(seasonEndInput).toHaveAttribute("type", "date");

    fireEvent.click(screen.getByRole("button", { name: "Next" }));

    const poolStartInput = await screen.findByLabelText(/Pool play start/i);
    const poolEndInput = screen.getByLabelText(/Pool play end/i);
    const championshipStartInput = screen.getByLabelText(/Championship start/i);
    const championshipEndInput = screen.getByLabelText(/Championship end/i);

    expect(poolStartInput).toHaveAttribute("type", "date");
    expect(poolEndInput).toHaveAttribute("type", "date");
    expect(championshipStartInput).toHaveAttribute("type", "date");
    expect(championshipEndInput).toHaveAttribute("type", "date");

    expect(poolStartInput).toHaveValue("2026-06-21");
    expect(poolEndInput).toHaveValue("2026-06-26");
    expect(championshipStartInput).toHaveValue("2026-06-27");
    expect(championshipEndInput).toHaveValue("2026-06-28");
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

  it("applies rule presets to the rules inputs", async () => {
    await advanceToRules();

    expect(screen.queryByText("Preferred weeknights")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Max games" }));

    expect(screen.getByLabelText(/No doubleheaders/i)).not.toBeChecked();
    expect(screen.getByLabelText(/Balance home\/away/i)).not.toBeChecked();

    fireEvent.click(screen.getByRole("button", { name: "Conservative" }));

    expect(screen.getByLabelText(/Max games per team per week/i)).toHaveValue(1);
    expect(screen.getByLabelText(/No doubleheaders/i)).toBeChecked();
    expect(screen.getByLabelText(/Balance home\/away/i)).toBeChecked();
  });

  it("filters preview assignments by phase and field", async () => {
    installApiMock({ previewResponse: MULTI_ASSIGNMENT_PREVIEW });

    await advanceToPreview();
    fireEvent.click(screen.getByRole("button", { name: "Expand all" }));

    await waitFor(() => expect(screen.getByText("Game explainability (preview)")).toBeInTheDocument());

    fireEvent.change(screen.getByLabelText("Phase filter"), { target: { value: "Pool Play" } });
    const assignmentsTable = screen.getByRole("table", { name: "Preview assignments" });

    expect(within(assignmentsTable).queryByText("2026-04-07")).not.toBeInTheDocument();
    expect(within(assignmentsTable).getByText("2026-05-01")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Field filter"), { target: { value: "FIELD-1" } });

    expect(within(assignmentsTable).getByText("No assignments match the current filters.")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Clear filters" }));

    expect(within(assignmentsTable).getByText("2026-04-07")).toBeInTheDocument();
    expect(within(assignmentsTable).getByText("2026-05-01")).toBeInTheDocument();
  });

  it("keeps rules step unblocked when preview fetch fails", async () => {
    installApiMock({ previewError: new Error("Failed to fetch") });

    await advanceToRules();
    fireEvent.click(screen.getByRole("button", { name: "Preview schedule" }));

    await waitFor(() => {
      expect(screen.getByText("Preview request failed. The scheduler service did not respond.")).toBeInTheDocument();
    });

    expect(screen.getByText("No blocking issues detected for this step.")).toBeInTheDocument();
    expect(screen.getByText("In progress")).toBeInTheDocument();
  });

  it("shows pattern and field totals and links to availability setup", async () => {
    installApiMock({ slotsResponse: SEQUENTIAL_SLOT_AVAILABILITY });

    await advanceToSlotPlan();

    expect(screen.getAllByText("Pattern: 1").length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText("Field total: 2").length).toBeGreaterThanOrEqual(2);

    const replaceStateSpy = vi.spyOn(window.history, "replaceState");
    try {
      fireEvent.click(screen.getByRole("button", { name: "Open availability setup" }));

      expect(replaceStateSpy).toHaveBeenCalled();
      const nextPath = replaceStateSpy.mock.calls.at(-1)?.[2];
      expect(nextPath).toContain("manageTab=fields");
      expect(nextPath).toContain("#main-content");
      expect(localStorage.getItem("collapsible-manage-availability-setup")).toBe("true");
      expect(localStorage.getItem("collapsible-manage-fields-import")).toBe("false");
    } finally {
      replaceStateSpy.mockRestore();
    }
  });

  it("pushes later slot times when a pattern is expanded to game length", async () => {
    installApiMock({ slotsResponse: SEQUENTIAL_SLOT_AVAILABILITY });

    await advanceToSlotPlan();

    fireEvent.click(screen.getAllByRole("button", { name: "Game 120m" })[0]);

    await waitFor(() => {
      expect(screen.queryByDisplayValue("21:00")).not.toBeInTheDocument();
      expect(screen.getByDisplayValue("21:30")).toBeInTheDocument();
    });

    expect(screen.getAllByDisplayValue("20:00").length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText(/shifted 1 later pattern/i)).toBeInTheDocument();
  });
});
