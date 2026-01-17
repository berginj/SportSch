/**
 * NotificationDropdown - Dropdown showing list of notifications
 * Displayed when NotificationBell is clicked
 */
export default function NotificationDropdown({
  notifications,
  loading,
  error,
  unreadCount,
  onMarkAsRead,
  onMarkAllAsRead,
  onClose,
  onRefresh,
}) {
  function handleNotificationClick(notification) {
    // Mark as read if unread
    if (!notification.isRead) {
      onMarkAsRead(notification.notificationId);
    }

    // Navigate to link if provided
    if (notification.link) {
      window.location.hash = notification.link;
      onClose();
    }
  }

  function formatTimeAgo(utcString) {
    if (!utcString) return '';

    const date = new Date(utcString);
    const now = new Date();
    const diffMs = now - date;
    const diffMins = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMins / 60);
    const diffDays = Math.floor(diffHours / 24);

    if (diffMins < 1) return 'Just now';
    if (diffMins < 60) return `${diffMins}m ago`;
    if (diffHours < 24) return `${diffHours}h ago`;
    if (diffDays < 7) return `${diffDays}d ago`;

    return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
  }

  return (
    <div
      className="notification-dropdown"
      style={{
        position: 'absolute',
        top: '100%',
        right: '0',
        marginTop: '8px',
        backgroundColor: 'white',
        border: '1px solid #e5e7eb',
        borderRadius: '8px',
        boxShadow: '0 10px 15px -3px rgba(0, 0, 0, 0.1), 0 4px 6px -2px rgba(0, 0, 0, 0.05)',
        width: '360px',
        maxHeight: '480px',
        overflowY: 'auto',
        zIndex: 1000,
      }}
    >
      {/* Header */}
      <div
        className="notification-dropdown-header"
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '12px 16px',
          borderBottom: '1px solid #e5e7eb',
          backgroundColor: '#f9fafb',
        }}
      >
        <h3 style={{ margin: 0, fontSize: '16px', fontWeight: '600' }}>
          Notifications
        </h3>
        <div style={{ display: 'flex', gap: '8px' }}>
          {unreadCount > 0 && (
            <button
              onClick={onMarkAllAsRead}
              style={{
                background: 'none',
                border: 'none',
                color: '#3b82f6',
                fontSize: '13px',
                cursor: 'pointer',
                padding: '4px 8px',
              }}
              title="Mark all as read"
            >
              Mark all read
            </button>
          )}
          <button
            onClick={onRefresh}
            style={{
              background: 'none',
              border: 'none',
              fontSize: '16px',
              cursor: 'pointer',
              padding: '4px',
            }}
            title="Refresh"
            aria-label="Refresh notifications"
          >
            ↻
          </button>
        </div>
      </div>

      {/* Body */}
      <div className="notification-dropdown-body">
        {loading ? (
          <div style={{ padding: '24px', textAlign: 'center', color: '#6b7280' }}>
            Loading notifications...
          </div>
        ) : error ? (
          <div style={{ padding: '24px', textAlign: 'center', color: '#ef4444' }}>
            {error}
          </div>
        ) : notifications.length === 0 ? (
          <div style={{ padding: '24px', textAlign: 'center', color: '#6b7280' }}>
            No notifications yet.
          </div>
        ) : (
          <ul style={{ margin: 0, padding: 0, listStyle: 'none' }}>
            {notifications.map((notification) => (
              <li
                key={notification.notificationId}
                onClick={() => handleNotificationClick(notification)}
                style={{
                  padding: '12px 16px',
                  borderBottom: '1px solid #f3f4f6',
                  cursor: notification.link ? 'pointer' : 'default',
                  backgroundColor: notification.isRead ? 'white' : '#eff6ff',
                  transition: 'background-color 0.2s',
                }}
                onMouseEnter={(e) => {
                  if (notification.link) {
                    e.currentTarget.style.backgroundColor = notification.isRead ? '#f9fafb' : '#dbeafe';
                  }
                }}
                onMouseLeave={(e) => {
                  e.currentTarget.style.backgroundColor = notification.isRead ? 'white' : '#eff6ff';
                }}
              >
                <div style={{ display: 'flex', alignItems: 'flex-start', gap: '8px' }}>
                  {/* Unread Indicator */}
                  {!notification.isRead && (
                    <div
                      style={{
                        width: '8px',
                        height: '8px',
                        borderRadius: '50%',
                        backgroundColor: '#3b82f6',
                        marginTop: '6px',
                        flexShrink: 0,
                      }}
                      aria-label="Unread"
                    />
                  )}

                  <div style={{ flex: 1, minWidth: 0 }}>
                    {/* Notification Type Badge */}
                    {notification.type && (
                      <span
                        style={{
                          display: 'inline-block',
                          fontSize: '11px',
                          fontWeight: '600',
                          textTransform: 'uppercase',
                          color: '#6b7280',
                          marginBottom: '4px',
                        }}
                      >
                        {notification.type.replace(/([A-Z])/g, ' $1').trim()}
                      </span>
                    )}

                    {/* Message */}
                    <p
                      style={{
                        margin: '0 0 4px 0',
                        fontSize: '14px',
                        color: '#111827',
                        lineHeight: '1.4',
                      }}
                    >
                      {notification.message}
                    </p>

                    {/* Time */}
                    <span style={{ fontSize: '12px', color: '#9ca3af' }}>
                      {formatTimeAgo(notification.createdUtc)}
                    </span>
                  </div>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>

      {/* Footer with Settings Link */}
      <div
        style={{
          padding: '12px 16px',
          borderTop: '1px solid #e5e7eb',
          textAlign: 'center',
        }}
      >
        <a
          href="#settings"
          style={{
            color: '#3b82f6',
            textDecoration: 'none',
            fontSize: '14px',
            fontWeight: '500',
          }}
          onClick={onClose}
        >
          ⚙️ Notification Settings
        </a>
      </div>
    </div>
  );
}
