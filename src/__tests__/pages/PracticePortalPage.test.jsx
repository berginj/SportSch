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
      slotId: "canon-slot-1",
      division: "PONY",
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
      normalizationState: "normalized",
      normalizationIssues: [],
      capacity: 1,
      approvedCount: 0,
      pendingCount: 0,
      remainingCapacity: 1,
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
              slotId: "canon-slot-1",
              division: "PONY",
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
              isMove: false,
              moveFromRequestId: null,
              moveFromDate: null,
              moveFromStartTime: null,
              moveFromEndTime: null,
              moveFromFieldName: null,
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
              remainingCapacity: 0,
              approvedTeamIds: ["TEAM-1"],
            },
          ],
          summary: {
            ...coachResponse.summary,
            approvedRequests: 1,
          },
        });
      }
      if (path === "/api/field-inventory/practice/requests/req-1/move" && options.method === "PATCH") {
        return Promise.resolve({
          ...coachResponse,
          slots: [
            {
              ...coachResponse.slots[0],
              practiceSlotKey: "slot-new",
              slotId: "canon-slot-2",
              fieldName: "Gunston Turf",
              fieldId: "park1/field2",
              date: "2026-04-06",
            },
          ],
          requests: [
            {
              requestId: "req-2",
              seasonLabel: "Spring 2026",
              practiceSlotKey: "slot-new",
              liveRecordId: "live-2",
              slotId: "canon-slot-2",
              division: "PONY",
              date: "2026-04-06",
              dayOfWeek: "Monday",
              startTime: "09:00",
              endTime: "10:30",
              fieldId: "park1/field2",
              fieldName: "Gunston Turf",
              teamId: "TEAM-1",
              teamName: "Ponytails Red",
              status: "Approved",
              bookingPolicy: "auto_approve",
              bookingPolicyLabel: "Auto-approve",
              isMove: true,
              moveFromRequestId: "req-1",
              moveFromDate: "2026-04-05",
              moveFromStartTime: "09:00",
              moveFromEndTime: "10:30",
              moveFromFieldName: "Barcroft #3",
              notes: "Move requested from 2026-04-05 09:00-10:30 Barcroft #3",
              createdBy: "user-1",
              createdAt: "2026-03-09T00:00:00Z",
              reviewedBy: "user-1",
              reviewedAt: "2026-03-09T00:00:00Z",
              reviewReason: "Moved and auto-approved from normalized field inventory.",
            },
          ],
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

  it("moves an active practice request to another normalized slot", async () => {
    api.apiFetch.mockImplementationOnce(() => Promise.resolve({
      ...coachResponse,
      requests: [
        {
          requestId: "req-1",
          seasonLabel: "Spring 2026",
          practiceSlotKey: "slot-auto",
          liveRecordId: "live-1",
          slotId: "canon-slot-1",
          division: "PONY",
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
          isMove: false,
          notes: "",
        },
      ],
      slots: [
        coachResponse.slots[0],
        {
          ...coachResponse.slots[0],
          practiceSlotKey: "slot-new",
          slotId: "canon-slot-2",
          fieldName: "Gunston Turf",
          fieldId: "park1/field2",
          date: "2026-04-06",
          dayOfWeek: "Monday",
        },
      ],
    }));

    render(<PracticePortalPage me={{ name: "Coach A" }} leagueId="league-1" />);

    expect(await screen.findByText("My Practice Requests")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Move" }));
    fireEvent.click(screen.getByRole("button", { name: "Move Here" }));

    expect(await screen.findByText(/Move from 2026-04-05/)).toBeInTheDocument();
    expect(screen.getAllByText("Gunston Turf").length).toBeGreaterThan(0);
  });
});
