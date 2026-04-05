import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import PracticeSpaceManager from "../../manage/PracticeSpaceManager";
import * as api from "../../lib/api";

vi.mock("../../lib/api", () => ({
  apiFetch: vi.fn(),
}));

vi.mock("../../components/Toast", () => ({
  default: function Toast() {
    return null;
  },
}));

vi.mock("../../components/PracticeSpaceComparisonCalendar", () => ({
  default: function PracticeSpaceComparisonCalendar({ items, mode }) {
    return <div>{`Calendar ${mode}: ${items.length}`}</div>;
  },
}));

const adminResponse = {
  seasonLabel: "Spring 2026",
  seasons: [{ seasonLabel: "Spring 2026", isDefault: true }],
  summary: {
    totalRecords: 8,
    requestableBlocks: 3,
    autoApproveBlocks: 1,
    commissionerReviewBlocks: 2,
    pendingRequests: 1,
    approvedRequests: 0,
    unmappedDivisions: 1,
    unmappedTeams: 1,
    unmappedPolicies: 1,
  },
  normalization: {
    candidateBlocks: 1,
    normalizedBlocks: 0,
    missingBlocks: 1,
    conflictBlocks: 0,
    blockedBlocks: 0,
  },
  requests: [
    {
      requestId: "req-1",
      seasonLabel: "Spring 2026",
      practiceSlotKey: "slot-1",
      liveRecordId: "live-1",
      slotId: "canon-1",
      division: "PONY",
      date: "2026-04-05",
      dayOfWeek: "Sunday",
      startTime: "09:00",
      endTime: "10:30",
      fieldId: "park1/field1",
      fieldName: "Barcroft #3",
      teamId: "TEAM-1",
      teamName: "Ponytails Red",
      status: "Pending",
      bookingPolicy: "commissioner_review",
      bookingPolicyLabel: "Commissioner review",
      isMove: false,
      moveFromRequestId: null,
      moveFromDate: null,
      moveFromStartTime: null,
      moveFromEndTime: null,
      moveFromFieldName: null,
      notes: "",
      createdBy: "user-1",
      createdAt: "2026-03-09T00:00:00Z",
      reviewedBy: null,
      reviewedAt: null,
      reviewReason: null,
      openToShareField: true,
      shareWithTeamId: "TEAM-2",
      reservedTeamIds: ["TEAM-1", "TEAM-2"],
    },
  ],
  slots: [
    {
      practiceSlotKey: "slot-1",
      seasonLabel: "Spring 2026",
      liveRecordId: "live-1",
      slotId: "canon-1",
      division: "PONY",
      date: "2026-04-05",
      dayOfWeek: "Sunday",
      startTime: "09:00",
      endTime: "10:30",
      slotDurationMinutes: 90,
      fieldId: "park1/field1",
      fieldName: "Barcroft #3",
      bookingPolicy: "not_requestable",
      bookingPolicyLabel: "Not requestable",
      bookingPolicyReason: "Needs policy mapping before coaches can request it.",
      normalizationState: "missing",
      normalizationIssues: ["division_unmapped", "team_unmapped", "policy_unmapped"],
      assignedGroup: "Ponytail",
      assignedDivision: "PONY",
      assignedTeamOrEvent: "Red",
      isAvailable: true,
      pendingTeamIds: [],
      shareable: true,
      maxTeamsPerBooking: 2,
      reservedTeamIds: [],
      pendingShareTeamIds: [],
    },
  ],
  canonicalFields: [],
  canonicalDivisions: [{ code: "PONY", name: "Ponytail" }],
  canonicalTeams: [{ divisionCode: "PONY", teamId: "TEAM-1", teamName: "Ponytails Red" }],
  rows: [
    {
      recordId: "live-1",
      seasonLabel: "Spring 2026",
      date: "2026-04-05",
      dayOfWeek: "Sunday",
      startTime: "09:00",
      endTime: "12:00",
      slotDurationMinutes: 180,
      availabilityStatus: "available",
      utilizationStatus: "not_used",
      usageType: null,
      usedBy: "AGSA",
      fieldId: "park1/field1",
      fieldName: "Barcroft #3",
      rawFieldName: "Barcroft #3",
      assignedGroup: "Ponytail",
      rawAssignedDivision: "Ponytail",
      rawAssignedTeamOrEvent: "Red",
      canonicalDivisionCode: "",
      canonicalDivisionName: "",
      canonicalTeamId: "",
      canonicalTeamName: "",
      bookingPolicy: "not_requestable",
      bookingPolicyReason: "Needs policy mapping before coaches can request it.",
      mappingIssues: ["division_unmapped", "team_unmapped", "policy_unmapped"],
    },
  ],
};

describe("PracticeSpaceManager", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.apiFetch.mockImplementation((path, options = {}) => {
      if (path === "/api/field-inventory/practice/admin") return Promise.resolve(adminResponse);
      if (path.startsWith("/api/field-inventory/practice/availability/options?")) {
        return Promise.resolve({
          seasonLabel: "Spring 2026",
          division: "PONY",
          teamId: "TEAM-1",
          teamName: "Ponytails Red",
          date: "2026-04-05",
          startTime: "09:00",
          endTime: "10:30",
          fieldKey: null,
          exactMatchRequested: true,
          count: 1,
          options: [
            {
              slotId: "canon-1",
              practiceSlotKey: "slot-1",
              seasonLabel: "Spring 2026",
              division: "PONY",
              date: "2026-04-05",
              dayOfWeek: "Sunday",
              startTime: "09:00",
              endTime: "10:30",
              fieldKey: "park1/field1",
              fieldName: "Barcroft #3",
              bookingPolicy: "commissioner_review",
              bookingPolicyLabel: "Commissioner review",
              isAvailable: true,
              shareable: true,
              maxTeamsPerBooking: 2,
              reservedTeamIds: [],
              pendingTeamIds: ["TEAM-1"],
              pendingShareTeamIds: [],
            },
          ],
        });
      }
      if (path.startsWith("/api/field-inventory/practice/availability/check?")) {
        return Promise.resolve({
          seasonLabel: "Spring 2026",
          division: "PONY",
          teamId: "TEAM-1",
          teamName: "Ponytails Red",
          date: "2026-04-05",
          startTime: "09:00",
          endTime: "10:30",
          fieldKey: null,
          available: true,
          matchingOptionCount: 1,
          options: [],
        });
      }
      if (path === "/api/field-inventory/practice/normalize" && options.method === "POST") {
        return Promise.resolve({
          result: {
            candidateBlocks: 1,
            createdBlocks: 1,
            updatedBlocks: 0,
            alreadyNormalizedBlocks: 0,
            conflictBlocks: 0,
            blockedBlocks: 0,
          },
          adminView: {
            ...adminResponse,
            normalization: {
              candidateBlocks: 1,
              normalizedBlocks: 1,
              missingBlocks: 0,
              conflictBlocks: 0,
              blockedBlocks: 0,
            },
            slots: [
              {
                ...adminResponse.slots[0],
                bookingPolicy: "auto_approve",
                bookingPolicyLabel: "Auto-approve",
                bookingPolicyReason: "Mapped from group 'Ponytail'.",
                normalizationState: "normalized",
                normalizationIssues: [],
              },
            ],
          },
        });
      }
      if (path === "/api/field-inventory/practice/policies" && options.method === "POST") {
        return Promise.resolve({
          ...adminResponse,
          rows: [
            {
              ...adminResponse.rows[0],
              bookingPolicy: "auto_approve",
              bookingPolicyReason: "Mapped from group 'Ponytail'.",
              mappingIssues: ["division_unmapped", "team_unmapped"],
            },
          ],
          slots: [
            {
              ...adminResponse.slots[0],
              bookingPolicy: "auto_approve",
              bookingPolicyLabel: "Auto-approve",
              bookingPolicyReason: "Mapped from group 'Ponytail'.",
              normalizationIssues: ["division_unmapped", "team_unmapped"],
            },
          ],
        });
      }
      if (path === "/api/field-inventory/practice/requests/req-1/approve" && options.method === "PATCH") {
        return Promise.resolve({
          ...adminResponse,
          requests: [{ ...adminResponse.requests[0], status: "Approved", reviewReason: "Approved by commissioner" }],
        });
      }
      throw new Error(`Unexpected apiFetch call: ${path}`);
    });
  });

  it("saves policy mappings, normalizes missing blocks, and approves pending requests", async () => {
    render(<PracticeSpaceManager leagueId="league-1" />);

    expect(await screen.findByText("Practice Space Admin")).toBeInTheDocument();
    expect(await screen.findByText("Availability Normalization")).toBeInTheDocument();
    expect(await screen.findByText("Availability Search")).toBeInTheDocument();
    expect(await screen.findByText("Calendar compare: 1")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Start"), { target: { value: "09:00" } });
    fireEvent.change(screen.getByLabelText("End"), { target: { value: "10:30" } });
    fireEvent.click(screen.getByRole("button", { name: "Search Availability" }));

    expect(await screen.findByText("Exact window available")).toBeInTheDocument();
    expect(screen.getAllByText("Barcroft #3").length).toBeGreaterThan(0);
    expect(screen.getByText("Sharing with TEAM-2")).toBeInTheDocument();
    fireEvent.change(screen.getByDisplayValue("Not requestable"), { target: { value: "auto_approve" } });
    fireEvent.click(screen.getByRole("button", { name: "Save Policy" }));

    expect((await screen.findAllByText("Mapped from group 'Ponytail'.")).length).toBeGreaterThan(0);
    fireEvent.click(screen.getByRole("button", { name: "Normalize Missing" }));

    expect(await screen.findByText("Normalized")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Approve" }));

    expect(await screen.findByText("Approved")).toBeInTheDocument();
  }, 15000);
});
