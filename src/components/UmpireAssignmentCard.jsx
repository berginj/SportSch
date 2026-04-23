import { useState } from "react";

export default function UmpireAssignmentCard({ assignment, onAccept, onDecline, showActions }) {
  const [showDeclineModal, setShowDeclineModal] = useState(false);

  const statusConfig = {
    'Assigned': { icon: '⏳', text: 'Pending', className: 'warning' },
    'Accepted': { icon: '✓', text: 'Confirmed', className: 'success' },
    'Declined': { icon: '✗', text: 'Declined', className: 'error' },
    'Cancelled': { icon: '🚫', text: 'Cancelled', className: 'secondary' }
  };

  const config = statusConfig[assignment.status] || { icon: '', text: assignment.status, className: '' };

  return (
    <div className={`assignment-card assignment-card--${config.className}`}>
      <div className="assignment-header">
        <div className="assignment-teams">
          <strong>{assignment.homeTeamId} vs {assignment.awayTeamId}</strong>
        </div>
        <span className={`badge badge-${config.className}`}>
          {config.icon} {config.text}
        </span>
      </div>

      <div className="assignment-details">
        <div className="detail-row">
          <span className="icon">📅</span>
          <span className="detail-text">{formatDate(assignment.gameDate)}</span>
        </div>
        <div className="detail-row">
          <span className="icon">🕒</span>
          <span className="detail-text">{assignment.startTime} - {assignment.endTime}</span>
        </div>
        <div className="detail-row">
          <span className="icon">📍</span>
          <span className="detail-text">{assignment.fieldDisplayName || assignment.fieldKey}</span>
        </div>
        {assignment.division && (
          <div className="detail-row">
            <span className="icon">🏆</span>
            <span className="detail-text">{assignment.division} Division</span>
          </div>
        )}
      </div>

      {showActions && assignment.status === 'Assigned' && (
        <div className="assignment-actions">
          <button onClick={onAccept} className="btn btn-success btn-block">
            ✓ Accept Assignment
          </button>
          <button onClick={() => setShowDeclineModal(true)} className="btn btn-secondary btn-block">
            ✗ Decline
          </button>
        </div>
      )}

      {assignment.status === 'Accepted' && (
        <div className="assignment-confirmed-note">
          <span className="icon">✓</span>
          <span>You confirmed this assignment</span>
          {assignment.responseUtc && (
            <span className="timestamp"> on {formatDateTime(assignment.responseUtc)}</span>
          )}
        </div>
      )}

      {assignment.status === 'Declined' && assignment.declineReason && (
        <div className="assignment-decline-note">
          <span className="icon">ℹ️</span>
          <span>You declined: {assignment.declineReason}</span>
        </div>
      )}

      {showDeclineModal && (
        <DeclineAssignmentModal
          assignment={assignment}
          onConfirm={(reason) => {
            onDecline(reason);
            setShowDeclineModal(false);
          }}
          onCancel={() => setShowDeclineModal(false)}
        />
      )}
    </div>
  );
}

function DeclineAssignmentModal({ assignment, onConfirm, onCancel }) {
  const [reason, setReason] = useState('');
  const [submitting, setSubmitting] = useState(false);

  async function handleConfirm() {
    setSubmitting(true);
    await onConfirm(reason.trim());
    setSubmitting(false);
  }

  return (
    <div className="modal-overlay" onClick={onCancel}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h2>Decline Assignment</h2>
          <button onClick={onCancel} className="btn-close">×</button>
        </div>

        <div className="modal-body">
          <p>Are you sure you want to decline this game?</p>

          <div className="game-summary">
            <strong>{assignment.homeTeamId} vs {assignment.awayTeamId}</strong>
            <div>{formatDate(assignment.gameDate)} at {assignment.startTime}</div>
            <div>{assignment.fieldDisplayName || assignment.fieldKey}</div>
          </div>

          <div className="form-group">
            <label>Reason (optional but helpful):</label>
            <textarea
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder="Why are you declining? (Helps admin with reassignment)"
              rows={3}
              className="textarea"
            />
          </div>

          <p className="note">
            The league admin will be notified and can reassign this game to another umpire.
          </p>
        </div>

        <div className="modal-footer">
          <button onClick={onCancel} className="btn" disabled={submitting}>
            Cancel
          </button>
          <button onClick={handleConfirm} className="btn btn-danger" disabled={submitting}>
            {submitting ? 'Declining...' : 'Confirm Decline'}
          </button>
        </div>
      </div>
    </div>
  );
}

function formatDate(dateStr) {
  if (!dateStr) return '';
  const date = new Date(dateStr + 'T00:00:00');
  return date.toLocaleDateString('en-US', {
    weekday: 'long',
    month: 'long',
    day: 'numeric',
    year: 'numeric'
  });
}

function formatDateTime(dateTimeStr) {
  if (!dateTimeStr) return '';
  const date = new Date(dateTimeStr);
  return date.toLocaleString('en-US', {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit'
  });
}
