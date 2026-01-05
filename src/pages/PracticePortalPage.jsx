import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { apiFetch } from "../lib/api";
import StatusCard from "../components/StatusCard";
import Toast from "../components/Toast";
import { ConfirmDialog } from "../components/Dialogs";
import { useConfirmDialog } from "../lib/useDialogs";

function normalizeRole(role) {
  return (role || "").trim();
}

function weekKeyFromDate(isoDate) {
  const parts = (isoDate || "").split("-");
  if (parts.length !== 3) return "";
  const year = Number(parts[0]);
  const month = Number(parts[1]);
  const day = Number(parts[2]);
  if (!year || !month || !day) return "";
  const date = new Date(Date.UTC(year, month - 1, day));
  const dayNum = date.getUTCDay() || 7;
  date.setUTCDate(date.getUTCDate() + 4 - dayNum);
  const yearStart = new Date(Date.UTC(date.getUTCFullYear(), 0, 1));
  const weekNo = Math.ceil((((date - yearStart) / 86400000) + 1) / 7);
  return `${date.getUTCFullYear()}-W${String(weekNo).padStart(2, "0")}`;
}

function formatSlotTime(slot) {
  const start = (slot?.startTime || "").trim();
  const end = (slot?.endTime || "").trim();
  if (!start || !end) return "";
  return `${start} - ${end}`;
}

function formatSlotLocation(slot) {
  return slot?.displayName || `${slot?.parkName || ""} ${slot?.fieldName || ""}`.trim() || slot?.fieldKey || "";
}

export default function PracticePortalPage({ me, leagueId }) {
  const isGlobalAdmin = !!me?.isGlobalAdmin;
  const memberships = Array.isArray(me?.memberships) ? me.memberships : [];
  const role = useMemo(() => {
    const inLeague = memberships.filter((m) => (m?.leagueId || "").trim() === (leagueId || "").trim());
    const roles = inLeague.map((m) => normalizeRole(m?.role));
    if (roles.includes("LeagueAdmin")) return "LeagueAdmin";
    if (roles.includes("Coach")) return "Coach";
    return roles.includes("Viewer") ? "Viewer" : "";
  }, [memberships, leagueId]);

  const coachTeam = useMemo(() => {
    const inLeague = memberships.filter((m) => (m?.leagueId || "").trim() === (leagueId || "").trim());
    const coach = inLeague.find((m) => normalizeRole(m?.role) === "Coach");
    const division = (coach?.team?.division || coach?.division || "").trim();
    const teamId = (coach?.team?.teamId || coach?.teamId || "").trim();
    return { division, teamId };
  }, [memberships, leagueId]);

  const [divisions, setDivisions] = useState([]);
  const [division, setDivision] = useState("");
  const [slots, setSlots] = useState([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState("");
  const [notice, setNotice] = useState("");
  const [toast, setToast] = useState(null);
  const initializedRef = useRef(false);
  const loadedDivisionRef = useRef("");
  const { confirmState, requestConfirm, handleConfirm, handleCancel } = useConfirmDialog();

  const canSelectPractice = role === "Coach" || role === "LeagueAdmin" || isGlobalAdmin;
  const canPickDivision = isGlobalAdmin || role === "LeagueAdmin" || !coachTeam.division;

  const applyFiltersFromUrl = useCallback(() => {
    if (typeof window === "undefined") return { division: "" };
    const params = new URLSearchParams(window.location.search);
    return { division: (params.get("division") || "").trim() };
  }, []);

  async function loadAll(selectedDivision) {
    setErr("");
    setNotice("");
    setLoading(true);
    try {
      const [divs] = await Promise.all([apiFetch("/api/divisions")]);
      const divList = Array.isArray(divs) ? divs : [];
      setDivisions(divList);

      if (coachTeam.division && selectedDivision && selectedDivision !== coachTeam.division) {
        setNotice(`Your account is assigned to ${coachTeam.division}. Showing that division.`);
      }

      const preferred = coachTeam.division || selectedDivision || divList?.[0]?.code || "";
      setDivision(preferred);

      if (preferred) {
        const params = new URLSearchParams({ division: preferred, status: "Open,Confirmed" });
        const s = await apiFetch(`/api/slots?${params.toString()}`);
        setSlots(Array.isArray(s) ? s : []);
        loadedDivisionRef.current = preferred;
      } else {
        setSlots([]);
        loadedDivisionRef.current = "";
      }
    } catch (e) {
      setErr(e?.message || String(e));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    const preferred = applyFiltersFromUrl();
    loadAll(preferred.division).finally(() => {
      initializedRef.current = true;
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId]);

  useEffect(() => {
    if (!initializedRef.current) return;
    if (!division || division === loadedDivisionRef.current) return;
    const reload = async () => {
      setErr("");
      setLoading(true);
      try {
        const params = new URLSearchParams({ division, status: "Open,Confirmed" });
        const s = await apiFetch(`/api/slots?${params.toString()}`);
        setSlots(Array.isArray(s) ? s : []);
        loadedDivisionRef.current = division;
      } catch (e) {
        setErr(e?.message || String(e));
      } finally {
        setLoading(false);
      }
    };
    reload();
  }, [division]);

  useEffect(() => {
    if (!initializedRef.current || typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    if (division) params.set("division", division);
    else params.delete("division");
    const next = `${window.location.pathname}?${params.toString()}${window.location.hash}`;
    window.history.replaceState({}, "", next);
  }, [division]);

  const practiceSelections = useMemo(() => {
    if (!coachTeam.teamId) return [];
    return (slots || [])
      .filter((s) => (s?.gameType || "").trim().toLowerCase() === "practice")
      .filter((s) => (s?.status || "") === "Confirmed")
      .filter((s) => {
        const confirmed = (s?.confirmedTeamId || "").trim();
        const offering = (s?.offeringTeamId || "").trim();
        return confirmed === coachTeam.teamId || offering === coachTeam.teamId;
      });
  }, [slots, coachTeam.teamId]);

  const practiceByWeek = useMemo(() => {
    const map = new Map();
    for (const s of practiceSelections) {
      const key = weekKeyFromDate(s.gameDate);
      if (!key) continue;
      if (!map.has(key)) map.set(key, s);
    }
    return map;
  }, [practiceSelections]);

  const availableSlots = useMemo(() => {
    return (slots || [])
      .filter((s) => s?.isAvailability)
      .filter((s) => (s?.status || "") === "Open")
      .sort((a, b) => {
        const ad = `${a.gameDate || ""} ${a.startTime || ""}`.trim();
        const bd = `${b.gameDate || ""} ${b.startTime || ""}`.trim();
        return ad.localeCompare(bd);
      });
  }, [slots]);

  async function claimPractice(slot) {
    if (!slot?.slotId || !division) return;
    const weekKey = weekKeyFromDate(slot.gameDate);
    if (weekKey && practiceByWeek.has(weekKey)) {
      setErr("You already selected a practice slot for this week.");
      return;
    }
    if (!coachTeam.teamId) {
      setErr("Your coach profile needs a team assignment before selecting a practice slot.");
      return;
    }

    const ok = await requestConfirm({
      title: "Select practice slot",
      message: `Claim ${slot.gameDate} ${formatSlotTime(slot)} at ${formatSlotLocation(slot)}?`,
      confirmLabel: "Select",
    });
    if (!ok) return;

    setErr("");
    try {
      await apiFetch(`/api/slots/${encodeURIComponent(division)}/${encodeURIComponent(slot.slotId)}/practice`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({}),
      });
      await loadAll(division);
      setToast({ tone: "success", message: "Practice slot confirmed." });
    } catch (e) {
      setErr(e?.message || String(e));
    }
  }

  if (loading) {
    return (
      <div className="page">
        <StatusCard title="Loading" message="Loading practice slots..." />
      </div>
    );
  }

  if (!canSelectPractice) {
    return (
      <div className="page">
        <div className="card">
          <h2>Practice selection</h2>
          <p className="muted">You do not have access to the practice selection portal.</p>
        </div>
      </div>
    );
  }

  return (
    <div className="page">
      <div className="card">
        <h2>Practice selection portal</h2>
        <p className="muted">
          Choose one practice slot per week. These are the remaining availability slots after games are scheduled.
        </p>
        <div className="formGrid">
          <label>
            Division
            <select value={division} onChange={(e) => setDivision(e.target.value)} disabled={!canPickDivision}>
              <option value="">Select a division</option>
              {divisions.map((d) => (
                <option key={d.code} value={d.code}>
                  {d.name} ({d.code})
                </option>
              ))}
            </select>
          </label>
          <label>
            Team
            <input value={coachTeam.teamId || "Unassigned"} readOnly />
          </label>
        </div>
        {err ? <div className="callout callout--error">{err}</div> : null}
        {notice ? <div className="callout callout--ok">{notice}</div> : null}
      </div>

      <div className="card">
        <h3>Your selected practices</h3>
        {practiceSelections.length ? (
          <div className="tableWrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Week</th>
                  <th>Date</th>
                  <th>Time</th>
                  <th>Location</th>
                </tr>
              </thead>
              <tbody>
                {practiceSelections.map((s) => (
                  <tr key={s.slotId}>
                    <td>{weekKeyFromDate(s.gameDate)}</td>
                    <td>{s.gameDate}</td>
                    <td>{formatSlotTime(s)}</td>
                    <td>{formatSlotLocation(s)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="muted">No practice slots selected yet.</div>
        )}
      </div>

      <div className="card">
        <h3>Available practice slots</h3>
        {availableSlots.length ? (
          <div className="tableWrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Week</th>
                  <th>Date</th>
                  <th>Time</th>
                  <th>Location</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {availableSlots.map((s) => {
                  const weekKey = weekKeyFromDate(s.gameDate);
                  const disabled = weekKey && practiceByWeek.has(weekKey);
                  return (
                    <tr key={s.slotId}>
                      <td>{weekKey}</td>
                      <td>{s.gameDate}</td>
                      <td>{formatSlotTime(s)}</td>
                      <td>{formatSlotLocation(s)}</td>
                      <td className="tableActions">
                        <button
                          className="btn btn--primary"
                          type="button"
                          disabled={disabled}
                          onClick={() => claimPractice(s)}
                          title={disabled ? "Already selected a practice this week" : "Select this practice slot"}
                        >
                          Select
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="muted">No open practice slots available for this division.</div>
        )}
      </div>

      {toast ? <Toast tone={toast.tone} message={toast.message} onClose={() => setToast(null)} /> : null}
      <ConfirmDialog state={confirmState} onConfirm={handleConfirm} onCancel={handleCancel} />
    </div>
  );
}
