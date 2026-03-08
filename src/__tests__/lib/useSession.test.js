import { describe, it, expect, beforeEach } from "vitest";
import { getInitialLeagueId, persistLeagueId } from "../../lib/useSession";
import { LEAGUE_STORAGE_KEY } from "../../lib/constants";

describe("useSession helpers", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it("persists only the canonical league storage key", () => {
    persistLeagueId("league-123");

    expect(localStorage.setItem).toHaveBeenCalledWith(LEAGUE_STORAGE_KEY, "league-123");
    expect(localStorage.setItem).toHaveBeenCalledTimes(1);
    expect(localStorage.removeItem).not.toHaveBeenCalled();
  });

  it("clears only the canonical league storage key", () => {
    persistLeagueId("");

    expect(localStorage.removeItem).toHaveBeenCalledWith(LEAGUE_STORAGE_KEY);
    expect(localStorage.removeItem).toHaveBeenCalledTimes(1);
  });

  it("prefers the canonical stored league id", () => {
    localStorage.getItem.mockImplementation((key) => (key === LEAGUE_STORAGE_KEY ? "league-123" : ""));

    const leagueId = getInitialLeagueId({
      homeLeagueId: "home-league",
      memberships: [{ leagueId: "fallback-league" }],
    });

    expect(leagueId).toBe("league-123");
    expect(localStorage.getItem).toHaveBeenCalledTimes(1);
  });
});
