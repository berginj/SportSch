import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";

function fmtDate(d) {
  return d || "";
}

export default function OffersPage({ me }) {
  const email = me?.email || "";
  const [divisions, setDivisions] = useState([]);
  const [division, setDivision] = useState("");
  const [fields, setFields] = useState([]);
  const [slots, setSlots] = useState([]);
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

  async function loadAll(selectedDivision) {
    setErr("");
    setLoading(true);
    try {
      const [divs, flds] = await Promise.all([
        apiFetch("/api/divisions"),
        apiFetch("/api/fields"),
      ]);
      const divList = Array.isArray(divs) ? divs : [];
      setDivisions(divList);
      const firstDiv = selectedDivision || divList?.[0]?.code || "";
      setDivision(firstDiv);

      const fieldList = Array.isArray(flds) ? flds : [];
      setFields(fieldList);

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
  }, []);

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
  async function requestSlot(slot) {
    setErr("");
    const notes = prompt("Notes for the other team? (optional)") || "";
    try {
      await apiFetch(`/api/slots/${encodeURIComponent(division)}/${encodeURIComponent(slot.slotId)}/requests`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ notes }),
      });
      await reloadSlots(division);
    } catch (e) {
      setErr(e?.message || String(e));
    }
  }

  if (loading) return <div className="card">Loading…</div>;

  return (
    <div className="stack">
      {err ? <div className="card error">{err}</div> : null}

      <div className="card">
        <div className="cardTitle">Division</div>
        <div className="row">
          <select value={division} onChange={(e) => reloadSlots(e.target.value)}>
            {divisions.map((d) => (
              <option key={d.code} value={d.code}>
                {d.name} ({d.code})
              </option>
            ))}
          </select>
          <button className="btn" onClick={() => loadAll(division)}>
            Refresh
          </button>
        </div>
      </div>

      <div className="card">
        <div className="cardTitle">Create a slot</div>
        <div className="grid2">
          <label>
            Offering Team ID
            <input value={offeringTeamId} onChange={(e) => setOfferingTeamId(e.target.value)} />
          </label>
          <label>
            Field
            <select value={fieldKey} onChange={(e) => setFieldKey(e.target.value)}>
              <option value="">Select…</option>
              {fields.map((f) => (
                <option key={f.fieldKey} value={f.fieldKey}>
                  {f.displayName}
                </option>
              ))}
            </select>
          </label>
          <label>
            GameDate (YYYY-MM-DD)
            <input value={gameDate} onChange={(e) => setGameDate(e.target.value)} placeholder="2026-03-29" />
          </label>
          <label>
            StartTime (HH:MM)
            <input value={startTime} onChange={(e) => setStartTime(e.target.value)} placeholder="09:00" />
          </label>
          <label>
            EndTime (HH:MM)
            <input value={endTime} onChange={(e) => setEndTime(e.target.value)} placeholder="10:15" />
          </label>
          <label>
            Notes
            <input value={notes} onChange={(e) => setNotes(e.target.value)} />
          </label>
        </div>
        <div className="row">
          <button className="btn primary" onClick={createSlot}>
            Create Slot
          </button>
        </div>
      </div>

      <div className="card">
        <div className="cardTitle">Open slots</div>
        {slots.length === 0 ? (
          <div className="muted">No slots found for this division.</div>
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
                      {s.startTime}–{s.endTime}
                    </td>
                    <td>{s.displayName || s.fieldKey}</td>
                    <td>{s.offeringTeamId}</td>
                    <td>{s.status}</td>
                    <td style={{ textAlign: "right" }}>
                      {s.status === "Open" ? (
                        <button className="btn" onClick={() => requestSlot(s)}>
                          Request
                        </button>
                      ) : (
                        <span className="muted">—</span>
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
