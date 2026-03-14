import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import CalendarView from "../../components/CalendarView";

let lastSchedulerProps = null;

vi.mock("@daypilot/daypilot-lite-react", () => ({
  DayPilotScheduler: function DayPilotSchedulerMock(props) {
    lastSchedulerProps = props;
    const { events = [], onEventClick } = props;
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

const BASE_SLOT = {
  slotId: "slot-1",
  gameDate: "2026-03-16",
  startTime: "18:00",
  endTime: "19:00",
  fieldKey: "field-1",
  displayName: "County Diamond 9",
  homeTeamId: "AGSA",
};

describe("CalendarView", () => {
  beforeEach(() => {
    localStorage.clear();
    lastSchedulerProps = null;
  });

  it("keeps view preference scoped to the provided storage key", async () => {
    const setItemSpy = vi.spyOn(window.localStorage, "setItem");

    const { unmount } = render(
      <CalendarView
        slots={[BASE_SLOT]}
        defaultView="timeline"
        showViewToggle={true}
        viewStorageKey="field-inventory-view"
      />
    );

    expect(await screen.findByTestId("daypilot-scheduler")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Month" }));
    expect(setItemSpy).toHaveBeenCalledWith("field-inventory-view", "month");
    unmount();

    render(
      <CalendarView
        slots={[BASE_SLOT]}
        defaultView="timeline"
        showViewToggle={true}
        viewStorageKey="calendar-page-view"
      />
    );

    expect(await screen.findByTestId("daypilot-scheduler")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Month" }));
    expect(setItemSpy).toHaveBeenCalledWith("calendar-page-view", "month");
    expect(setItemSpy).not.toHaveBeenCalledWith("calendar-view-preference", "month");
  });

  it("treats legacy week-cards defaults as timeline", () => {
    render(
      <CalendarView
        slots={[BASE_SLOT]}
        defaultView="week-cards"
        showViewToggle={false}
      />
    );

    expect(screen.getByTestId("daypilot-scheduler")).toBeInTheDocument();
  });

  it("shows mapped and unmapped availability states distinctly", () => {
    render(
      <CalendarView
        slots={[
          {
            ...BASE_SLOT,
            slotId: "mapped-slot",
            isAvailability: true,
            mappingStatus: "mapped",
            status: "available",
          },
          {
            ...BASE_SLOT,
            slotId: "unmapped-slot",
            displayName: "County Diamond 10",
            fieldKey: "field-2",
            isAvailability: true,
            mappingStatus: "unmapped",
            status: "available",
          },
        ]}
        defaultView="timeline"
        showViewToggle={false}
      />
    );

    expect(screen.getByRole("button", { name: /AGSA vs TBD \| Mapped$/i })).toBeInTheDocument();
    const unmappedButton = screen.getByRole("button", { name: /AGSA vs TBD \| Unmapped$/i });
    expect(unmappedButton).toBeInTheDocument();

    fireEvent.click(unmappedButton);

    const badge = screen.getByText("Unmapped");
    expect(badge.className).toContain("calendar-selection__badge--unmapped");
  });

  it("limits timeline hours to the visible content window with a buffer", () => {
    render(
      <CalendarView
        slots={[
          {
            ...BASE_SLOT,
            startTime: "18:30",
            endTime: "20:00",
          },
        ]}
        events={[
          {
            eventId: "event-1",
            title: "Skills Clinic",
            eventDate: "2026-03-16",
            startTime: "17:00",
            endTime: "21:30",
            location: "County Diamond 9",
          },
        ]}
        defaultView="timeline"
        showViewToggle={false}
      />
    );

    expect(lastSchedulerProps.businessBeginsHour).toBe(16);
    expect(lastSchedulerProps.businessEndsHour).toBe(23);
  });
});
