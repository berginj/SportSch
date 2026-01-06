import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";

export default function LeaguePicker({ leagueId, setLeagueId, me, label = "League", title }) {
  const memberships = Array.isArray(me?.memberships) ? me.memberships : [];
  const isGlobalAdmin = !!me?.isGlobalAdmin;
  const [globalLeagues, setGlobalLeagues] = useState([]);
  const [globalErr, setGlobalErr] = useState("");
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!isGlobalAdmin) return;
    let cancelled = false;
    (async () => {
      setLoading(true);
      setGlobalErr("");
      try {
        const list = await apiFetch("/api/global/leagues");
        if (!cancelled) setGlobalLeagues(Array.isArray(list) ? list : []);
      } catch (e) {
        if (!cancelled) {
          setGlobalErr(e?.message || "Failed to load leagues.");
          setGlobalLeagues([]);
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [isGlobalAdmin]);

  const roleByLeague = useMemo(() => {
    const map = new Map();
    for (const m of memberships) {
      const id = (m?.leagueId || "").trim();
      const role = (m?.role || "").trim();
      if (id) map.set(id, role);
    }
    return map;
  }, [memberships]);

  const leagueOptions = useMemo(() => {
    if (isGlobalAdmin && globalLeagues.length) {
      return globalLeagues.map((l) => {
        const id = (l?.leagueId || "").trim();
        const name = (l?.name || "").trim();
        const role = roleByLeague.get(id) || "";
        const labelText = name ? `${name} (${id})${role ? ` — ${role}` : ""}` : `${id}${role ? ` — ${role}` : ""}`;
        return { id, label: labelText };
      }).filter((x) => x.id);
    }
    return memberships.map((m) => {
      const id = (m?.leagueId || "").trim();
      const role = (m?.role || "").trim();
      return { id, label: role ? `${id} (${role})` : id };
    }).filter((x) => x.id);
  }, [isGlobalAdmin, globalLeagues, memberships, roleByLeague]);

  const hasLeagues = leagueOptions.length > 0;

  return (
    <label className="leaguePicker" title={title || "Switch the active league for this view."}>
      {label}
      <select
        value={leagueId || ""}
        onChange={(e) => setLeagueId(e.target.value)}
        disabled={!hasLeagues}
        aria-label="Select league"
      >
        {!hasLeagues ? (
          <option value="">{loading ? "Loading leagues..." : "No leagues"}</option>
        ) : (
          leagueOptions.map((opt) => (
            <option key={opt.id} value={opt.id}>
              {opt.label}
            </option>
          ))
        )}
      </select>
      {globalErr ? <div className="muted text-xs">{globalErr}</div> : null}
    </label>
  );
}
