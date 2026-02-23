import { useEffect, useState, useCallback } from 'react';
import { apiFetch } from '../lib/api';
import Toast from '../components/Toast';
import { useConfirmDialog } from '../lib/useDialogs';
import { ConfirmDialog } from '../components/Dialogs';

const WEEKDAY_OPTIONS = [
  { key: '', label: 'All days' },
  { key: '0', label: 'Sunday' },
  { key: '1', label: 'Monday' },
  { key: '2', label: 'Tuesday' },
  { key: '3', label: 'Wednesday' },
  { key: '4', label: 'Thursday' },
  { key: '5', label: 'Friday' },
  { key: '6', label: 'Saturday' }
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
  return WEEKDAY_OPTIONS.find((opt) => opt.key === key)?.label || '';
}

/**
 * Practice Requests Manager
 *
 * Allows commissioners to:
 * - View all practice slot requests from coaches
 * - Filter by status (Pending, Approved, Rejected)
 * - Approve or reject requests with optional notes
 * - View request details including team, slot info, and date/time
 */
export default function PracticeRequestsManager({ leagueId }) {
  const [requests, setRequests] = useState([]);
  const [divisions, setDivisions] = useState([]);
  const [portalDivision, setPortalDivision] = useState('');
  const [portalSettings, setPortalSettings] = useState(null);
  const [loading, setLoading] = useState(false);
  const [portalLoading, setPortalLoading] = useState(false);
  const [portalSaving, setPortalSaving] = useState(false);
  const [error, setError] = useState('');
  const [toast, setToast] = useState(null);
  const [statusFilter, setStatusFilter] = useState('Pending');
  const [dayFilter, setDayFilter] = useState('');
  const [processingId, setProcessingId] = useState('');

  const { confirmState, requestConfirm, handleConfirm, handleCancel } = useConfirmDialog();

  const loadRequests = useCallback(async () => {
    if (!leagueId) return;

    setLoading(true);
    setError('');

    try {
      const params = new URLSearchParams();
      if (statusFilter) params.set('status', statusFilter);

      const data = await apiFetch(`/api/practice-requests?${params.toString()}`);
      setRequests(Array.isArray(data) ? data : []);
    } catch (err) {
      setError(err.message || 'Failed to load practice requests');
    } finally {
      setLoading(false);
    }
  }, [leagueId, statusFilter]);

  useEffect(() => {
    loadRequests();
  }, [loadRequests]);

  const loadPortalSettings = useCallback(async (nextDivision = '') => {
    if (!leagueId) return;
    setPortalLoading(true);
    try {
      const [divs, settings] = await Promise.all([
        apiFetch('/api/divisions').catch(() => []),
        apiFetch(`/api/practice-portal/settings${nextDivision ? `?division=${encodeURIComponent(nextDivision)}` : ''}`).catch(() => null)
      ]);
      const divisionList = Array.isArray(divs) ? divs : [];
      setDivisions(divisionList);

      let resolvedDivision = nextDivision;
      if (!resolvedDivision && divisionList.length) {
        resolvedDivision = (divisionList[0]?.code || divisionList[0]?.division || '').trim();
      }
      if (resolvedDivision && resolvedDivision !== nextDivision) {
        try {
          const refreshed = await apiFetch(`/api/practice-portal/settings?division=${encodeURIComponent(resolvedDivision)}`);
          setPortalSettings(refreshed && typeof refreshed === 'object' ? refreshed : null);
        } catch {
          setPortalSettings(settings && typeof settings === 'object' ? settings : null);
        }
      } else {
        setPortalSettings(settings && typeof settings === 'object' ? settings : null);
      }
      setPortalDivision((prev) => (prev === resolvedDivision ? prev : resolvedDivision));
    } catch (err) {
      setError(err.message || 'Failed to load practice portal settings');
    } finally {
      setPortalLoading(false);
    }
  }, [leagueId]);

  useEffect(() => {
    loadPortalSettings(portalDivision);
  }, [loadPortalSettings, portalDivision]);

  async function approveRequest(request) {
    const confirmed = await requestConfirm({
      title: 'Approve Practice Request',
      message: `Approve practice request from ${request.teamId} for ${request.slot?.gameDate} at ${request.slot?.startTime}? This locks the recurring field/day/time pattern where matching availability is open.`,
      confirmLabel: 'Approve'
    });

    if (!confirmed) return;

    setProcessingId(request.requestId);
    setError('');

    try {
      await apiFetch(`/api/practice-requests/${encodeURIComponent(request.requestId)}/approve`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          reason: 'Approved by commissioner'
        })
      });

      setToast({ tone: 'success', message: 'Practice request approved' });
      await loadRequests();
      await loadPortalSettings(portalDivision);
    } catch (err) {
      setError(err.message || 'Failed to approve request');
    } finally {
      setProcessingId('');
    }
  }

  async function rejectRequest(request) {
    const confirmed = await requestConfirm({
      title: 'Reject Practice Request',
      message: `Reject practice request from ${request.teamId} for ${request.slot?.gameDate}? You can provide a reason in the next step.`,
      confirmLabel: 'Reject'
    });

    if (!confirmed) return;

    setProcessingId(request.requestId);
    setError('');

    try {
      await apiFetch(`/api/practice-requests/${encodeURIComponent(request.requestId)}/reject`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          reason: 'Slot no longer available'
        })
      });

      setToast({ tone: 'success', message: 'Practice request rejected' });
      await loadRequests();
      await loadPortalSettings(portalDivision);
    } catch (err) {
      setError(err.message || 'Failed to reject request');
    } finally {
      setProcessingId('');
    }
  }

  async function setOneOffPracticeEnabled(enabled) {
    setPortalSaving(true);
    setError('');
    try {
      const payload = {
        oneOffRequestsEnabled: !!enabled,
        division: portalDivision || ''
      };
      const result = await apiFetch('/api/practice-portal/settings', {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      setPortalSettings(result && typeof result === 'object' ? result : null);
      setToast({
        tone: 'success',
        message: enabled
          ? 'One-off practice self-booking enabled (coverage gate still applies by division).'
          : 'One-off practice self-booking disabled.'
      });
    } catch (err) {
      setError(err.message || 'Failed to update practice portal settings');
    } finally {
      setPortalSaving(false);
    }
  }

  const requestsByStatus = {
    Pending: requests.filter(r => r.status === 'Pending'),
    Approved: requests.filter(r => r.status === 'Approved'),
    Rejected: requests.filter(r => r.status === 'Rejected')
  };

  const pendingCount = requestsByStatus.Pending.length;
  const approvedCount = requestsByStatus.Approved.length;
  const rejectedCount = requestsByStatus.Rejected.length;

  const visibleRequests = requests
    .filter((request) => {
      if (!dayFilter) return true;
      return weekdayKeyFromDate(request?.slot?.gameDate) === dayFilter;
    })
    .sort((a, b) => {
      const pa = Number.isFinite(Number(a?.priority)) ? Number(a.priority) : 99;
      const pb = Number.isFinite(Number(b?.priority)) ? Number(b.priority) : 99;
      if (pa !== pb) return pa - pb;
      const ad = `${a?.slot?.gameDate || ''} ${a?.slot?.startTime || ''}`.trim();
      const bd = `${b?.slot?.gameDate || ''} ${b?.slot?.startTime || ''}`.trim();
      return ad.localeCompare(bd);
    });

  if (loading && requests.length === 0) {
    return (
      <div className="card">
        <h3>Practice Slot Requests</h3>
        <p className="muted">Loading practice requests...</p>
      </div>
    );
  }

  return (
    <div className="card">
      <div className="flex items-center justify-between mb-4">
        <h3>Practice Slot Requests</h3>
        <button
          className="btn btn--sm"
          onClick={loadRequests}
          disabled={loading}
        >
          {loading ? 'Refreshing...' : 'Refresh'}
        </button>
      </div>

      {error && (
        <div className="callout callout--error mb-4">{error}</div>
      )}

      <div className="callout mb-4">
        <div className="row row--wrap gap-3" style={{ alignItems: 'end' }}>
          <label style={{ minWidth: 220 }}>
            Coverage check division
            <select
              value={portalDivision}
              onChange={(e) => setPortalDivision(e.target.value)}
              disabled={portalLoading}
            >
              <option value="">Select division</option>
              {divisions.map((d) => {
                const code = (d?.code || d?.division || '').trim();
                const name = (d?.name || code || '').trim();
                return (
                  <option key={code} value={code}>
                    {name} ({code})
                  </option>
                );
              })}
            </select>
          </label>
          <label className="row row--wrap gap-2" style={{ alignItems: 'center' }}>
            <input
              type="checkbox"
              checked={!!portalSettings?.oneOffRequestsEnabled}
              onChange={(e) => setOneOffPracticeEnabled(e.target.checked)}
              disabled={portalLoading || portalSaving}
            />
            <span>Enable one-off practice self-booking</span>
          </label>
          <button
            type="button"
            className="btn btn--sm"
            onClick={() => loadPortalSettings(portalDivision)}
            disabled={portalLoading || portalSaving}
          >
            {portalLoading ? 'Checking...' : 'Refresh gate status'}
          </button>
        </div>
        <div className="muted mt-2">
          One-off self-booking only unlocks for a division after all teams in that division have an approved recurring practice request.
        </div>
        {portalSettings?.divisionStatus ? (
          <div className="mt-2">
            <div>
              Division coverage: <b>{portalSettings.divisionStatus.teamsWithApprovedRecurringPractice}/{portalSettings.divisionStatus.teamCount}</b>{" "}
              {portalSettings.divisionStatus.allTeamsHaveRecurringPractice ? '(ready)' : '(not ready)'}
            </div>
            {Array.isArray(portalSettings.divisionStatus.missingTeams) && portalSettings.divisionStatus.missingTeams.length > 0 ? (
              <div className="muted mt-1">
                Missing recurring approvals: {portalSettings.divisionStatus.missingTeams
                  .slice(0, 8)
                  .map((t) => t?.name || t?.teamId)
                  .filter(Boolean)
                  .join(', ')}
                {portalSettings.divisionStatus.missingTeams.length > 8 ? ' ...' : ''}
              </div>
            ) : null}
          </div>
        ) : null}
      </div>

      <div className="callout mb-4">
        <div className="row row--wrap gap-3" style={{ alignItems: 'center' }}>
          <label style={{ minWidth: 220 }}>
            Filter by day
            <select value={dayFilter} onChange={(e) => setDayFilter(e.target.value)}>
              {WEEKDAY_OPTIONS.map((opt) => (
                <option key={opt.key || 'all'} value={opt.key}>
                  {opt.label}
                </option>
              ))}
            </select>
          </label>
          <div className="muted">
            Approving a practice request locks the recurring field/day/time pattern where matching availability remains open.
          </div>
        </div>
      </div>

      {/* Status Filter Tabs */}
      <div className="flex gap-2 mb-4 border-b border-gray-200 overflow-x-auto -mx-3 px-3 sm:mx-0 sm:px-0">
        <button
          className={`px-4 py-2 font-medium transition-colors whitespace-nowrap flex-shrink-0 ${
            statusFilter === 'Pending'
              ? 'border-b-2 border-accent text-accent'
              : 'text-gray-600 hover:text-gray-900'
          }`}
          onClick={() => setStatusFilter('Pending')}
        >
          Pending {pendingCount > 0 && <span className="badge badge--sm ml-1">{pendingCount}</span>}
        </button>
        <button
          className={`px-4 py-2 font-medium transition-colors whitespace-nowrap flex-shrink-0 ${
            statusFilter === 'Approved'
              ? 'border-b-2 border-accent text-accent'
              : 'text-gray-600 hover:text-gray-900'
          }`}
          onClick={() => setStatusFilter('Approved')}
        >
          Approved {approvedCount > 0 && <span className="badge badge--sm ml-1">{approvedCount}</span>}
        </button>
        <button
          className={`px-4 py-2 font-medium transition-colors whitespace-nowrap flex-shrink-0 ${
            statusFilter === 'Rejected'
              ? 'border-b-2 border-accent text-accent'
              : 'text-gray-600 hover:text-gray-900'
          }`}
          onClick={() => setStatusFilter('Rejected')}
        >
          Rejected {rejectedCount > 0 && <span className="badge badge--sm ml-1">{rejectedCount}</span>}
        </button>
        <button
          className={`px-4 py-2 font-medium transition-colors whitespace-nowrap flex-shrink-0 ${
            statusFilter === ''
              ? 'border-b-2 border-accent text-accent'
              : 'text-gray-600 hover:text-gray-900'
          }`}
          onClick={() => setStatusFilter('')}
        >
          All {requests.length > 0 && <span className="badge badge--sm ml-1">{requests.length}</span>}
        </button>
      </div>

      {/* Summary Stats */}
      {statusFilter === '' && (
        <div className="grid grid-cols-3 gap-4 mb-6">
          <div className="p-4 bg-yellow-50 rounded-lg border border-yellow-200">
            <div className="text-2xl font-bold text-yellow-900">{pendingCount}</div>
            <div className="text-sm text-yellow-700">Pending Review</div>
          </div>
          <div className="p-4 bg-green-50 rounded-lg border border-green-200">
            <div className="text-2xl font-bold text-green-900">{approvedCount}</div>
            <div className="text-sm text-green-700">Approved</div>
          </div>
          <div className="p-4 bg-red-50 rounded-lg border border-red-200">
            <div className="text-2xl font-bold text-red-900">{rejectedCount}</div>
            <div className="text-sm text-red-700">Rejected</div>
          </div>
        </div>
      )}

      {/* Request List */}
      {visibleRequests.length === 0 ? (
        <div className="text-center py-8 text-gray-600">
          {dayFilter
            ? `No ${statusFilter ? statusFilter.toLowerCase() : ''} requests for the selected day`.trim()
            : (statusFilter ? `No ${statusFilter.toLowerCase()} requests` : 'No practice requests yet')}
        </div>
      ) : (
        <div className="grid gap-4">
          {visibleRequests.map((request) => (
            <div
              key={request.requestId}
              className="p-4 border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
            >
              <div className="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-4">
                {/* Request Details */}
                <div className="flex-1 min-w-0">
                  <div className="flex flex-wrap items-center gap-2 mb-2">
                    <StatusBadge status={request.status} />
                    {request.priority ? <span className="badge badge--sm">P{request.priority}</span> : null}
                    <span className="font-bold text-lg">{request.teamId}</span>
                    <span className="text-sm text-gray-600">{request.division}</span>
                  </div>

                  {request.slot ? (
                    <div className="grid gap-1 text-sm">
                      <div className="flex items-center gap-2">
                        <span className="font-semibold text-gray-700">Day:</span>
                        <span>{weekdayLabelFromDate(request.slot.gameDate) || '-'}</span>
                      </div>
                      <div className="flex items-center gap-2">
                        <span className="font-semibold text-gray-700">Date:</span>
                        <span>{request.slot.gameDate}</span>
                      </div>
                      <div className="flex items-center gap-2">
                        <span className="font-semibold text-gray-700">Time:</span>
                        <span>{request.slot.startTime} - {request.slot.endTime}</span>
                      </div>
                      <div className="flex items-center gap-2">
                        <span className="font-semibold text-gray-700">Location:</span>
                        <span>{request.slot.displayName || request.slot.fieldKey || 'TBD'}</span>
                      </div>
                    </div>
                  ) : (
                    <div className="text-sm text-gray-600 italic">Slot details unavailable</div>
                  )}

                  {request.reason && (
                    <div className="mt-2 text-sm text-gray-600">
                      <span className="font-semibold">Reason:</span> {request.reason}
                    </div>
                  )}

                  {request.openToShareField && (
                    <div className="mt-2 text-sm text-gray-600">
                      <span className="font-semibold">Sharing:</span> Open to share field
                      {request.shareWithTeamId ? ` (proposed team: ${request.shareWithTeamId})` : ''}
                    </div>
                  )}

                  <div className="mt-2 text-xs text-gray-500">
                    Requested: {new Date(request.requestedUtc).toLocaleDateString()} at {new Date(request.requestedUtc).toLocaleTimeString()}
                  </div>

                  {request.reviewedUtc && (
                    <div className="text-xs text-gray-500">
                      Reviewed: {new Date(request.reviewedUtc).toLocaleDateString()} at {new Date(request.reviewedUtc).toLocaleTimeString()}
                      {request.reviewedBy && ` by ${request.reviewedBy}`}
                    </div>
                  )}
                </div>

                {/* Actions */}
                {request.status === 'Pending' && (
                  <div className="flex flex-col gap-2">
                    <button
                      className="btn btn--sm btn--primary"
                      onClick={() => approveRequest(request)}
                      disabled={processingId === request.requestId}
                    >
                      {processingId === request.requestId ? 'Processing...' : 'Approve'}
                    </button>
                    <button
                      className="btn btn--sm"
                      onClick={() => rejectRequest(request)}
                      disabled={processingId === request.requestId}
                    >
                      Reject
                    </button>
                  </div>
                )}
              </div>
            </div>
          ))}
        </div>
      )}

      {toast && <Toast tone={toast.tone} message={toast.message} onClose={() => setToast(null)} />}
      <ConfirmDialog state={confirmState} onConfirm={handleConfirm} onCancel={handleCancel} />
    </div>
  );
}

function StatusBadge({ status }) {
  const config = {
    Pending: { className: 'badge bg-yellow-100 text-yellow-800 border border-yellow-300', label: '⏳ Pending' },
    Approved: { className: 'badge bg-green-100 text-green-800 border border-green-300', label: '✅ Approved' },
    Rejected: { className: 'badge bg-red-100 text-red-800 border border-red-300', label: '❌ Rejected' }
  };

  const { className, label } = config[status] || config.Pending;
  return <span className={className}>{label}</span>;
}
