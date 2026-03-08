import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import App from "../App";
import { useSession } from "../lib/useSession";

const PAGE_LOAD_TIMEOUT_MS = 3000;

vi.mock("../lib/useSession", () => ({
  useSession: vi.fn(),
}));

vi.mock("../lib/telemetry", () => ({
  trackPageView: vi.fn(),
}));

vi.mock("../components/TopNav", () => ({
  default: ({ tab }) => <div data-testid="topnav">TopNav:{tab}</div>,
}));

vi.mock("../components/StatusCard", () => ({
  default: ({ title }) => <div>{title}</div>,
}));

vi.mock("../components/KeyboardShortcutsModal", () => ({
  default: () => null,
}));

vi.mock("../pages/HomePage", () => ({
  default: () => <div>HOME_PAGE</div>,
}));

vi.mock("../pages/ManagePage", () => ({
  default: () => <div>MANAGE_PAGE</div>,
}));

vi.mock("../pages/OffersPage", () => ({
  default: () => <div>OFFERS_PAGE</div>,
}));

vi.mock("../pages/CalendarPage", () => ({
  default: () => <div>CALENDAR_PAGE</div>,
}));

vi.mock("../pages/AccessPage", () => ({
  default: () => <div>ACCESS_PAGE</div>,
}));

vi.mock("../pages/AdminPage", () => ({
  default: () => <div>ADMIN_PAGE</div>,
}));

vi.mock("../pages/InviteAcceptPage", () => ({
  default: () => <div>INVITE_ACCEPT_PAGE</div>,
}));

vi.mock("../pages/DebugPage", () => ({
  default: () => <div>DEBUG_PAGE</div>,
}));

vi.mock("../pages/PracticePortalPage", () => ({
  default: () => <div>PRACTICE_PAGE</div>,
}));

vi.mock("../pages/CoachOnboardingPage", () => ({
  default: () => <div>COACH_SETUP_PAGE</div>,
}));

vi.mock("../pages/NotificationSettingsPage", () => ({
  default: () => <div>SETTINGS_PAGE</div>,
}));

vi.mock("../pages/NotificationCenterPage", () => ({
  default: () => <div>NOTIFICATIONS_PAGE</div>,
}));

describe("App manage access control", () => {
  beforeEach(() => {
    window.location.hash = "";
  });

  it("redirects non-admin users from #manage to home", async () => {
    useSession.mockReturnValue({
      me: {
        userId: "user-1",
        email: "coach@example.com",
        memberships: [{ leagueId: "league-1", role: "Coach" }],
        isGlobalAdmin: false,
      },
      memberships: [{ leagueId: "league-1", role: "Coach" }],
      leagueId: "league-1",
      setLeagueId: vi.fn(),
      refreshMe: vi.fn(),
    });

    window.location.hash = "#manage";
    render(<App />);

    expect(await screen.findByText("HOME_PAGE", {}, { timeout: PAGE_LOAD_TIMEOUT_MS })).toBeInTheDocument();
    expect(screen.queryByText("MANAGE_PAGE")).not.toBeInTheDocument();
  });

  it("allows LeagueAdmin users to stay on #manage", async () => {
    useSession.mockReturnValue({
      me: {
        userId: "user-2",
        email: "admin@example.com",
        memberships: [{ leagueId: "league-1", role: "LeagueAdmin" }],
        isGlobalAdmin: false,
      },
      memberships: [{ leagueId: "league-1", role: "LeagueAdmin" }],
      leagueId: "league-1",
      setLeagueId: vi.fn(),
      refreshMe: vi.fn(),
    });

    window.location.hash = "#manage";
    render(<App />);

    expect(await screen.findByText("MANAGE_PAGE", {}, { timeout: PAGE_LOAD_TIMEOUT_MS })).toBeInTheDocument();
    expect(screen.queryByText("HOME_PAGE")).not.toBeInTheDocument();
  });

  it("treats #schedule as an invalid hash instead of aliasing it to calendar", async () => {
    useSession.mockReturnValue({
      me: {
        userId: "user-3",
        email: "coach@example.com",
        memberships: [{ leagueId: "league-1", role: "Coach" }],
        isGlobalAdmin: false,
      },
      memberships: [{ leagueId: "league-1", role: "Coach" }],
      leagueId: "league-1",
      setLeagueId: vi.fn(),
      refreshMe: vi.fn(),
    });

    window.location.hash = "#schedule";
    render(<App />);

    expect(await screen.findByText("HOME_PAGE", {}, { timeout: PAGE_LOAD_TIMEOUT_MS })).toBeInTheDocument();
    expect(screen.queryByText("CALENDAR_PAGE")).not.toBeInTheDocument();
  });
});
