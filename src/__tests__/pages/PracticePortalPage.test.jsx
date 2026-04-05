import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
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
      isAvailable: true,
      pendingTeamIds: [],
      shareable: true,
      maxTeamsPerBooking: 2,
      reservedTeamIds: [],
      pendingShareTeamIds: [],
    },
    {
      practiceSlotKey: "slot-new",
      seasonLabel: "Spring 2026",
      liveRecordId: "live-2",
      slotId: "canon-slot-2",
      division: "PONY",
      date: "2026-04-06",
      dayOfWeek: "Monday",
      startTime: "09:00",
      endTime: "10:30",
      slotDurationMinutes: 90,
      fieldId: "park1/field2",
      fieldName: "Gunston Turf",
      bookingPolicy: "auto_approve",
      bookingPolicyLabel: "Auto-approve",
      bookingPolicyReason: "Ponytail-assigned space auto-approves coach requests.",
      assignedGroup: "Ponytail",
      assignedDivision: "PONY",
      assignedTeamOrEvent: "",
      normalizationState: "normalized",
      normalizationIssues: [],
      isAvailable: true,
      pendingTeamIds: [],
      shareable: true,
      maxTeamsPerBooking: 2,
      reservedTeamIds: [],
      pendingShareTeamIds: [],
    },
  ],
  requests: [],
};

const teamResponse = [
  { division: "PONY", teamId: "TEAM-1", name: "Ponytails Red" },
  { division: "PONY", teamId: "TEAM-2", name: "Ponytails Blue" },
];

const availabilityByDate = {
  "2026-04-05": {
    seasonLabel: "Spring 2026",
    division: "PONY",
    teamId: "TEAM-1",
    teamName: "Ponytails Red",
    date: "2026-04-05",
    startTime: null,
    endTime: null,
    fieldKey: null,
    exactMatchRequested: false,
    count: 1,
    options: [
      {
        slotId: "canon-slot-1",
        practiceSlotKey: "slot-auto",
        seasonLabel: "Spring 2026",
        division: "PONY",
        date: "2026-04-05",
        dayOfWeek: "Sunday",
        startTime: "09:00",
        endTime: "10:30",
        fieldKey: "park1/field1",
        fieldName: "Barcroft #3",
        bookingPolicy: "auto_approve",
        bookingPolicyLabel: "Auto-approve",
        isAvailable: true,
        shareable: true,
        maxTeamsPerBooking: 2,
        reservedTeamIds: [],
        pendingTeamIds: [],
        pendingShareTeamIds: [],
      },
    ],
  },
  "2026-04-06": {
    seasonLabel: "Spring 2026",
    division: "PONY",
    teamId: "TEAM-1",
    teamName: "Ponytails Red",
    date: "2026-04-06",
    startTime: null,
    endTime: null,
    fieldKey: null,
    exactMatchRequested: false,
    count: 1,
    options: [
      {
        slotId: "canon-slot-2",
        practiceSlotKey: "slot-new",
        seasonLabel: "Spring 2026",
        division: "PONY",
        date: "2026-04-06",
        dayOfWeek: "Monday",
        startTime: "09:00",
        endTime: "10:30",
        fieldKey: "park1/field2",
        fieldName: "Gunston Turf",
        bookingPolicy: "auto_approve",
        bookingPolicyLabel: "Auto-approve",
        isAvailable: true,
        shareable: true,
        maxTeamsPerBooking: 2,
        reservedTeamIds: [],
        pendingTeamIds: [],
        pendingShareTeamIds: [],
      },
    ],
  },
};

function readDateFromPath(path) {
  const [, query = ""] = path.split("?");
  return new URLSearchParams(query).get("date");
}

describe("PracticePortalPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.apiFetch.mockImplementation((path, options = {}) => {
      if (path === "/api/field-inventory/practice/coach") return Promise.resolve(coachResponse);
      if (path === "/api/teams?division=PONY") return Promise.resolve(teamResponse);
      if (path.startsWith("/api/field-inventory/practice/availability/options?")) {
        return Promise.resolve(availabilityByDate[readDateFromPath(path)] || { ...availabilityByDate["2026-04-05"], count: 0, options: [] });
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
          options: availabilityByDate["2026-04-05"].options,
        });
      }
      if (path === "/api/field-inventory/practice/requests" && options.method === "POST") {
        const body = JSON.parse(options.body);
        expect(body.openToShareField).toBe(true);
        expect(body.shareWithTeamId).toBe("TEAM-2");
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
              openToShareField: true,
              shareWithTeamId: "TEAM-2",
              reservedTeamIds: ["TEAM-1", "TEAM-2"],
            },
          ],
          summary: {
            ...coachResponse.summary,
            approvedRequests: 1,
          },
        });
      }
      if (path === "/api/field-inventory/practice/requests/req-1/move" && options.method === "PATCH") {
        const body = JSON.parse(options.body);
        expect(body.openToShareField).toBe(true);
        expect(body.shareWithTeamId).toBe("TEAM-2");
        return Promise.resolve({
          ...coachResponse,
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
              openToShareField: true,
              shareWithTeamId: "TEAM-2",
              reservedTeamIds: ["TEAM-1", "TEAM-2"],
            },
          ],
        });
      }
      throw new Error(`Unexpected apiFetch call: ${path}`);
    });
  });

  it("loads canonical availability, checks an exact window, and books a shared practice", async () => {
    render(<PracticePortalPage me={{ name: "Coach A" }} leagueId="league-1" />);

    expect(await screen.findByText("Available Practice Space")).toBeInTheDocument();
    await screen.findByText("Barcroft #3");

    fireEvent.click(screen.getByLabelText("Book as shared practice"));
    fireEvent.change(screen.getByLabelText("Share with"), { target: { value: "TEAM-2" } });
    fireEvent.change(screen.getByLabelText("Start"), { target: { value: "09:00" } });
    fireEvent.change(screen.getByLabelText("End"), { target: { value: "10:30" } });

    expect(await screen.findByText("Exact window available")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Book Now" }));

    expect(await screen.findByText("My Practice Requests")).toBeInTheDocument();
    expect(screen.getByText("Sharing with TEAM-2")).toBeInTheDocument();
    expect(screen.getByText("TEAM-1, TEAM-2")).toBeInTheDocument();
  });

  it("moves an active practice request to another queried availability option", async () => {
    api.apiFetch.mockImplementationOnce(() =>
      Promise.resolve({
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
            openToShareField: false,
            shareWithTeamId: null,
            reservedTeamIds: ["TEAM-1"],
          },
        ],
      })
    );

    render(<PracticePortalPage me={{ name: "Coach A" }} leagueId="league-1" />);

    expect(await screen.findByText("My Practice Requests")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Move" }));
    fireEvent.click(screen.getByLabelText("Book as shared practice"));
    fireEvent.change(screen.getByLabelText("Share with"), { target: { value: "TEAM-2" } });
    fireEvent.change(screen.getByLabelText("Date"), { target: { value: "2026-04-06" } });

    await waitFor(() => {
      expect(screen.getByText("Gunston Turf")).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole("button", { name: "Move Here" }));

    expect(await screen.findByText(/Move from 2026-04-05/)).toBeInTheDocument();
    expect(screen.getAllByText("Gunston Turf").length).toBeGreaterThan(0);
    expect(screen.getByText("Sharing with TEAM-2")).toBeInTheDocument();
  });
});
