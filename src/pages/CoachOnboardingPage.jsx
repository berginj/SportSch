import { useEffect, useState, useCallback, useMemo } from 'react';
import { apiFetch } from '../lib/api';
import StatusCard from '../components/StatusCard';
import Toast from '../components/Toast';
import { ConfirmDialog } from '../components/Dialogs';
import { useConfirmDialog } from '../lib/useDialogs';

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

  // Team editing state
  const [teamName, setTeamName] = useState('');
  const [primaryContactName, setPrimaryContactName] = useState('');
  const [primaryContactEmail, setPrimaryContactEmail] = useState('');
  const [primaryContactPhone, setPrimaryContactPhone] = useState('');
  const [assistantCoaches, setAssistantCoaches] = useState([]);
  const [clinicPreference, setClinicPreference] = useState('');

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
      const practiceSlots = (Array.isArray(slotsResp) ? slotsResp : []).filter(
        s => s.isAvailability === true && s.status === 'Open'
      );
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

  async function requestPracticeSlot(slot) {
    if (!slot?.slotId) return;

    // Check if already at max requests
    const pendingOrApproved = practiceRequests.filter(
      r => r.status === 'Pending' || r.status === 'Approved'
    );
    if (pendingOrApproved.length >= 3) {
      setError('You can only request up to 3 practice slots. Please wait for commissioner approval or withdraw a request.');
      return;
    }

    const confirmed = await requestConfirm({
      title: 'Request Practice Slot',
      message: `Request ${slot.gameDate} at ${slot.startTime}-${slot.endTime} for practice? This requires commissioner approval.`,
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
          reason: 'Practice request from coach onboarding'
        })
      });

      setToast({ tone: 'success', message: 'Practice slot requested. Awaiting commissioner approval.' });
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
          <h1 className="text-2xl font-bold mb-4">Coach Onboarding</h1>
          <div className="callout callout--error">
            <strong>Team Assignment Required</strong>
            <p className="mt-2">
              You need to be assigned to a team before completing onboarding. Contact your league administrator.
            </p>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="page">
      {/* Header */}
      <div className="mb-6">
        <h1 className="text-3xl font-bold mb-2">Coach Onboarding</h1>
        <p className="text-gray-600">Complete your team setup and schedule preferences</p>
      </div>

      {/* Progress Card */}
      <div className="card mb-6">
        <h2 className="text-xl font-bold mb-4">Setup Progress</h2>
        <div className="mb-4">
          <div className="flex items-center justify-between mb-2">
            <span className="font-semibold">{progress.completed} of {progress.total} completed</span>
            <span className="text-sm text-gray-600">{progress.percentage}%</span>
          </div>
          <div className="w-full bg-gray-200 rounded-full h-3">
            <div
              className="bg-accent h-3 rounded-full transition-all duration-300"
              style={{ width: `${progress.percentage}%` }}
            />
          </div>
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

      {error && (
        <div className="callout callout--error mb-6">{error}</div>
      )}

      {/* Section 1: Team Information */}
      <div className="card mb-6">
        <h2 className="text-xl font-bold mb-4">1. Team Information</h2>
        <p className="text-sm text-gray-600 mb-4">
          Update your team name and contact information. This will be visible to other teams and league administrators.
        </p>

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

          <div className="grid md:grid-cols-3 gap-4">
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
            <div className="flex items-center justify-between mb-2">
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
              <div className="text-sm text-gray-600">No assistant coaches added yet</div>
            ) : (
              <div className="grid gap-3">
                {assistantCoaches.map((coach, idx) => (
                  <div key={idx} className="grid md:grid-cols-4 gap-2 p-3 bg-gray-50 rounded border border-gray-200">
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
                      className="btn btn--sm"
                      onClick={() => removeAssistantCoach(idx)}
                    >
                      Remove
                    </button>
                  </div>
                ))}
              </div>
            )}
          </div>

          <button
            className="btn btn--primary w-full md:w-auto"
            onClick={saveTeamInfo}
            disabled={savingTeam}
          >
            {savingTeam ? 'Saving...' : 'Save Team Information'}
          </button>
        </div>
      </div>

      {/* Section 2: Practice Slot Requests */}
      <div className="card mb-6">
        <h2 className="text-xl font-bold mb-4">2. Practice Slot Requests</h2>
        <p className="text-sm text-gray-600 mb-4">
          Request 1-3 practice slots. Your requests will be reviewed by the commissioner for approval.
          You can request up to 3 slots total.
        </p>

        {/* Your Requests */}
        {practiceRequests.length > 0 && (
          <div className="mb-6">
            <h3 className="font-semibold mb-3">Your Practice Requests</h3>
            <div className="grid gap-3">
              {practiceRequests.map((req) => (
                <div
                  key={req.requestId}
                  className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 p-3 bg-gray-50 rounded border border-gray-200"
                >
                  <div className="flex-1 min-w-0">
                    <div className="font-semibold">
                      {req.slot?.gameDate} • {req.slot?.startTime}-{req.slot?.endTime}
                    </div>
                    <div className="text-sm text-gray-600 truncate">
                      {req.slot?.displayName || req.slot?.fieldKey || 'Field TBD'}
                    </div>
                  </div>
                  <StatusBadge status={req.status} />
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Available Slots */}
        <div>
          <h3 className="font-semibold mb-3">Available Practice Slots</h3>
          {availableSlots.length === 0 ? (
            <div className="text-sm text-gray-600">No available practice slots at this time</div>
          ) : (
            <div className="grid gap-2 max-h-96 overflow-y-auto">
              {availableSlots.slice(0, 20).map((slot) => {
                const alreadyRequested = practiceRequests.some(
                  r => r.slotId === slot.slotId && (r.status === 'Pending' || r.status === 'Approved')
                );
                const maxRequests = practiceRequests.filter(r => r.status === 'Pending' || r.status === 'Approved').length >= 3;

                return (
                  <div
                    key={slot.slotId}
                    className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 p-3 bg-white border border-gray-200 rounded hover:bg-gray-50"
                  >
                    <div className="flex-1 min-w-0">
                      <div className="font-semibold">
                        {slot.gameDate} • {slot.startTime}-{slot.endTime}
                      </div>
                      <div className="text-sm text-gray-600 truncate">
                        {slot.displayName || slot.fieldKey || 'Field TBD'}
                      </div>
                    </div>
                    <button
                      className="btn btn--sm btn--primary sm:flex-shrink-0"
                      onClick={() => requestPracticeSlot(slot)}
                      disabled={alreadyRequested || maxRequests || requestingSlot === slot.slotId}
                      title={
                        alreadyRequested ? 'Already requested' :
                        maxRequests ? 'Maximum 3 requests' :
                        'Request this slot'
                      }
                    >
                      {alreadyRequested ? 'Requested' : requestingSlot === slot.slotId ? 'Requesting...' : 'Request'}
                    </button>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>

      {/* Section 3: Clinic Preference */}
      <div className="card mb-6">
        <h2 className="text-xl font-bold mb-4">3. Clinic & Open Practice Preference</h2>
        <p className="text-sm text-gray-600 mb-4">
          Select your preferred time window for league-wide clinics and open practice sessions.
        </p>

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

        <button
          className="btn btn--primary mt-4"
          onClick={saveTeamInfo}
          disabled={savingTeam}
        >
          {savingTeam ? 'Saving...' : 'Save Preference'}
        </button>
      </div>

      {/* Section 4: Schedule Review */}
      <div className="card mb-6">
        <h2 className="text-xl font-bold mb-4">4. Your Game Schedule</h2>
        <p className="text-sm text-gray-600 mb-4">
          Review your team's upcoming games for the season.
        </p>

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
                  className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2 p-3 bg-gray-50 rounded border border-gray-200"
                >
                  <div className="flex-1 min-w-0">
                    <div className="font-semibold">
                      {game.gameDate} • {game.startTime}
                    </div>
                    <div className="text-sm text-gray-600 truncate">
                      {vsText} • {game.displayName || game.fieldKey}
                    </div>
                  </div>
                  {isHome && <span className="badge badge--success">Home</span>}
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* Complete Onboarding Button */}
      {!teamData?.onboardingComplete && (
        <div className="card bg-gradient-to-br from-green-50 to-green-100 border-green-200">
          <h2 className="text-xl font-bold text-green-900 mb-2">Ready to Complete Setup?</h2>
          <p className="text-sm text-green-800 mb-4">
            Once you've reviewed everything, mark your onboarding as complete. You can still make changes later if needed.
          </p>
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
        <div className="card bg-gradient-to-br from-blue-50 to-blue-100 border-blue-200">
          <h2 className="text-xl font-bold text-blue-900 mb-2">✅ Onboarding Complete</h2>
          <p className="text-sm text-blue-800">
            You've completed the onboarding process. You can still update your information at any time.
          </p>
        </div>
      )}

      {toast && <Toast tone={toast.tone} message={toast.message} onClose={() => setToast(null)} />}
      <ConfirmDialog state={confirmState} onConfirm={handleConfirm} onCancel={handleCancel} />
    </div>
  );
}

function CheckItem({ checked, label }) {
  return (
    <div className="flex items-center gap-2">
      <span className={`text-lg ${checked ? 'text-green-600' : 'text-gray-400'}`}>
        {checked ? '✅' : '⬜'}
      </span>
      <span className={checked ? 'text-green-900 font-medium' : 'text-gray-600'}>{label}</span>
    </div>
  );
}

function StatusBadge({ status }) {
  const config = {
    Pending: { className: 'badge bg-yellow-100 text-yellow-800', label: 'Pending Approval' },
    Approved: { className: 'badge bg-green-100 text-green-800', label: 'Approved' },
    Rejected: { className: 'badge bg-red-100 text-red-800', label: 'Rejected' }
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
