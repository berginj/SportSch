import { useState, useEffect, useCallback } from 'react';
import { apiFetch } from '../api';

/**
 * Custom hook for managing access requests.
 * @param {string} leagueId - Current league ID
 * @param {boolean} isGlobalAdmin - Whether user is global admin
 * @param {string} accessStatus - Filter by status (Pending, Approved, Denied)
 * @param {string} accessScope - Scope (league or all)
 * @param {string} accessLeagueFilter - League filter for global admin view
 * @returns {object} Access requests state and actions
 */
export function useAccessRequests(leagueId, isGlobalAdmin, accessStatus, accessScope, accessLeagueFilter) {
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState("");
  const [items, setItems] = useState([]);

  const load = useCallback(async () => {
    if (!leagueId && accessScope !== "all") {
      setLoading(false);
      return;
    }

    setLoading(true);
    setErr("");

    try {
      let url = `/api/accessrequests?status=${encodeURIComponent(accessStatus)}`;
      if (accessScope === "all" && isGlobalAdmin) {
        url += "&all=true";
      }

      const data = await apiFetch(url);
      let filtered = data || [];

      // Apply league filter for global admin view
      if (accessScope === "all" && accessLeagueFilter) {
        filtered = filtered.filter(r =>
          (r.leagueId || "").toLowerCase().includes(accessLeagueFilter.toLowerCase())
        );
      }

      setItems(filtered);
    } catch (error) {
      setErr(error.message || "Failed to load access requests");
      setItems([]);
    } finally {
      setLoading(false);
    }
  }, [leagueId, isGlobalAdmin, accessStatus, accessScope, accessLeagueFilter]);

  useEffect(() => {
    load();
  }, [load]);

  const approve = async (userId) => {
    try {
      await apiFetch(`/api/accessrequests/${userId}/approve`, {
        method: "PATCH",
        body: JSON.stringify({}),
      });
      await load();
      return { success: true };
    } catch (error) {
      return { success: false, error: error.message || "Failed to approve" };
    }
  };

  const deny = async (userId, reason = "") => {
    try {
      await apiFetch(`/api/accessrequests/${userId}/deny`, {
        method: "PATCH",
        body: JSON.stringify({ reason }),
      });
      await load();
      return { success: true };
    } catch (error) {
      return { success: false, error: error.message || "Failed to deny" };
    }
  };

  return {
    items,
    loading,
    err,
    reload: load,
    approve,
    deny,
  };
}
