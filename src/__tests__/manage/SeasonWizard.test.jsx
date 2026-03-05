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
const ODD_TEAM_PREVIEW = {
  ...BASE_PREVIEW,
  summary: {
    teamCount: 5,
    totalSlots: 2,
    regularSeason: {
      phase: "Regular Season",
      slotsTotal: 2,
      matchupsTotal: 3,
      slotsAssigned: 2,
      unassignedMatchups: 1,
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
      isExternalOffer: false,
    },
    {
      phase: "Regular Season",
      slotId: "slot-2",
      gameDate: "2026-04-07",
      startTime: "20:00",
      endTime: "22:00",
      fieldKey: "FIELD-1",
      homeTeamId: "TEAM-3",
      awayTeamId: "TEAM-4",
      isExternalOffer: false,
    },
  ],
  unassignedSlots: [],
  unassignedMatchups: [
    {
      phase: "Regular Season",
      homeTeamId: "TEAM-5",
      awayTeamId: "TEAM-1",
    },
  ],
};
const ODD_TEAM_GUEST_PREVIEW = {
  ...BASE_PREVIEW,
  summary: {
    teamCount: 5,
    totalSlots: 4,
    regularSeason: {
      phase: "Regular Season",
      slotsTotal: 4,
      matchupsTotal: 2,
      slotsAssigned: 4,
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
      isExternalOffer: false,
    },
    {
      phase: "Regular Season",
      slotId: "slot-2",
      gameDate: "2026-04-07",
      startTime: "20:00",
      endTime: "22:00",
      fieldKey: "FIELD-1",
      homeTeamId: "TEAM-3",
      awayTeamId: "TEAM-4",
      isExternalOffer: false,
    },
    {
      phase: "Regular Season",
      slotId: "slot-3",
      gameDate: "2026-04-07",
      startTime: "18:00",
      endTime: "20:00",
      fieldKey: "FIELD-2",
      homeTeamId: "TEAM-5",
      awayTeamId: "",
      isExternalOffer: true,
    },
    {
      phase: "Regular Season",
      slotId: "slot-4",
      gameDate: "2026-04-07",
      startTime: "20:00",
      endTime: "22:00",
      fieldKey: "FIELD-2",
      homeTeamId: "TEAM-1",
      awayTeamId: "",
      isExternalOffer: true,
    },
  ],
  unassignedSlots: [],
  unassignedMatchups: [],
};
const SINGLE_MISSING_MATCHUP_PREVIEW = {
  ...BASE_PREVIEW,
  summary: {
    ...BASE_PREVIEW.summary,
    teamCount: 9,
  },
  issues: [
    {
      phase: "Regular Season",
      ruleId: "unscheduled-required-matchups",
      severity: "error",
      message: "1 required matchup(s) could not be assigned.",
      details: { count: 1 },
    },
  ],
  totalIssues: 1,
  applyBlocked: true,
  ruleHealth: {
    status: "red",
    hardViolationCount: 1,
    softViolationCount: 0,
    softScore: 980,
    groups: [
      {
        ruleId: "unscheduled-required-matchups",
        severity: "hard",
        count: 1,
        summary: "1 required matchup(s) could not be assigned.",
        violations: [
          {
            ruleId: "unscheduled-required-matchups",
            severity: "hard",
            message: "1 required matchup(s) could not be assigned.",
            teamIds: ["TEAM-1", "TEAM-2"],
            slotIds: [],
            weekKeys: [],
            details: { count: 1 },
          },
        ],
        smallestAffectedSet: {
          teams: ["TEAM-1", "TEAM-2"],
          slots: [],
          weeks: [],
          teamCount: 2,
          slotCount: 0,
          weekCount: 0,
        },
      },
    ],
  },
};
const RULE_HINT_PREVIEW = {
  ...BASE_PREVIEW,
  summary: {
    ...BASE_PREVIEW.summary,
    teamCount: 9,
  },
  issues: [
    {
      phase: "Regular Season",
      ruleId: "double-header",
      severity: "error",
      message: "2 violation(s) for rule double-header.",
      details: {
        count: 2,
        primaryViolation: {
          teamId: "TEAM-9",
          gameDate: "2026-05-12",
          count: 2,
        },
      },
    },
    {
      phase: "Regular Season",
      ruleId: "home-away-balance",
      severity: "warning",
      message: "Home/away balance is uneven for 2 team(s).",
      details: {
        count: 1,
        primaryViolation: {
          offenders: [
            { teamId: "TEAM-3", gap: 3 },
            { teamId: "TEAM-6", gap: 2 },
          ],
        },
      },
    },
    {
      phase: "Regular Season",
      ruleId: "idle-gap-balance",
      severity: "warning",
      message: "Long idle gaps detected for 6 team(s).",
      details: {
        count: 1,
        primaryViolation: {
          offenders: [
            { teamId: "TEAM-5", extraGapWeeks: 2 },
          ],
        },
      },
    },
    {
      phase: "Regular Season",
      ruleId: "opponent-repeat-balance",
      severity: "warning",
      message: "18 opponent pairing(s) repeat more than once.",
      details: {
        count: 1,
        primaryViolation: {
          pairs: [
            { pairKey: "TEAM-1|TEAM-2", count: 3 },
          ],
        },
      },
    },
    {
      phase: "Regular Season",
      ruleId: "unused-game-capacity",
      severity: "warning",
      message: "22 game-capable slot(s) were left unused.",
      details: {
        count: 1,
        primaryViolation: {
          count: 22,
        },
      },
    },
  ],
  totalIssues: 5,
  applyBlocked: true,
  constructionStrategy: "backward_greedy_v1",
  ruleHealth: {
    status: "yellow",
    hardViolationCount: 1,
    softViolationCount: 4,
    softScore: 900,
    groups: [],
  },
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

function installApiMock({ previewResponse = BASE_PREVIEW, previewResponses = null, previewError = null, slotsResponse, teamsResponse } = {}) {
  let previewIndex = 0;
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
      return Promise.resolve(teamsResponse || [
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
      if (Array.isArray(previewResponses) && previewResponses.length > 0) {
        const next = previewResponses[Math.min(previewIndex, previewResponses.length - 1)];
        previewIndex += 1;
        return Promise.resolve(next);
      }
      return Promise.resolve(previewResponse);
    }
    if (url === "/api/schedule/wizard/reset-generated") {
      return Promise.resolve({ resetCount: 1 });
    }
    if (url === "/api/schedule/feedback") {
      return Promise.resolve({ recorded: true });
    }
    if (url === "/api/schedule/wizard/apply") {
      return Promise.resolve({ ok: true });
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

  it("makes clear that rerunning the wizard does not clear availability", async () => {
    await renderWizard();

    expect(screen.getByText(/applies the current preview into slots for the selected division and season window/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Reset existing non-practice game, guest, and request slots in this season window before each new Preview run/i)).toBeChecked();
    expect(document.body.textContent).toContain("rerunning Preview first resets existing non-practice game, guest, and request rows");
    expect(document.body.textContent).toContain("does not clear recurring allocations or field blackouts");
  });

  it("resets prior wizard-generated slots before preview when enabled", async () => {
    await advanceToRules();

    fireEvent.click(screen.getByRole("button", { name: "Preview schedule" }));

    await waitFor(() => expect(screen.getByText("Preview overview")).toBeInTheDocument());

    const resetCallIndex = api.apiFetch.mock.calls.findIndex(([path]) => path === "/api/schedule/wizard/reset-generated");
    const previewCallIndex = api.apiFetch.mock.calls.findIndex(([path]) => path === "/api/schedule/wizard/preview");
    expect(resetCallIndex).toBeGreaterThanOrEqual(0);
    expect(previewCallIndex).toBeGreaterThan(resetCallIndex);
  });

  it("sends the reset-before-apply toggle with the apply request", async () => {
    await advanceToPreview();

    const resetToggle = screen.getByLabelText(/Reset existing non-practice game, guest, and request slots in this season window before each new Preview run/i);
    fireEvent.click(resetToggle);
    const resetCallsBeforeApply = api.apiFetch.mock.calls.filter(([path]) => path === "/api/schedule/wizard/reset-generated").length;

    const confirmSpy = vi.spyOn(window, "confirm").mockReturnValue(true);
    try {
      fireEvent.click(screen.getByRole("button", { name: "Apply schedule" }));

      await waitFor(() => {
        expect(api.apiFetch).toHaveBeenCalledWith(
          "/api/schedule/wizard/apply",
          expect.objectContaining({ method: "POST" })
        );
      });

      const applyCall = api.apiFetch.mock.calls.find(([path]) => path === "/api/schedule/wizard/apply");
      const payload = JSON.parse(applyCall?.[1]?.body || "{}");
      expect(payload.resetGeneratedSlotsBeforeApply).toBe(false);
      const resetCallsAfterApply = api.apiFetch.mock.calls.filter(([path]) => path === "/api/schedule/wizard/reset-generated").length;
      expect(resetCallsAfterApply).toBe(resetCallsBeforeApply);
    } finally {
      confirmSpy.mockRestore();
    }
  });

  it("allows apply with warnings when required matchups remain unassigned", async () => {
    installApiMock({ previewResponse: SINGLE_MISSING_MATCHUP_PREVIEW });
    await advanceToPreview();

    const confirmSpy = vi.spyOn(window, "confirm").mockReturnValue(true);
    try {
      fireEvent.click(screen.getByRole("button", { name: "Apply with warnings" }));

      await waitFor(() => {
        expect(api.apiFetch).toHaveBeenCalledWith(
          "/api/schedule/wizard/apply",
          expect.objectContaining({ method: "POST" })
        );
      });

      const applyCall = api.apiFetch.mock.calls.find(([path]) => path === "/api/schedule/wizard/apply");
      const payload = JSON.parse(applyCall?.[1]?.body || "{}");
      expect(payload.allowApplyWithSingleMissingRequiredMatchup).toBeUndefined();
      expect(String(confirmSpy.mock.calls[0]?.[0] || "")).toContain("Includes unresolved warnings/issues shown in preview");
    } finally {
      confirmSpy.mockRestore();
    }
  });

  it("keeps apply available for hard-rule failures and uses warning labeling", async () => {
    installApiMock({ previewResponse: RULE_HINT_PREVIEW });
    await advanceToPreview();

    expect(screen.getByRole("button", { name: "Apply with warnings" })).toBeEnabled();
    expect(screen.getByText(/Applying will keep current warnings\/issues/i)).toBeInTheDocument();
  });

  it("shows apply outcome details and opens calendar with matching filters", async () => {
    await advanceToPreview();

    const confirmSpy = vi.spyOn(window, "confirm").mockReturnValue(true);
    const replaceStateSpy = vi.spyOn(window.history, "replaceState");
    try {
      fireEvent.click(screen.getByRole("button", { name: "Apply schedule" }));

      await waitFor(() => {
        expect(screen.getByText("Apply result")).toBeInTheDocument();
      });
      expect(document.body.textContent).toContain("Assignments written: 1");

      fireEvent.click(screen.getByRole("button", { name: /Open calendar with these filters/i }));

      expect(replaceStateSpy).toHaveBeenCalled();
      const nextPath = replaceStateSpy.mock.calls.at(-1)?.[2];
      expect(nextPath).toContain("#calendar");
      expect(nextPath).toContain("status=Open%2CConfirmed");
      expect(nextPath).toContain("division=U12");
      expect(nextPath).toContain("dateFrom=2026-04-01");
      expect(nextPath).toContain("dateTo=2026-06-30");
    } finally {
      replaceStateSpy.mockRestore();
      confirmSpy.mockRestore();
    }
  });

  it("applies the selected generated schedule option using that option's seed and strategy", async () => {
    installApiMock({
      previewResponses: [
        { ...BASE_PREVIEW, seed: 101, constructionStrategy: "backward_greedy_v1+strict_validation_v2" },
        { ...BASE_PREVIEW, seed: 202, constructionStrategy: "backward_greedy_v1+strict_validation_v2" },
        { ...BASE_PREVIEW, seed: 303, constructionStrategy: "backward_greedy_v1+strict_validation_v2" },
        { ...BASE_PREVIEW, seed: 404, constructionStrategy: "backward_greedy_v1+strict_validation_v2" },
      ],
    });

    await advanceToRules();

    fireEvent.click(screen.getByRole("button", { name: /Generate 4 Options/i }));
    await waitFor(() => expect(screen.getByText(/Schedule Comparison - Pick Your Favorite/i)).toBeInTheDocument());

    fireEvent.click(screen.getByText("Option 2"));
    fireEvent.click(screen.getByRole("button", { name: /Continue with Option 2/i }));
    await waitFor(() => expect(screen.getByText("Preview overview")).toBeInTheDocument());

    const confirmSpy = vi.spyOn(window, "confirm").mockReturnValue(true);
    try {
      fireEvent.click(screen.getByRole("button", { name: "Apply schedule" }));

      await waitFor(() => {
        expect(api.apiFetch).toHaveBeenCalledWith(
          "/api/schedule/wizard/apply",
          expect.objectContaining({ method: "POST" })
        );
      });

      const applyCall = api.apiFetch.mock.calls.find(([path]) => path === "/api/schedule/wizard/apply");
      const payload = JSON.parse(applyCall?.[1]?.body || "{}");
      expect(payload.seed).toBe(202);
      expect(payload.constructionStrategy).toBe("backward_greedy_v1");
    } finally {
      confirmSpy.mockRestore();
    }
  });

  it("sends fixed request games in the preview payload and shows them as locked preview rows", async () => {
    installApiMock({
      previewResponse: {
        ...BASE_PREVIEW,
        assignments: [
          ...BASE_PREVIEW.assignments,
          {
            phase: "Regular Season",
            slotId: "TEAM-1|2026-04-10|17:00|19:00|tournament_main",
            gameDate: "2026-04-10",
            startTime: "17:00",
            endTime: "19:00",
            fieldKey: "tournament/main",
            homeTeamId: "Wildcats",
            awayTeamId: "TEAM-1",
            isExternalOffer: false,
            isRequestGame: true,
            requestGameOpponent: "Wildcats",
          },
        ],
      },
    });

    await advanceToRules();

    fireEvent.click(screen.getByRole("button", { name: /Add request game/i }));
    fireEvent.change(screen.getByLabelText("Request game 1 date"), { target: { value: "2026-04-10" } });
    fireEvent.change(screen.getByLabelText("Request game 1 start"), { target: { value: "17:00" } });
    fireEvent.change(screen.getByLabelText("Request game 1 end"), { target: { value: "19:00" } });
    fireEvent.change(screen.getByLabelText("Request game 1 field"), { target: { value: "tournament/main" } });
    fireEvent.change(screen.getByLabelText("Request game 1 away team"), { target: { value: "TEAM-1" } });
    fireEvent.change(screen.getByLabelText("Request game 1 opponent"), { target: { value: "Wildcats" } });

    fireEvent.click(screen.getByRole("button", { name: "Preview schedule" }));
    await waitFor(() => expect(screen.getByText("Preview overview")).toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: /Expand all/i }));

    const previewCall = api.apiFetch.mock.calls.find(([path]) => path === "/api/schedule/wizard/preview");
    const payload = JSON.parse(previewCall?.[1]?.body || "{}");
    expect(payload.requestGames).toEqual([
      {
        gameDate: "2026-04-10",
        startTime: "17:00",
        endTime: "19:00",
        fieldKey: "tournament/main",
        teamId: "TEAM-1",
        opponentName: "Wildcats",
      },
    ]);

    expect(screen.getAllByText("REQUEST").length).toBeGreaterThan(0);
    expect(screen.getByText("TEAM-1 at Wildcats")).toBeInTheDocument();
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

  it("treats guest anchor slots as exact weekly requirements", async () => {
    await advanceToSlotPlan();

    expect(screen.getByText(/Guest games\/week is reserved before regular scheduling\./i)).toBeInTheDocument();
    expect(screen.getByText(/selected day, time, and field are treated as exact weekly requirements/i)).toBeInTheDocument();
  });

  it("applies rule presets to the rules inputs", async () => {
    await advanceToRules();

    expect(screen.queryByText("Preferred weeknights")).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Max games" }));

    expect(screen.getByLabelText(/No doubleheaders/i)).toBeChecked();
    expect(screen.getByLabelText(/No doubleheaders/i)).toBeDisabled();
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

  it("recommends two guest slots per week for odd-team divisions before preview repairs", async () => {
    installApiMock({
      previewResponse: ODD_TEAM_PREVIEW,
      teamsResponse: [
        { teamId: "TEAM-1", name: "Team 1" },
        { teamId: "TEAM-2", name: "Team 2" },
        { teamId: "TEAM-3", name: "Team 3" },
        { teamId: "TEAM-4", name: "Team 4" },
        { teamId: "TEAM-5", name: "Team 5" },
      ],
    });

    await advanceToRules();
    fireEvent.click(screen.getByRole("button", { name: "Preview schedule" }));
    fireEvent.click(await screen.findByRole("button", { name: "Expand all" }));

    await waitFor(() => {
      expect(screen.getByRole("button", { name: "Set Guest games/week = 2" })).toBeInTheDocument();
    });
    expect(screen.getByText(/odd team count \(5\) creates weekly idle-team pressure unless guest slots absorb it/i)).toBeInTheDocument();
  });

  it("explains that placed guest slots stay locked and recommends a two-game weekly cap", async () => {
    installApiMock({
      previewResponse: ODD_TEAM_GUEST_PREVIEW,
      teamsResponse: [
        { teamId: "TEAM-1", name: "Team 1" },
        { teamId: "TEAM-2", name: "Team 2" },
        { teamId: "TEAM-3", name: "Team 3" },
        { teamId: "TEAM-4", name: "Team 4" },
        { teamId: "TEAM-5", name: "Team 5" },
      ],
    });

    await advanceToRules();
    fireEvent.change(screen.getByLabelText(/Guest games per week/i), { target: { value: "2" } });
    fireEvent.change(screen.getByLabelText(/Max games per team per week/i), { target: { value: "1" } });
    fireEvent.click(screen.getByRole("button", { name: "Preview schedule" }));
    fireEvent.click(await screen.findByRole("button", { name: "Expand all" }));

    await waitFor(() => {
      expect(screen.getByRole("button", { name: "Set Max games/week = 2" })).toBeInTheDocument();
    });
    expect(screen.getByText(/placed guest slots stay locked in preview/i)).toBeInTheDocument();
  });

  it("shows actionable rule hints for odd-team guest scheduling and soft-balance issues", async () => {
    installApiMock({ previewResponse: RULE_HINT_PREVIEW });

    await advanceToPreview();
    fireEvent.click(screen.getByRole("button", { name: "Expand all" }));

    await waitFor(() => {
      expect(screen.getByText(/two guest slots\/week plus Max games\/week at 2 should normally absorb the idle team/i)).toBeInTheDocument();
    });
    expect(screen.getByText(/Worst gap: TEAM-3 is off by 3 home\/away results\./i)).toBeInTheDocument();
    expect(screen.getByText(/Worst idle stretch: TEAM-5 has 2 extra idle weeks/i)).toBeInTheDocument();
    expect(screen.getByText(/Most repeated pairing is TEAM-1 vs TEAM-2 \(3 times\)\./i)).toBeInTheDocument();
    expect(screen.getByText(/Backward loading intentionally pushes regular games later in the season/i)).toBeInTheDocument();
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

  it("pushes later slot times when a pattern duration is expanded", async () => {
    installApiMock({ slotsResponse: SEQUENTIAL_SLOT_AVAILABILITY });

    await advanceToSlotPlan();

    fireEvent.change(screen.getAllByDisplayValue("90")[0], { target: { value: "120" } });

    await waitFor(() => {
      expect(screen.queryByDisplayValue("21:00")).not.toBeInTheDocument();
      expect(screen.getByDisplayValue("21:30")).toBeInTheDocument();
    });

    expect(screen.getAllByDisplayValue("20:00").length).toBeGreaterThanOrEqual(2);
  });
});
