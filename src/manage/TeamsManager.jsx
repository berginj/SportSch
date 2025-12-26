import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import { ROLE } from "../lib/constants";
import Toast from "../components/Toast";

export default function TeamsManager({ leagueId }) {
  const [teams, setTeams] = useState([]);
  const [memberships, setMemberships] = useState([]);
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState("");
  const [ok, setOk] = useState("");
  const [toast, setToast] = useState(null);

  const [teamsFile, setTeamsFile] = useState(null);
  const [teamsBusy, setTeamsBusy] = useState(false);
  const [teamsErrors, setTeamsErrors] = useState([]);

  const [coachDraft, setCoachDraft] = useState({});

  async function load() {
    if (!leagueId) return;
    setErr("");
    setLoading(true);
    try {
      const [t, m] = await Promise.all([apiFetch("/api/teams"), apiFetch("/api/memberships")]);
      setTeams(Array.isArray(t) ? t : []);
      setMemberships(Array.isArray(m) ? m : []);
    } catch (e) {
      setErr(e?.message || "Failed to load teams or memberships.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId]);

  const coaches = useMemo(() => {
    return (memberships || []).filter((m) => (m.role || "").trim() === ROLE.COACH);
  }, [memberships]);

  const teamsByDivision = useMemo(() => {
    const map = new Map();
    for (const t of teams || []) {
      const div = (t.division || "").trim();
      if (!div) continue;
      if (!map.has(div)) map.set(div, []);
      map.get(div).push(t);
    }
    for (const [k, v] of map.entries()) {
      v.sort((a, b) => (a.name || a.teamId || "").localeCompare(b.name || b.teamId || ""));
      map.set(k, v);
    }
    return map;
  }, [teams]);

  function setDraftForCoach(userId, patch) {
    setCoachDraft((prev) => {
      const cur = prev[userId] || { division: "", teamId: "" };
      return { ...prev, [userId]: { ...cur, ...patch } };
    });
  }

  async function saveCoachAssignment(userId) {
    const draft = coachDraft[userId] || { division: "", teamId: "" };
    const division = (draft.division || "").trim();
    const teamId = (draft.teamId || "").trim();
    const body = division && teamId ? { team: { division, teamId } } : { team: null };
    try {
      await apiFetch(`/api/memberships/${encodeURIComponent(userId)}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      await load();
      setOk("Coach assignment updated.");
      setToast({ tone: "success", message: "Coach assignment updated." });
    } catch (e) {
      setErr(e?.message || "Failed to update coach assignment.");
    }
  }

  async function clearCoachAssignment(userId) {
    setDraftForCoach(userId, { division: "", teamId: "" });
    await saveCoachAssignment(userId);
  }

  async function importTeamsCsv() {
    setErr("");
    setOk("");
    setTeamsErrors([]);
    if (!teamsFile) return setErr("Choose a CSV file to upload.");

    setTeamsBusy(true);
    try {
      const fd = new FormData();
      fd.append("file", teamsFile);
      const res = await apiFetch("/api/import/teams", { method: "POST", body: fd });
      setOk(`Imported. Upserted: ${res?.upserted ?? 0}, Rejected: ${res?.rejected ?? 0}, Skipped: ${res?.skipped ?? 0}`);
      setToast({ tone: "success", message: "Teams import complete." });
      if (Array.isArray(res?.errors) && res.errors.length) setTeamsErrors(res.errors);
      await load();
    } catch (e) {
      setErr(e?.message || "Import failed");
    } finally {
      setTeamsBusy(false);
    }
  }

  return (
    <div className="stack">
      {err ? <div className="callout callout--error">{err}</div> : null}
      {ok ? <div className="callout callout--ok">{ok}</div> : null}
      <Toast
        open={!!toast}
        tone={toast?.tone}
        message={toast?.message}
        onClose={() => setToast(null)}
      />

      <div className="card">
        <div className="font-bold mb-2">Teams CSV upload</div>
        <div className="subtle mb-2">
          Required columns: <code>division</code>, <code>teamId</code>, <code>name</code>. Optional:{" "}
          <code>coachName</code>, <code>coachEmail</code>, <code>coachPhone</code>.
        </div>
        <div className="row items-end gap-3">
          <label className="flex-1" title="Upload a CSV of teams to create or update.">
            CSV file
            <input
              type="file"
              accept=".csv,text/csv"
              onChange={(e) => setTeamsFile(e.target.files?.[0] || null)}
              disabled={teamsBusy}
            />
          </label>
          <button className="btn" onClick={importTeamsCsv} disabled={teamsBusy || !teamsFile} title="Import teams from CSV.">
            {teamsBusy ? "Importing..." : "Upload & Import"}
          </button>
          <button className="btn btn--ghost" onClick={load} disabled={loading} title="Refresh teams and coaches.">
            Refresh
          </button>
        </div>
        {teamsErrors.length ? (
          <div className="mt-3">
            <div className="font-bold mb-2">Rejected rows ({teamsErrors.length})</div>
            <table className="table">
              <thead>
                <tr>
                  <th>Row</th>
                  <th>Error</th>
                </tr>
              </thead>
              <tbody>
                {teamsErrors.slice(0, 50).map((x, idx) => (
                  <tr key={idx}>
                    <td>{x.row}</td>
                    <td>{x.error}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {teamsErrors.length > 50 ? <div className="subtle">Showing first 50.</div> : null}
          </div>
        ) : null}
      </div>

      <div className="card">
        <div className="font-bold mb-2">Teams</div>
        {loading ? (
          <div className="subtle">Loading...</div>
        ) : teams.length === 0 ? (
          <div className="subtle">No teams yet.</div>
        ) : (
          <div className="tableWrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Division</th>
                  <th>Team</th>
                  <th>Name</th>
                  <th>Coach contact</th>
                </tr>
              </thead>
              <tbody>
                {teams.map((t) => (
                  <tr key={`${t.division}-${t.teamId}`}>
                    <td>{t.division}</td>
                    <td>{t.teamId}</td>
                    <td>{t.name}</td>
                    <td>
                      <div>{t?.primaryContact?.name || "-"}</div>
                      <div className="muted text-xs">
                        {(t?.primaryContact?.email || "") + (t?.primaryContact?.phone ? ` | ${t.primaryContact.phone}` : "")}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div className="card">
        <div className="font-bold mb-2">Coach assignments</div>
        <div className="subtle mb-2">
          Assign a coach to a team for scheduling permissions.
        </div>
        {coaches.length === 0 ? (
          <div className="subtle">No coaches in this league yet.</div>
        ) : (
          <div className="tableWrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Coach</th>
                  <th>Current team</th>
                  <th>Assign to</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {coaches.map((c) => {
                  const draft = coachDraft[c.userId] || { division: c.team?.division || "", teamId: c.team?.teamId || "" };
                  const currentDiv = (draft.division || "").trim();
                  const teamsForDiv = currentDiv ? (teamsByDivision.get(currentDiv) || []) : [];

                  return (
                    <tr key={c.userId}>
                      <td>
                        <div className="font-semibold">{c.email || c.userId}</div>
                        <div className="muted text-xs">{c.userId}</div>
                      </td>
                      <td>{c.team?.division && c.team?.teamId ? `${c.team.division} / ${c.team.teamId}` : "-"}</td>
                      <td>
                        <div className="row">
                          <select
                            value={draft.division || ""}
                            onChange={(e) => setDraftForCoach(c.userId, { division: e.target.value, teamId: "" })}
                            title="Choose a division to filter teams."
                          >
                            <option value="">Division</option>
                            {[...teamsByDivision.keys()].sort().map((d) => (
                              <option key={d} value={d}>
                                {d}
                              </option>
                            ))}
                          </select>
                          <select
                            value={draft.teamId || ""}
                            onChange={(e) => setDraftForCoach(c.userId, { teamId: e.target.value })}
                            disabled={!draft.division}
                            title="Choose a team within the selected division."
                          >
                            <option value="">Team</option>
                            {teamsForDiv.map((t) => (
                              <option key={t.teamId} value={t.teamId}>
                                {t.name || t.teamId}
                              </option>
                            ))}
                          </select>
                        </div>
                      </td>
                      <td className="text-right">
                        <div className="row">
                          <button className="btn" onClick={() => saveCoachAssignment(c.userId)} title="Save coach assignment.">
                            Save
                          </button>
                          <button className="btn btn--ghost" onClick={() => clearCoachAssignment(c.userId)} title="Clear coach assignment.">
                            Clear
                          </button>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
