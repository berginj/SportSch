import { useEffect, useState, useCallback, useMemo } from 'react';
import { apiFetch } from '../lib/api';
import StatusCard from '../components/StatusCard';
import Toast from '../components/Toast';
import { ConfirmDialog } from '../components/Dialogs';
import { useConfirmDialog } from '../lib/useDialogs';

const WEEKDAY_FILTER_OPTIONS = [
  { key: '', label: 'All days' },
  { key: '0', label: 'Sunday' },
  { key: '1', label: 'Monday' },
  { key: '2', label: 'Tuesday' },
  { key: '3', label: 'Wednesday' },
  { key: '4', label: 'Thursday' },
  { key: '5', label: 'Friday' },
  { key: '6', label: 'Saturday' },
];

function weekdayKeyFromDate(isoDate) {
  const parts = String(isoDate || '').split('-');
  if (parts.length !== 3) return '';
  const year = Number(parts[0]);
  const month = Number(parts[1]);
  const day = Number(parts[2]);
  if (!year || !month || !day) return '';
  return String(new Date(Date.UTC(year, month - 1, day)).getUTCDay());
}

function weekdayLabelFromDate(isoDate) {
  const key = weekdayKeyFromDate(isoDate);
  return WEEKDAY_FILTER_OPTIONS.find((opt) => opt.key === key)?.label || '';
}

function weekKeyFromDate(isoDate) {
  const parts = String(isoDate || '').split('-');
  if (parts.length !== 3) return '';
  const year = Number(parts[0]);
  const month = Number(parts[1]);
  const day = Number(parts[2]);
  if (!year || !month || !day) return '';
  const date = new Date(Date.UTC(year, month - 1, day));
  const dayNum = date.getUTCDay() || 7;
  date.setUTCDate(date.getUTCDate() + 4 - dayNum);
  const yearStart = new Date(Date.UTC(date.getUTCFullYear(), 0, 1));
  const weekNo = Math.ceil((((date - yearStart) / 86400000) + 1) / 7);
  return `${date.getUTCFullYear()}-W${String(weekNo).padStart(2, '0')}`;
}

function practicePatternKey(slot) {
  const weekday = weekdayKeyFromDate(slot?.gameDate);
  const fieldKey = String(slot?.fieldKey || '').trim();
  const start = String(slot?.startTime || '').trim();
  const end = String(slot?.endTime || '').trim();
  if (!weekday || !fieldKey || !start || !end) return '';
  return `${weekday}|${fieldKey}|${start}|${end}`;
}

function formatSlotTime(slot) {
  const start = String(slot?.startTime || '').trim();
  const end = String(slot?.endTime || '').trim();
  if (!start || !end) return '';
  return `${start}-${end}`;
}

function formatSlotLocation(slot) {
  return slot?.displayName || `${slot?.parkName || ''} ${slot?.fieldName || ''}`.trim() || slot?.fieldKey || 'Field TBD';
}

function isPracticeCapableAvailability(slot) {
  if (!slot?.isAvailability || (slot?.status || '') !== 'Open') return false;
  const allocationType = String(slot?.allocationSlotType || slot?.slotType || '').trim().toLowerCase();
  if (!allocationType) return true;
  return allocationType === 'practice' || allocationType === 'both';
}

/**
 * Coach Onboarding Page
 *
 * Guides coaches through initial team setup:
 * 1. Update team info (name, contacts, assistant coaches)
 * 2. Request 1-3 practice slots (requires commissioner approval)
 * 3. Select preferred clinic time window
 * 4. View game schedule
 * 5. Track onboarding progress
 */
export default function CoachOnboardingPage({ me, leagueId }) {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [toast, setToast] = useState(null);

  // Get coach's team assignment
  const membership = (me?.memberships || []).find(m => m.leagueId === leagueId && m.role === 'Coach');
  const team = membership?.team;
  const division = team?.division;
  const teamId = team?.teamId;

  // Data state
  const [teamData, setTeamData] = useState(null);
  const [practiceRequests, setPracticeRequests] = useState([]);
  const [availableSlots, setAvailableSlots] = useState([]);
  const [upcomingGames, setUpcomingGames] = useState([]);
  const [divisionTeams, setDivisionTeams] = useState([]);

  // Team editing state
  const [teamName, setTeamName] = useState('');
  const [primaryContactName, setPrimaryContactName] = useState('');
  const [primaryContactEmail, setPrimaryContactEmail] = useState('');
  const [primaryContactPhone, setPrimaryContactPhone] = useState('');
  const [assistantCoaches, setAssistantCoaches] = useState([]);
  const [clinicPreference, setClinicPreference] = useState('');
  const [openToShareField, setOpenToShareField] = useState(false);
  const [shareWithTeamId, setShareWithTeamId] = useState('');
  const [practiceDayFilter, setPracticeDayFilter] = useState('0');

  // UI state
  const [savingTeam, setSavingTeam] = useState(false);
  const [requestingSlot, setRequestingSlot] = useState('');

  const { confirmState, requestConfirm, handleConfirm, handleCancel } = useConfirmDialog();

  const loadAll = useCallback(async () => {
    if (!division || !teamId) {
      setLoading(false);
      return;
    }

    setLoading(true);
    setError('');

    try {
      // Load team details, practice requests, available slots, and games in parallel
      const [teamResp, requestsResp, slotsResp, gamesResp] = await Promise.all([
        apiFetch(`/api/teams?division=${division}`).catch(() => []),
        apiFetch(`/api/practice-requests?teamId=${teamId}`).catch(() => []),
        apiFetch(`/api/slots?division=${division}&status=Open`).catch(() => []),
        apiFetch(`/api/slots?division=${division}&status=Confirmed&dateFrom=${getTodayDate()}&dateTo=${getDateInDays(90)}`).catch(() => [])
      ]);

      // Find this team's data
      const myTeam = (Array.isArray(teamResp) ? teamResp : []).find(
        t => t.division === division && t.teamId === teamId
      );
      setDivisionTeams(Array.isArray(teamResp) ? teamResp : []);

      if (myTeam) {
        setTeamData(myTeam);
        setTeamName(myTeam.name || '');
        setPrimaryContactName(myTeam.primaryContact?.name || '');
        setPrimaryContactEmail(myTeam.primaryContact?.email || '');
        setPrimaryContactPhone(myTeam.primaryContact?.phone || '');
        setAssistantCoaches(myTeam.assistantCoaches || []);
        setClinicPreference(myTeam.clinicPreference || '');
      }

      setPracticeRequests(Array.isArray(requestsResp) ? requestsResp : []);

      // Filter available practice slots (isAvailability slots only)
      const practiceSlots = (Array.isArray(slotsResp) ? slotsResp : []).filter(isPracticeCapableAvailability);
      setAvailableSlots(practiceSlots);

      // Filter games where this team is playing
      const teamGames = (Array.isArray(gamesResp) ? gamesResp : []).filter(
        g => g.offeringTeamId === teamId ||
             g.confirmedTeamId === teamId ||
             g.homeTeamId === teamId ||
             g.awayTeamId === teamId
      );
      setUpcomingGames(teamGames);
    } catch (err) {
      setError(err.message || 'Failed to load onboarding data');
    } finally {
      setLoading(false);
    }
  }, [division, teamId]);

  useEffect(() => {
    loadAll();
  }, [loadAll]);

  const shareableTeams = useMemo(() => {
    return (Array.isArray(divisionTeams) ? divisionTeams : [])
      .filter((t) => (t?.teamId || '') && t.teamId !== teamId)
      .sort((a, b) => String(a?.name || a?.teamId || '').localeCompare(String(b?.name || b?.teamId || '')));
  }, [divisionTeams, teamId]);

  useEffect(() => {
    if (!openToShareField && shareWithTeamId) {
      setShareWithTeamId('');
      return;
    }
    if (!openToShareField || !shareWithTeamId) return;
    if (!shareableTeams.some((t) => t.teamId === shareWithTeamId)) {
      setShareWithTeamId('');
    }
  }, [openToShareField, shareWithTeamId, shareableTeams]);

  const activePracticeRequests = useMemo(() => {
    return (Array.isArray(practiceRequests) ? practiceRequests : [])
      .filter((r) => r?.status === 'Pending' || r?.status === 'Approved')
      .sort((a, b) => {
        const pa = Number.isFinite(Number(a?.priority)) ? Number(a.priority) : 99;
        const pb = Number.isFinite(Number(b?.priority)) ? Number(b.priority) : 99;
        if (pa !== pb) return pa - pb;
        const ad = `${a?.slot?.gameDate || ''} ${a?.slot?.startTime || ''}`.trim();
        const bd = `${b?.slot?.gameDate || ''} ${b?.slot?.startTime || ''}`.trim();
        return ad.localeCompare(bd);
      });
  }, [practiceRequests]);

  const nextPracticeRequestPriority = useMemo(() => {
    const used = new Set(
      activePracticeRequests
        .map((r) => Number(r?.priority))
        .filter((p) => Number.isFinite(p) && p >= 1 && p <= 3)
    );
    for (const p of [1, 2, 3]) {
      if (!used.has(p)) return p;
    }
    return 0;
  }, [activePracticeRequests]);

  const seasonWeekOrdinalByKey = useMemo(() => {
    const keys = Array.from(new Set(
      (availableSlots || [])
        .map((slot) => weekKeyFromDate(slot?.gameDate))
        .filter(Boolean)
    )).sort();
    const map = new Map();
    keys.forEach((key, index) => map.set(key, index + 1));
    return map;
  }, [availableSlots]);

  const activeRequestPatternByKey = useMemo(() => {
    const map = new Map();
    for (const req of activePracticeRequests) {
      const key = practicePatternKey(req?.slot);
      if (key && !map.has(key)) map.set(key, req);
    }
    return map;
  }, [activePracticeRequests]);

  const filteredAvailableSlots = useMemo(() => {
    if (!practiceDayFilter) return availableSlots;
    return (availableSlots || []).filter((slot) => weekdayKeyFromDate(slot?.gameDate) === practiceDayFilter);
  }, [availableSlots, practiceDayFilter]);

  const recurringPracticeChoices = useMemo(() => {
    const groups = new Map();
    for (const slot of filteredAvailableSlots) {
      const key = practicePatternKey(slot);
      if (!key) continue;
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key).push(slot);
    }

    const sortedChoices = [];
    for (const [key, groupSlots] of groups.entries()) {
      const slotsSorted = [...groupSlots].sort((a, b) => {
        const ad = `${a?.gameDate || ''} ${a?.startTime || ''}`.trim();
        const bd = `${b?.gameDate || ''} ${b?.startTime || ''}`.trim();
        return ad.localeCompare(bd);
      });
      const representativeSlot = slotsSorted[0];
      const weekOrdinals = slotsSorted
        .map((slot) => seasonWeekOrdinalByKey.get(weekKeyFromDate(slot?.gameDate)))
        .filter((n) => Number.isFinite(n));
      const minWeek = weekOrdinals.length ? Math.min(...weekOrdinals) : null;
      const maxWeek = weekOrdinals.length ? Math.max(...weekOrdinals) : null;
      sortedChoices.push({
        key,
        representativeSlot,
        slots: slotsSorted,
        weeksCount: slotsSorted.length,
        weekRangeLabel: minWeek && maxWeek
          ? (minWeek === maxWeek ? `W${minWeek}` : `W${minWeek}-W${maxWeek}`)
          : '-',
        firstDate: slotsSorted[0]?.gameDate || '',
        lastDate: slotsSorted[slotsSorted.length - 1]?.gameDate || '',
        existingRequest: activeRequestPatternByKey.get(key) || null,
      });
    }

    return sortedChoices.sort((a, b) => {
      const aRequested = !!a.existingRequest;
      const bRequested = !!b.existingRequest;
      if (aRequested !== bRequested) return aRequested ? -1 : 1;
      const aPriority = Number(a?.existingRequest?.priority || 99);
      const bPriority = Number(b?.existingRequest?.priority || 99);
      if (aPriority !== bPriority) return aPriority - bPriority;
      if (a.weeksCount !== b.weeksCount) return b.weeksCount - a.weeksCount;
      const aKey = `${a?.representativeSlot?.gameDate || ''} ${a?.representativeSlot?.startTime || ''}`.trim();
      const bKey = `${b?.representativeSlot?.gameDate || ''} ${b?.representativeSlot?.startTime || ''}`.trim();
      return aKey.localeCompare(bKey);
    });
  }, [filteredAvailableSlots, seasonWeekOrdinalByKey, activeRequestPatternByKey]);

  // Calculate onboarding progress
  const progress = useMemo(() => {
    const checks = {
      teamNameSet: !!teamName && teamName !== teamId,
      contactInfoComplete: !!(primaryContactName && primaryContactEmail),
      assistantCoachesAdded: assistantCoaches.length > 0,
      practiceRequested: practiceRequests.filter(r => r.status === 'Pending' || r.status === 'Approved').length > 0,
      clinicPreferenceSet: !!clinicPreference,
      scheduleReviewed: teamData?.onboardingComplete || false
    };

    const completed = Object.values(checks).filter(Boolean).length;
    const total = Object.keys(checks).length;
    const percentage = Math.round((completed / total) * 100);

    return { checks, completed, total, percentage };
  }, [teamName, teamId, primaryContactName, primaryContactEmail, assistantCoaches, practiceRequests, clinicPreference, teamData]);

  async function saveTeamInfo() {
    setSavingTeam(true);
    setError('');

    try {
      await apiFetch(`/api/teams/${encodeURIComponent(division)}/${encodeURIComponent(teamId)}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: teamName,
          primaryContact: {
            name: primaryContactName,
            email: primaryContactEmail,
            phone: primaryContactPhone
          },
          assistantCoaches: assistantCoaches,
          clinicPreference: clinicPreference
        })
      });

      setToast({ tone: 'success', message: 'Team information saved successfully' });
      await loadAll();
    } catch (err) {
      setError(err.message || 'Failed to save team information');
    } finally {
      setSavingTeam(false);
    }
  }

  async function requestPracticePattern(choice) {
    const slot = choice?.representativeSlot;
    if (!slot?.slotId) return;

    // Check if already at max requests
    const pendingOrApproved = activePracticeRequests;
    if (pendingOrApproved.length >= 3) {
      setError('You can only request up to 3 practice slots. Please wait for commissioner approval or withdraw a request.');
      return;
    }
    if (!nextPracticeRequestPriority) {
      setError('You already have 3 active recurring practice requests. Please wait for commissioner review.');
      return;
    }
    if (choice?.existingRequest) {
      setError('You already requested this recurring field/day/time pattern.');
      return;
    }
    if (openToShareField && !shareWithTeamId) {
      setError('Select a team to propose sharing with, or uncheck "Open to sharing a field".');
      return;
    }

    const proposedShareTeam = shareableTeams.find((t) => t.teamId === shareWithTeamId);
    const shareMessage = openToShareField
      ? ` Proposed sharing team: ${proposedShareTeam?.name || shareWithTeamId}.`
      : '';
    const weekRange = choice?.weekRangeLabel && choice.weekRangeLabel !== '-' ? ` (${choice.weekRangeLabel})` : '';
    const recurringSummary = choice?.weeksCount
      ? ` This requests the recurring ${weekdayLabelFromDate(slot.gameDate)} pattern for ${choice.weeksCount} week(s)${weekRange}.`
      : ' This requests the recurring field/day/time pattern.';

    const confirmed = await requestConfirm({
      title: 'Request Recurring Practice Pattern',
      message: `Request priority #${nextPracticeRequestPriority} for ${formatSlotLocation(slot)} at ${formatSlotTime(slot)}?${recurringSummary} Commissioner approval will reserve matching weeks.${shareMessage}`,
      confirmLabel: 'Request'
    });

    if (!confirmed) return;

    setRequestingSlot(slot.slotId);
    setError('');

    try {
      await apiFetch('/api/practice-requests', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          division: division,
          teamId: teamId,
          slotId: slot.slotId,
          priority: nextPracticeRequestPriority,
          reason: 'Recurring practice request from coach onboarding',
          openToShareField,
          shareWithTeamId: openToShareField ? shareWithTeamId : ''
        })
      });

      setToast({
        tone: 'success',
        message: openToShareField
          ? `Recurring practice request P${nextPracticeRequestPriority} submitted. Sharing preference sent to commissioner.`
          : `Recurring practice request P${nextPracticeRequestPriority} submitted. Awaiting commissioner approval.`
      });
      await loadAll();
    } catch (err) {
      setError(err.message || 'Failed to request practice slot');
    } finally {
      setRequestingSlot('');
    }
  }

  async function markOnboardingComplete() {
    const confirmed = await requestConfirm({
      title: 'Complete Onboarding',
      message: 'Mark onboarding as complete? You can still make changes later.',
      confirmLabel: 'Complete'
    });

    if (!confirmed) return;

    setSavingTeam(true);
    setError('');

    try {
      await apiFetch(`/api/teams/${encodeURIComponent(division)}/${encodeURIComponent(teamId)}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          onboardingComplete: true
        })
      });

      setToast({ tone: 'success', message: 'Onboarding complete! Thank you.' });
      await loadAll();
    } catch (err) {
      setError(err.message || 'Failed to complete onboarding');
    } finally {
      setSavingTeam(false);
    }
  }

  function addAssistantCoach() {
    setAssistantCoaches([...assistantCoaches, { name: '', email: '', phone: '' }]);
  }

  function updateAssistantCoach(index, field, value) {
    const updated = [...assistantCoaches];
    updated[index][field] = value;
    setAssistantCoaches(updated);
  }

  function removeAssistantCoach(index) {
    setAssistantCoaches(assistantCoaches.filter((_, i) => i !== index));
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
          <div className="subtle">Complete your team setup and schedule preferences</div>
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
            <div className="layoutStat__value">{recurringPracticeChoices.length}</div>
            <div className="layoutStat__label">Recurring patterns available</div>
          </div>
          <div className="layoutStat">
            <div className="layoutStat__value">{upcomingGames.length}</div>
            <div className="layoutStat__label">Upcoming games (90 days)</div>
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
          <CheckItem checked={progress.checks.practiceRequested} label="Request practice slots (1-3)" />
          <CheckItem checked={progress.checks.clinicPreferenceSet} label="Set clinic preference" />
          <CheckItem checked={progress.checks.scheduleReviewed} label="Review schedule & complete setup" />
        </div>
      </div>

      {error && <div className="callout callout--error">{error}</div>}

      <div className="card">
        <div className="card__header">
          <div className="h2">1. Team Information</div>
          <div className="subtle">
            Update your team name and contact information. This will be visible to other teams and league administrators.
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
                      value={coach.name || ''}
                      onChange={(e) => updateAssistantCoach(idx, 'name', e.target.value)}
                      placeholder="Name"
                    />
                    <input
                      type="email"
                      value={coach.email || ''}
                      onChange={(e) => updateAssistantCoach(idx, 'email', e.target.value)}
                      placeholder="Email"
                    />
                    <input
                      type="tel"
                      value={coach.phone || ''}
                      onChange={(e) => updateAssistantCoach(idx, 'phone', e.target.value)}
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
            {savingTeam ? 'Saving...' : 'Save Team Information'}
          </button>
        </div>
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h2">2. Practice Slot Requests</div>
          <div className="subtle">
            Request 1-3 recurring practice patterns. Pick a field/time pattern once and commissioner approval will reserve matching weeks.
          </div>
        </div>

        {activePracticeRequests.length > 0 && (
          <div className="mb-4">
            <div className="font-semibold mb-3">Your practice requests</div>
            <div className="grid gap-3">
              {activePracticeRequests.map((req) => (
                <div
                  key={req.requestId}
                  className="layoutPanel row row--between row--wrap"
                >
                  <div className="min-w-0">
                    <div className="font-semibold">
                      {req.slot?.gameDate} - {req.slot?.startTime}-{req.slot?.endTime}
                    </div>
                    <div className="subtle truncate">
                      {formatSlotLocation(req.slot)}
                    </div>
                    {req.openToShareField ? (
                      <div className="subtle mt-1">
                        Open to share field{req.shareWithTeamId ? ` with ${req.shareWithTeamId}` : ''}.
                      </div>
                    ) : null}
                  </div>
                  <StatusBadge status={req.status} />
                </div>
              ))}
            </div>
          </div>
        )}

        <div>
          <div className="callout mb-4">
            <div className="font-semibold mb-2">Sharing preference (applies to new requests)</div>
            <div className="formGrid">
              <label className="inlineCheck inlineCheck--compact">
                <input
                  type="checkbox"
                  checked={openToShareField}
                  onChange={(e) => setOpenToShareField(e.target.checked)}
                />
                <span>Open to sharing a field</span>
              </label>
              <label>
                Propose sharing with team
                <select
                  value={shareWithTeamId}
                  onChange={(e) => setShareWithTeamId(e.target.value)}
                  disabled={!openToShareField || shareableTeams.length === 0}
                >
                  <option value="">
                    {!openToShareField ? 'Enable sharing first' : (shareableTeams.length ? 'Select a team' : 'No other teams in division')}
                  </option>
                  {shareableTeams.map((t) => (
                    <option key={t.teamId} value={t.teamId}>
                      {t.name ? `${t.name} (${t.teamId})` : t.teamId}
                    </option>
                  ))}
                </select>
              </label>
            </div>
            <div className="subtle mt-2">
              Request up to 3 recurring practice patterns. Your sharing preference is attached to each new request.
            </div>
          </div>
          <div className="row row--between row--wrap mb-3">
            <div className="font-semibold">Recurring practice choices (reserve all matching weeks)</div>
            <label className="min-w-[220px]">
              View by day
              <select
                value={practiceDayFilter}
                onChange={(e) => setPracticeDayFilter(e.target.value)}
              >
                {WEEKDAY_FILTER_OPTIONS.map((opt) => (
                  <option key={opt.key || 'all'} value={opt.key}>{opt.label}</option>
                ))}
              </select>
            </label>
          </div>
          <div className="subtle mb-3">
            Each row is one recurring field/day/time pattern. Weeks show the season week range (W1-Wx) that will be reserved when approved.
          </div>
          {recurringPracticeChoices.length === 0 ? (
            <div className="muted mb-3">
              {practiceDayFilter ? 'No recurring practice patterns for the selected day.' : 'No recurring practice patterns available at this time.'}
            </div>
          ) : (
            <div className="tableWrap tableWrap--sticky mb-4 max-h-[22rem]">
              <table className="table">
                <thead>
                  <tr>
                    <th>Field</th>
                    <th>Day</th>
                    <th>Time</th>
                    <th>Weeks</th>
                    <th>Count</th>
                    <th>Season Span</th>
                    <th>Status</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {recurringPracticeChoices.map((choice) => {
                    const slot = choice.representativeSlot;
                    const existingRequest = choice.existingRequest;
                    const maxRequests = activePracticeRequests.length >= 3;
                    const disabled = !!existingRequest || maxRequests || !nextPracticeRequestPriority || requestingSlot === slot?.slotId;
                    return (
                      <tr key={choice.key}>
                        <td>{formatSlotLocation(slot)}</td>
                        <td>{weekdayLabelFromDate(slot?.gameDate)}</td>
                        <td>{formatSlotTime(slot)}</td>
                        <td>{choice.weekRangeLabel}</td>
                        <td>{choice.weeksCount}</td>
                        <td>{choice.firstDate && choice.lastDate ? `${choice.firstDate} - ${choice.lastDate}` : (choice.firstDate || '-')}</td>
                        <td>{existingRequest ? `Requested (P${existingRequest.priority || '?'}, ${existingRequest.status})` : '-'}</td>
                        <td className="tableActions">
                          <button
                            className="btn btn--sm btn--primary"
                            onClick={() => requestPracticePattern(choice)}
                            disabled={disabled}
                            title={
                              existingRequest
                                ? 'Already requested'
                                : !nextPracticeRequestPriority
                                  ? 'Maximum 3 requests'
                                  : 'Request this recurring pattern'
                            }
                          >
                            {existingRequest
                              ? 'Requested'
                              : requestingSlot === slot?.slotId
                                ? 'Requesting...'
                                : (nextPracticeRequestPriority ? `Request P${nextPracticeRequestPriority}` : 'Max 3')}
                          </button>
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
          {savingTeam ? 'Saving...' : 'Save Preference'}
        </button>
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h2">4. Your Game Schedule</div>
          <div className="subtle">Review your team's upcoming games for the season.</div>
        </div>

        {upcomingGames.length === 0 ? (
          <div className="callout callout--info">
            No games scheduled yet. Games will appear here once the commissioner finalizes the schedule.
          </div>
        ) : (
          <div className="grid gap-3 max-h-96 overflow-y-auto">
            {upcomingGames.slice(0, 15).map((game) => {
              const isHome = game.homeTeamId === teamId;
              const opponent = isHome ? game.awayTeamId : game.homeTeamId;
              const vsText = opponent ? (isHome ? `vs ${opponent}` : `@ ${opponent}`) : 'TBD';

              return (
                <div
                  key={game.slotId}
                  className="layoutPanel row row--between row--wrap"
                >
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

      {!teamData?.onboardingComplete && (
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
            title={progress.percentage < 50 ? 'Complete at least 50% of setup first' : ''}
          >
            Complete Onboarding Setup
          </button>
        </div>
      )}

      {teamData?.onboardingComplete && (
        <div className="card">
          <div className="card__header">
            <div className="h2">Onboarding Complete</div>
            <div className="subtle">You can still update your information at any time.</div>
          </div>
          <div className="callout callout--info">
            Your team onboarding is marked complete.
          </div>
        </div>
      )}

      {toast && <Toast tone={toast.tone} message={toast.message} onClose={() => setToast(null)} />}
      <ConfirmDialog state={confirmState} onConfirm={handleConfirm} onCancel={handleCancel} />
    </div>
  );
}

function CheckItem({ checked, label }) {
  return (
    <div className="row">
      <span className={`statusBadge ${checked ? 'status-confirmed' : 'status-open'}`}>
        {checked ? 'Done' : 'Pending'}
      </span>
      <span className={checked ? 'font-semibold' : 'muted'}>{label}</span>
    </div>
  );
}

function StatusBadge({ status }) {
  const config = {
    Pending: { className: 'statusBadge status-open', label: 'Pending Approval' },
    Approved: { className: 'statusBadge status-confirmed', label: 'Approved' },
    Rejected: { className: 'statusBadge status-cancelled', label: 'Rejected' }
  };

  const { className, label } = config[status] || config.Pending;
  return <span className={className}>{label}</span>;
}

function getTodayDate() {
  return new Date().toISOString().split('T')[0];
}

function getDateInDays(days) {
  const date = new Date();
  date.setDate(date.getDate() + days);
  return date.toISOString().split('T')[0];
}
