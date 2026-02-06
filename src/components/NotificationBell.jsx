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
    <div className="notification-bell-container" style={{ position: 'relative', display: 'inline-block' }}>
      <button
        ref={bellRef}
        className="notification-bell"
        onClick={toggleDropdown}
        title={`${unreadCount} unread notification${unreadCount !== 1 ? 's' : ''}`}
        aria-label={`Notifications. ${unreadCount} unread.`}
        style={{
          position: 'relative',
          background: 'none',
          border: 'none',
          cursor: 'pointer',
          padding: '8px',
          fontSize: '20px',
          color: 'inherit',
        }}
      >
        {/* Bell Icon (Unicode bell character) */}
        <span aria-hidden="true">Bell</span>

        {/* Unread Badge */}
        {unreadCount > 0 && (
          <span
            className="notification-badge"
            style={{
              position: 'absolute',
              top: '4px',
              right: '4px',
              backgroundColor: '#ef4444',
              color: 'white',
              borderRadius: '50%',
              padding: '2px 6px',
              fontSize: '11px',
              fontWeight: 'bold',
              minWidth: '18px',
              height: '18px',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              lineHeight: '1',
            }}
            aria-label={`${unreadCount} unread`}
          >
            {unreadCount > 99 ? '99+' : unreadCount}
          </span>
        )}
      </button>

      {/* Dropdown */}
      {isOpen && (
        <div ref={dropdownRef}>
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
