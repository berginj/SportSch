import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "./api";
import { ErrorCodes, LEAGUE_STORAGE_KEY } from "./constants";

export function persistLeagueId(leagueId) {
  try {
    if (!leagueId) {
      localStorage.removeItem(LEAGUE_STORAGE_KEY);
    } else {
      localStorage.setItem(LEAGUE_STORAGE_KEY, leagueId);
    }
  } catch {
    // ignore
  }
}

export function isLeagueAccessible(me, leagueId) {
  const normalizedLeagueId = String(leagueId || "").trim();
  if (!normalizedLeagueId) return false;
  if (me?.isGlobalAdmin) return true;
  const memberships = Array.isArray(me?.memberships) ? me.memberships : [];
  return memberships.some((membership) => String(membership?.leagueId || "").trim() === normalizedLeagueId);
}

export function getInitialLeagueId(me, options = {}) {
  const { includeStored = true } = options;

  // 1) Persisted value, but only if still accessible for this session.
  try {
    if (includeStored) {
      const saved = (localStorage.getItem(LEAGUE_STORAGE_KEY) || "").trim();
      if (isLeagueAccessible(me, saved)) return saved;
    }
  } catch {
    // ignore
  }

  // 2) Home league preference
  const homeLeagueId = (me?.homeLeagueId || "").trim();
  if (isLeagueAccessible(me, homeLeagueId)) return homeLeagueId;

  // 3) First membership, if any
  const memberships = Array.isArray(me?.memberships) ? me.memberships : [];
  return (memberships[0]?.leagueId || "").trim();
}

export function isUnauthenticatedError(error) {
  if (!error) return false;
  if (Number(error?.status) === 401) return true;
  return String(error?.code || "").trim() === ErrorCodes.UNAUTHENTICATED;
}

export function useSession() {
  const [me, setMe] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  const markSignedOut = (message) => {
    setMe({ userId: "UNKNOWN", email: "UNKNOWN", memberships: [] });
    persistLeagueId(""); // Clear invalid leagueId on logout
    if (message) setError(message);
  };

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setLoading(true);
      setError("");
      try {
        const data = await apiFetch("/api/me");
        if (!cancelled) setMe(data || {});
      } catch (e) {
        if (cancelled) return;
        const message = e?.message || "Failed to load session";
        if (isUnauthenticatedError(e)) {
          markSignedOut(message);
        } else {
          setError(message);
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }
    load();
    return () => {
      cancelled = true;
    };
  }, []);

  const memberships = useMemo(() => (Array.isArray(me?.memberships) ? me.memberships : []), [me]);
  const hasMemberships = memberships.length > 0;
  const isGlobalAdmin = !!me?.isGlobalAdmin;

  const [leagueId, setLeagueId] = useState(() => {
    try {
      return (localStorage.getItem(LEAGUE_STORAGE_KEY) || "").trim();
    } catch {
      return "";
    }
  });

  // Pick an initial leagueId once `me` loads.
  useEffect(() => {
    if (!me) return;
    const initial = getInitialLeagueId(me);
    if (initial) {
      setLeagueId((prev) => prev || initial);
    }
  }, [me]);

  // Validate leagueId is in user's memberships; clear if invalid or user removed from league
  useEffect(() => {
    if (!me || !Array.isArray(me.memberships)) return;

    if (leagueId && !isLeagueAccessible(me, leagueId)) {
      // User no longer belongs to this league; clear stale storage and pick a new valid one.
      persistLeagueId("");
      const fallback = getInitialLeagueId(me, { includeStored: false });
      setLeagueId(fallback || "");
    }
  }, [me, leagueId]);

  // Persist league changes
  useEffect(() => {
    persistLeagueId(leagueId);
  }, [leagueId]);

  return {
    me: me || {},
    memberships,
    hasMemberships,
    isGlobalAdmin,
    leagueId,
    setLeagueId,
    loading,
    error,
    refreshMe: async () => {
      try {
        const data = await apiFetch("/api/me");
        setMe(data || {});
        return data;
      } catch (e) {
        const message = e?.message || "Failed to load session";
        if (isUnauthenticatedError(e)) {
          markSignedOut(message);
          return { userId: "UNKNOWN", email: "UNKNOWN", memberships: [] };
        }
        throw e;
      }
    },
  };
}
