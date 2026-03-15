import { describe, it, expect, beforeEach, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import CoachDashboard from "../../pages/CoachDashboard";
import * as api from "../../lib/api";

vi.mock("../../lib/api", () => ({
  apiFetch: vi.fn(),
}));

describe("CoachDashboard", () => {
  beforeEach(() => {
    vi.clearAllMocks();

    api.apiFetch.mockImplementation((path) => {
      const url = String(path || "");
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
  });

  it("renders coach actions from the consolidated dashboard endpoint", async () => {
    render(
      <CoachDashboard
        leagueId="league-1"
        setTab={() => {}}
      />
    );

    await waitFor(() => expect(screen.getByText("Coach Dashboard")).toBeInTheDocument());

    const divisionOffers = screen.getByText("Open offers in division").closest(".layoutStat");
    expect(divisionOffers).toHaveTextContent("1");

    const myOffers = screen.getByText("Your open offers").closest(".layoutStat");
    expect(myOffers).toHaveTextContent("1");

    const upcomingGames = screen.getByText("Upcoming games").closest(".layoutStat");
    expect(upcomingGames).toHaveTextContent("1");

    expect(screen.getByText("1 New Offer")).toBeInTheDocument();
  });
});
