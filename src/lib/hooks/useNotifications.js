import { useState, useEffect, useCallback, useRef } from 'react';
import { apiFetch } from '../api';

/**
 * Hook for managing in-app notifications
 * Polls for unread count and provides notification CRUD operations
 * @param {string} leagueId - Current league ID
 * @param {number} pollInterval - Polling interval in milliseconds (default: 30000 = 30s)
 * @returns {object} Notification state and operations
 */
export function useNotifications(leagueId, pollInterval = 30000) {
  const [unreadCount, setUnreadCount] = useState(0);
  const [notifications, setNotifications] = useState([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [isOpen, setIsOpen] = useState(false);

  const pollTimerRef = useRef(null);

  // Fetch unread count
  const fetchUnreadCount = useCallback(async () => {
    if (!leagueId) return;

    try {
      const result = await apiFetch('/api/notifications/unread-count');
      const count = result?.data?.count ?? result?.count ?? 0;
      setUnreadCount(count);
    } catch (err) {
      console.error('Failed to fetch unread count:', err);
      // Don't set error for background polling failures
    }
  }, [leagueId]);

  // Fetch notifications (when dropdown is opened)
  const fetchNotifications = useCallback(async () => {
    if (!leagueId) return;

    setLoading(true);
    setError(null);

    try {
      const result = await apiFetch('/api/notifications?pageSize=20');
      const items = result?.data?.items || result?.items || [];
      setNotifications(items);
    } catch (err) {
      setError(err.message || 'Failed to load notifications');
      setNotifications([]);
    } finally {
      setLoading(false);
    }
  }, [leagueId]);

  // Mark notification as read
  const markAsRead = useCallback(async (notificationId) => {
    if (!notificationId) return;

    try {
      await apiFetch(`/api/notifications/${notificationId}/read`, { method: 'PATCH' });

      // Update local state
      setNotifications(prev =>
        prev.map(n =>
          n.notificationId === notificationId
            ? { ...n, isRead: true, readUtc: new Date().toISOString() }
            : n
        )
      );

      // Decrement unread count
      setUnreadCount(prev => Math.max(0, prev - 1));
    } catch (err) {
      console.error('Failed to mark notification as read:', err);
    }
  }, []);

  // Mark all notifications as read
  const markAllAsRead = useCallback(async () => {
    try {
      await apiFetch('/api/notifications/read-all', { method: 'POST' });

      // Update local state
      setNotifications(prev =>
        prev.map(n => ({ ...n, isRead: true, readUtc: new Date().toISOString() }))
      );

      setUnreadCount(0);
    } catch (err) {
      console.error('Failed to mark all as read:', err);
    }
  }, []);

  // Toggle dropdown
  const toggleDropdown = useCallback(() => {
    setIsOpen(prev => !prev);
  }, []);

  const closeDropdown = useCallback(() => {
    setIsOpen(false);
  }, []);

  // Start polling for unread count
  useEffect(() => {
    if (!leagueId) {
      // Clear state when no league selected
      setUnreadCount(0);
      setNotifications([]);
      return;
    }

    // Initial fetch
    fetchUnreadCount();

    // Set up polling
    pollTimerRef.current = setInterval(() => {
      fetchUnreadCount();
    }, pollInterval);

    // Cleanup
    return () => {
      if (pollTimerRef.current) {
        clearInterval(pollTimerRef.current);
        pollTimerRef.current = null;
      }
    };
  }, [leagueId, pollInterval, fetchUnreadCount]);

  // Fetch notifications when dropdown opens
  useEffect(() => {
    if (isOpen && notifications.length === 0) {
      fetchNotifications();
    }
  }, [isOpen, notifications.length, fetchNotifications]);

  return {
    unreadCount,
    notifications,
    loading,
    error,
    isOpen,
    toggleDropdown,
    closeDropdown,
    fetchNotifications,
    markAsRead,
    markAllAsRead,
    refreshCount: fetchUnreadCount,
  };
}
