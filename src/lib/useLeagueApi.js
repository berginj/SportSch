import { useCallback } from "react";
import { apiFetch } from "./api";

// Capture the tenant of this mounted workflow, including follow-up requests
// made after an await. Never infer a new tenant halfway through a workflow.
export function useLeagueApi(leagueId) {
  return useCallback((path, options = {}) => apiFetch(path, { ...options, leagueId }), [leagueId]);
}
