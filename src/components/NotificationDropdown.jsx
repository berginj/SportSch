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
      window.location.assign(notification.link);
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
    <div className="notificationDropdown">
      {/* Header */}
      <div className="notificationDropdown__header">
        <a
          href="#notifications"
          className="notificationDropdown__title"
          onClick={onClose}
          title="View all notifications"
        >
          Notifications
        </a>
        <div className="notificationDropdown__actions">
          {unreadCount > 0 && (
            <button
              type="button"
              onClick={onMarkAllAsRead}
              className="notificationDropdown__textButton"
              title="Mark all as read"
            >
              Mark all read
            </button>
          )}
          <button
            type="button"
            onClick={onRefresh}
            className="notificationDropdown__refreshButton"
            title="Refresh"
            aria-label="Refresh notifications"
          >
            Refresh
          </button>
        </div>
      </div>

      {/* Body */}
      <div className="notificationDropdown__body">
        {loading ? (
          <div className="notificationDropdown__state">
            Loading notifications...
          </div>
        ) : error ? (
          <div className="notificationDropdown__state notificationDropdown__state--error">
            {error}
          </div>
        ) : notifications.length === 0 ? (
          <div className="notificationDropdown__state">
            No notifications yet.
          </div>
        ) : (
          <ul className="notificationDropdown__list">
            {notifications.map((notification) => (
              <li
                key={notification.notificationId}
                className={[
                  'notificationDropdown__item',
                  notification.isRead ? 'is-read' : 'is-unread',
                  notification.link || !notification.isRead ? 'is-link' : '',
                ].filter(Boolean).join(' ')}
              >
                <button
                  type="button"
                  className="notificationDropdown__itemButton"
                  onClick={() => handleNotificationClick(notification)}
                >
                  <div className="notificationDropdown__row">
                    {/* Unread Indicator */}
                    {!notification.isRead && (
                      <div className="notificationDropdown__dot" aria-label="Unread" />
                    )}

                    <div className="notificationDropdown__content">
                      {/* Notification Type Badge */}
                      {notification.type && (
                        <span className="notificationDropdown__type">
                          {notification.type.replace(/([A-Z])/g, ' $1').trim()}
                        </span>
                      )}

                      {/* Message */}
                      <p className="notificationDropdown__message">
                        {notification.message}
                      </p>

                      {/* Time */}
                      <span className="notificationDropdown__time">
                        {formatTimeAgo(notification.createdUtc)}
                      </span>
                    </div>
                  </div>
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>

      {/* Footer with Settings Link */}
      <div className="notificationDropdown__footer">
        <a
          href="#settings"
          className="notificationDropdown__settings"
          onClick={onClose}
        >
          Notification Settings
        </a>
      </div>
    </div>
  );
}
