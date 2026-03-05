import { describe, it, expect, beforeEach, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import CoachDashboard from "../../pages/CoachDashboard";
import * as api from "../../lib/api";

vi.mock("../../lib/api", () => ({
  apiFetch: vi.fn(),
}));

function toIsoDate(date) {
  const yyyy = date.getFullYear();
  const mm = String(date.getMonth() + 1).padStart(2, "0");
  const dd = String(date.getDate()).padStart(2, "0");
  return `${yyyy}-${mm}-${dd}`;
}

function isoInDays(days) {
  const date = new Date();
  date.setDate(date.getDate() + days);
  return toIsoDate(date);
}

describe("CoachDashboard", () => {
  beforeEach(() => {
    vi.clearAllMocks();

    api.apiFetch.mockImplementation((path) => {
      const url = String(path || "");
      if (url.startsWith("/api/slots?")) {
        return Promise.resolve([
          {
            slotId: "open-offer-other",
            status: "Open",
            division: "AAA",
            gameType: "Swap",
            offeringTeamId: "TEAM-2",
          },
          {
            slotId: "open-request-other",
            status: "Open",
            division: "AAA",
            gameType: "Request",
            offeringTeamId: "TEAM-3",
          },
          {
            slotId: "open-practice-other",
            status: "Open",
            division: "AAA",
            gameType: "Practice",
            offeringTeamId: "TEAM-4",
          },
          {
            slotId: "open-availability",
            status: "Open",
            division: "AAA",
            isAvailability: true,
            offeringTeamId: "TEAM-5",
          },
          {
            slotId: "open-offer-mine",
            status: "Open",
            division: "AAA",
            gameType: "Swap",
            offeringTeamId: "TEAM-1",
          },
          {
            slotId: "confirmed-game",
            status: "Confirmed",
            division: "AAA",
            gameType: "Swap",
            gameDate: isoInDays(2),
            startTime: "18:00",
            fieldKey: "FIELD-1",
            homeTeamId: "TEAM-1",
            awayTeamId: "TEAM-2",
          },
          {
            slotId: "confirmed-practice",
            status: "Confirmed",
            division: "AAA",
            gameType: "Practice",
            gameDate: isoInDays(2),
            startTime: "19:00",
            fieldKey: "FIELD-2",
            homeTeamId: "TEAM-1",
          },
        ]);
      }
      if (url === "/api/teams?division=AAA") {
        return Promise.resolve([
          { division: "AAA", teamId: "TEAM-1", name: "Blue Waves" },
          { division: "AAA", teamId: "TEAM-2", name: "Tigers" },
        ]);
      }
      throw new Error(`Unexpected apiFetch call: ${url}`);
    });
  });

  it("counts only open game offers and excludes requests, practice, and availability rows", async () => {
    render(
      <CoachDashboard
        me={{
          memberships: [
            {
              leagueId: "league-1",
              role: "Coach",
              team: { division: "AAA", teamId: "TEAM-1" },
            },
          ],
        }}
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
