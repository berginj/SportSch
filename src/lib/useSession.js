import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "./api";
import { LEAGUE_STORAGE_KEY } from "./constants";

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

export function getInitialLeagueId(me) {
  // 1) Persisted value
  try {
    const saved = (localStorage.getItem(LEAGUE_STORAGE_KEY) || "").trim();
    if (saved) return saved;
  } catch {
    // ignore
  }

  // 2) First membership, if any
  const memberships = Array.isArray(me?.memberships) ? me.memberships : [];
  return (memberships[0]?.leagueId || "").trim();
}

export function useSession() {
  const [me, setMe] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setLoading(true);
      setError("");
      try {
        const data = await apiFetch("/api/me");
        if (!cancelled) setMe(data || {});
      } catch (e) {
        if (!cancelled) setError(e?.message || "Failed to load session");
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

  const [activeLeagueId, setActiveLeagueId] = useState("");

  // Pick an initial leagueId once `me` loads.
  useEffect(() => {
    if (!me) return;
    const initial = getInitialLeagueId(me);
    if (initial && !activeLeagueId) setActiveLeagueId(initial);
  }, [me]); // intentionally omit activeLeagueId

  // Persist league changes
  useEffect(() => {
    if (activeLeagueId) persistLeagueId(activeLeagueId);
  }, [activeLeagueId]);

  return {
    me: me || {},
    memberships,
    hasMemberships,
    isGlobalAdmin,
    activeLeagueId,
    setActiveLeagueId,
    loading,
    error,
    refreshMe: async () => {
      const data = await apiFetch("/api/me");
      setMe(data || {});
      return data;
    },
  };
}
