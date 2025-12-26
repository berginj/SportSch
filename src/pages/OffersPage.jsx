import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import LeaguePicker from "../components/LeaguePicker";

function fmtDate(d) {
  return d || "";
}

export default function OffersPage({ me, leagueId, setLeagueId }) {
  const email = me?.email || "";
  const isGlobalAdmin = !!me?.isGlobalAdmin;
  const memberships = Array.isArray(me?.memberships) ? me.memberships : [];
  const role = useMemo(() => {
    const inLeague = memberships.filter((m) => (m?.leagueId || "").trim() === (leagueId || "").trim());
    const roles = inLeague.map((m) => (m?.role || "").trim());
    if (roles.includes("LeagueAdmin")) return "LeagueAdmin";
    if (roles.includes("Coach")) return "Coach";
    return roles.includes("Viewer") ? "Viewer" : "";
  }, [memberships, leagueId]);
  const canPickTeam = isGlobalAdmin || role === "LeagueAdmin";
  const [divisions, setDivisions] = useState([]);
  const [division, setDivision] = useState("");
  const [fields, setFields] = useState([]);
  const [slots, setSlots] = useState([]);
  const [teams, setTeams] = useState([]);
  const [acceptTeamBySlot, setAcceptTeamBySlot] = useState({});
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState("");

  const fieldByKey = useMemo(() => {
    const m = new Map();
    for (const f of fields || []) {
      const k = f?.fieldKey || "";
      if (k) m.set(k, f);
    }
    return m;
  }, [fields]);

  const teamsByDivision = useMemo(() => {
    const map = new Map();
    for (const t of teams || []) {
      const div = (t.division || "").trim().toUpperCase();
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

  async function loadAll(selectedDivision) {
    setErr("");
    setLoading(true);
    try {
      const [divs, flds, tms] = await Promise.all([
        apiFetch("/api/divisions"),
        apiFetch("/api/fields"),
        canPickTeam ? apiFetch("/api/teams") : Promise.resolve([]),
      ]);
      const divList = Array.isArray(divs) ? divs : [];
      setDivisions(divList);
      const firstDiv = selectedDivision || divList?.[0]?.code || "";
      setDivision(firstDiv);

      const fieldList = Array.isArray(flds) ? flds : [];
      setFields(fieldList);
      setTeams(Array.isArray(tms) ? tms : []);

      if (firstDiv) {
        const s = await apiFetch(`/api/slots?division=${encodeURIComponent(firstDiv)}`);
        setSlots(Array.isArray(s) ? s : []);
      } else {
        setSlots([]);
      }
    } catch (e) {
      setErr(e?.message || String(e));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    loadAll("");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId]);

  async function reloadSlots(nextDivision) {
    const d = nextDivision ?? division;
    setDivision(d);
    if (!d) return;
    setErr("");
    try {
      const s = await apiFetch(`/api/slots?division=${encodeURIComponent(d)}`);
      setSlots(Array.isArray(s) ? s : []);
    } catch (e) {
      setErr(e?.message || String(e));
    }
  }

  // --- Create slot ---
  const [offeringTeamId, setOfferingTeamId] = useState("");
  const [gameDate, setGameDate] = useState("");
  const [startTime, setStartTime] = useState("");
  const [endTime, setEndTime] = useState("");
  const [fieldKey, setFieldKey] = useState("");
  const [notes, setNotes] = useState("");

  async function createSlot() {
    setErr("");
    const f = fieldByKey.get(fieldKey);
    if (!division) return setErr("Select a division first.");
    if (!offeringTeamId.trim()) return setErr("Offering Team ID is required.");
    if (!gameDate.trim()) return setErr("GameDate is required.");
    if (!startTime.trim() || !endTime.trim()) return setErr("StartTime/EndTime are required.");
    if (!f) return setErr("Select a field.");

    const body = {
      division,
      offeringTeamId: offeringTeamId.trim(),
      offeringEmail: email,
      gameDate: gameDate.trim(),
      startTime: startTime.trim(),
      endTime: endTime.trim(),
      parkName: f.parkName,
      fieldName: f.fieldName,
      displayName: f.displayName,
      fieldKey: f.fieldKey,
      notes: notes.trim(),
    };

    try {
      await apiFetch(`/api/slots`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      setOfferingTeamId("");
      setGameDate("");
      setStartTime("");
      setEndTime("");
      setFieldKey("");
      setNotes("");
      await reloadSlots(division);
    } catch (e) {
      setErr(e?.message || String(e));
    }
  }

  // --- Request slot ---
  function setAcceptTeam(slotId, teamId) {
    setAcceptTeamBySlot((prev) => ({ ...prev, [slotId]: teamId }));
  }

  async function requestSlot(slot, requestingTeamId) {
    setErr("");
    const note = prompt("Notes for the other team? (optional)") || "";
    try {
      const div = slot?.division || division;
      await apiFetch(`/api/slots/${encodeURIComponent(div)}/${encodeURIComponent(slot.slotId)}/requests`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          notes: note,
          requestingTeamId: requestingTeamId || undefined,
          requestingDivision: div,
        }),
      });
      await reloadSlots(division);
    } catch (e) {
      setErr(e?.message || String(e));
    }
  }

  if (loading) return <div className="card">Loading...</div>;

  return (
    <div className="stack">
      {err ? <div className="card error">{err}</div> : null}

      <div className="card">
        <div className="cardTitle">
          Create game offer
          <span className="hint" title="Post an open game offer that other teams can accept.">?</span>
        </div>
        <div className="row filterRow">
          <LeaguePicker leagueId={leagueId} setLeagueId={setLeagueId} me={me} label="League" />
          <label title="Choose a division for this offer.">
            Division
            <select value={division} onChange={(e) => reloadSlots(e.target.value)}>
              {divisions.map((d) => (
                <option key={d.code} value={d.code}>
                  {d.name} ({d.code})
                </option>
              ))}
            </select>
          </label>
          <button className="btn" onClick={() => loadAll(division)} title="Refresh divisions, fields, and offers.">
            Refresh
          </button>
        </div>
        <div className="muted" style={{ marginTop: 8 }}>
          Create offers for <b>{leagueId || "(no league)"}</b>.
        </div>
      </div>

      <div className="card">
        <div className="cardTitle">Offer details</div>
        <div className="grid2">
          <label title="Team making the offer (must match your coach assignment).">
            Offering Team ID
            <input value={offeringTeamId} onChange={(e) => setOfferingTeamId(e.target.value)} />
          </label>
          <label title="Field for this game.">
            Field
            <select value={fieldKey} onChange={(e) => setFieldKey(e.target.value)}>
              <option value="">Select...</option>
              {fields.map((f) => (
                <option key={f.fieldKey} value={f.fieldKey}>
                  {f.displayName}
                </option>
              ))}
            </select>
          </label>
          <label title="Game date (YYYY-MM-DD).">
            GameDate (YYYY-MM-DD)
            <input value={gameDate} onChange={(e) => setGameDate(e.target.value)} placeholder="2026-03-29" />
          </label>
          <label title="Start time in 24h format.">
            StartTime (HH:MM)
            <input value={startTime} onChange={(e) => setStartTime(e.target.value)} placeholder="09:00" />
          </label>
          <label title="End time in 24h format.">
            EndTime (HH:MM)
            <input value={endTime} onChange={(e) => setEndTime(e.target.value)} placeholder="10:15" />
          </label>
          <label title="Optional notes visible to other teams.">
            Notes
            <input value={notes} onChange={(e) => setNotes(e.target.value)} />
          </label>
        </div>
        <div className="row">
          <button className="btn primary" onClick={createSlot} title="Post this offer to the calendar.">
            Create Game Offer
          </button>
        </div>
      </div>

      <div className="card">
        <div className="cardTitle">Open offers</div>
        {slots.length === 0 ? (
          <div className="muted">No offers found for this division.</div>
        ) : (
          <div className="tableWrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Time</th>
                  <th>Field</th>
                  <th>Offering Team</th>
                  <th>Status</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {slots.map((s) => (
                  <tr key={s.slotId}>
                    <td>{fmtDate(s.gameDate)}</td>
                    <td>
                      {s.startTime}-{s.endTime}
                    </td>
                    <td>{s.displayName || s.fieldKey}</td>
                    <td>{s.offeringTeamId}</td>
                    <td>{s.status}</td>
                    <td style={{ textAlign: "right" }}>
                      {s.status === "Open" ? (
                        canPickTeam ? (
                          (() => {
                            const divisionKey = (s.division || division || "").trim().toUpperCase();
                            const teamsForDivision = teamsByDivision.get(divisionKey) || [];
                            const selectedTeamId = acceptTeamBySlot[s.slotId] || "";
                            return (
                              <div className="row" style={{ justifyContent: "flex-end" }}>
                                <select
                                  value={selectedTeamId}
                                  onChange={(e) => setAcceptTeam(s.slotId, e.target.value)}
                                  title="Pick a team to accept this offer as."
                                >
                                  <option value="">Select team</option>
                                  {teamsForDivision.map((t) => (
                                    <option key={t.teamId} value={t.teamId}>
                                      {t.name || t.teamId}
                                    </option>
                                  ))}
                                </select>
                                <button
                                  className="btn"
                                  onClick={() => requestSlot(s, selectedTeamId)}
                                  disabled={!selectedTeamId}
                                  title="Accept this offer on behalf of the selected team."
                                >
                                  Accept as
                                </button>
                              </div>
                            );
                          })()
                        ) : (
                          <button className="btn" onClick={() => requestSlot(s)} title="Accept this offer.">
                            Accept
                          </button>
                        )
                      ) : (
                        <span className="muted">-</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
