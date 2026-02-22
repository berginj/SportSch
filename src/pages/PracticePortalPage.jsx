import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { apiFetch } from "../lib/api";
import StatusCard from "../components/StatusCard";
import Toast from "../components/Toast";
import { ConfirmDialog } from "../components/Dialogs";
import { useConfirmDialog } from "../lib/useDialogs";

const WEEKDAY_FILTER_OPTIONS = [
  { key: "", label: "All days" },
  { key: "1", label: "Monday" },
  { key: "2", label: "Tuesday" },
  { key: "3", label: "Wednesday" },
  { key: "4", label: "Thursday" },
  { key: "5", label: "Friday" },
  { key: "6", label: "Saturday" },
  { key: "0", label: "Sunday" },
];

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

function weekdayKeyFromDate(isoDate) {
  const parts = (isoDate || "").split("-");
  if (parts.length !== 3) return "";
  const year = Number(parts[0]);
  const month = Number(parts[1]);
  const day = Number(parts[2]);
  if (!year || !month || !day) return "";
  const date = new Date(Date.UTC(year, month - 1, day));
  return String(date.getUTCDay());
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
  const memberships = useMemo(
    () => (Array.isArray(me?.memberships) ? me.memberships : []),
    [me]
  );
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
  const [divisionTeams, setDivisionTeams] = useState([]);
  const [slots, setSlots] = useState([]);
  const [loading, setLoading] = useState(true);
  const [err, setErr] = useState("");
  const [notice, setNotice] = useState("");
  const [toast, setToast] = useState(null);
  const [openToShareField, setOpenToShareField] = useState(false);
  const [shareWithTeamId, setShareWithTeamId] = useState("");
  const [availableDayFilter, setAvailableDayFilter] = useState("");
  const [requestingSlot, setRequestingSlot] = useState("");
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
        const [s, teams] = await Promise.all([
          apiFetch(`/api/slots?${params.toString()}`),
          apiFetch(`/api/teams?division=${encodeURIComponent(preferred)}`).catch(() => []),
        ]);
        setSlots(Array.isArray(s) ? s : []);
        setDivisionTeams(Array.isArray(teams) ? teams : []);
        loadedDivisionRef.current = preferred;
      } else {
        setSlots([]);
        setDivisionTeams([]);
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
        const [s, teams] = await Promise.all([
          apiFetch(`/api/slots?${params.toString()}`),
          apiFetch(`/api/teams?division=${encodeURIComponent(division)}`).catch(() => []),
        ]);
        setSlots(Array.isArray(s) ? s : []);
        setDivisionTeams(Array.isArray(teams) ? teams : []);
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

  const shareableTeams = useMemo(() => {
    return (Array.isArray(divisionTeams) ? divisionTeams : [])
      .filter((t) => (t?.teamId || "").trim())
      .filter((t) => (t?.teamId || "").trim() !== (coachTeam.teamId || "").trim())
      .sort((a, b) => {
        const aLabel = (a?.name || a?.teamId || "").trim();
        const bLabel = (b?.name || b?.teamId || "").trim();
        return aLabel.localeCompare(bLabel);
      });
  }, [divisionTeams, coachTeam.teamId]);

  useEffect(() => {
    if (!openToShareField && shareWithTeamId) {
      setShareWithTeamId("");
      return;
    }
    if (!openToShareField || !shareWithTeamId) return;
    if (!shareableTeams.some((t) => (t?.teamId || "").trim() === shareWithTeamId)) {
      setShareWithTeamId("");
    }
  }, [openToShareField, shareWithTeamId, shareableTeams]);

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

  const filteredAvailableSlots = useMemo(() => {
    const dayKey = String(availableDayFilter || "").trim();
    if (!dayKey) return availableSlots;
    return availableSlots.filter((slot) => weekdayKeyFromDate(slot?.gameDate) === dayKey);
  }, [availableSlots, availableDayFilter]);

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
    if (openToShareField && !shareWithTeamId) {
      setErr('Select a team to propose sharing with, or uncheck "Open to sharing a field".');
      return;
    }

    const proposedShareTeam = shareableTeams.find((t) => (t?.teamId || "").trim() === shareWithTeamId);
    const shareMsg = openToShareField
      ? ` Open to share with: ${proposedShareTeam?.name || shareWithTeamId}.`
      : "";
    const selectedDate = String(slot.gameDate || "").trim();
    const selectedWeekday = weekdayKeyFromDate(selectedDate);
    const selectedFieldKey = String(slot.fieldKey || "").trim();
    const selectedStart = String(slot.startTime || "").trim();
    const selectedEnd = String(slot.endTime || "").trim();
    const existingPracticeWeeks = new Set(Array.from(practiceByWeek.keys()));
    const recurringCandidates = [...availableSlots]
      .filter((candidate) => {
        const candidateDate = String(candidate?.gameDate || "").trim();
        if (!candidateDate) return false;
        if (selectedDate && candidateDate < selectedDate) return false;
        if (selectedWeekday && weekdayKeyFromDate(candidateDate) !== selectedWeekday) return false;
        if (String(candidate?.fieldKey || "").trim() !== selectedFieldKey) return false;
        if (String(candidate?.startTime || "").trim() !== selectedStart) return false;
        if (String(candidate?.endTime || "").trim() !== selectedEnd) return false;
        const candidateWeek = weekKeyFromDate(candidateDate);
        if (!candidateWeek) return false;
        if (existingPracticeWeeks.has(candidateWeek)) return false;
        return true;
      })
      .sort((a, b) => {
        const aKey = `${a?.gameDate || ""} ${a?.startTime || ""}`.trim();
        const bKey = `${b?.gameDate || ""} ${b?.startTime || ""}`.trim();
        return aKey.localeCompare(bKey);
      });

    const seriesCount = recurringCandidates.length || 1;
    const recurringMsg =
      seriesCount > 1
        ? ` This will claim the matching weekly slot pattern for ${seriesCount} weeks (same field and time) starting ${selectedDate} across the regular-season availability set.`
        : "";

    const ok = await requestConfirm({
      title: "Select practice slot",
      message: `Claim ${slot.gameDate} ${formatSlotTime(slot)} at ${formatSlotLocation(slot)}?${recurringMsg}${shareMsg}`,
      confirmLabel: "Select",
    });
    if (!ok) return;

    setErr("");
    setRequestingSlot(String(slot.slotId || ""));
    try {
      const payload = {
        openToShareField,
        shareWithTeamId: openToShareField ? shareWithTeamId : "",
      };
      const targets = recurringCandidates.length ? recurringCandidates : [slot];
      let successCount = 0;
      const failures = [];

      for (const candidate of targets) {
        try {
          await apiFetch(`/api/slots/${encodeURIComponent(division)}/${encodeURIComponent(candidate.slotId)}/practice`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload),
          });
          successCount += 1;
        } catch (e) {
          failures.push({
            slot: candidate,
            message: e?.message || String(e),
          });
        }
      }

      await loadAll(division);
      if (successCount > 0) {
        const baseMessage = successCount === 1
          ? "Practice slot confirmed."
          : `Practice slot pattern confirmed for ${successCount} weeks.`;
        const sharingSuffix = openToShareField ? " Sharing preference saved." : "";
        const partialSuffix = failures.length ? ` ${failures.length} week(s) could not be claimed.` : "";
        setToast({
          tone: failures.length ? "warning" : "success",
          message: `${baseMessage}${sharingSuffix}${partialSuffix}`.trim(),
          duration: failures.length ? 4200 : 3000,
        });
      } else {
        setErr(failures[0]?.message || "No matching weekly practice slots could be claimed.");
      }

      if (failures.length) {
        const firstFew = failures
          .slice(0, 3)
          .map((f) => `${f.slot?.gameDate || "?"}: ${f.message}`)
          .join(" | ");
        setErr(`Some weeks were not claimed. ${firstFew}${failures.length > 3 ? " ..." : ""}`);
      }
    } catch (e) {
      setErr(e?.message || String(e));
    } finally {
      setRequestingSlot("");
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
          Choose a practice slot pattern. Selecting a slot will claim the same field/day/time for matching open weeks in the regular-season availability set.
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
        <div className="callout mb-3">
          <div className="row row--wrap gap-3">
            <label className="row row--wrap gap-2" style={{ alignItems: "center" }}>
              <input
                type="checkbox"
                checked={openToShareField}
                onChange={(e) => setOpenToShareField(e.target.checked)}
                disabled={!coachTeam.teamId}
              />
              <span>Open to sharing a field</span>
            </label>
            <label style={{ minWidth: 260 }}>
              Propose sharing with team
              <select
                value={shareWithTeamId}
                onChange={(e) => setShareWithTeamId(e.target.value)}
                disabled={!openToShareField || !coachTeam.teamId || shareableTeams.length === 0}
              >
                <option value="">
                  {!coachTeam.teamId
                    ? "Coach team assignment required"
                    : !openToShareField
                      ? "Enable sharing first"
                      : shareableTeams.length
                        ? "Select a team"
                        : "No other teams in division"}
                </option>
                {shareableTeams.map((t) => (
                  <option key={t.teamId} value={t.teamId}>
                    {t.name ? `${t.name} (${t.teamId})` : t.teamId}
                  </option>
                ))}
              </select>
            </label>
            <label style={{ minWidth: 220 }}>
              Filter by day
              <select
                value={availableDayFilter}
                onChange={(e) => setAvailableDayFilter(e.target.value)}
              >
                {WEEKDAY_FILTER_OPTIONS.map((opt) => (
                  <option key={opt.key || "all"} value={opt.key}>
                    {opt.label}
                  </option>
                ))}
              </select>
            </label>
          </div>
          <div className="muted mt-2">
            This preference is attached to your practice slot selection(s) and can help commissioners coordinate shared fields.
          </div>
          <div className="muted">
            Filter to a day (for example, Saturdays) and select one slot to claim that recurring field/time pattern for matching open regular-season weeks.
          </div>
        </div>
        {filteredAvailableSlots.length ? (
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
                {filteredAvailableSlots.map((s) => {
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
                          disabled={disabled || !!requestingSlot}
                          onClick={() => claimPractice(s)}
                          title={
                            disabled
                              ? "Already selected a practice this week"
                              : requestingSlot
                                ? "Processing practice selection..."
                                : "Select this weekly pattern"
                          }
                        >
                          {requestingSlot ? "Selecting..." : "Select"}
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="muted">
            {availableDayFilter
              ? "No open practice slots match the selected day."
              : "No open practice slots available for this division."}
          </div>
        )}
      </div>

      {toast ? <Toast tone={toast.tone} message={toast.message} onClose={() => setToast(null)} /> : null}
      <ConfirmDialog state={confirmState} onConfirm={handleConfirm} onCancel={handleCancel} />
    </div>
  );
}
