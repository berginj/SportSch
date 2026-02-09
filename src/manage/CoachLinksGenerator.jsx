import { useEffect, useState, useCallback } from 'react';
import { apiFetch } from '../lib/api';
import Toast from '../components/Toast';

/**
 * Coach Onboarding Links Generator
 *
 * Generates personalized onboarding links for all teams in the league.
 * Commissioners can:
 * - View all team onboarding links
 * - Copy individual links
 * - Export all links as CSV for email campaigns
 * - Filter by division or onboarding status
 */
export default function CoachLinksGenerator({ leagueId }) {
  const [teams, setTeams] = useState([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [toast, setToast] = useState(null);
  const [divisionFilter, setDivisionFilter] = useState('');
  const [statusFilter, setStatusFilter] = useState('');

  const loadTeams = useCallback(async () => {
    if (!leagueId) return;

    setLoading(true);
    setError('');

    try {
      const data = await apiFetch('/api/teams');
      setTeams(Array.isArray(data) ? data : []);
    } catch (err) {
      setError(err.message || 'Failed to load teams');
    } finally {
      setLoading(false);
    }
  }, [leagueId]);

  useEffect(() => {
    loadTeams();
  }, [loadTeams]);

  function generateLink(team) {
    const baseUrl = typeof window !== 'undefined' ? window.location.origin : '';
    return `${baseUrl}/?leagueId=${encodeURIComponent(leagueId)}&teamId=${encodeURIComponent(team.teamId)}#coach-setup`;
  }

  function copyLink(team) {
    const link = generateLink(team);
    navigator.clipboard.writeText(link).then(() => {
      setToast({ tone: 'success', message: `Link copied for ${team.name || team.teamId}` });
    }).catch(() => {
      setToast({ tone: 'error', message: 'Failed to copy link' });
    });
  }

  function copyAllLinks() {
    const filtered = getFilteredTeams();
    const links = filtered.map(team => {
      const link = generateLink(team);
      const email = team.primaryContact?.email || '';
      return `${team.name || team.teamId},${team.division},${email},${link}`;
    }).join('\n');

    const csv = `Team Name,Division,Coach Email,Onboarding Link\n${links}`;

    navigator.clipboard.writeText(csv).then(() => {
      setToast({ tone: 'success', message: `Copied ${filtered.length} links as CSV` });
    }).catch(() => {
      setToast({ tone: 'error', message: 'Failed to copy links' });
    });
  }

  function downloadCSV() {
    const filtered = getFilteredTeams();
    const rows = filtered.map(team => {
      const link = generateLink(team);
      const name = (team.name || team.teamId).replace(/"/g, '""');
      const email = (team.primaryContact?.email || '').replace(/"/g, '""');
      return `"${name}","${team.division}","${email}","${link}"`;
    });

    const csv = `Team Name,Division,Coach Email,Onboarding Link\n${rows.join('\n')}`;
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.setAttribute('download', `coach-onboarding-links-${new Date().toISOString().split('T')[0]}.csv`);
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);

    setToast({ tone: 'success', message: `Downloaded ${filtered.length} links as CSV` });
  }

  function getFilteredTeams() {
    return teams.filter(team => {
      if (divisionFilter && team.division !== divisionFilter) return false;
      if (statusFilter === 'complete' && !team.onboardingComplete) return false;
      if (statusFilter === 'incomplete' && team.onboardingComplete) return false;
      return true;
    });
  }

  const divisions = [...new Set(teams.map(t => t.division))].sort();
  const filteredTeams = getFilteredTeams();
  const completeCount = teams.filter(t => t.onboardingComplete).length;
  const incompleteCount = teams.length - completeCount;

  if (loading && teams.length === 0) {
    return (
      <div className="card">
        <h3>Coach Onboarding Links</h3>
        <p className="muted">Loading teams...</p>
      </div>
    );
  }

  return (
    <div className="card">
      <div className="mb-4">
        <h3 className="text-xl font-bold mb-2">Coach Onboarding Links</h3>
        <p className="text-sm text-gray-600">
          Generate and share personalized onboarding links with your coaches. Each link takes coaches to a custom setup page for their team.
        </p>
      </div>

      {error && (
        <div className="callout callout--error mb-4">{error}</div>
      )}

      {/* Summary Stats */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 mb-6">
        <div className="p-4 bg-blue-50 rounded-lg border border-blue-200">
          <div className="text-2xl font-bold text-blue-900">{teams.length}</div>
          <div className="text-sm text-blue-700">Total Teams</div>
        </div>
        <div className="p-4 bg-green-50 rounded-lg border border-green-200">
          <div className="text-2xl font-bold text-green-900">{completeCount}</div>
          <div className="text-sm text-green-700">Onboarding Complete</div>
        </div>
        <div className="p-4 bg-yellow-50 rounded-lg border border-yellow-200">
          <div className="text-2xl font-bold text-yellow-900">{incompleteCount}</div>
          <div className="text-sm text-yellow-700">Pending Setup</div>
        </div>
      </div>

      {/* Filters and Actions */}
      <div className="flex flex-col sm:flex-row sm:flex-wrap items-start sm:items-center gap-4 mb-6 pb-4 border-b border-gray-200">
        <div className="flex flex-col sm:flex-row items-start sm:items-center gap-2 w-full sm:w-auto">
          <label className="text-sm font-medium whitespace-nowrap">Division:</label>
          <select
            value={divisionFilter}
            onChange={(e) => setDivisionFilter(e.target.value)}
            className="text-sm w-full sm:w-auto"
          >
            <option value="">All Divisions</option>
            {divisions.map(div => (
              <option key={div} value={div}>{div}</option>
            ))}
          </select>
        </div>

        <div className="flex flex-col sm:flex-row items-start sm:items-center gap-2 w-full sm:w-auto">
          <label className="text-sm font-medium whitespace-nowrap">Status:</label>
          <select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            className="text-sm w-full sm:w-auto"
          >
            <option value="">All Teams</option>
            <option value="incomplete">Incomplete</option>
            <option value="complete">Complete</option>
          </select>
        </div>

        <div className="flex flex-col sm:flex-row gap-2 w-full sm:w-auto sm:ml-auto">
          <button
            className="btn btn--sm"
            onClick={copyAllLinks}
            disabled={filteredTeams.length === 0}
          >
            📋 Copy All as CSV
          </button>
          <button
            className="btn btn--sm btn--primary"
            onClick={downloadCSV}
            disabled={filteredTeams.length === 0}
          >
            ⬇️ Download CSV
          </button>
        </div>
      </div>

      {/* Team List */}
      {filteredTeams.length === 0 ? (
        <div className="text-center py-8 text-gray-600">
          No teams match the current filters
        </div>
      ) : (
        <div className="space-y-3">
          {filteredTeams.map((team) => {
            const link = generateLink(team);
            const hasContact = team.primaryContact?.email;

            return (
              <div
                key={`${team.division}-${team.teamId}`}
                className="p-4 border border-gray-200 rounded-lg hover:bg-gray-50 transition-colors"
              >
                <div className="flex items-start justify-between gap-4">
                  {/* Team Info */}
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-3 mb-2">
                      <span className="font-bold text-lg">{team.name || team.teamId}</span>
                      <span className="badge badge--sm">{team.division}</span>
                      {team.onboardingComplete && (
                        <span className="badge bg-green-100 text-green-800 border border-green-300">
                          ✅ Complete
                        </span>
                      )}
                    </div>

                    {hasContact ? (
                      <div className="text-sm text-gray-600 mb-2">
                        <div className="font-semibold">{team.primaryContact.name}</div>
                        <div>{team.primaryContact.email}</div>
                        {team.primaryContact.phone && <div>{team.primaryContact.phone}</div>}
                      </div>
                    ) : (
                      <div className="text-sm text-gray-500 italic mb-2">
                        No contact information
                      </div>
                    )}

                    {/* Onboarding Link */}
                    <div className="flex flex-col sm:flex-row items-stretch sm:items-center gap-2 mt-3">
                      <input
                        type="text"
                        value={link}
                        readOnly
                        className="flex-1 min-w-0 text-xs sm:text-sm font-mono bg-gray-50 border border-gray-300 rounded px-2 py-1.5"
                        onClick={(e) => e.target.select()}
                      />
                      <button
                        className="btn btn--sm flex-shrink-0"
                        onClick={() => copyLink(team)}
                        title="Copy link to clipboard"
                      >
                        📋 Copy
                      </button>
                    </div>

                    {/* Assistant Coaches */}
                    {team.assistantCoaches && team.assistantCoaches.length > 0 && (
                      <div className="mt-3 text-xs text-gray-600">
                        <span className="font-semibold">Assistant Coaches:</span>
                        {team.assistantCoaches.map((coach, idx) => (
                          <span key={idx} className="ml-2">
                            {coach.name || coach.email}
                            {idx < team.assistantCoaches.length - 1 ? ',' : ''}
                          </span>
                        ))}
                      </div>
                    )}
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* Usage Instructions */}
      <div className="mt-6 p-4 bg-blue-50 rounded-lg border border-blue-200">
        <div className="font-semibold text-blue-900 mb-2">📧 How to Send Links to Coaches</div>
        <ol className="text-sm text-blue-800 space-y-1 list-decimal list-inside">
          <li>Download the CSV file using the button above</li>
          <li>Open the CSV in Excel or Google Sheets</li>
          <li>Use mail merge to send personalized emails with each coach's unique link</li>
          <li>Or manually copy individual links and send via email/text</li>
        </ol>
        <div className="mt-3 text-xs text-blue-700">
          <strong>Tip:</strong> Include the onboarding link in your welcome email along with league information and important dates.
        </div>
      </div>

      {toast && <Toast tone={toast.tone} message={toast.message} onClose={() => setToast(null)} />}
    </div>
  );
}
