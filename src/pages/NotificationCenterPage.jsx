import { useState, useEffect, useCallback, useMemo } from "react";
import { apiFetch } from "../lib/api";

function getNotificationTypeBadgeClass(type) {
  const value = String(type || "").trim();
  if (value === "RequestApproved" || value === "RequestDenied") return "softballBadge softballBadge--request";
  if (value === "GameReminder") return "softballBadge softballBadge--game";
  if (value === "SlotCreated" || value === "RequestReceived") return "softballBadge softballBadge--offer";
  return "softballBadge softballBadge--neutral";
}

function formatNotificationType(type) {
  const value = String(type || "").trim();
  if (value === "SlotCreated") return "Open Game Posted";
  if (value === "SlotCancelled") return "Game Cancelled";
  if (value === "RequestReceived") return "Open Game Accepted";
  if (value === "RequestApproved") return "Game Confirmed";
  if (value === "RequestDenied") return "Acceptance Conflict";
  return String(type || "Notification").replace(/([A-Z])/g, " $1").trim();
}

export default function NotificationCenterPage({ leagueId }) {
  const [notifications, setNotifications] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [filterType, setFilterType] = useState("all");
  const [filterStatus, setFilterStatus] = useState("all");
  const [continuationToken, setContinuationToken] = useState(null);
  const [hasMore, setHasMore] = useState(false);

  const loadNotifications = useCallback(async (append = false, token = null) => {
    if (!leagueId) {
      setLoading(false);
      return;
    }

    setLoading(true);
    setError("");

    try {
      const requestToken = append ? token : null;
      const url = `/api/notifications?pageSize=50${requestToken ? `&continuationToken=${requestToken}` : ""}`;
      const result = await apiFetch(url);

      const data = result?.data || result;
      const newNotifications = data?.items || data || [];
      const nextToken = data?.continuationToken || null;

      setNotifications((prev) => (append ? [...prev, ...newNotifications] : newNotifications));
      setContinuationToken(nextToken);
      setHasMore(!!nextToken);
    } catch (err) {
      setError(err.message || "Failed to load notifications");
    } finally {
      setLoading(false);
    }
  }, [leagueId]);

  useEffect(() => {
    loadNotifications(false);
  }, [loadNotifications]);

  const handleMarkAsRead = async (notificationId) => {
    try {
      await apiFetch(`/api/notifications/${notificationId}/read`, { method: "PATCH" });

      setNotifications((prev) =>
        prev.map((notification) =>
          notification.notificationId === notificationId
            ? { ...notification, isRead: true, readUtc: new Date().toISOString() }
            : notification
        )
      );
    } catch (err) {
      console.error("Failed to mark notification as read:", err);
    }
  };

  const handleMarkAllAsRead = async () => {
    try {
      await apiFetch("/api/notifications/read-all", { method: "POST" });

      setNotifications((prev) =>
        prev.map((notification) => ({ ...notification, isRead: true, readUtc: new Date().toISOString() }))
      );
    } catch (err) {
      setError(err.message || "Failed to mark all as read");
    }
  };

  const handleLoadMore = () => {
    if (!loading && hasMore) {
      loadNotifications(true, continuationToken);
    }
  };

  const handleNotificationClick = (notification) => {
    if (!notification.isRead) {
      handleMarkAsRead(notification.notificationId);
    }

    if (notification.link) {
      const nextHash = String(notification.link).startsWith("#")
        ? notification.link
        : `#${notification.link}`;
      window.location.hash = nextHash;
    }
  };

  const handleNotificationKeyDown = (event, notification) => {
    if (event.key !== "Enter" && event.key !== " ") return;
    event.preventDefault();
    handleNotificationClick(notification);
  };

  const filteredNotifications = useMemo(() => notifications.filter((notification) => {
    if (filterType !== "all" && notification.type !== filterType) {
      return false;
    }

    if (filterStatus === "unread" && notification.isRead) {
      return false;
    }
    if (filterStatus === "read" && !notification.isRead) {
      return false;
    }

    return true;
  }), [notifications, filterType, filterStatus]);

  const unreadCount = useMemo(
    () => notifications.filter((notification) => !notification.isRead).length,
    [notifications]
  );

  const notificationSummary = useMemo(() => ({
    total: notifications.length,
    visible: filteredNotifications.length,
    unread: unreadCount,
  }), [notifications.length, filteredNotifications.length, unreadCount]);

  function formatDateTime(utcString) {
    if (!utcString) return "";

    const date = new Date(utcString);
    return date.toLocaleString("en-US", {
      month: "short",
      day: "numeric",
      year: "numeric",
      hour: "numeric",
      minute: "2-digit",
    });
  }

  function formatTimeAgo(utcString) {
    if (!utcString) return "";

    const date = new Date(utcString);
    const now = new Date();
    const diffMs = now - date;
    const diffMins = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMins / 60);
    const diffDays = Math.floor(diffHours / 24);

    if (diffMins < 1) return "Just now";
    if (diffMins < 60) return `${diffMins}m ago`;
    if (diffHours < 24) return `${diffHours}h ago`;
    if (diffDays < 7) return `${diffDays}d ago`;

    return formatDateTime(utcString);
  }

  if (!leagueId) {
    return (
      <div className="page">
        <div className="card">
          <div className="card__header">
            <h2>Notification Center</h2>
            <div className="subtle">Choose a league to view in-app notifications.</div>
          </div>
          <p className="muted">Please select a league to view notifications.</p>
        </div>
      </div>
    );
  }

  return (
    <div className="page">
      <div className="card">
        <div className="card__header">
          <div className="h2">Notification Center</div>
          <div className="subtle">Full notification history with type and read-state filtering.</div>
          {unreadCount > 0 ? (
            <button className="btn btn--sm" onClick={handleMarkAllAsRead}>
              Mark all as read ({unreadCount})
            </button>
          ) : null}
        </div>

        <div className="layoutStatRow mb-3">
          <div className="layoutStat">
            <div className="layoutStat__value">{notificationSummary.total}</div>
            <div className="layoutStat__label">Loaded</div>
          </div>
          <div className="layoutStat">
            <div className="layoutStat__value">{notificationSummary.visible}</div>
            <div className="layoutStat__label">Visible</div>
          </div>
          <div className="layoutStat">
            <div className="layoutStat__value">{notificationSummary.unread}</div>
            <div className="layoutStat__label">Unread</div>
          </div>
        </div>

        <div className="controlBand">
          <div className="formGrid">
            <label className="min-w-[180px]">
              Type
              <select value={filterType} onChange={(e) => setFilterType(e.target.value)}>
                <option value="all">All Types</option>
                <option value="SlotCreated">Open Game Posted</option>
                <option value="SlotCancelled">Game Cancelled</option>
                <option value="RequestReceived">Open Game Accepted</option>
                <option value="RequestApproved">Game Confirmed</option>
                <option value="RequestDenied">Acceptance Conflict</option>
                <option value="GameReminder">Game Reminder</option>
              </select>
            </label>

            <label className="min-w-[180px]">
              Status
              <select value={filterStatus} onChange={(e) => setFilterStatus(e.target.value)}>
                <option value="all">All</option>
                <option value="unread">Unread Only</option>
                <option value="read">Read Only</option>
              </select>
            </label>

            <div className="row row--end">
              <button
                className="btn btn--ghost"
                onClick={() => loadNotifications(false)}
                disabled={loading}
                title="Refresh notifications"
              >
                Refresh
              </button>
            </div>
          </div>
        </div>

        {error ? <div className="callout callout--error mt-3">{error}</div> : null}

        {loading && notifications.length === 0 ? (
          <div className="empty-state">
            <div className="empty-state__message">Loading notifications...</div>
          </div>
        ) : filteredNotifications.length === 0 ? (
          <div className="empty-state">
            <div className="empty-state__message">
              {notifications.length === 0 ? "No notifications yet." : "No notifications match the selected filters."}
            </div>
          </div>
        ) : (
          <div>
            <div className="notificationCenterList">
              {filteredNotifications.map((notification) => (
                <div
                  key={notification.notificationId}
                  onClick={() => handleNotificationClick(notification)}
                  onKeyDown={(event) => handleNotificationKeyDown(event, notification)}
                  role="button"
                  tabIndex={0}
                  aria-label={`Notification: ${notification.message || formatNotificationType(notification.type)}`}
                  className={`notificationCenterItem ${notification.isRead ? "is-read" : "is-unread"} ${notification.link ? "is-link" : ""}`}
                >
                  <div className="notificationCenterItem__row">
                    {!notification.isRead ? <div className="notificationCenterItem__dot" title="Unread"></div> : null}

                    <div className="notificationCenterItem__content">
                      <div className="notificationCenterItem__meta">
                        <span className={getNotificationTypeBadgeClass(notification.type)}>
                          {formatNotificationType(notification.type)}
                        </span>
                        <span className={`statusBadge ${notification.isRead ? "status-confirmed" : "status-open"}`}>
                          {notification.isRead ? "Read" : "Unread"}
                        </span>
                      </div>

                      <p className="notificationCenterItem__message">
                        {notification.message}
                      </p>

                      <div className="notificationCenterItem__time">
                        {formatTimeAgo(notification.createdUtc)}
                      </div>
                    </div>

                    {notification.link ? (
                      <div className="notificationCenterItem__link">Open</div>
                    ) : null}
                  </div>
                </div>
              ))}
            </div>

            {hasMore ? (
              <div className="row row--end mt-3">
                <button className="btn" onClick={handleLoadMore} disabled={loading}>
                  {loading ? "Loading..." : "Load More"}
                </button>
              </div>
            ) : null}
          </div>
        )}

        <div className="callout callout--info mt-3">
          <p>
            Showing {filteredNotifications.length} of {notifications.length} notifications
            {unreadCount > 0 ? ` (${unreadCount} unread)` : ""}
          </p>
        </div>
      </div>
    </div>
  );
}
