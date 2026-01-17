import { useState, useEffect, useCallback } from 'react';
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
      setTimeout(() => setSuccessMessage(''), 3000);
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
          <h2>Notification Settings</h2>
          <p className="muted">Please select a league to manage notification settings.</p>
        </div>
      </div>
    );
  }

  if (loading) {
    return (
      <div className="page">
        <div className="card">
          <h2>Notification Settings</h2>
          <p className="muted">Loading preferences...</p>
        </div>
      </div>
    );
  }

  if (!preferences) {
    return (
      <div className="page">
        <div className="card">
          <h2>Notification Settings</h2>
          {error && <div className="error">{error}</div>}
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
        <h2>Notification Settings</h2>
        <p className="muted">
          Configure how you want to be notified about game swaps and schedule changes for this league.
        </p>

        {error && <div className="error mb-3">{error}</div>}
        {successMessage && (
          <div className="callout callout--success mb-3">
            {successMessage}
          </div>
        )}

        {/* Master Toggles */}
        <div className="mb-6">
          <h3 className="text-lg font-bold mb-3">Master Settings</h3>

          <label className="flex items-center gap-3 mb-3 cursor-pointer">
            <input
              type="checkbox"
              checked={preferences.enableInAppNotifications}
              onChange={() => handleToggle('enableInAppNotifications')}
              className="w-5 h-5"
            />
            <div>
              <div className="font-semibold">In-App Notifications</div>
              <div className="text-sm text-gray-600">
                Show notifications in the bell icon at the top of the page
              </div>
            </div>
          </label>

          <label className="flex items-center gap-3 mb-3 cursor-pointer">
            <input
              type="checkbox"
              checked={preferences.enableEmailNotifications}
              onChange={() => handleToggle('enableEmailNotifications')}
              className="w-5 h-5"
            />
            <div>
              <div className="font-semibold">Email Notifications</div>
              <div className="text-sm text-gray-600">
                Send notifications to {preferences.email || 'your email address'}
              </div>
            </div>
          </label>
        </div>

        {/* Email Notification Types */}
        <div className="mb-6">
          <h3 className="text-lg font-bold mb-3">Email Notification Types</h3>
          <p className="text-sm text-gray-600 mb-4">
            Choose which events trigger email notifications
            {!preferences.enableEmailNotifications && ' (Email notifications are currently disabled)'}
          </p>

          <div className="pl-4 border-l-2 border-gray-200">
            <label className="flex items-center gap-3 mb-3 cursor-pointer">
              <input
                type="checkbox"
                checked={preferences.emailOnSlotCreated}
                onChange={() => handleToggle('emailOnSlotCreated')}
                disabled={!preferences.enableEmailNotifications}
                className="w-4 h-4"
              />
              <div>
                <div className="font-semibold">New Game Slots</div>
                <div className="text-sm text-gray-600">
                  When a new game slot becomes available in your division
                </div>
              </div>
            </label>

            <label className="flex items-center gap-3 mb-3 cursor-pointer">
              <input
                type="checkbox"
                checked={preferences.emailOnSlotCancelled}
                onChange={() => handleToggle('emailOnSlotCancelled')}
                disabled={!preferences.enableEmailNotifications}
                className="w-4 h-4"
              />
              <div>
                <div className="font-semibold">Cancelled Slots</div>
                <div className="text-sm text-gray-600">
                  When a game slot is cancelled
                </div>
              </div>
            </label>

            <label className="flex items-center gap-3 mb-3 cursor-pointer">
              <input
                type="checkbox"
                checked={preferences.emailOnRequestReceived}
                onChange={() => handleToggle('emailOnRequestReceived')}
                disabled={!preferences.enableEmailNotifications}
                className="w-4 h-4"
              />
              <div>
                <div className="font-semibold">Swap Requests Received</div>
                <div className="text-sm text-gray-600">
                  When another coach requests to take your game slot
                </div>
              </div>
            </label>

            <label className="flex items-center gap-3 mb-3 cursor-pointer">
              <input
                type="checkbox"
                checked={preferences.emailOnRequestApproved}
                onChange={() => handleToggle('emailOnRequestApproved')}
                disabled={!preferences.enableEmailNotifications}
                className="w-4 h-4"
              />
              <div>
                <div className="font-semibold">Requests Approved</div>
                <div className="text-sm text-gray-600">
                  When your swap request is approved
                </div>
              </div>
            </label>

            <label className="flex items-center gap-3 mb-3 cursor-pointer">
              <input
                type="checkbox"
                checked={preferences.emailOnRequestDenied}
                onChange={() => handleToggle('emailOnRequestDenied')}
                disabled={!preferences.enableEmailNotifications}
                className="w-4 h-4"
              />
              <div>
                <div className="font-semibold">Requests Denied</div>
                <div className="text-sm text-gray-600">
                  When your swap request is not approved
                </div>
              </div>
            </label>

            <label className="flex items-center gap-3 mb-3 cursor-pointer">
              <input
                type="checkbox"
                checked={preferences.emailOnGameReminder}
                onChange={() => handleToggle('emailOnGameReminder')}
                disabled={!preferences.enableEmailNotifications}
                className="w-4 h-4"
              />
              <div>
                <div className="font-semibold">Game Reminders</div>
                <div className="text-sm text-gray-600">
                  Reminders about upcoming games
                </div>
              </div>
            </label>
          </div>
        </div>

        {/* Save Button */}
        <div className="flex gap-3">
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

        <div className="mt-6 p-4 bg-gray-50 rounded">
          <h4 className="font-semibold mb-2">About Notifications</h4>
          <ul className="text-sm text-gray-600 space-y-1">
            <li>• In-app notifications appear in the bell icon at the top of the page</li>
            <li>• Email notifications are sent to your registered email address</li>
            <li>• These preferences are specific to this league</li>
            <li>• Changes take effect immediately</li>
          </ul>
        </div>
      </div>
    </div>
  );
}
