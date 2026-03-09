import { useCallback, useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import { readPagedItems } from "../lib/pagedResults";
import StatusCard from "../components/StatusCard";
import Toast from "../components/Toast";
import { ConfirmDialog } from "../components/Dialogs";
import { useConfirmDialog } from "../lib/useDialogs";

function getTodayDate() {
  return new Date().toISOString().split("T")[0];
}

function getDateInDays(days) {
  const date = new Date();
  date.setDate(date.getDate() + days);
  return date.toISOString().split("T")[0];
}

export default function CoachOnboardingPage({ me, leagueId, setTab }) {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [toast, setToast] = useState(null);

  const membership = (me?.memberships || []).find((m) => m.leagueId === leagueId && m.role === "Coach");
  const team = membership?.team;
  const division = team?.division;
  const teamId = team?.teamId;

  const [teamData, setTeamData] = useState(null);
  const [practiceRequests, setPracticeRequests] = useState([]);
  const [practicePortal, setPracticePortal] = useState(null);
  const [upcomingGames, setUpcomingGames] = useState([]);

  const [teamName, setTeamName] = useState("");
  const [primaryContactName, setPrimaryContactName] = useState("");
  const [primaryContactEmail, setPrimaryContactEmail] = useState("");
  const [primaryContactPhone, setPrimaryContactPhone] = useState("");
  const [assistantCoaches, setAssistantCoaches] = useState([]);
  const [clinicPreference, setClinicPreference] = useState("");

  const [savingTeam, setSavingTeam] = useState(false);

  const { confirmState, requestConfirm, handleConfirm, handleCancel } = useConfirmDialog();

  const loadAll = useCallback(async () => {
    if (!division || !teamId) {
      setLoading(false);
      return;
    }

    setLoading(true);
    setError("");

    try {
      const [teamResp, requestsResp, gamesResp] = await Promise.all([
        apiFetch(`/api/teams?division=${encodeURIComponent(division)}`).catch(() => []),
        apiFetch("/api/field-inventory/practice/coach").catch(() => null),
        apiFetch(
          `/api/slots?division=${encodeURIComponent(division)}&status=Confirmed&dateFrom=${getTodayDate()}&dateTo=${getDateInDays(90)}`
        ).catch(() => ({ items: [] })),
      ]);

      const myTeam = (Array.isArray(teamResp) ? teamResp : []).find(
        (candidate) => candidate.division === division && candidate.teamId === teamId
      );

      if (myTeam) {
        setTeamData(myTeam);
        setTeamName(myTeam.name || "");
        setPrimaryContactName(myTeam.primaryContact?.name || "");
        setPrimaryContactEmail(myTeam.primaryContact?.email || "");
        setPrimaryContactPhone(myTeam.primaryContact?.phone || "");
        setAssistantCoaches(myTeam.assistantCoaches || []);
        setClinicPreference(myTeam.clinicPreference || "");
      }

      const portalData = requestsResp && typeof requestsResp === "object" ? requestsResp : null;
      setPracticePortal(portalData);
      setPracticeRequests(Array.isArray(portalData?.requests) ? portalData.requests : []);

      const teamGames = readPagedItems(gamesResp).filter(
        (game) =>
          game.offeringTeamId === teamId ||
          game.confirmedTeamId === teamId ||
          game.homeTeamId === teamId ||
          game.awayTeamId === teamId
      );
      setUpcomingGames(teamGames);
    } catch (err) {
      setError(err.message || "Failed to load onboarding data");
    } finally {
      setLoading(false);
    }
  }, [division, teamId]);

  useEffect(() => {
    loadAll();
  }, [loadAll]);

  const activePracticeRequests = useMemo(() => {
    return (Array.isArray(practiceRequests) ? practiceRequests : [])
      .filter((request) => request?.status === "Pending" || request?.status === "Approved")
      .sort((a, b) => {
        const ad = `${a?.date || ""} ${a?.startTime || ""}`.trim();
        const bd = `${b?.date || ""} ${b?.startTime || ""}`.trim();
        return ad.localeCompare(bd);
      });
  }, [practiceRequests]);

  const progress = useMemo(() => {
    const checks = {
      teamNameSet: !!teamName && teamName !== teamId,
      contactInfoComplete: !!(primaryContactName && primaryContactEmail),
      assistantCoachesAdded: assistantCoaches.length > 0,
      practiceSetupStarted: activePracticeRequests.length > 0,
      clinicPreferenceSet: !!clinicPreference,
      scheduleReviewed: upcomingGames.length > 0 || !!teamData?.onboardingComplete,
    };

    const completed = Object.values(checks).filter(Boolean).length;
    const total = Object.keys(checks).length;
    const percentage = Math.round((completed / total) * 100);

    return { checks, completed, total, percentage };
  }, [
    activePracticeRequests.length,
    assistantCoaches.length,
    clinicPreference,
    primaryContactEmail,
    primaryContactName,
    teamData?.onboardingComplete,
    teamId,
    teamName,
    upcomingGames.length,
  ]);

  async function saveTeamInfo() {
    setSavingTeam(true);
    setError("");

    try {
      await apiFetch(`/api/teams/${encodeURIComponent(division)}/${encodeURIComponent(teamId)}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          name: teamName,
          primaryContact: {
            name: primaryContactName,
            email: primaryContactEmail,
            phone: primaryContactPhone,
          },
          assistantCoaches,
          clinicPreference,
        }),
      });

      setToast({ tone: "success", message: "Team information saved successfully" });
      await loadAll();
    } catch (err) {
      setError(err.message || "Failed to save team information");
    } finally {
      setSavingTeam(false);
    }
  }

  async function markOnboardingComplete() {
    const confirmed = await requestConfirm({
      title: "Complete Onboarding",
      message: "Mark onboarding as complete? You can still make changes later.",
      confirmLabel: "Complete",
    });

    if (!confirmed) return;

    setSavingTeam(true);
    setError("");

    try {
      await apiFetch(`/api/teams/${encodeURIComponent(division)}/${encodeURIComponent(teamId)}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          onboardingComplete: true,
        }),
      });

      setToast({ tone: "success", message: "Onboarding complete! Thank you." });
      await loadAll();
    } catch (err) {
      setError(err.message || "Failed to complete onboarding");
    } finally {
      setSavingTeam(false);
    }
  }

  function addAssistantCoach() {
    setAssistantCoaches([...assistantCoaches, { name: "", email: "", phone: "" }]);
  }

  function updateAssistantCoach(index, field, value) {
    const updated = [...assistantCoaches];
    updated[index][field] = value;
    setAssistantCoaches(updated);
  }

  function removeAssistantCoach(index) {
    setAssistantCoaches(assistantCoaches.filter((_, i) => i !== index));
  }

  function openPracticePortal() {
    if (typeof setTab === "function") {
      setTab("practice");
      return;
    }
    if (typeof window !== "undefined") {
      window.location.hash = "#practice";
    }
  }

  if (loading) {
    return (
      <div className="page">
        <StatusCard title="Loading" message="Loading onboarding information..." />
      </div>
    );
  }

  if (!team) {
    return (
      <div className="page">
        <div className="card">
          <div className="card__header">
            <div className="h1">Coach Onboarding</div>
            <div className="subtle">Complete your team setup and schedule preferences</div>
          </div>
          <div className="callout callout--error">
            <div className="font-semibold">Team assignment required</div>
            <div className="mt-2">
              You need to be assigned to a team before completing onboarding. Contact your league administrator.
            </div>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="page">
      <div className="card">
        <div className="card__header">
          <div className="h1">Coach Onboarding</div>
          <div className="subtle">Complete your team setup and use the Practice Portal for recurring and one-off practice workflows.</div>
        </div>
        <div className="layoutStatRow">
          <div className="layoutStat">
            <div className="layoutStat__value">{progress.completed} / {progress.total}</div>
            <div className="layoutStat__label">Setup checks complete</div>
          </div>
          <div className="layoutStat">
            <div className="layoutStat__value">{activePracticeRequests.length}</div>
            <div className="layoutStat__label">Active practice requests</div>
          </div>
          <div className="layoutStat">
            <div className="layoutStat__value">{upcomingGames.length}</div>
            <div className="layoutStat__label">Upcoming games (90 days)</div>
          </div>
          <div className="layoutStat">
            <div className="layoutStat__value">{teamData?.onboardingComplete ? "Done" : "In progress"}</div>
            <div className="layoutStat__label">Onboarding status</div>
          </div>
        </div>
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h2">Setup Progress</div>
        </div>
        <div className="mb-4">
          <div className="row row--between mb-2">
            <span className="font-semibold">{progress.completed} of {progress.total} completed</span>
            <span className="subtle">{progress.percentage}%</span>
          </div>
          <progress className="progressMeter" value={progress.percentage} max={100} />
        </div>
        <div className="grid gap-2">
          <CheckItem checked={progress.checks.teamNameSet} label="Set team name" />
          <CheckItem checked={progress.checks.contactInfoComplete} label="Complete contact information" />
          <CheckItem checked={progress.checks.assistantCoachesAdded} label="Add assistant coach(es)" />
          <CheckItem checked={progress.checks.practiceSetupStarted} label="Start practice setup in the Practice Portal" />
          <CheckItem checked={progress.checks.clinicPreferenceSet} label="Set clinic preference" />
          <CheckItem checked={progress.checks.scheduleReviewed} label="Review your current schedule" />
        </div>
      </div>

      {error ? <div className="callout callout--error">{error}</div> : null}

      <div className="card">
        <div className="card__header">
          <div className="h2">1. Team Information</div>
          <div className="subtle">
            Update your team name and contact information. This is visible to other teams and league administrators.
          </div>
        </div>

        <div className="grid gap-4">
          <label>
            Team Name
            <input
              type="text"
              value={teamName}
              onChange={(e) => setTeamName(e.target.value)}
              placeholder="Enter team name"
            />
          </label>

          <div className="formGrid">
            <label>
              Primary Contact Name
              <input
                type="text"
                value={primaryContactName}
                onChange={(e) => setPrimaryContactName(e.target.value)}
                placeholder="Contact name"
              />
            </label>
            <label>
              Email
              <input
                type="email"
                value={primaryContactEmail}
                onChange={(e) => setPrimaryContactEmail(e.target.value)}
                placeholder="contact@example.com"
              />
            </label>
            <label>
              Phone
              <input
                type="tel"
                value={primaryContactPhone}
                onChange={(e) => setPrimaryContactPhone(e.target.value)}
                placeholder="(555) 123-4567"
              />
            </label>
          </div>

          <div>
            <div className="row row--between mb-2">
              <label className="font-semibold">Assistant Coaches</label>
              <button
                className="btn btn--sm btn--primary"
                onClick={addAssistantCoach}
                disabled={assistantCoaches.length >= 3}
              >
                + Add Assistant
              </button>
            </div>
            {assistantCoaches.length === 0 ? (
              <div className="muted">No assistant coaches added yet</div>
            ) : (
              <div className="grid gap-3">
                {assistantCoaches.map((coach, idx) => (
                  <div key={idx} className="layoutPanel grid gap-2 md:grid-cols-4">
                    <input
                      type="text"
                      value={coach.name || ""}
                      onChange={(e) => updateAssistantCoach(idx, "name", e.target.value)}
                      placeholder="Name"
                    />
                    <input
                      type="email"
                      value={coach.email || ""}
                      onChange={(e) => updateAssistantCoach(idx, "email", e.target.value)}
                      placeholder="Email"
                    />
                    <input
                      type="tel"
                      value={coach.phone || ""}
                      onChange={(e) => updateAssistantCoach(idx, "phone", e.target.value)}
                      placeholder="Phone"
                    />
                    <button
                      className="btn btn--sm btn--ghost"
                      onClick={() => removeAssistantCoach(idx)}
                    >
                      Remove
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>

          <button className="btn btn--primary" onClick={saveTeamInfo} disabled={savingTeam}>
            {savingTeam ? "Saving..." : "Save Team Information"}
          </button>
        </div>
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h2">2. Practice Setup</div>
          <div className="subtle">
            Recurring requests and one-off practice booking now live in the Practice Portal. Use this page to confirm your team profile, then finish practice setup there.
          </div>
        </div>

        <div className="callout mb-4">
          <div className="font-semibold">Practice Portal is the canonical workflow</div>
          <div className="mt-2">
            Open the Practice Portal to request normalized practice space, track commissioner approval, and move active practices without cancelling them first.
          </div>
          <div className="mt-3">
            <button className="btn btn--primary" onClick={openPracticePortal}>
              Open Practice Portal
            </button>
          </div>
        </div>

        {activePracticeRequests.length > 0 ? (
          <div>
            <div className="font-semibold mb-3">Current practice requests</div>
            <div className="grid gap-3">
              {activePracticeRequests.map((request) => (
                <div key={request.requestId} className="layoutPanel row row--between row--wrap">
                  <div className="min-w-0">
                    <div className="font-semibold">
                      {request.date || "-"} - {request.startTime || "-"}-{request.endTime || "-"}
                    </div>
                    <div className="subtle truncate">{request.fieldName || "-"}</div>
                    <div className="subtle mt-1">{request.bookingPolicyLabel || "-"}</div>
                    {request.isMove && request.moveFromDate ? (
                      <div className="subtle mt-1">
                        Move from {request.moveFromDate} {request.moveFromStartTime || "-"}-{request.moveFromEndTime || "-"} {request.moveFromFieldName || ""}
                      </div>
                    ) : null}
                  </div>
                  <StatusBadge status={request.status} />
                </div>
              ))}
            </div>
          </div>
        ) : (
          <div className="muted">
            {practicePortal?.summary?.requestableBlocks
              ? "No active practice requests yet. Start in the Practice Portal when you are ready."
              : "No requestable practice space is visible yet. Ask an administrator to finish inventory normalization if needed."}
          </div>
        )}
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h2">3. Clinic and Open Practice Preference</div>
          <div className="subtle">
            Select your preferred time window for league-wide clinics and open practice sessions.
          </div>
        </div>

        <label>
          Preferred Time Window
          <select
            value={clinicPreference}
            onChange={(e) => setClinicPreference(e.target.value)}
          >
            <option value="">Select preference</option>
            <option value="weekday-evenings">Weekday Evenings (6-9 PM)</option>
            <option value="saturday-mornings">Saturday Mornings (8-11 AM)</option>
            <option value="saturday-afternoons">Saturday Afternoons (12-3 PM)</option>
            <option value="sunday-mornings">Sunday Mornings (8-11 AM)</option>
            <option value="sunday-afternoons">Sunday Afternoons (12-3 PM)</option>
          </select>
        </label>

        <button className="btn btn--primary" onClick={saveTeamInfo} disabled={savingTeam}>
          {savingTeam ? "Saving..." : "Save Preference"}
        </button>
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h2">4. Your Game Schedule</div>
          <div className="subtle">Review your team's upcoming games for the season.</div>
        </div>

        {upcomingGames.length === 0 ? (
          <div className="callout callout--info">
            No games scheduled yet. Games will appear here once the league finalizes the schedule.
          </div>
        ) : (
          <div className="grid gap-3 max-h-96 overflow-y-auto">
            {upcomingGames.slice(0, 15).map((game) => {
              const isHome = game.homeTeamId === teamId;
              const opponent = isHome ? game.awayTeamId : game.homeTeamId;
              const vsText = opponent ? (isHome ? `vs ${opponent}` : `@ ${opponent}`) : "TBD";

              return (
                <div key={game.slotId} className="layoutPanel row row--between row--wrap">
                  <div className="min-w-0">
                    <div className="font-semibold">
                      {game.gameDate} - {game.startTime}
                    </div>
                    <div className="subtle truncate">
                      {vsText} - {game.displayName || game.fieldKey}
                    </div>
                  </div>
                  {isHome ? <span className="statusBadge status-confirmed">Home</span> : <span className="statusBadge">Away</span>}
                </div>
              );
            })}
          </div>
        )}
      </div>

      {!teamData?.onboardingComplete ? (
        <div className="card">
          <div className="card__header">
            <div className="h2">Ready to Complete Setup?</div>
            <div className="subtle">
              Once you've reviewed everything, mark your onboarding as complete. You can still make changes later if needed.
            </div>
          </div>
          <div className="callout callout--ok mb-3">
            Complete at least 50% of setup checks before submitting onboarding.
          </div>
          <button
            className="btn btn--primary"
            onClick={markOnboardingComplete}
            disabled={savingTeam || progress.percentage < 50}
            title={progress.percentage < 50 ? "Complete at least 50% of setup first" : ""}
          >
            Complete Onboarding Setup
          </button>
        </div>
      ) : (
        <div className="card">
          <div className="card__header">
            <div className="h2">Onboarding Complete</div>
            <div className="subtle">You can still update your information at any time.</div>
          </div>
          <div className="callout callout--info">Your team onboarding is marked complete.</div>
        </div>
      )}

      {toast ? <Toast tone={toast.tone} message={toast.message} onClose={() => setToast(null)} /> : null}
      <ConfirmDialog state={confirmState} onConfirm={handleConfirm} onCancel={handleCancel} />
    </div>
  );
}

function CheckItem({ checked, label }) {
  return (
    <div className="row">
      <span className={`statusBadge ${checked ? "status-confirmed" : "status-open"}`}>
        {checked ? "Done" : "Pending"}
      </span>
      <span className={checked ? "font-semibold" : "muted"}>{label}</span>
    </div>
  );
}

function StatusBadge({ status }) {
  const config = {
    Pending: { className: "statusBadge status-open", label: "Pending Approval" },
    Approved: { className: "statusBadge status-confirmed", label: "Approved" },
    Rejected: { className: "statusBadge status-cancelled", label: "Rejected" },
  };

  const { className, label } = config[status] || config.Pending;
  return <span className={className}>{label}</span>;
}
