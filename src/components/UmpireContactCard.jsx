export default function UmpireContactCard({ assignment, umpire, showContact = false, showAdminActions = false, onReassign, onRemove }) {
  const statusConfig = {
    'Assigned': { icon: '⏳', text: 'Pending Confirmation', className: 'warning' },
    'Accepted': { icon: '✓', text: 'Confirmed', className: 'success' },
    'Declined': { icon: '✗', text: 'Declined', className: 'error' },
    'Cancelled': { icon: '🚫', text: 'Cancelled', className: 'secondary' }
  };

  const config = statusConfig[assignment?.status] || { icon: '', text: 'Unknown', className: '' };

  // If no assignment, show empty state
  if (!assignment) {
    return (
      <div className="umpire-contact-card umpire-contact-card--empty">
        <div className="empty-icon">⚠️</div>
        <div className="empty-text">No umpire assigned yet</div>
        <div className="empty-subtext">League admin will assign an official for this game</div>
      </div>
    );
  }

  return (
    <div className="umpire-contact-card">
      <div className="umpire-header">
        <div className="umpire-main">
          {umpire?.photoUrl && (
            <img src={umpire.photoUrl} alt={umpire.name} className="umpire-photo" />
          )}
          <div className="umpire-info">
            <div className="umpire-name">{umpire?.name || 'Loading...'}</div>
            {umpire?.certificationLevel && (
              <div className="umpire-cert">{umpire.certificationLevel}</div>
            )}
          </div>
        </div>

        <span className={`badge badge-${config.className}`}>
          {config.icon} {config.text}
        </span>
      </div>

      {/* Contact Actions (only shown for confirmed assignments) */}
      {showContact && assignment.status === 'Accepted' && umpire && (
        <div className="umpire-contact-actions">
          {umpire.phone && (
            <a href={`tel:${umpire.phone}`} className="btn btn-sm btn-outline">
              📞 {formatPhone(umpire.phone)}
            </a>
          )}
          {umpire.email && (
            <a href={`mailto:${umpire.email}`} className="btn btn-sm btn-outline">
              ✉️ Email Umpire
            </a>
          )}
        </div>
      )}

      {/* Pending state message */}
      {assignment.status === 'Assigned' && (
        <div className="umpire-pending-note">
          <span className="icon">ℹ️</span>
          <span>Waiting for umpire to confirm assignment</span>
        </div>
      )}

      {/* Declined state message */}
      {assignment.status === 'Declined' && (
        <div className="umpire-declined-note">
          <span className="icon">⚠️</span>
          <span>Umpire declined this assignment.</span>
          {assignment.declineReason && (
            <div className="decline-reason">Reason: {assignment.declineReason}</div>
          )}
          <div className="help-text">Contact league admin for reassignment.</div>
        </div>
      )}

      {/* Admin Actions */}
      {showAdminActions && (
        <div className="umpire-admin-actions">
          <button onClick={onReassign} className="btn btn-sm btn-outline">
            🔄 Reassign
          </button>
          <button onClick={onRemove} className="btn btn-sm btn-danger">
            ✗ Remove
          </button>
        </div>
      )}
    </div>
  );
}

function formatPhone(phone) {
  if (!phone) return '';

  // Remove all non-digits
  const digits = phone.replace(/\D/g, '');

  // Format as (XXX) XXX-XXXX for 10-digit US numbers
  if (digits.length === 10) {
    return `(${digits.slice(0, 3)}) ${digits.slice(3, 6)}-${digits.slice(6)}`;
  }

  // Return as-is for other formats
  return phone;
}
