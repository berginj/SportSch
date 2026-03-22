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
      if (url === "/api/field-inventory/practice/coach") {
        return Promise.resolve({
          seasonLabel: "Spring 2026",
          seasons: [{ seasonLabel: "Spring 2026", isDefault: true }],
          division: "AAA",
          teamId: "TEAM-1",
          teamName: "Blue Waves",
          summary: {
            totalRecords: 4,
            requestableBlocks: 2,
            autoApproveBlocks: 1,
            commissionerReviewBlocks: 1,
            pendingRequests: 1,
            approvedRequests: 0,
            unmappedDivisions: 0,
            unmappedTeams: 0,
            unmappedPolicies: 0,
          },
          slots: [],
          requests: [
            {
              requestId: "req-1",
              seasonLabel: "Spring 2026",
              practiceSlotKey: "slot-1",
              liveRecordId: "live-1",
              slotId: "canon-slot-1",
              division: "AAA",
              date: "2026-04-10",
              dayOfWeek: "Friday",
              startTime: "18:00",
              endTime: "19:30",
              fieldId: "park1/field1",
              fieldName: "Gunston Turf",
              teamId: "TEAM-1",
              teamName: "Blue Waves",
              status: "Pending",
              bookingPolicy: "commissioner_review",
              bookingPolicyLabel: "Commissioner review",
              isMove: true,
              moveFromRequestId: "req-0",
              moveFromDate: "2026-04-03",
              moveFromStartTime: "18:00",
              moveFromEndTime: "19:30",
              moveFromFieldName: "Wakefield #2",
              notes: "Move requested from 2026-04-03 18:00-19:30 Wakefield #2",
              createdBy: "user-1",
              createdAt: "2026-03-09T00:00:00Z",
              reviewedBy: null,
              reviewedAt: null,
              reviewReason: null,
            },
          ],
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
          hasMore: false,
          pageSize: 1,
        });
      }
      throw new Error(`Unexpected apiFetch call: ${url}`);
    });
  });

  it("routes practice setup through the Practice Portal instead of embedding recurring request actions", async () => {
    const setTab = vi.fn();
    const replaceStateSpy = vi.spyOn(window.history, "replaceState");

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
    expect(screen.getByText(/Move from 2026-04-03/)).toBeInTheDocument();
    expect(screen.queryByText("Recurring practice choices")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /request p1/i })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Open Practice Portal" }));
    expect(setTab).toHaveBeenCalledWith("practice");

    fireEvent.click(screen.getByRole("button", { name: "View Full Schedule" }));
    expect(setTab).toHaveBeenLastCalledWith("calendar");
    expect(replaceStateSpy).toHaveBeenLastCalledWith({}, "", expect.stringContaining("division=AAA"));
    expect(replaceStateSpy).toHaveBeenLastCalledWith({}, "", expect.stringContaining("teamId=TEAM-1"));
    expect(replaceStateSpy).toHaveBeenLastCalledWith({}, "", expect.stringContaining("status=Confirmed"));
    expect(replaceStateSpy).toHaveBeenLastCalledWith({}, "", expect.stringContaining("#calendar"));
  });

  it("loads upcoming games across all slot pages", async () => {
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
      if (url.includes("continuationToken=page-2")) {
        return Promise.resolve({
          items: [
            {
              slotId: "slot-2",
              gameDate: "2026-04-19",
              startTime: "17:30",
              displayName: "Diamond 2",
              homeTeamId: "TEAM-3",
              awayTeamId: "TEAM-1",
            },
          ],
          continuationToken: "",
          pageSize: 1,
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
          continuationToken: "page-2",
          pageSize: 1,
        });
      }
      throw new Error(`Unexpected apiFetch call: ${url}`);
    });

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
        setTab={vi.fn()}
      />
    );

    await waitFor(() => expect(screen.getByText("Coach Onboarding")).toBeInTheDocument());

    const upcomingGamesStat = screen.getByText("Upcoming games (90 days)").closest(".layoutStat");
    expect(upcomingGamesStat).toHaveTextContent("2");
    expect(screen.getByText("2026-04-19 - 17:30")).toBeInTheDocument();
    expect(api.apiFetch).toHaveBeenCalledWith(expect.stringContaining("continuationToken=page-2"));
  });
});
