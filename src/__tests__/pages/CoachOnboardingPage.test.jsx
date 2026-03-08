import { describe, it, expect, beforeEach, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import CoachOnboardingPage from "../../pages/CoachOnboardingPage";
import * as api from "../../lib/api";

vi.mock("../../lib/api", () => ({
  apiFetch: vi.fn(),
}));

describe("CoachOnboardingPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();

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
            assistantCoaches: [{ name: "Assistant A", email: "assistant@example.com" }],
            onboardingComplete: false,
          },
        ]);
      }
      if (url === "/api/practice-requests?teamId=TEAM-1") {
        return Promise.resolve([
          {
            requestId: "req-1",
            priority: 1,
            status: "Pending",
            openToShareField: true,
            shareWithTeamId: "TEAM-2",
            slot: {
              gameDate: "2026-04-10",
              startTime: "18:00",
              endTime: "19:30",
              displayName: "Gunston > Turf",
            },
          },
        ]);
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
          hasMore: false,
          pageSize: 1,
        });
      }
      throw new Error(`Unexpected apiFetch call: ${url}`);
    });
  });

  it("routes practice setup through the Practice Portal instead of embedding recurring request actions", async () => {
    const setTab = vi.fn();

    render(
      <CoachOnboardingPage
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
        setTab={setTab}
      />
    );

    await waitFor(() => expect(screen.getByText("Coach Onboarding")).toBeInTheDocument());

    expect(screen.getByText("Current practice requests")).toBeInTheDocument();
    expect(screen.queryByText("Recurring practice choices")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /request p1/i })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Open Practice Portal" }));
    expect(setTab).toHaveBeenCalledWith("practice");
  });
});
