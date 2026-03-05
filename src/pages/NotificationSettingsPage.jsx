import { useState, useEffect, useCallback, useRef } from 'react';
import { apiFetch } from '../lib/api';

/**
 * NotificationSettingsPage - User notification preferences management
 * Allows users to configure which notifications they want to receive
 */
export default function NotificationSettingsPage({ leagueId }) {
  const [preferences, setPreferences] = useState(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [successMessage, setSuccessMessage] = useState('');
  const successTimerRef = useRef(null);

  const loadPreferences = useCallback(async () => {
    if (!leagueId) {
      setLoading(false);
      return;
    }

    setLoading(true);
    setError('');

    try {
      const result = await apiFetch('/api/notifications/preferences');
      const data = result?.data || result;
      setPreferences(data);
    } catch (err) {
      setError(err.message || 'Failed to load notification preferences');
    } finally {
      setLoading(false);
    }
  }, [leagueId]);

  useEffect(() => {
    loadPreferences();
  }, [loadPreferences]);

  useEffect(() => {
    return () => {
      if (successTimerRef.current) {
        clearTimeout(successTimerRef.current);
        successTimerRef.current = null;
      }
    };
  }, []);

  const handleToggle = (field) => {
    setPreferences(prev => ({
      ...prev,
      [field]: !prev[field]
    }));
  };

  const handleSave = async () => {
    if (!preferences) return;

    setSaving(true);
    setError('');
    setSuccessMessage('');

    try {
      await apiFetch('/api/notifications/preferences', {
        method: 'PATCH',
        body: JSON.stringify({
          enableInAppNotifications: preferences.enableInAppNotifications,
          enableEmailNotifications: preferences.enableEmailNotifications,
          emailOnSlotCreated: preferences.emailOnSlotCreated,
          emailOnSlotCancelled: preferences.emailOnSlotCancelled,
          emailOnRequestReceived: preferences.emailOnRequestReceived,
          emailOnRequestApproved: preferences.emailOnRequestApproved,
          emailOnRequestDenied: preferences.emailOnRequestDenied,
          emailOnGameReminder: preferences.emailOnGameReminder,
          enableDailyDigest: preferences.enableDailyDigest,
          digestTime: preferences.digestTime
        })
      });

      setSuccessMessage('Notification preferences saved successfully!');
      if (successTimerRef.current) {
        clearTimeout(successTimerRef.current);
      }
      successTimerRef.current = setTimeout(() => {
        setSuccessMessage('');
        successTimerRef.current = null;
      }, 3000);
    } catch (err) {
      setError(err.message || 'Failed to save preferences');
    } finally {
      setSaving(false);
    }
  };

  if (!leagueId) {
    return (
      <div className="page">
        <div className="card">
          <div className="card__header">
            <div className="h2">Notification Settings</div>
          </div>
          <p className="muted">Please select a league to manage notification settings.</p>
        </div>
      </div>
    );
  }

  if (loading) {
    return (
      <div className="page">
        <div className="card">
          <div className="card__header">
            <div className="h2">Notification Settings</div>
          </div>
          <p className="muted">Loading preferences...</p>
        </div>
      </div>
    );
  }

  if (!preferences) {
    return (
      <div className="page">
        <div className="card">
          <div className="card__header">
            <div className="h2">Notification Settings</div>
          </div>
          {error && <div className="callout callout--error">{error}</div>}
          <button className="btn" onClick={loadPreferences}>
            Retry
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="page">
      <div className="card">
        <div className="card__header">
          <div className="h2">Notification Settings</div>
          <div className="subtle">
            Configure how you want to be notified about game swaps and schedule changes for this league.
          </div>
        </div>

        {error && <div className="callout callout--error mb-3">{error}</div>}
        {successMessage && (
          <div className="callout callout--ok mb-3">
            {successMessage}
          </div>
        )}

        {/* Master Toggles */}
        <div className="mb-6">
          <div className="font-semibold mb-3">Master Settings</div>

          <label className="inlineCheck inlineCheck--compact mb-3 cursor-pointer">
            <input
              type="checkbox"
              checked={preferences.enableInAppNotifications}
              onChange={() => handleToggle('enableInAppNotifications')}
            />
            <div>
              <div className="font-semibold">In-App Notifications</div>
              <div className="subtle">
                Show notifications in the bell icon at the top of the page
              </div>
            </div>
          </label>

          <label className="inlineCheck inlineCheck--compact mb-3 cursor-pointer">
            <input
              type="checkbox"
              checked={preferences.enableEmailNotifications}
              onChange={() => handleToggle('enableEmailNotifications')}
            />
            <div>
              <div className="font-semibold">Email Notifications</div>
              <div className="subtle">
                Send notifications to {preferences.email || 'your email address'}
              </div>
            </div>
          </label>
        </div>

        {/* Email Notification Types */}
        <div className="mb-6">
          <div className="font-semibold mb-3">Email Notification Types</div>
          <p className="subtle mb-4">
            Choose which events trigger email notifications
            {!preferences.enableEmailNotifications && ' (Email notifications are currently disabled)'}
          </p>

          <div className="pl-4 border-l-2 border-border">
            <label className="inlineCheck inlineCheck--compact mb-3 cursor-pointer">
              <input
                type="checkbox"
                checked={preferences.emailOnSlotCreated}
                onChange={() => handleToggle('emailOnSlotCreated')}
                disabled={!preferences.enableEmailNotifications}
              />
              <div>
                <div className="font-semibold">New Game Slots</div>
                <div className="subtle">
                  When a new game slot becomes available in your division
                </div>
              </div>
            </label>

            <label className="inlineCheck inlineCheck--compact mb-3 cursor-pointer">
              <input
                type="checkbox"
                checked={preferences.emailOnSlotCancelled}
                onChange={() => handleToggle('emailOnSlotCancelled')}
                disabled={!preferences.enableEmailNotifications}
              />
              <div>
                <div className="font-semibold">Cancelled Slots</div>
                <div className="subtle">
                  When a game slot is cancelled
                </div>
              </div>
            </label>

            <label className="inlineCheck inlineCheck--compact mb-3 cursor-pointer">
              <input
                type="checkbox"
                checked={preferences.emailOnRequestReceived}
                onChange={() => handleToggle('emailOnRequestReceived')}
                disabled={!preferences.enableEmailNotifications}
              />
              <div>
                <div className="font-semibold">Swap Requests Received</div>
                <div className="subtle">
                  When another coach requests to take your game slot
                </div>
              </div>
            </label>

            <label className="inlineCheck inlineCheck--compact mb-3 cursor-pointer">
              <input
                type="checkbox"
                checked={preferences.emailOnRequestApproved}
                onChange={() => handleToggle('emailOnRequestApproved')}
                disabled={!preferences.enableEmailNotifications}
              />
              <div>
                <div className="font-semibold">Requests Approved</div>
                <div className="subtle">
                  When your swap request is approved
                </div>
              </div>
            </label>

            <label className="inlineCheck inlineCheck--compact mb-3 cursor-pointer">
              <input
                type="checkbox"
                checked={preferences.emailOnRequestDenied}
                onChange={() => handleToggle('emailOnRequestDenied')}
                disabled={!preferences.enableEmailNotifications}
              />
              <div>
                <div className="font-semibold">Requests Denied</div>
                <div className="subtle">
                  When your swap request is not approved
                </div>
              </div>
            </label>

            <label className="inlineCheck inlineCheck--compact mb-3 cursor-pointer">
              <input
                type="checkbox"
                checked={preferences.emailOnGameReminder}
                onChange={() => handleToggle('emailOnGameReminder')}
                disabled={!preferences.enableEmailNotifications}
              />
              <div>
                <div className="font-semibold">Game Reminders</div>
                <div className="subtle">
                  Reminders about upcoming games
                </div>
              </div>
            </label>
          </div>
        </div>

        {/* Save Button */}
        <div className="row gap-3 row--wrap">
          <button
            className="btn btn--primary"
            onClick={handleSave}
            disabled={saving}
          >
            {saving ? 'Saving...' : 'Save Preferences'}
          </button>

          <button
            className="btn"
            onClick={loadPreferences}
            disabled={loading || saving}
          >
            Reset
          </button>
        </div>

        <div className="callout mt-6">
          <div className="font-semibold mb-2">About Notifications</div>
          <ul className="subtle grid gap-1">
            <li>- In-app notifications appear in the bell icon at the top of the page.</li>
            <li>- Email notifications are sent to your registered email address.</li>
            <li>- These preferences are specific to this league.</li>
            <li>- Changes take effect immediately.</li>
          </ul>
        </div>
      </div>
    </div>
  );
}
