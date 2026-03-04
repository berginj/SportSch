import { describe, it, expect, beforeEach, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import CalendarPage from "../../pages/CalendarPage";
import * as api from "../../lib/api";

vi.mock("../../lib/api", () => ({
  apiFetch: vi.fn(),
}));

vi.mock("../../lib/telemetry", () => ({
  trackEvent: vi.fn(),
}));

vi.mock("../../components/LeaguePicker", () => ({
  default: function LeaguePicker() {
    return <div data-testid="league-picker" />;
  },
}));

vi.mock("../../components/StatusCard", () => ({
  default: function StatusCard() {
    return null;
  },
}));

vi.mock("../../components/Toast", () => ({
  default: function Toast() {
    return null;
  },
}));

vi.mock("../../components/Dialogs", () => ({
  ConfirmDialog: function ConfirmDialog() {
    return null;
  },
  PromptDialog: function PromptDialog() {
    return null;
  },
}));

vi.mock("../../lib/useDialogs", () => ({
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

describe("CalendarPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.history.replaceState({}, "", "/");
    localStorage.getItem.mockImplementation((key) => {
      if (key === "calendar-use-new-view") return "true";
      return null;
    });

    api.apiFetch.mockImplementation((path) => {
      const url = String(path || "");
      if (url === "/api/league") {
        return Promise.resolve({
          season: {
            springStart: "2026-04-01",
            springEnd: "2026-06-30",
          },
        });
      }
      if (url === "/api/divisions") {
        return Promise.resolve([{ code: "U12", name: "Under 12" }]);
      }
      if (url === "/api/fields") {
        return Promise.resolve([{ fieldKey: "FIELD-1", displayName: "Field 1" }]);
      }
      if (url.startsWith("/api/events?")) {
        return Promise.resolve([]);
      }
      if (url.startsWith("/api/slots?")) {
        return Promise.resolve([
          {
            slotId: "slot-1",
            division: "U12",
            gameDate: "2026-04-07",
            startTime: "18:00",
            endTime: "20:00",
            fieldKey: "FIELD-1",
            displayName: "Field 1",
            gameType: "game",
            status: "Confirmed",
            homeTeamId: "TEAM-1",
            awayTeamId: "TEAM-2",
            isAvailability: false,
          },
        ]);
      }
      throw new Error(`Unexpected apiFetch call: ${url}`);
    });
  });

  it("does not open the edit modal from week cards for non-admin users", async () => {
    render(
      <CalendarPage
        me={{
          memberships: [
            { leagueId: "league-1", role: "Coach", teamId: "TEAM-1" },
          ],
        }}
        leagueId="league-1"
        setLeagueId={vi.fn()}
      />
    );

    const detailsButton = await screen.findByRole("button", { name: /Details/i });
    fireEvent.click(detailsButton);

    const matchup = await screen.findByText("TEAM-1 vs TEAM-2");
    fireEvent.click(matchup);

    expect(screen.queryByRole("dialog", { name: /Edit scheduled game/i })).not.toBeInTheDocument();
  });
});
