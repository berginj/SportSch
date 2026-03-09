import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import PracticePortalPage from "../../pages/PracticePortalPage";
import * as api from "../../lib/api";

vi.mock("../../lib/api", () => ({
  apiFetch: vi.fn(),
}));

vi.mock("../../components/StatusCard", () => ({
  default: function StatusCard({ title, message }) {
    return <div>{title}: {message}</div>;
  },
}));

vi.mock("../../components/Toast", () => ({
  default: function Toast() {
    return null;
  },
}));

const coachResponse = {
  seasonLabel: "Spring 2026",
  seasons: [{ seasonLabel: "Spring 2026", isDefault: true }],
  division: "PONY",
  teamId: "TEAM-1",
  teamName: "Ponytails Red",
  summary: {
    totalRecords: 12,
    requestableBlocks: 4,
    autoApproveBlocks: 2,
    commissionerReviewBlocks: 2,
    pendingRequests: 0,
    approvedRequests: 0,
    unmappedDivisions: 0,
    unmappedTeams: 0,
    unmappedPolicies: 0,
  },
  slots: [
    {
      practiceSlotKey: "slot-auto",
      seasonLabel: "Spring 2026",
      liveRecordId: "live-1",
      date: "2026-04-05",
      dayOfWeek: "Sunday",
      startTime: "09:00",
      endTime: "10:30",
      slotDurationMinutes: 90,
      fieldId: "park1/field1",
      fieldName: "Barcroft #3",
      bookingPolicy: "auto_approve",
      bookingPolicyLabel: "Auto-approve",
      bookingPolicyReason: "Ponytail-assigned space auto-approves coach requests.",
      assignedGroup: "Ponytail",
      assignedDivision: "PONY",
      assignedTeamOrEvent: "",
      capacity: 2,
      approvedCount: 0,
      pendingCount: 0,
      remainingCapacity: 2,
      approvedTeamIds: [],
      pendingTeamIds: [],
    },
  ],
  requests: [],
};

describe("PracticePortalPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.apiFetch.mockImplementation((path, options = {}) => {
      if (path === "/api/field-inventory/practice/coach") return Promise.resolve(coachResponse);
      if (path === "/api/field-inventory/practice/requests" && options.method === "POST") {
        return Promise.resolve({
          ...coachResponse,
          requests: [
            {
              requestId: "req-1",
              seasonLabel: "Spring 2026",
              practiceSlotKey: "slot-auto",
              liveRecordId: "live-1",
              date: "2026-04-05",
              dayOfWeek: "Sunday",
              startTime: "09:00",
              endTime: "10:30",
              fieldId: "park1/field1",
              fieldName: "Barcroft #3",
              teamId: "TEAM-1",
              teamName: "Ponytails Red",
              status: "Approved",
              bookingPolicy: "auto_approve",
              bookingPolicyLabel: "Auto-approve",
              notes: "",
              createdBy: "user-1",
              createdAt: "2026-03-09T00:00:00Z",
              reviewedBy: "user-1",
              reviewedAt: "2026-03-09T00:00:00Z",
              reviewReason: "Auto-approved from Ponytail-assigned field space.",
            },
          ],
          slots: [
            {
              ...coachResponse.slots[0],
              approvedCount: 1,
              remainingCapacity: 1,
              approvedTeamIds: ["TEAM-1"],
            },
          ],
          summary: {
            ...coachResponse.summary,
            approvedRequests: 1,
          },
        });
      }
      throw new Error(`Unexpected apiFetch call: ${path}`);
    });
  });

  it("requests auto-approved practice space and shows the request in My Practice Requests", async () => {
    render(<PracticePortalPage me={{ name: "Coach A" }} leagueId="league-1" />);

    expect(await screen.findByText("Available Practice Space")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Book Now" }));

    expect(await screen.findByText("My Practice Requests")).toBeInTheDocument();
    expect(screen.getByText("Approved")).toBeInTheDocument();
    expect(screen.getAllByText("Barcroft #3").length).toBeGreaterThan(0);
  });
});
