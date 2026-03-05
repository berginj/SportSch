import { describe, it, expect, vi, beforeEach } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import App from "../App";
import { useSession } from "../lib/useSession";

const THEME_STORAGE_KEY = "gameswap_theme";

vi.mock("../lib/useSession", () => ({
  useSession: vi.fn(),
}));

vi.mock("../lib/telemetry", () => ({
  trackPageView: vi.fn(),
}));

vi.mock("../components/TopNav", () => ({
  default: ({ theme, onToggleTheme }) => (
    <div data-testid="topnav">
      <span data-testid="theme-value">{theme}</span>
      <button type="button" onClick={onToggleTheme}>
        Toggle Theme
      </button>
    </div>
  ),
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

vi.mock("../pages/SchedulePage", () => ({
  default: () => <div>SCHEDULE_PAGE</div>,
}));

vi.mock("../pages/HelpPage", () => ({
  default: () => <div>HELP_PAGE</div>,
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

describe("App theme behavior", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.location.hash = "#home";
    document.documentElement.removeAttribute("data-theme");

    const store = new Map();
    localStorage.getItem.mockImplementation((key) => (store.has(key) ? store.get(key) : null));
    localStorage.setItem.mockImplementation((key, value) => {
      store.set(String(key), String(value));
    });
    localStorage.removeItem.mockImplementation((key) => {
      store.delete(String(key));
    });
    localStorage.clear.mockImplementation(() => {
      store.clear();
    });
    localStorage.clear();

    window.matchMedia = vi.fn().mockImplementation((query) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    }));

    useSession.mockReturnValue({
      me: {
        userId: "user-1",
        email: "coach@example.com",
        memberships: [{ leagueId: "league-1", role: "LeagueAdmin" }],
        isGlobalAdmin: false,
      },
      memberships: [{ leagueId: "league-1", role: "LeagueAdmin" }],
      activeLeagueId: "league-1",
      setActiveLeagueId: vi.fn(),
      refreshMe: vi.fn(),
    });
  });

  it("loads stored dark theme and applies data-theme attribute", async () => {
    localStorage.setItem(THEME_STORAGE_KEY, "dark");

    render(<App />);
    expect(await screen.findByText("HOME_PAGE")).toBeInTheDocument();

    await waitFor(() => {
      expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    });
    expect(screen.getByTestId("theme-value")).toHaveTextContent("dark");
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe("dark");
  });

  it("toggles theme and persists updated value", async () => {
    localStorage.setItem(THEME_STORAGE_KEY, "light");

    render(<App />);
    expect(await screen.findByText("HOME_PAGE")).toBeInTheDocument();
    expect(screen.getByTestId("theme-value")).toHaveTextContent("light");

    fireEvent.click(screen.getByRole("button", { name: /toggle theme/i }));

    await waitFor(() => {
      expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    });
    expect(screen.getByTestId("theme-value")).toHaveTextContent("dark");
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe("dark");

    fireEvent.click(screen.getByRole("button", { name: /toggle theme/i }));

    await waitFor(() => {
      expect(document.documentElement.getAttribute("data-theme")).toBe("light");
    });
    expect(screen.getByTestId("theme-value")).toHaveTextContent("light");
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe("light");
  });
});
