import { useState, useEffect, useCallback } from 'react';
import { apiFetch } from '../lib/api';

/**
 * NotificationCenterPage - Full notification history with filtering
 * Displays all notifications with advanced filtering options
 */
export default function NotificationCenterPage({ leagueId }) {
  const [notifications, setNotifications] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [filterType, setFilterType] = useState('all');
  const [filterStatus, setFilterStatus] = useState('all'); // all, read, unread
  const [continuationToken, setContinuationToken] = useState(null);
  const [hasMore, setHasMore] = useState(false);

  const loadNotifications = useCallback(async (append = false, token = null) => {
    if (!leagueId) {
      setLoading(false);
      return;
    }

    setLoading(true);
    setError('');

    try {
      const requestToken = append ? token : null;
      const url = `/api/notifications?pageSize=50${requestToken ? `&continuationToken=${requestToken}` : ''}`;
      const result = await apiFetch(url);

      const data = result?.data || result;
      const newNotifications = data?.items || data || [];
      const nextToken = data?.continuationToken || null;

      setNotifications(prev => append ? [...prev, ...newNotifications] : newNotifications);
      setContinuationToken(nextToken);
      setHasMore(!!nextToken);
    } catch (err) {
      setError(err.message || 'Failed to load notifications');
    } finally {
      setLoading(false);
    }
  }, [leagueId]);

  useEffect(() => {
    loadNotifications(false);
  }, [loadNotifications]);

  const handleMarkAsRead = async (notificationId) => {
    try {
      await apiFetch(`/api/notifications/${notificationId}/read`, { method: 'PATCH' });

      setNotifications(prev =>
        prev.map(n =>
          n.notificationId === notificationId
            ? { ...n, isRead: true, readUtc: new Date().toISOString() }
            : n
        )
      );
    } catch (err) {
      console.error('Failed to mark notification as read:', err);
    }
  };

  const handleMarkAllAsRead = async () => {
    try {
      await apiFetch('/api/notifications/read-all', { method: 'POST' });

      setNotifications(prev =>
        prev.map(n => ({ ...n, isRead: true, readUtc: new Date().toISOString() }))
      );
    } catch (err) {
      setError(err.message || 'Failed to mark all as read');
    }
  };

  const handleLoadMore = () => {
    if (!loading && hasMore) {
      loadNotifications(true, continuationToken);
    }
  };

  const handleNotificationClick = (notification) => {
    // Mark as read if unread
    if (!notification.isRead) {
      handleMarkAsRead(notification.notificationId);
    }

    // Navigate to link if provided
    if (notification.link) {
      window.location.hash = notification.link;
    }
  };

  // Apply filters
  const filteredNotifications = notifications.filter(n => {
    // Filter by type
    if (filterType !== 'all' && n.type !== filterType) {
      return false;
    }

    // Filter by read status
    if (filterStatus === 'unread' && n.isRead) {
      return false;
    }
    if (filterStatus === 'read' && !n.isRead) {
      return false;
    }

    return true;
  });

  const unreadCount = notifications.filter(n => !n.isRead).length;

  function formatDateTime(utcString) {
    if (!utcString) return '';

    const date = new Date(utcString);
    return date.toLocaleString('en-US', {
      month: 'short',
      day: 'numeric',
      year: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
    });
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

    return formatDateTime(utcString);
  }

  if (!leagueId) {
    return (
      <div className="page">
        <div className="card">
          <h2>Notification Center</h2>
          <p className="muted">Please select a league to view notifications.</p>
        </div>
      </div>
    );
  }

  return (
    <div className="page">
      <div className="card">
        <div className="flex items-center justify-between mb-4">
          <h2 className="m-0">Notification Center</h2>
          {unreadCount > 0 && (
            <button
              className="btn btn--sm"
              onClick={handleMarkAllAsRead}
            >
              Mark all as read ({unreadCount})
            </button>
          )}
        </div>

        {/* Filters */}
        <div className="flex gap-3 mb-4 flex-wrap">
          <label className="min-w-[180px]">
            Type
            <select
              value={filterType}
              onChange={(e) => setFilterType(e.target.value)}
              className="mt-1"
            >
              <option value="all">All Types</option>
              <option value="SlotCreated">Slot Created</option>
              <option value="SlotCancelled">Slot Cancelled</option>
              <option value="RequestReceived">Request Received</option>
              <option value="RequestApproved">Request Approved</option>
              <option value="RequestDenied">Request Denied</option>
              <option value="GameReminder">Game Reminder</option>
            </select>
          </label>

          <label className="min-w-[180px]">
            Status
            <select
              value={filterStatus}
              onChange={(e) => setFilterStatus(e.target.value)}
              className="mt-1"
            >
              <option value="all">All</option>
              <option value="unread">Unread Only</option>
              <option value="read">Read Only</option>
            </select>
          </label>

          <div className="flex-1"></div>

          <button
            className="btn btn--ghost"
            onClick={() => loadNotifications(false)}
            disabled={loading}
            title="Refresh notifications"
          >
            ↻ Refresh
          </button>
        </div>

        {error && <div className="error mb-3">{error}</div>}

        {/* Notifications List */}
        {loading && notifications.length === 0 ? (
          <div className="text-center py-8 text-gray-500">
            Loading notifications...
          </div>
        ) : filteredNotifications.length === 0 ? (
          <div className="text-center py-8 text-gray-500">
            {notifications.length === 0 ? 'No notifications yet.' : 'No notifications match the selected filters.'}
          </div>
        ) : (
          <div>
            <div className="space-y-2">
              {filteredNotifications.map((notification) => (
                <div
                  key={notification.notificationId}
                  onClick={() => handleNotificationClick(notification)}
                  className={`card cursor-pointer transition-colors ${
                    notification.isRead ? 'bg-white' : 'bg-blue-50 border-blue-200'
                  } hover:bg-gray-50`}
                  style={{ padding: '16px' }}
                >
                  <div className="flex items-start gap-3">
                    {/* Unread Indicator */}
                    {!notification.isRead && (
                      <div
                        className="flex-shrink-0 w-3 h-3 rounded-full bg-blue-500 mt-1"
                        title="Unread"
                      ></div>
                    )}

                    <div className="flex-1 min-w-0">
                      {/* Type Badge */}
                      <div className="mb-2">
                        <span className="inline-block px-2 py-1 text-xs font-semibold rounded bg-gray-200 text-gray-700">
                          {notification.type?.replace(/([A-Z])/g, ' $1').trim() || 'Notification'}
                        </span>
                      </div>

                      {/* Message */}
                      <p className="mb-2 text-gray-900">
                        {notification.message}
                      </p>

                      {/* Timestamp */}
                      <div className="text-sm text-gray-600">
                        {formatTimeAgo(notification.createdUtc)}
                      </div>
                    </div>

                    {/* Link Indicator */}
                    {notification.link && (
                      <div className="flex-shrink-0 text-blue-500 text-xl">
                        →
                      </div>
                    )}
                  </div>
                </div>
              ))}
            </div>

            {/* Load More Button */}
            {hasMore && (
              <div className="text-center mt-4">
                <button
                  className="btn"
                  onClick={handleLoadMore}
                  disabled={loading}
                >
                  {loading ? 'Loading...' : 'Load More'}
                </button>
              </div>
            )}
          </div>
        )}

        {/* Footer Info */}
        <div className="mt-6 p-4 bg-gray-50 rounded text-sm text-gray-600">
          <p>
            Showing {filteredNotifications.length} of {notifications.length} notifications
            {unreadCount > 0 && ` (${unreadCount} unread)`}
          </p>
        </div>
      </div>
    </div>
  );
}
