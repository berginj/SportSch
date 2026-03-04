import { useRef, useEffect } from 'react';
import { useNotifications } from '../lib/hooks/useNotifications';
import NotificationDropdown from './NotificationDropdown';

/**
 * NotificationBell - Bell icon with unread count badge and dropdown
 * Integrates into TopNav to show in-app notifications
 */
export default function NotificationBell({ leagueId }) {
  const {
    unreadCount,
    notifications,
    loading,
    error,
    isOpen,
    toggleDropdown,
    closeDropdown,
    markAsRead,
    markAllAsRead,
    refreshCount,
  } = useNotifications(leagueId);

  const bellRef = useRef(null);
  const dropdownRef = useRef(null);

  // Close dropdown when clicking outside
  useEffect(() => {
    if (!isOpen) return;

    function handleClickOutside(event) {
      if (
        bellRef.current &&
        !bellRef.current.contains(event.target) &&
        dropdownRef.current &&
        !dropdownRef.current.contains(event.target)
      ) {
        closeDropdown();
      }
    }

    document.addEventListener('mousedown', handleClickOutside);
    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
    };
  }, [isOpen, closeDropdown]);

  // Don't render if no league selected
  if (!leagueId) {
    return null;
  }

  return (
    <div className="notificationBell">
      <button
        ref={bellRef}
        className="notificationBell__button"
        onClick={toggleDropdown}
        title={`${unreadCount} unread notification${unreadCount !== 1 ? 's' : ''}`}
        aria-label={`Notifications. ${unreadCount} unread.`}
      >
        <span className="notificationBell__label" aria-hidden="true">Alerts</span>

        {/* Unread Badge */}
        {unreadCount > 0 && (
          <span
            className="notificationBell__badge"
            aria-label={`${unreadCount} unread`}
          >
            {unreadCount > 99 ? '99+' : unreadCount}
          </span>
        )}
      </button>

      {/* Dropdown */}
      {isOpen && (
        <div ref={dropdownRef} className="notificationBell__dropdown">
          <NotificationDropdown
            notifications={notifications}
            loading={loading}
            error={error}
            unreadCount={unreadCount}
            onMarkAsRead={markAsRead}
            onMarkAllAsRead={markAllAsRead}
            onClose={closeDropdown}
            onRefresh={refreshCount}
          />
        </div>
      )}
    </div>
  );
}
