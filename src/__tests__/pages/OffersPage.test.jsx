import { describe, it, expect, beforeEach, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import OffersPage from "../../pages/OffersPage";
import * as api from "../../lib/api";

vi.mock("../../lib/api", () => ({
  apiFetch: vi.fn(),
}));

vi.mock("../../lib/telemetry", () => ({
  trackEvent: vi.fn(),
}));

vi.mock("../../components/LeaguePicker", () => ({
  default: ({ label }) => <div>{label}</div>,
}));

vi.mock("../../components/StatusCard", () => ({
  default: ({ title, message }) => (
    <div>
      <div>{title}</div>
      <div>{message}</div>
    </div>
  ),
}));

vi.mock("../../components/Toast", () => ({
  default: () => null,
}));

vi.mock("../../components/Dialogs", () => ({
  PromptDialog: () => null,
}));

vi.mock("../../lib/useDialogs", () => ({
  usePromptDialog: () => ({
    promptState: null,
    promptValue: "",
    setPromptValue: vi.fn(),
    requestPrompt: vi.fn(),
    handleConfirm: vi.fn(),
    handleCancel: vi.fn(),
  }),
}));

describe("OffersPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.history.replaceState({}, "", "/offers");

    api.apiFetch.mockImplementation((path) => {
      const url = String(path || "");
      if (url === "/api/divisions") {
        return Promise.resolve([{ code: "AAA", name: "10U" }]);
      }
      if (url === "/api/fields") {
        return Promise.resolve([
          { fieldKey: "FIELD-1", displayName: "Diamond 1" },
        ]);
      }
      if (url.startsWith("/api/slots?")) {
        return Promise.resolve([
          {
            slotId: "slot-open-offer",
            division: "AAA",
            gameDate: "2026-03-16",
            startTime: "18:00",
            endTime: "19:30",
            displayName: "Diamond 1",
            fieldKey: "FIELD-1",
            gameType: "Swap",
            offeringTeamId: "TEAM-1",
            status: "Open",
          },
          {
            slotId: "slot-confirmed-offer",
            division: "AAA",
            gameDate: "2026-03-17",
            startTime: "18:00",
            endTime: "19:30",
            displayName: "Diamond 1",
            fieldKey: "FIELD-1",
            gameType: "Swap",
            offeringTeamId: "TEAM-2",
            status: "Confirmed",
          },
          {
            slotId: "slot-open-request",
            division: "AAA",
            gameDate: "2026-03-18",
            startTime: "18:00",
            endTime: "19:30",
            displayName: "Diamond 1",
            fieldKey: "FIELD-1",
            gameType: "Request",
            offeringTeamId: "TEAM-3",
            status: "Open",
          },
          {
            slotId: "slot-open-practice",
            division: "AAA",
            gameDate: "2026-03-19",
            startTime: "18:00",
            endTime: "19:30",
            displayName: "Diamond 1",
            fieldKey: "FIELD-1",
            gameType: "Practice",
            offeringTeamId: "TEAM-4",
            status: "Open",
          },
        ]);
      }
      throw new Error(`Unexpected apiFetch call: ${url}`);
    });
  });

  it("defaults to open offers for the selected division and shows weekday plus date", async () => {
    render(
      <OffersPage
        me={{ memberships: [{ leagueId: "league-1", role: "Viewer" }] }}
        leagueId="league-1"
        setLeagueId={() => {}}
      />
    );

    await waitFor(() => expect(screen.getByText("Open offers & requests")).toBeInTheDocument());

    expect(api.apiFetch).toHaveBeenCalledWith("/api/slots?division=AAA&status=Open");
    expect(screen.getByLabelText(/Slot type/i)).toHaveValue("offer");

    expect(screen.getByText("Monday")).toBeInTheDocument();
    expect(screen.getByText("2026-03-16")).toBeInTheDocument();
    expect(screen.getByText("TEAM-1 vs TBD")).toBeInTheDocument();

    expect(screen.queryByText("2026-03-17")).not.toBeInTheDocument();
    expect(screen.queryByText("2026-03-18")).not.toBeInTheDocument();
    expect(screen.queryByText("2026-03-19")).not.toBeInTheDocument();
  });
});
