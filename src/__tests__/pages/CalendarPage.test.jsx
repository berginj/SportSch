import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import CalendarPage from "../../pages/CalendarPage";
import * as api from "../../lib/api";

vi.mock("../../lib/api", () => ({
  apiFetch: vi.fn(),
}));

vi.mock("../../lib/telemetry", () => ({
  trackEvent: vi.fn(),
}));

vi.mock("@daypilot/daypilot-lite-react", () => ({
  DayPilotScheduler: function DayPilotSchedulerMock({ events = [], onEventClick }) {
    return (
      <div data-testid="daypilot-scheduler">
        {events.map((event) => (
          <button key={event.id} type="button" onClick={() => onEventClick?.({ e: { data: event } })}>
            {event.text}
          </button>
        ))}
      </div>
    );
  },
  DayPilotMonth: function DayPilotMonthMock({ events = [], onEventClick }) {
    return (
      <div data-testid="daypilot-month">
        {events.map((event) => (
          <button key={event.id} type="button" onClick={() => onEventClick?.({ e: { data: event } })}>
            {event.text}
          </button>
        ))}
      </div>
    );
  },
}));

vi.mock("../../components/LeaguePicker", () => ({
  default: function LeaguePicker() {
    return <div data-testid="league-picker" />;
  },
}));

vi.mock("../../components/StatusCard", () => ({
  default: function StatusCard() {
    return null;
  },
}));

vi.mock("../../components/Toast", () => ({
  default: function Toast() {
    return null;
  },
}));

vi.mock("../../components/Dialogs", () => ({
  ConfirmDialog: function ConfirmDialog() {
    return null;
  },
  PromptDialog: function PromptDialog() {
    return null;
  },
}));

vi.mock("../../lib/useDialogs", () => ({
  useConfirmDialog: () => ({
    confirmState: null,
    requestConfirm: vi.fn(),
    handleConfirm: vi.fn(),
    handleCancel: vi.fn(),
  }),
  usePromptDialog: () => ({
    promptState: null,
    promptValue: "",
    setPromptValue: vi.fn(),
    requestPrompt: vi.fn(),
    handleConfirm: vi.fn(),
    handleCancel: vi.fn(),
  }),
}));

function mockCalendarApi({
  events = [],
  slots = [
    {
      slotId: "slot-1",
      division: "U12",
      gameDate: "2026-04-07",
      startTime: "18:00",
      endTime: "20:00",
      fieldKey: "FIELD-1",
      displayName: "Field 1",
      gameType: "game",
      status: "Confirmed",
      homeTeamId: "TEAM-1",
      awayTeamId: "TEAM-2",
      isAvailability: false,
    },
  ],
  divisions = [
    { code: "U12", name: "Under 12" },
    { code: "U10", name: "Under 10" },
  ],
} = {}) {
  api.apiFetch.mockImplementation((path) => {
    const url = String(path || "");
    const parsedUrl = new URL(url, "https://example.test");
    const division = (parsedUrl.searchParams.get("division") || "").trim();
    const activeStatuses = new Set(
      ((parsedUrl.searchParams.get("status") || "").trim())
        .split(",")
        .map((value) => value.trim())
        .filter(Boolean)
    );
    if (url === "/api/league") {
      return Promise.resolve({
        season: {
          springStart: "2026-04-01",
          springEnd: "2026-06-30",
        },
      });
    }
    if (url === "/api/divisions") {
      return Promise.resolve(divisions);
    }
    if (url === "/api/fields") {
      return Promise.resolve([{ fieldKey: "FIELD-1", displayName: "Field 1" }]);
    }
    if (url.startsWith("/api/events?")) {
      if (Array.isArray(events)) return Promise.resolve(events);
      return Promise.resolve(events[division] || events.default || []);
    }
    if (url.startsWith("/api/slots?")) {
      const slotList = Array.isArray(slots) ? slots : (slots[division] || slots.default || []);
      const filteredSlots = !activeStatuses.size
        ? slotList
        : slotList.filter((slot) => activeStatuses.has((slot?.status || "").trim()));
      return Promise.resolve({
        items: filteredSlots,
        continuationToken: "",
        hasMore: false,
        pageSize: filteredSlots.length,
      });
    }
    throw new Error(`Unexpected apiFetch call: ${url}`);
  });
}

function renderCalendarPage({
  me = {
    memberships: [{ leagueId: "league-1", role: "Coach", team: { division: "U12", teamId: "TEAM-1" } }],
  },
} = {}) {
  return render(<CalendarPage me={me} leagueId="league-1" setLeagueId={vi.fn()} />);
}

async function expandWeekCards() {
  await screen.findByTestId("daypilot-scheduler");
}

describe("CalendarPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.history.replaceState({}, "", "/");
    localStorage.getItem.mockImplementation((key) => {
      if (key === "calendar-use-new-view") return "true";
      return null;
    });
    mockCalendarApi();
  });

  it("does not open the edit modal from week cards for non-admin users", async () => {
    renderCalendarPage();

    const matchup = await screen.findByText("TEAM-1 vs TEAM-2");
    fireEvent.click(matchup);

    expect(screen.queryByRole("dialog", { name: /Edit scheduled game/i })).not.toBeInTheDocument();
  });

  it("shows eventDate events in week cards", async () => {
    mockCalendarApi({
      events: [
        {
          eventId: "event-1",
          title: "Skills Clinic",
          type: "Practice",
          eventDate: "2026-04-08",
          startTime: "17:30",
          endTime: "19:00",
          location: "Field 1",
          notes: "Bring catching gear.",
        },
      ],
      slots: [],
    });

    renderCalendarPage();

    expect(await screen.findByText("Practice: Skills Clinic")).toBeInTheDocument();
    expect(screen.getAllByText(/17:30-19:00/).length).toBeGreaterThan(0);
  });

  it("shows accept actions for coaches on open week-card slots", async () => {
    mockCalendarApi({
      slots: [
        {
          slotId: "slot-open-1",
          division: "U12",
          gameDate: "2026-04-07",
          startTime: "18:00",
          endTime: "20:00",
          fieldKey: "FIELD-1",
          displayName: "Field 1",
          gameType: "game",
          status: "Open",
          offeringTeamId: "TEAM-1",
          isAvailability: false,
        },
      ],
    });

    renderCalendarPage({
      me: {
        memberships: [{ leagueId: "league-1", role: "Coach", team: { division: "U12", teamId: "TEAM-9" } }],
      },
    });

    expect(await screen.findByRole("button", { name: "Accept" })).toBeInTheDocument();
  });

  it("reloads calendar data when a server-backed filter changes", async () => {
    mockCalendarApi({
      slots: {
        default: [
          {
            slotId: "slot-1",
            division: "U12",
            gameDate: "2026-04-07",
            startTime: "18:00",
            endTime: "20:00",
            fieldKey: "FIELD-1",
            displayName: "Field 1",
            gameType: "game",
            status: "Confirmed",
            homeTeamId: "TEAM-1",
            awayTeamId: "TEAM-2",
            isAvailability: false,
          },
        ],
        U10: [
          {
            slotId: "slot-2",
            division: "U10",
            gameDate: "2026-04-07",
            startTime: "18:30",
            endTime: "20:00",
            fieldKey: "FIELD-1",
            displayName: "Field 1",
            gameType: "game",
            status: "Confirmed",
            homeTeamId: "TEAM-3",
            awayTeamId: "TEAM-4",
            isAvailability: false,
          },
        ],
      },
    });

    renderCalendarPage();

    await expandWeekCards();

    expect(await screen.findByText("TEAM-1 vs TEAM-2")).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Division"), { target: { value: "U10" } });

    expect(await screen.findByText("TEAM-3 vs TEAM-4")).toBeInTheDocument();
  });

  it("filters the current page view by team without reloading data", async () => {
    mockCalendarApi({
      slots: [
        {
          slotId: "slot-team-1",
          division: "U12",
          gameDate: "2026-04-07",
          startTime: "18:00",
          endTime: "20:00",
          fieldKey: "FIELD-1",
          displayName: "Field 1",
          gameType: "game",
          status: "Confirmed",
          homeTeamId: "TEAM-1",
          awayTeamId: "TEAM-2",
          isAvailability: false,
        },
        {
          slotId: "slot-team-2",
          division: "U12",
          gameDate: "2026-04-08",
          startTime: "18:00",
          endTime: "20:00",
          fieldKey: "FIELD-1",
          displayName: "Field 1",
          gameType: "game",
          status: "Confirmed",
          homeTeamId: "TEAM-3",
          awayTeamId: "TEAM-4",
          isAvailability: false,
        },
      ],
    });

    renderCalendarPage();

    await expandWeekCards();

    expect((await screen.findAllByText(/TEAM-1 vs TEAM-2/)).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/TEAM-3 vs TEAM-4/).length).toBeGreaterThan(0);

    fireEvent.change(screen.getByLabelText("Team"), { target: { value: "TEAM-1" } });

    expect((await screen.findAllByText(/TEAM-1 vs TEAM-2/)).length).toBeGreaterThan(0);
    expect(screen.queryAllByText(/TEAM-3 vs TEAM-4/)).toHaveLength(0);
  });

  it("offers a coach quick view for My Team", async () => {
    mockCalendarApi({
      slots: [
        {
          slotId: "slot-team-1",
          division: "U12",
          gameDate: "2026-04-07",
          startTime: "18:00",
          endTime: "20:00",
          fieldKey: "FIELD-1",
          displayName: "Field 1",
          gameType: "game",
          status: "Confirmed",
          homeTeamId: "TEAM-1",
          awayTeamId: "TEAM-2",
          isAvailability: false,
        },
        {
          slotId: "slot-team-2",
          division: "U12",
          gameDate: "2026-04-08",
          startTime: "18:00",
          endTime: "20:00",
          fieldKey: "FIELD-1",
          displayName: "Field 1",
          gameType: "game",
          status: "Confirmed",
          homeTeamId: "TEAM-3",
          awayTeamId: "TEAM-4",
          isAvailability: false,
        },
      ],
    });

    renderCalendarPage();

    await expandWeekCards();

    expect((await screen.findAllByText(/TEAM-1 vs TEAM-2/)).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/TEAM-3 vs TEAM-4/).length).toBeGreaterThan(0);

    fireEvent.click(screen.getByRole("button", { name: "My Team" }));

    expect((await screen.findAllByText(/TEAM-1 vs TEAM-2/)).length).toBeGreaterThan(0);
    expect(screen.queryAllByText(/TEAM-3 vs TEAM-4/)).toHaveLength(0);
  });

  it("defaults league admins to open slots when the calendar URL has no filters", async () => {
    mockCalendarApi({
      events: [
        {
          eventId: "event-1",
          title: "Skills Clinic",
          type: "Practice",
          eventDate: "2026-04-08",
          startTime: "17:30",
          endTime: "19:00",
          location: "Field 1",
        },
      ],
      slots: [
        {
          slotId: "slot-open-1",
          division: "U12",
          gameDate: "2026-04-07",
          startTime: "18:00",
          endTime: "20:00",
          fieldKey: "FIELD-1",
          displayName: "Field 1",
          gameType: "game",
          status: "Open",
          offeringTeamId: "TEAM-1",
          isAvailability: false,
        },
        {
          slotId: "slot-confirmed-1",
          division: "U12",
          gameDate: "2026-04-08",
          startTime: "18:00",
          endTime: "20:00",
          fieldKey: "FIELD-1",
          displayName: "Field 1",
          gameType: "game",
          status: "Confirmed",
          homeTeamId: "TEAM-3",
          awayTeamId: "TEAM-4",
          isAvailability: false,
        },
      ],
    });

    renderCalendarPage({
      me: {
        memberships: [{ leagueId: "league-1", role: "LeagueAdmin" }],
      },
    });

    await expandWeekCards();

    expect((await screen.findAllByText(/TEAM-1 vs TBD/)).length).toBeGreaterThan(0);
    expect(screen.queryAllByText(/TEAM-3 vs TEAM-4/)).toHaveLength(0);
    expect(screen.queryByText(/^Practice: Skills Clinic/)).not.toBeInTheDocument();

    const apiPaths = api.apiFetch.mock.calls.map(([path]) => String(path || ""));
    expect(apiPaths.some((path) => path.startsWith("/api/events?"))).toBe(false);
    expect(apiPaths.some((path) => path.startsWith("/api/slots?") && path.includes("status=Open"))).toBe(true);
  });

  it("lets admins replace the default open-slot view with explicit filters", async () => {
    mockCalendarApi({
      events: [
        {
          eventId: "event-1",
          title: "Skills Clinic",
          type: "Practice",
          eventDate: "2026-04-08",
          startTime: "17:30",
          endTime: "19:00",
          location: "Field 1",
        },
      ],
      slots: [
        {
          slotId: "slot-open-1",
          division: "U12",
          gameDate: "2026-04-07",
          startTime: "18:00",
          endTime: "20:00",
          fieldKey: "FIELD-1",
          displayName: "Field 1",
          gameType: "game",
          status: "Open",
          offeringTeamId: "TEAM-1",
          isAvailability: false,
        },
        {
          slotId: "slot-confirmed-1",
          division: "U12",
          gameDate: "2026-04-08",
          startTime: "18:00",
          endTime: "20:00",
          fieldKey: "FIELD-1",
          displayName: "Field 1",
          gameType: "game",
          status: "Confirmed",
          homeTeamId: "TEAM-3",
          awayTeamId: "TEAM-4",
          isAvailability: false,
        },
      ],
    });

    renderCalendarPage({
      me: {
        memberships: [{ leagueId: "league-1", role: "LeagueAdmin" }],
      },
    });

    await expandWeekCards();

    expect((await screen.findAllByText(/TEAM-1 vs TBD/)).length).toBeGreaterThan(0);
    expect(screen.queryAllByText(/TEAM-3 vs TEAM-4/)).toHaveLength(0);
    expect(screen.queryByText(/^Practice: Skills Clinic/)).not.toBeInTheDocument();

    fireEvent.click(screen.getByLabelText("Events"));
    fireEvent.click(screen.getByLabelText("Confirmed"));
    fireEvent.click(screen.getByLabelText("Open"));

    expect((await screen.findAllByText(/TEAM-3 vs TEAM-4/)).length).toBeGreaterThan(0);
    expect(screen.queryAllByText(/TEAM-1 vs TBD/)).toHaveLength(0);
    expect(screen.getByText(/^Practice: Skills Clinic/)).toBeInTheDocument();
    expect(screen.getByLabelText("Events")).toBeChecked();
    expect(screen.getByLabelText("Confirmed")).toBeChecked();
    expect(screen.getByLabelText("Open")).not.toBeChecked();
  });
});
