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
  requests: [
    {
      requestId: "req-1",
      seasonLabel: "Spring 2026",
      practiceSlotKey: "slot-1",
      liveRecordId: "live-1",
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
      notes: "",
      createdBy: "user-1",
      createdAt: "2026-03-09T00:00:00Z",
      reviewedBy: null,
      reviewedAt: null,
      reviewReason: null,
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
      requestableBlockCount: 0,
      approvedTeamCount: 0,
      pendingTeamCount: 1,
      mappingIssues: ["division_unmapped", "team_unmapped", "policy_unmapped"],
    },
  ],
};

describe("PracticeSpaceManager", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.apiFetch.mockImplementation((path, options = {}) => {
      if (path === "/api/field-inventory/practice/admin") return Promise.resolve(adminResponse);
      if (String(path).startsWith("/api/availability-slots?")) {
        return Promise.resolve({
          items: [
            {
              slotId: "slot-a",
              gameDate: "2026-04-05",
              startTime: "09:00",
              endTime: "12:00",
              fieldKey: "park1/field1",
              fieldName: "Barcroft #3",
              displayName: "Barcroft #3",
              division: "Ponytail",
              isAvailability: true,
            },
          ],
          count: 1,
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
              requestableBlockCount: 2,
              mappingIssues: ["division_unmapped", "team_unmapped"],
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

  it("saves booking policy mappings and approves pending requests", async () => {
    render(<PracticeSpaceManager leagueId="league-1" />);

    expect(await screen.findByText("Practice Space Admin")).toBeInTheDocument();
    expect(await screen.findByText("Inventory Comparison Calendar")).toBeInTheDocument();
    expect(await screen.findByText("Calendar compare: 1")).toBeInTheDocument();
    fireEvent.change(screen.getByDisplayValue("Not requestable"), { target: { value: "auto_approve" } });
    fireEvent.click(screen.getByRole("button", { name: "Save Policy" }));

    expect(await screen.findByText("Mapped from group 'Ponytail'.")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Approve" }));

    expect(await screen.findByText("Approved")).toBeInTheDocument();
  });
});
