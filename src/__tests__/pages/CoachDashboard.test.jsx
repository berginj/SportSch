import { describe, it, expect, beforeEach, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import CoachDashboard from "../../pages/CoachDashboard";
import * as api from "../../lib/api";

vi.mock("../../lib/api", () => ({
  apiFetch: vi.fn(),
}));

describe("CoachDashboard", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.history.replaceState({}, "", "/");

    api.apiFetch.mockImplementation((path) => {
      const url = String(path || "");
      if (url === "/api/coach/dashboard") {
        return Promise.resolve({
          team: {
            division: "AAA",
            teamId: "TEAM-1",
            name: "Blue Waves",
          },
          upcomingGames: [
            {
              slotId: "confirmed-game",
              gameDate: "2099-01-15",
              startTime: "18:00",
              fieldKey: "FIELD-1",
              displayName: "Main Field",
              homeTeamId: "TEAM-1",
              awayTeamId: "",
              confirmedTeamId: "TEAM-2",
            },
          ],
          openOffersInDivision: 1,
          myOpenOffers: 1,
        });
      }
      throw new Error(`Unexpected apiFetch call: ${url}`);
    });
  });

  it("renders coach actions from the consolidated dashboard endpoint", async () => {
    render(
      <CoachDashboard
        leagueId="league-1"
        setTab={() => {}}
      />
    );

    await waitFor(() => expect(screen.getByText("Coach Dashboard")).toBeInTheDocument());

    const divisionOffers = screen.getByText("Open offers in division").closest(".layoutStat");
    expect(divisionOffers).toHaveTextContent("1");

    const myOffers = screen.getByText("Your open offers").closest(".layoutStat");
    expect(myOffers).toHaveTextContent("1");

    const upcomingGames = screen.getByText("Upcoming games").closest(".layoutStat");
    expect(upcomingGames).toHaveTextContent("1");

    expect(screen.getByText("1 New Offer")).toBeInTheDocument();
    expect(screen.getByText("vs TEAM-2 - Main Field")).toBeInTheDocument();
  });

  it("renders dashboard failures with an error tone", async () => {
    api.apiFetch.mockRejectedValueOnce(new Error("Dashboard unavailable"));

    render(
      <CoachDashboard
        leagueId="league-1"
        setTab={() => {}}
      />
    );

    await waitFor(() => expect(screen.getByText("Error")).toBeInTheDocument());
    expect(screen.getByText("Error").closest(".statusCard")).toHaveClass("statusCard--error");
    expect(screen.getByText("Dashboard unavailable")).toBeInTheDocument();
  });

  it("routes quick actions into the intended offer and calendar workflows", async () => {
    const setTab = vi.fn();
    const replaceStateSpy = vi.spyOn(window.history, "replaceState");

    render(
      <CoachDashboard
        leagueId="league-1"
        setTab={setTab}
      />
    );

    await waitFor(() => expect(screen.getByText("Coach Dashboard")).toBeInTheDocument());

    fireEvent.click(screen.getByRole("button", { name: "Offer a Game Slot" }));
    expect(setTab).toHaveBeenLastCalledWith("offers");
    expect(replaceStateSpy).toHaveBeenLastCalledWith({}, "", expect.stringContaining("division=AAA"));
    expect(replaceStateSpy).toHaveBeenLastCalledWith({}, "", expect.stringContaining("slotType=offer"));
    expect(replaceStateSpy).toHaveBeenLastCalledWith({}, "", expect.stringContaining("#offers"));

    fireEvent.click(screen.getByRole("button", { name: "Browse Available Slots" }));
    expect(setTab).toHaveBeenLastCalledWith("calendar");
    expect(replaceStateSpy).toHaveBeenLastCalledWith({}, "", expect.stringContaining("showSlots=1"));
    expect(replaceStateSpy).toHaveBeenLastCalledWith({}, "", expect.not.stringContaining("showEvents=1"));
    expect(replaceStateSpy).toHaveBeenLastCalledWith({}, "", expect.stringContaining("slotType=offer"));
    expect(replaceStateSpy).toHaveBeenLastCalledWith({}, "", expect.stringContaining("status=Open"));
    expect(replaceStateSpy).toHaveBeenLastCalledWith({}, "", expect.stringContaining("#calendar"));

    fireEvent.click(screen.getByRole("button", { name: "View Team Schedule" }));
    expect(setTab).toHaveBeenLastCalledWith("calendar");
    expect(replaceStateSpy).toHaveBeenLastCalledWith({}, "", expect.stringContaining("showSlots=1"));
    expect(replaceStateSpy).toHaveBeenLastCalledWith({}, "", expect.stringContaining("showEvents=1"));
    expect(replaceStateSpy).toHaveBeenLastCalledWith({}, "", expect.stringContaining("status=Confirmed"));
    expect(replaceStateSpy).toHaveBeenLastCalledWith({}, "", expect.stringContaining("teamId=TEAM-1"));
  });
});
