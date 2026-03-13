import { describe, it, expect, beforeEach, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { getInitialLeagueId, isLeagueAccessible, isUnauthenticatedError, persistLeagueId, useSession } from "../../lib/useSession";
import { ErrorCodes, LEAGUE_STORAGE_KEY } from "../../lib/constants";
import * as api from "../../lib/api";

vi.mock("../../lib/api", () => ({
  apiFetch: vi.fn(),
}));

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
      isGlobalAdmin: false,
      memberships: [{ leagueId: "league-123" }, { leagueId: "fallback-league" }],
    });

    expect(leagueId).toBe("league-123");
    expect(localStorage.getItem).toHaveBeenCalledTimes(1);
  });

  it("ignores a stale stored league id and falls back to the first accessible membership", () => {
    localStorage.getItem.mockImplementation((key) => (key === LEAGUE_STORAGE_KEY ? "stale-league" : ""));

    const leagueId = getInitialLeagueId({
      homeLeagueId: "home-league",
      isGlobalAdmin: false,
      memberships: [{ leagueId: "league-123" }, { leagueId: "fallback-league" }],
    });

    expect(leagueId).toBe("league-123");
  });

  it("accepts a stored league id for global admins without memberships", () => {
    localStorage.getItem.mockImplementation((key) => (key === LEAGUE_STORAGE_KEY ? "league-123" : ""));

    const leagueId = getInitialLeagueId({
      homeLeagueId: "",
      isGlobalAdmin: true,
      memberships: [],
    });

    expect(leagueId).toBe("league-123");
  });

  it("reports league accessibility from memberships unless the user is a global admin", () => {
    expect(isLeagueAccessible({ isGlobalAdmin: false, memberships: [{ leagueId: "league-123" }] }, "league-123")).toBe(true);
    expect(isLeagueAccessible({ isGlobalAdmin: false, memberships: [{ leagueId: "league-123" }] }, "other-league")).toBe(false);
    expect(isLeagueAccessible({ isGlobalAdmin: true, memberships: [] }, "other-league")).toBe(true);
  });

  it("treats structured 401 responses as unauthenticated", () => {
    expect(isUnauthenticatedError({ status: 401, code: ErrorCodes.UNAUTHENTICATED })).toBe(true);
    expect(isUnauthenticatedError({ status: 401, code: null })).toBe(true);
    expect(isUnauthenticatedError({ status: 403, code: ErrorCodes.FORBIDDEN })).toBe(false);
  });
});

describe("useSession hook", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it("preserves the stored league id until session data loads", async () => {
    localStorage.getItem.mockImplementation((key) => (key === LEAGUE_STORAGE_KEY ? "league-123" : ""));
    api.apiFetch.mockResolvedValueOnce({
      userId: "user-1",
      email: "user@example.com",
      homeLeagueId: "",
      isGlobalAdmin: false,
      memberships: [{ leagueId: "league-123" }, { leagueId: "league-456" }],
    });

    const { result } = renderHook(() => useSession());

    expect(result.current.leagueId).toBe("league-123");
    expect(localStorage.removeItem).not.toHaveBeenCalledWith(LEAGUE_STORAGE_KEY);

    await waitFor(() => expect(result.current.loading).toBe(false));

    expect(result.current.leagueId).toBe("league-123");
    expect(localStorage.removeItem).not.toHaveBeenCalledWith(LEAGUE_STORAGE_KEY);
  });

  it("marks the session signed out for structured unauthenticated responses", async () => {
    api.apiFetch.mockRejectedValueOnce({
      status: 401,
      code: ErrorCodes.UNAUTHENTICATED,
      message: "Please sign in to continue.",
    });

    const { result } = renderHook(() => useSession());

    await waitFor(() => expect(result.current.loading).toBe(false));

    expect(result.current.me.userId).toBe("UNKNOWN");
    expect(result.current.error).toBe("Please sign in to continue.");
    expect(localStorage.removeItem).toHaveBeenCalledWith(LEAGUE_STORAGE_KEY);
  });
});
