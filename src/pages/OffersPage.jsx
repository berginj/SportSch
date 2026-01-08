import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { apiFetch } from "../lib/api";
import LeaguePicker from "../components/LeaguePicker";
import StatusCard from "../components/StatusCard";
import Toast from "../components/Toast";
import { PromptDialog } from "../components/Dialogs";
import { usePromptDialog } from "../lib/useDialogs";

function fmtDate(d) {
  return d || "";
}

function addDaysToDate(isoDate, days) {
  const parts = (isoDate || "").split("-");
  if (parts.length !== 3) return "";
  const year = Number(parts[0]);
  const month = Number(parts[1]);
  const day = Number(parts[2]);
  if (!year || !month || !day) return "";
  const base = Date.UTC(year, month - 1, day);
  const next = new Date(base + days * 24 * 60 * 60 * 1000);
  const yyyy = String(next.getUTCFullYear());
  const mm = String(next.getUTCMonth() + 1).padStart(2, "0");
  const dd = String(next.getUTCDate()).padStart(2, "0");
  return `${yyyy}-${mm}-${dd}`;
}

function formatGameType(gameType) {
  const raw = (gameType || "").trim().toLowerCase();
  if (raw === "request") return "Request";
  return "Offer";
}

function matchesTypeFilter(gameType, filter) {
  const normalized = formatGameType(gameType);
  if (!filter || filter === "all") return true;
  if (filter === "request") return normalized === "Request";
  if (filter === "offer") return normalized === "Offer";
  return true;
}

function isPracticeSlot(slot) {
  return (slot?.gameType || "").trim().toLowerCase() === "practice";
}

function canAcceptSlot(slot) {
  if (!slot || (slot.status || "") !== "Open") return false;
  if (slot.isAvailability) return false;
  if ((slot.awayTeamId || "").trim() && !slot.isExternalOffer) return false;
  return true;
}

function formatTeams(slot) {
  const home = (slot?.homeTeamId || slot?.offeringTeamId || "").trim();
  const away = (slot?.awayTeamId || "").trim();
  if (away) return `${home} vs ${away}`;
  return home ? `${home} vs TBD` : "";
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
  const [slotTypeFilter, setSlotTypeFilter] = useState("all");
  const [teams, setTeams] = useState([]);
  const [acceptTeamBySlot, setAcceptTeamBySlot] = useState({});
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState("");
  const [toast, setToast] = useState(null);
  const { promptState, promptValue, setPromptValue, requestPrompt, handleConfirm, handleCancel } = usePromptDialog();
  const initializedRef = useRef(false);

  const coachTeam = useMemo(() => {
    if (canPickTeam) return null;
    const mem = memberships.find(
      (m) => (m?.leagueId || "").trim() === (leagueId || "").trim() && (m?.role || "").trim() === "Coach"
    );
    if (!mem?.team?.division || !mem?.team?.teamId) return null;
    return { division: mem.team.division, teamId: mem.team.teamId };
  }, [canPickTeam, memberships, leagueId]);

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

  const filteredSlots = useMemo(() => {
    return (slots || [])
      .filter((s) => !s.isAvailability)
      .filter((s) => !isPracticeSlot(s))
      .filter((s) => matchesTypeFilter(s.gameType, slotTypeFilter));
  }, [slots, slotTypeFilter]);

  const applyFiltersFromUrl = useCallback(() => {
    if (typeof window === "undefined") return { division: "", type: "all" };
    const params = new URLSearchParams(window.location.search);
    const div = (params.get("division") || "").trim();
    const rawType = (params.get("slotType") || "").trim().toLowerCase();
    const type = rawType === "request" || rawType === "offer" ? rawType : "all";
    return { division: div, type };
  }, []);

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
    const preferred = applyFiltersFromUrl();
    setSlotTypeFilter(preferred.type);
    loadAll(preferred.division);
    initializedRef.current = true;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId]);

  useEffect(() => {
    if (!coachTeam || canPickTeam) return;
    if (coachTeam.division) setDivision(coachTeam.division);
    if (coachTeam.teamId) setOfferingTeamId(coachTeam.teamId);
  }, [coachTeam, canPickTeam]);

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

  useEffect(() => {
    if (!initializedRef.current || typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    if (division) params.set("division", division);
    else params.delete("division");
    if (slotTypeFilter && slotTypeFilter !== "all") params.set("slotType", slotTypeFilter);
    else params.delete("slotType");
    const next = `${window.location.pathname}?${params.toString()}${window.location.hash}`;
    window.history.replaceState({}, "", next);
  }, [division, slotTypeFilter]);

  const teamsForDivision = useMemo(() => {
    const key = (division || "").trim().toUpperCase();
    return teamsByDivision.get(key) || [];
  }, [division, teamsByDivision]);

  useEffect(() => {
    if (!canPickTeam) return;
    if (!offeringTeamId) return;
    if (!teamsForDivision.some((t) => t.teamId === offeringTeamId)) {
      setOfferingTeamId("");
    }
  }, [offeringTeamId, teamsForDivision, canPickTeam]);

  // --- Create slot ---
  const [entryType, setEntryType] = useState("Offer");
  const [isRecurring, setIsRecurring] = useState(false);
  const [repeatMode, setRepeatMode] = useState("weekly");
  const [repeatEveryWeeks, setRepeatEveryWeeks] = useState(1);
  const [repeatCustomEvery, setRepeatCustomEvery] = useState(10);
  const [repeatCustomUnit, setRepeatCustomUnit] = useState("days");
  const [repeatCount, setRepeatCount] = useState(1);
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
    if (!offeringTeamId.trim()) return setErr("Team ID is required.");
    if (!gameDate.trim()) return setErr("GameDate is required.");
    if (!startTime.trim() || !endTime.trim()) return setErr("StartTime/EndTime are required.");
    if (!f) return setErr("Select a field.");

    const repeatEvery = Math.max(1, Number.parseInt(repeatEveryWeeks, 10) || 1);
    const customEvery = Math.max(1, Number.parseInt(repeatCustomEvery, 10) || 1);
    const customUnit = repeatCustomUnit === "weeks" ? "weeks" : "days";
    const repeatTotal = Math.max(1, Number.parseInt(repeatCount, 10) || 1);
    const maxOccurrences = 26;
    if (isRecurring && repeatTotal > maxOccurrences) {
      return setErr(`Recurring posts are limited to ${maxOccurrences} occurrences at a time.`);
    }

    const occurrenceDates = [];
    const occurrences = isRecurring ? repeatTotal : 1;
    const stepDays = isRecurring
      ? (repeatMode === "custom" ? (customUnit === "weeks" ? 7 * customEvery : customEvery) : 7 * repeatEvery)
      : 0;
    for (let i = 0; i < occurrences; i += 1) {
      const nextDate = addDaysToDate(gameDate.trim(), i * stepDays);
      if (!nextDate) return setErr("GameDate must be YYYY-MM-DD.");
      occurrenceDates.push(nextDate);
    }

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
      gameType: entryType === "Request" ? "Request" : "Swap",
      notes: notes.trim(),
    };

    try {
      for (const date of occurrenceDates) {
        await apiFetch(`/api/slots`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ ...body, gameDate: date }),
        });
      }
      setOfferingTeamId("");
      setGameDate("");
      setStartTime("");
      setEndTime("");
      setFieldKey("");
      setNotes("");
      setIsRecurring(false);
      setRepeatMode("weekly");
      setRepeatEveryWeeks(1);
      setRepeatCustomEvery(10);
      setRepeatCustomUnit("days");
      setRepeatCount(1);
      await reloadSlots(division);
      const verb = entryType === "Request" ? "Request" : "Offer";
      const countLabel = occurrenceDates.length > 1 ? ` (${occurrenceDates.length})` : "";
      setToast({ tone: "success", message: `${verb} posted${countLabel}.` });
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
    const note = await requestPrompt({
      title: "Add a note",
      message: "Optional notes for the other team.",
      placeholder: "Type a quick note (optional)",
      confirmLabel: "Send",
    });
    if (note === null) return;
    try {
      const div = slot?.division || division;
      await apiFetch(`/api/slots/${encodeURIComponent(div)}/${encodeURIComponent(slot.slotId)}/requests`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          notes: note || "",
          requestingTeamId: requestingTeamId || undefined,
          requestingDivision: div,
        }),
      });
      await reloadSlots(division);
      setToast({ tone: "success", message: "Slot accepted." });
    } catch (e) {
      setErr(e?.message || String(e));
    }
  }

  if (loading) return <StatusCard title="Loading" message="Loading offers..." />;

  return (
    <div className="stack">
      {err ? <StatusCard tone="error" title="Unable to load offers" message={err} /> : null}
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

      <div className="card">
        <div className="cardTitle">
          Create game offer or request
          <span className="hint" title="Post an open offer or request that other teams can accept.">?</span>
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
          <label title="Filter offers vs requests in the list.">
            Slot type
            <select value={slotTypeFilter} onChange={(e) => setSlotTypeFilter(e.target.value)}>
              <option value="all">All</option>
              <option value="offer">Offers</option>
              <option value="request">Requests</option>
            </select>
          </label>
          <button className="btn" onClick={() => loadAll(division)} title="Refresh divisions, fields, and offers.">
            Refresh
          </button>
        </div>
        <div className="muted mt-2">
          Create offers or requests for <b>{leagueId || "(no league)"}</b>.
        </div>
      </div>

      <div className="card">
        <div className="cardTitle">Offer/request details</div>
        <div className="grid2">
          <label title="Offer or request." className="min-w-[160px]">
            Type
            <select value={entryType} onChange={(e) => setEntryType(e.target.value)}>
              <option value="Offer">Offer</option>
              <option value="Request">Request</option>
            </select>
          </label>
          <label title="Team making the offer/request (must match your coach assignment).">
            {entryType === "Request" ? "Requesting Team ID" : "Offering Team ID"}
            <select
              value={offeringTeamId}
              onChange={(e) => setOfferingTeamId(e.target.value)}
              disabled={!canPickTeam && !!coachTeam?.teamId}
            >
              <option value="">Select team</option>
              {teamsForDivision.map((t) => (
                <option key={t.teamId} value={t.teamId}>
                  {t.name || t.teamId}
                </option>
              ))}
              {!canPickTeam && coachTeam?.teamId && !teamsForDivision.some((t) => t.teamId === coachTeam.teamId) ? (
                <option value={coachTeam.teamId}>
                  {coachTeam.teamId}
                </option>
              ) : null}
            </select>
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
          <label className="row row--wrap gap-2 items-end" title="Post as a recurring offer or request.">
            <span>Recurring</span>
            <input
              type="checkbox"
              checked={isRecurring}
              onChange={(e) => setIsRecurring(e.target.checked)}
              aria-label="Toggle recurring posting"
            />
          </label>
          {isRecurring ? (
            <label title="Recurring cadence.">
              Interval
              <select value={repeatMode} onChange={(e) => setRepeatMode(e.target.value)}>
                <option value="weekly">Weekly</option>
                <option value="custom">Custom</option>
              </select>
            </label>
          ) : null}
          {isRecurring && repeatMode === "weekly" ? (
            <label title="Repeat every N weeks.">
              Repeat every (weeks)
              <input
                type="number"
                min="1"
                max="8"
                value={repeatEveryWeeks}
                onChange={(e) => setRepeatEveryWeeks(e.target.value)}
              />
            </label>
          ) : null}
          {isRecurring && repeatMode === "custom" ? (
            <label title="Repeat every interval.">
              Repeat every
              <input
                type="number"
                min="1"
                max="60"
                value={repeatCustomEvery}
                onChange={(e) => setRepeatCustomEvery(e.target.value)}
              />
            </label>
          ) : null}
          {isRecurring && repeatMode === "custom" ? (
            <label title="Repeat unit.">
              Unit
              <select value={repeatCustomUnit} onChange={(e) => setRepeatCustomUnit(e.target.value)}>
                <option value="days">Days</option>
                <option value="weeks">Weeks</option>
              </select>
            </label>
          ) : null}
          {isRecurring ? (
            <label title="Number of occurrences.">
              Occurrences
              <input
                type="number"
                min="1"
                max="26"
                value={repeatCount}
                onChange={(e) => setRepeatCount(e.target.value)}
              />
            </label>
          ) : null}
          <label title="Optional notes visible to other teams.">
            Notes
            <input value={notes} onChange={(e) => setNotes(e.target.value)} />
          </label>
        </div>
        <div className="row">
          <button className="btn btn--primary" onClick={createSlot} title="Post this offer to the calendar.">
            {entryType === "Request" ? "Post Game Request" : "Post Game Offer"}
          </button>
        </div>
      </div>

      <div className="card">
        <div className="cardTitle">Open offers & requests</div>
        {filteredSlots.length === 0 ? (
          <div className="muted">No offers or requests found for this division.</div>
        ) : (
          <div className="tableWrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Time</th>
                  <th>Field</th>
                  <th>Type</th>
                  <th>Team</th>
                  <th>Status</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {filteredSlots.map((s) => (
                  <tr key={s.slotId}>
                    <td>{fmtDate(s.gameDate)}</td>
                    <td>
                      {s.startTime}-{s.endTime}
                    </td>
                    <td>{s.displayName || s.fieldKey}</td>
                    <td>{formatGameType(s.gameType)}</td>
                    <td>{formatTeams(s) || s.offeringTeamId}</td>
                    <td>{s.status}</td>
                    <td className="text-right">
                      {canAcceptSlot(s) ? (
                        canPickTeam ? (
                          (() => {
                            const divisionKey = (s.division || division || "").trim().toUpperCase();
                            const teamsForDivision = teamsByDivision.get(divisionKey) || [];
                            const selectedTeamId = acceptTeamBySlot[s.slotId] || "";
                            return (
                              <div className="row row--end">
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
                                  title="Accept this slot on behalf of the selected team."
                                >
                                  Accept as
                                </button>
                              </div>
                            );
                          })()
                        ) : (
                          <button className="btn" onClick={() => requestSlot(s)} title="Accept this slot.">
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
