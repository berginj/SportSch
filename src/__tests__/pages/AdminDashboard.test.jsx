import { describe, it, expect, beforeEach, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import AdminDashboard from "../../pages/AdminDashboard";
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

describe("AdminDashboard", () => {
  beforeEach(() => {
    vi.clearAllMocks();

    api.apiFetch.mockImplementation((path) => {
      const url = String(path || "");
      if (url === "/api/accessrequests?status=Pending") {
        return Promise.resolve([{ userId: "u1" }, { userId: "u2" }]);
      }
      if (url === "/api/memberships") {
        return Promise.resolve([
          { userId: "coach-1", role: "Coach", team: { division: "AAA", teamId: "TEAM-1" } },
          { userId: "coach-2", role: "Coach", team: null },
        ]);
      }
      if (url === "/api/slots") {
        return Promise.resolve({
          items: [
            {
              slotId: "game-open",
              status: "Open",
              gameType: "Swap",
              gameDate: isoInDays(3),
            },
            {
              slotId: "game-confirmed",
              status: "Confirmed",
              gameType: "Swap",
              gameDate: isoInDays(1),
            },
            {
              slotId: "practice-confirmed",
              status: "Confirmed",
              gameType: "Practice",
              gameDate: isoInDays(1),
            },
            {
              slotId: "availability-open",
              status: "Open",
              isAvailability: true,
              gameDate: isoInDays(2),
            },
            {
              slotId: "game-cancelled",
              status: "Cancelled",
              gameType: "Swap",
              gameDate: isoInDays(2),
            },
          ],
          continuationToken: "",
          hasMore: false,
          pageSize: 5,
        });
      }
      if (url === "/api/divisions") {
        return Promise.resolve([{ code: "AAA" }, { code: "AA" }]);
      }
      throw new Error(`Unexpected apiFetch call: ${url}`);
    });
  });

  it("computes coverage from active game-capable slots only", async () => {
    render(<AdminDashboard leagueId="league-1" onNavigate={() => {}} />);

    await waitFor(() => expect(screen.getByText("League Admin Dashboard")).toBeInTheDocument());

    const coverageCard = screen.getByText("Schedule Coverage").closest(".card");
    expect(coverageCard).toHaveTextContent("50%");
    expect(coverageCard).toHaveTextContent("1 of 2 slots");

    const openStat = screen.getByText("Open slots").closest(".layoutStat");
    expect(openStat).toHaveTextContent("1");

    const confirmedStat = screen.getByText("Confirmed games").closest(".layoutStat");
    expect(confirmedStat).toHaveTextContent("1");

    const upcomingCard = screen.getByText("Upcoming Games").closest(".card");
    expect(upcomingCard).toHaveTextContent("1");
  });
});
