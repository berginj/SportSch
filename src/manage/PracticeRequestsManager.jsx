import { useEffect, useState, useCallback } from 'react';
import { apiFetch } from '../lib/api';
import Toast from '../components/Toast';
import { useConfirmDialog } from '../lib/useDialogs';
import { ConfirmDialog } from '../components/Dialogs';

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
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [toast, setToast] = useState(null);
  const [statusFilter, setStatusFilter] = useState('Pending');
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

  async function approveRequest(request) {
    const confirmed = await requestConfirm({
      title: 'Approve Practice Request',
      message: `Approve practice request from ${request.teamId} for ${request.slot?.gameDate} at ${request.slot?.startTime}?`,
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
    } catch (err) {
      setError(err.message || 'Failed to reject request');
    } finally {
      setProcessingId('');
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
      {requests.length === 0 ? (
        <div className="text-center py-8 text-gray-600">
          {statusFilter ? `No ${statusFilter.toLowerCase()} requests` : 'No practice requests yet'}
        </div>
      ) : (
        <div className="grid gap-4">
          {requests.map((request) => (
            <div
              key={request.requestId}
              className="p-4 border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
            >
              <div className="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-4">
                {/* Request Details */}
                <div className="flex-1 min-w-0">
                  <div className="flex flex-wrap items-center gap-2 mb-2">
                    <StatusBadge status={request.status} />
                    <span className="font-bold text-lg">{request.teamId}</span>
                    <span className="text-sm text-gray-600">{request.division}</span>
                  </div>

                  {request.slot ? (
                    <div className="grid gap-1 text-sm">
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
