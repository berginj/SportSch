import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import LeaguePicker from "../components/LeaguePicker";
import Toast from "../components/Toast";
import { PromptDialog } from "../components/Dialogs";
import { usePromptDialog } from "../lib/useDialogs";

const ROLE_OPTIONS = [
  "LeagueAdmin",
  "Coach",
  "Viewer",
];

export default function AdminPage({ me, leagueId, setLeagueId }) {
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState("");
  const [items, setItems] = useState([]);
  const [memLoading, setMemLoading] = useState(false);
  const [memberships, setMemberships] = useState([]);
  const [divisions, setDivisions] = useState([]);
  const [teams, setTeams] = useState([]);
  const [coachDraft, setCoachDraft] = useState({});
  const [slotsFile, setSlotsFile] = useState(null);
  const [teamsFile, setTeamsFile] = useState(null);
  const [slotsBusy, setSlotsBusy] = useState(false);
  const [teamsBusy, setTeamsBusy] = useState(false);
  const [slotsErr, setSlotsErr] = useState("");
  const [teamsErr, setTeamsErr] = useState("");
  const [slotsOk, setSlotsOk] = useState("");
  const [teamsOk, setTeamsOk] = useState("");
  const [slotsErrors, setSlotsErrors] = useState([]);
  const [teamsErrors, setTeamsErrors] = useState([]);
  const [globalErr, setGlobalErr] = useState("");
  const [globalOk, setGlobalOk] = useState("");
  const [globalLoading, setGlobalLoading] = useState(false);
  const [globalLeagues, setGlobalLeagues] = useState([]);
  const [newLeague, setNewLeague] = useState({ leagueId: "", name: "", timezone: "America/New_York" });
  const [toast, setToast] = useState(null);
  const [accessStatus, setAccessStatus] = useState("Pending");
  const { promptState, promptValue, setPromptValue, requestPrompt, handleConfirm, handleCancel } = usePromptDialog();

  const isGlobalAdmin = !!me?.isGlobalAdmin;

  async function load() {
    setLoading(true);
    setErr("");
    try {
      const qs = new URLSearchParams();
      qs.set("status", accessStatus);
      const data = await apiFetch(`/api/accessrequests?${qs.toString()}`);
      setItems(Array.isArray(data) ? data : []);
    } catch (e) {
      setErr(e?.message || "Failed to load pending requests");
      setItems([]);
    } finally {
      setLoading(false);
    }
  }

  async function loadMembershipsAndTeams() {
    setMemLoading(true);
    try {
      const [m, d, t] = await Promise.all([
        apiFetch(`/api/memberships`),
        apiFetch(`/api/divisions`),
        apiFetch(`/api/teams`),
      ]);
      const mems = Array.isArray(m) ? m : [];
      setMemberships(mems);
      setDivisions(Array.isArray(d) ? d : []);
      setTeams(Array.isArray(t) ? t : []);

      // initialize draft assignments from current server state
      const draft = {};
      for (const mm of mems) {
        if ((mm.role || "").toLowerCase() !== "coach") continue;
        draft[mm.userId] = {
          division: mm.team?.division || "",
          teamId: mm.team?.teamId || "",
        };
      }
      setCoachDraft(draft);
    } catch (e) {
      setToast({ tone: "error", message: e?.message || "Failed to load memberships/teams" });
      setMemberships([]);
      setDivisions([]);
      setTeams([]);
    } finally {
      setMemLoading(false);
    }
  }

  useEffect(() => {
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId, accessStatus]);

  useEffect(() => {
    loadMembershipsAndTeams();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId]);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    const status = (params.get("accessStatus") || "Pending").trim();
    const allowed = new Set(["Pending", "Approved", "Denied"]);
    setAccessStatus(allowed.has(status) ? status : "Pending");
  }, []);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    if (accessStatus) params.set("accessStatus", accessStatus);
    else params.delete("accessStatus");
    const next = `${window.location.pathname}?${params.toString()}${window.location.hash}`;
    window.history.replaceState({}, "", next);
  }, [accessStatus]);

  useEffect(() => {
    if (!isGlobalAdmin) return;
    loadGlobalLeagues();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isGlobalAdmin]);

  async function loadGlobalLeagues() {
    setGlobalErr("");
    setGlobalLoading(true);
    try {
      const list = await apiFetch("/api/global/leagues");
      setGlobalLeagues(Array.isArray(list) ? list : []);
    } catch (e) {
      setGlobalErr(e?.message || "Failed to load leagues");
      setGlobalLeagues([]);
    } finally {
      setGlobalLoading(false);
    }
  }

  async function createLeague() {
    setGlobalErr("");
    setGlobalOk("");
    const leagueId = (newLeague.leagueId || "").trim();
    const name = (newLeague.name || "").trim();
    const timezone = (newLeague.timezone || "America/New_York").trim();
    if (!leagueId || !name) return setGlobalErr("leagueId and name are required.");

    setGlobalLoading(true);
    try {
      await apiFetch("/api/global/leagues", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ leagueId, name, timezone }),
      });
      setGlobalOk(`Created league ${leagueId}.`);
      setToast({ tone: "success", message: `Created league ${leagueId}.` });
      setNewLeague({ leagueId: "", name: "", timezone });
      await loadGlobalLeagues();
    } catch (e) {
      setGlobalErr(e?.message || "Create league failed");
    } finally {
      setGlobalLoading(false);
    }
  }

  async function approve(req, roleOverride) {
    const userId = req?.userId || "";
    const role = (roleOverride || req?.requestedRole || "Viewer").trim();
    if (!userId) return;
    try {
      await apiFetch(`/api/accessrequests/${encodeURIComponent(userId)}/approve`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ role }),
      });
      await load();
      setToast({ tone: "success", message: "Access request approved." });
    } catch (e) {
      setToast({ tone: "error", message: e?.message || "Approve failed" });
    }
  }

  async function deny(req) {
    const userId = req?.userId || "";
    if (!userId) return;
    const reason = await requestPrompt({
      title: "Deny access request",
      message: "Optional reason for denial.",
      placeholder: "Reason (optional)",
      confirmLabel: "Deny",
    });
    if (reason === null) return;
    try {
      await apiFetch(`/api/accessrequests/${encodeURIComponent(userId)}/deny`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ reason }),
      });
      await load();
      setToast({ tone: "success", message: "Access request denied." });
    } catch (e) {
      setToast({ tone: "error", message: e?.message || "Deny failed" });
    }
  }

  const sorted = useMemo(() => {
    return [...items].sort((a, b) => (b.updatedUtc || "").localeCompare(a.updatedUtc || ""));
  }, [items]);

  const coaches = useMemo(() => {
    return (memberships || []).filter((m) => (m.role || "").toLowerCase() === "coach");
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
      await loadMembershipsAndTeams();
      setToast({ tone: "success", message: "Coach assignment updated." });
    } catch (e) {
      setToast({ tone: "error", message: e?.message || "Failed to update coach assignment" });
    }
  }

  async function clearCoachAssignment(userId) {
    setDraftForCoach(userId, { division: "", teamId: "" });
    await saveCoachAssignment(userId);
  }

  async function importSlotsCsv() {
    setSlotsErr("");
    setSlotsOk("");
    setSlotsErrors([]);
    if (!slotsFile) return setSlotsErr("Choose a CSV file to upload.");

    setSlotsBusy(true);
    try {
      const fd = new FormData();
      fd.append("file", slotsFile);
      const res = await apiFetch("/api/import/slots", { method: "POST", body: fd });
      setSlotsOk(`Imported. Upserted: ${res?.upserted ?? 0}, Rejected: ${res?.rejected ?? 0}, Skipped: ${res?.skipped ?? 0}`);
      if (Array.isArray(res?.errors) && res.errors.length) setSlotsErrors(res.errors);
    } catch (e) {
      setSlotsErr(e?.message || "Import failed");
    } finally {
      setSlotsBusy(false);
    }
  }

  async function importTeamsCsv() {
    setTeamsErr("");
    setTeamsOk("");
    setTeamsErrors([]);
    if (!teamsFile) return setTeamsErr("Choose a CSV file to upload.");

    setTeamsBusy(true);
    try {
      const fd = new FormData();
      fd.append("file", teamsFile);
      const res = await apiFetch("/api/import/teams", { method: "POST", body: fd });
      setTeamsOk(`Imported. Upserted: ${res?.upserted ?? 0}, Rejected: ${res?.rejected ?? 0}, Skipped: ${res?.skipped ?? 0}`);
      if (Array.isArray(res?.errors) && res.errors.length) setTeamsErrors(res.errors);
      await loadMembershipsAndTeams();
    } catch (e) {
      setTeamsErr(e?.message || "Import failed");
    } finally {
      setTeamsBusy(false);
    }
  }

  return (
    <div className="card">
      <Toast
        open={!!toast}
        tone={toast?.tone}
        message={toast?.message}
        onClose={() => setToast(null)}
      />
      <PromptDialog
        open={!!promptState}
        title={promptState?.title}
        message={promptState?.message}
        placeholder={promptState?.placeholder}
        confirmLabel={promptState?.confirmLabel}
        cancelLabel={promptState?.cancelLabel}
        value={promptValue}
        onChange={setPromptValue}
        onConfirm={handleConfirm}
        onCancel={handleCancel}
      />
      <h2>Admin: access requests</h2>
      <p className="muted">
        Approve or deny access requests for the currently selected league.
      </p>
      <div className="row gap-3 row--wrap mb-2">
        <LeaguePicker leagueId={leagueId} setLeagueId={setLeagueId} me={me} label="League" />
        <label className="min-w-[160px]">
          Status
          <select value={accessStatus} onChange={(e) => setAccessStatus(e.target.value)}>
            <option value="Pending">Pending</option>
            <option value="Approved">Approved</option>
            <option value="Denied">Denied</option>
          </select>
        </label>
      </div>

      <div className="row gap-3 row--wrap">
        <button className="btn" onClick={load} disabled={loading} title="Refresh access requests.">
          Refresh
        </button>
        <button className="btn" onClick={loadMembershipsAndTeams} disabled={memLoading} title="Refresh memberships and teams.">
          Refresh members/teams
        </button>
      </div>

      {err && <div className="error">{err}</div>}
      {loading ? (
        <div className="muted">Loading...</div>
      ) : sorted.length === 0 ? (
        <div className="muted">No pending requests.</div>
      ) : (
        <div className="tableWrap">
          <table className="table">
            <thead>
              <tr>
                <th>User</th>
                <th>Requested role</th>
                <th>Notes</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {sorted.map((r) => (
                <tr key={r.userId}>
                  <td>
                    <div className="font-semibold">{r.email || r.userId}</div>
                    <div className="muted text-xs">{r.userId}</div>
                  </td>
                  <td>
                    <select
                      defaultValue={r.requestedRole || "Viewer"}
                      onChange={(e) => approve(r, e.target.value)}
                      title="Pick a role to approve"
                    >
                      {ROLE_OPTIONS.map((x) => (
                        <option key={x} value={x}>
                          {x}
                        </option>
                      ))}
                    </select>
                    <div className="muted text-xs">{r.updatedUtc || r.createdUtc || ""}</div>
                  </td>
                  <td className="max-w-[320px]">
                    <div className="whitespace-pre-wrap">{r.notes || ""}</div>
                  </td>
                  <td>
                    <div className="row gap-2 row--wrap">
                      <button className="btn btn--primary" onClick={() => approve(r)}>
                        Approve
                      </button>
                      <button className="btn" onClick={() => deny(r)}>
                        Deny
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <details className="mt-4">
        <summary>Notes</summary>
        <ul>
          <li>Approving creates (or updates) a membership record for this league and marks the request Approved.</li>
          <li>Deny marks the request Denied.</li>
        </ul>
      </details>

      {isGlobalAdmin ? (
        <div className="card mt-4">
          <h3 className="m-0">Global admin: leagues</h3>
          <p className="muted">
            Create new leagues and review existing ones. This is global admin only.
          </p>
          {globalErr ? <div className="callout callout--error">{globalErr}</div> : null}
          {globalOk ? <div className="callout callout--ok">{globalOk}</div> : null}

          <div className="row gap-3 row--wrap mb-3">
            <label className="flex-1 min-w-[160px]">
              League ID
              <input
                value={newLeague.leagueId}
                onChange={(e) => setNewLeague((p) => ({ ...p, leagueId: e.target.value }))}
                placeholder="ARL"
              />
            </label>
            <label className="flex-[2] min-w-[220px]">
              League name
              <input
                value={newLeague.name}
                onChange={(e) => setNewLeague((p) => ({ ...p, name: e.target.value }))}
                placeholder="Arlington"
              />
            </label>
            <label className="flex-[2] min-w-[220px]">
              Timezone
              <input
                value={newLeague.timezone}
                onChange={(e) => setNewLeague((p) => ({ ...p, timezone: e.target.value }))}
                placeholder="America/New_York"
              />
            </label>
            <button className="btn btn--primary" onClick={createLeague} disabled={globalLoading}>
              {globalLoading ? "Saving..." : "Create league"}
            </button>
            <button className="btn" onClick={loadGlobalLeagues} disabled={globalLoading}>
              Refresh leagues
            </button>
          </div>

          {globalLoading ? (
            <div className="muted">Loading...</div>
          ) : globalLeagues.length === 0 ? (
            <div className="muted">No leagues yet.</div>
          ) : (
            <div className="tableWrap">
              <table className="table">
                <thead>
                  <tr>
                    <th>League ID</th>
                    <th>Name</th>
                    <th>Timezone</th>
                    <th>Status</th>
                  </tr>
                </thead>
                <tbody>
                  {globalLeagues.map((l) => (
                    <tr key={l.leagueId}>
                      <td><code>{l.leagueId}</code></td>
                      <td>{l.name}</td>
                      <td>{l.timezone}</td>
                      <td>{l.status}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      ) : null}

      <div className="card mt-4">
        <h3 className="m-0">Coach assignments</h3>
        <p className="muted">
          Coaches can be approved without a team. Assign teams here when you're ready.
        </p>

        {memLoading ? (
          <div className="muted">Loading memberships...</div>
        ) : coaches.length === 0 ? (
          <div className="muted">No coaches in this league yet.</div>
        ) : (
          <div className="tableWrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Coach</th>
                  <th>Division</th>
                  <th>Team</th>
                  <th>Action</th>
                </tr>
              </thead>
              <tbody>
                {coaches.map((c) => {
                  const draft = coachDraft[c.userId] || { division: c.team?.division || "", teamId: c.team?.teamId || "" };
                  const currentDiv = draft.division || "";
                  const currentTeam = draft.teamId || "";
                  const divOptions = (divisions || [])
                    .map((d) => (typeof d === "string" ? d : d.code || d.division || ""))
                    .filter(Boolean);
                  const teamsForDiv = currentDiv ? (teamsByDivision.get(currentDiv) || []) : [];

                  return (
                    <tr key={c.userId}>
                      <td>
                        <div className="font-semibold">{c.email || c.userId}</div>
                        <div className="muted text-xs">{c.userId}</div>
                      </td>
                      <td>
                        <select
                          value={currentDiv}
                          onChange={(e) => {
                            const v = e.target.value;
                            setDraftForCoach(c.userId, { division: v, teamId: "" });
                          }}
                          title="Set division (clears team until you select one)"
                        >
                          <option value="">(unassigned)</option>
                          {divOptions.map((d) => (
                            <option key={d} value={d}>{d}</option>
                          ))}
                        </select>
                      </td>
                      <td>
                        <select
                          value={currentTeam}
                          onChange={(e) => {
                            const v = e.target.value;
                            setDraftForCoach(c.userId, { division: currentDiv, teamId: v });
                          }}
                          disabled={!currentDiv}
                          title={!currentDiv ? "Pick a division first" : "Pick a team"}
                        >
                          <option value="">(unassigned)</option>
                          {teamsForDiv.map((t) => (
                            <option key={t.teamId} value={t.teamId}>
                              {t.name || t.teamId}
                            </option>
                          ))}
                        </select>
                      </td>
                      <td>
                        <div className="row gap-2 row--wrap">
                          <button className="btn btn--primary" onClick={() => saveCoachAssignment(c.userId)}>
                            Save
                          </button>
                          <button
                            className="btn"
                            onClick={() => clearCoachAssignment(c.userId)}
                          >
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

      <div className="card mt-4">
        <h3 className="m-0">League admin uploads</h3>
        <p className="muted">
          Upload CSVs for schedules (slots) and teams. Team imports can prefill coach contact info.
        </p>

        <div className="card mt-3">
          <div className="font-bold mb-2">Schedule (slots) CSV upload</div>
          <div className="subtle mb-2">
            Required columns: <code>division</code>, <code>offeringTeamId</code>, <code>gameDate</code>,{" "}
            <code>startTime</code>, <code>endTime</code>, <code>fieldKey</code>. Optional:{" "}
            <code>offeringEmail</code>, <code>gameType</code>, <code>notes</code>, <code>status</code>.
          </div>
          {slotsErr ? <div className="callout callout--error">{slotsErr}</div> : null}
          {slotsOk ? <div className="callout callout--ok">{slotsOk}</div> : null}
          <div className="row items-end gap-3">
            <label className="flex-1">
              CSV file
              <input
                type="file"
                accept=".csv,text/csv"
                onChange={(e) => setSlotsFile(e.target.files?.[0] || null)}
                disabled={slotsBusy}
              />
            </label>
            <button className="btn" onClick={importSlotsCsv} disabled={slotsBusy || !slotsFile}>
              {slotsBusy ? "Importing..." : "Upload & Import"}
            </button>
          </div>
          {slotsErrors.length ? (
            <div className="mt-3">
              <div className="font-bold mb-2">Rejected rows ({slotsErrors.length})</div>
              <table className="table">
                <thead>
                  <tr>
                    <th>Row</th>
                    <th>Error</th>
                  </tr>
                </thead>
                <tbody>
                  {slotsErrors.slice(0, 50).map((x, idx) => (
                    <tr key={idx}>
                      <td>{x.row}</td>
                      <td>{x.error}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              {slotsErrors.length > 50 ? <div className="subtle">Showing first 50.</div> : null}
            </div>
          ) : null}
        </div>

        <div className="card mt-3">
          <div className="font-bold mb-2">Teams CSV upload</div>
          <div className="subtle mb-2">
            Required columns: <code>division</code>, <code>teamId</code>, <code>name</code>. Optional:{" "}
            <code>coachName</code>, <code>coachEmail</code>, <code>coachPhone</code>.
          </div>
          {teamsErr ? <div className="callout callout--error">{teamsErr}</div> : null}
          {teamsOk ? <div className="callout callout--ok">{teamsOk}</div> : null}
          <div className="row items-end gap-3">
            <label className="flex-1">
              CSV file
              <input
                type="file"
                accept=".csv,text/csv"
                onChange={(e) => setTeamsFile(e.target.files?.[0] || null)}
                disabled={teamsBusy}
              />
            </label>
            <button className="btn" onClick={importTeamsCsv} disabled={teamsBusy || !teamsFile}>
              {teamsBusy ? "Importing..." : "Upload & Import"}
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
      </div>
    </div>
  );
}
