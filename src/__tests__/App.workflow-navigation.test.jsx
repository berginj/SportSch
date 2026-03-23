import { describe, it, expect, vi, beforeEach } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import App from "../App";
import { useSession } from "../lib/useSession";
import * as api from "../lib/api";

vi.mock("../lib/useSession", () => ({
  useSession: vi.fn(),
}));

vi.mock("../lib/api", () => ({
  apiFetch: vi.fn(),
}));

vi.mock("../lib/telemetry", () => ({
  trackPageView: vi.fn(),
}));

vi.mock("../components/TopNav", () => ({
  default: () => <div data-testid="topnav" />,
}));

vi.mock("../components/KeyboardShortcutsModal", () => ({
  default: () => null,
}));

vi.mock("../components/Toast", () => ({
  default: () => null,
}));

vi.mock("../components/Dialogs", () => ({
  ConfirmDialog: () => null,
  PromptDialog: () => null,
}));

vi.mock("../lib/useDialogs", () => ({
  useConfirmDialog: () => ({
    confirmState: null,
    requestConfirm: vi.fn(),
    handleConfirm: vi.fn(),
    handleCancel: vi.fn(),
  }),
  usePromptDialog: () => ({
    promptState: null,
    promptValue: "",
    setPromptValue: vi.fn(),
    requestPrompt: vi.fn(),
    handleConfirm: vi.fn(),
    handleCancel: vi.fn(),
  }),
}));

vi.mock("../pages/OffersPage", () => ({
  default: () => <div>OFFERS_PAGE</div>,
}));

vi.mock("../pages/CalendarPage", () => ({
  default: () => <div>CALENDAR_PAGE</div>,
}));

vi.mock("../pages/ManagePage", () => ({
  default: () => <div>MANAGE_PAGE</div>,
}));

vi.mock("../pages/AdminPage", () => ({
  default: () => <div>ADMIN_PAGE</div>,
}));

function createMatchMedia(matches = false) {
  return vi.fn().mockImplementation((query) => ({
    matches,
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  }));
}

function mockSession({ role = "Coach", isGlobalAdmin = false, hash = "#home" } = {}) {
  window.history.replaceState({}, "", "/");
  window.location.hash = hash;

  useSession.mockReturnValue({
    me: {
      userId: "user-1",
      email: "user@example.com",
      memberships: [{ leagueId: "league-1", role, team: { division: "AAA", teamId: "TEAM-1" } }],
      isGlobalAdmin,
    },
    memberships: [{ leagueId: "league-1", role, team: { division: "AAA", teamId: "TEAM-1" } }],
    leagueId: "league-1",
    setLeagueId: vi.fn(),
    refreshMe: vi.fn(),
  });
}

function mockHomeApi() {
  api.apiFetch.mockImplementation((path) => {
    const url = String(path || "");
    if (url === "/api/league") return Promise.resolve({});
    if (url === "/api/divisions") return Promise.resolve([{ code: "AAA", name: "10U" }]);
    if (url.startsWith("/api/slots?")) return Promise.resolve({ items: [], continuationToken: "" });
    if (url.startsWith("/api/events?")) return Promise.resolve([]);
    if (url.startsWith("/api/accessrequests?status=Pending")) return Promise.resolve([]);
    if (url === "/api/coach/dashboard") {
      return Promise.resolve({
        team: {
          division: "AAA",
          teamId: "TEAM-1",
          name: "Blue Waves",
        },
        upcomingGames: [
          {
            slotId: "confirmed-game",
            gameDate: "2099-01-15",
            startTime: "18:00",
            fieldKey: "FIELD-1",
            displayName: "Main Field",
            homeTeamId: "TEAM-1",
            awayTeamId: "TEAM-2",
          },
        ],
        openOffersInDivision: 1,
        myOpenOffers: 1,
      });
    }
    throw new Error(`Unexpected apiFetch call: ${url}`);
  });
}

function mockOnboardingApi() {
  api.apiFetch.mockImplementation((path) => {
    const url = String(path || "");
    if (url === "/api/teams?division=AAA") {
      return Promise.resolve([
        {
          division: "AAA",
          teamId: "TEAM-1",
          name: "Blue Waves",
          clinicPreference: "weekday-evenings",
          primaryContact: {
            name: "Coach Blue",
            email: "coach@example.com",
          },
          assistantCoaches: [],
          onboardingComplete: false,
        },
      ]);
    }
    if (url === "/api/field-inventory/practice/coach") {
      return Promise.resolve({
        division: "AAA",
        teamId: "TEAM-1",
        requests: [],
      });
    }
    if (url.startsWith("/api/slots?division=AAA&status=Confirmed")) {
      return Promise.resolve({
        items: [
          {
            slotId: "slot-1",
            gameDate: "2026-04-12",
            startTime: "18:00",
            displayName: "Gunston > Turf",
            homeTeamId: "TEAM-1",
            awayTeamId: "TEAM-2",
          },
        ],
        continuationToken: "",
        pageSize: 1,
      });
    }
    throw new Error(`Unexpected apiFetch call: ${url}`);
  });
}

function expectReplaceStateCallContaining(replaceStateSpy, fragment) {
  expect(
    replaceStateSpy.mock.calls.some((call) => String(call[2] || "").includes(fragment))
  ).toBe(true);
}

describe("App workflow navigation", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.matchMedia = createMatchMedia(false);
  });

  it("routes coach dashboard offer actions into the offers workspace with filters", async () => {
    mockSession({ role: "Coach", hash: "#home" });
    mockHomeApi();
    const replaceStateSpy = vi.spyOn(window.history, "replaceState");

    render(<App />);

    fireEvent.click(await screen.findByRole("button", { name: "Offer a Game Slot" }));

    expect(await screen.findByText("OFFERS_PAGE")).toBeInTheDocument();
    expectReplaceStateCallContaining(replaceStateSpy, "division=AAA");
    expectReplaceStateCallContaining(replaceStateSpy, "slotType=offer");
    expectReplaceStateCallContaining(replaceStateSpy, "#offers");
  });

  it("routes coach dashboard browse actions into the filtered calendar workspace", async () => {
    mockSession({ role: "Coach", hash: "#home" });
    mockHomeApi();
    const replaceStateSpy = vi.spyOn(window.history, "replaceState");

    render(<App />);

    fireEvent.click(await screen.findByRole("button", { name: "Browse Available Slots" }));

    expect(await screen.findByText("CALENDAR_PAGE")).toBeInTheDocument();
    expectReplaceStateCallContaining(replaceStateSpy, "division=AAA");
    expectReplaceStateCallContaining(replaceStateSpy, "status=Open");
    expectReplaceStateCallContaining(replaceStateSpy, "slotType=offer");
    expectReplaceStateCallContaining(replaceStateSpy, "#calendar");
  });

  it("routes homepage admin shortcuts into the access requests section", async () => {
    mockSession({ role: "LeagueAdmin", hash: "#home" });
    mockHomeApi();
    const replaceStateSpy = vi.spyOn(window.history, "replaceState");

    render(<App />);

    fireEvent.click(await screen.findByRole("button", { name: "Access requests" }));

    expect(await screen.findByText("ADMIN_PAGE")).toBeInTheDocument();
    expectReplaceStateCallContaining(replaceStateSpy, "adminSection=access-requests");
    expectReplaceStateCallContaining(replaceStateSpy, "#admin");
  });

  it("routes homepage admin shortcuts into the manage settings section", async () => {
    mockSession({ role: "LeagueAdmin", hash: "#home" });
    mockHomeApi();
    const replaceStateSpy = vi.spyOn(window.history, "replaceState");

    render(<App />);

    fireEvent.click(await screen.findByRole("button", { name: "Teams and coaches" }));

    expect(await screen.findByText("MANAGE_PAGE")).toBeInTheDocument();
    expectReplaceStateCallContaining(replaceStateSpy, "manageTab=settings");
    expectReplaceStateCallContaining(replaceStateSpy, "#manage");
  });

  it("routes coach onboarding schedule handoff into the filtered calendar view", async () => {
    mockSession({ role: "Coach", hash: "#coach-setup" });
    mockOnboardingApi();
    const replaceStateSpy = vi.spyOn(window.history, "replaceState");

    render(<App />);

    fireEvent.click(await screen.findByRole("button", { name: "View Full Schedule" }));

    await waitFor(() => expect(screen.getByText("CALENDAR_PAGE")).toBeInTheDocument());
    expectReplaceStateCallContaining(replaceStateSpy, "division=AAA");
    expectReplaceStateCallContaining(replaceStateSpy, "status=Confirmed");
    expectReplaceStateCallContaining(replaceStateSpy, "teamId=TEAM-1");
    expectReplaceStateCallContaining(replaceStateSpy, "#calendar");
  });
});
