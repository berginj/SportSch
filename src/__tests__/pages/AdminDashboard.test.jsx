import { describe, it, expect, beforeEach, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import AdminDashboard from "../../pages/AdminDashboard";
import * as api from "../../lib/api";

vi.mock("../../lib/api", () => ({
  apiFetch: vi.fn(),
}));

describe("AdminDashboard", () => {
  beforeEach(() => {
    vi.clearAllMocks();

    api.apiFetch.mockImplementation((path) => {
      const url = String(path || "");
      if (url === "/api/admin/dashboard") {
        return Promise.resolve({
          pendingRequests: 2,
          unassignedCoaches: 1,
          totalCoaches: 2,
          scheduleCoverage: 50,
          upcomingGames: 1,
          totalSlots: 2,
          confirmedSlots: 1,
          openSlots: 1,
          divisions: 2,
        });
      }
      throw new Error(`Unexpected apiFetch call: ${url}`);
    });
  });

  it("renders metrics from the consolidated admin dashboard endpoint", async () => {
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
