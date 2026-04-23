import { useState, useCallback, useEffect } from "react";
import { apiFetch } from "../../lib/api";
import { logError } from "../../lib/errorLogger";
import StatusCard from "../../components/StatusCard";
import Toast from "../../components/Toast";

export default function UmpireAssignmentsSection({ leagueId }) {
  const [unassignedGames, setUnassignedGames] = useState([]);
  const [umpires, setUmpires] = useState([]);
  const [loading, setLoading] = useState(true);
  const [toast, setToast] = useState(null);
  const [dateFilter, setDateFilter] = useState('');

  useEffect(() => {
    loadData();
  }, [leagueId, dateFilter]);

  const loadData = useCallback(async () => {
    if (!leagueId) return;

    setLoading(true);
    try {
      const params = new URLSearchParams();
      if (dateFilter) params.set('dateFrom', dateFilter);

      const [gamesData, umpiresData] = await Promise.all([
        apiFetch(`/api/umpires/unassigned-games?${params}`),
        apiFetch('/api/umpires?active=true')
      ]);

      setUnassignedGames(gamesData?.games || []);
      setUmpires(umpiresData || []);
    } catch (err) {
      logError("Failed to load unassigned games", err, { leagueId });
      setToast({ message: "Failed to load data", tone: "error" });
    } finally {
      setLoading(false);
    }
  }, [leagueId, dateFilter]);

  async function quickAssign(game, umpireUserId) {
    try {
      await apiFetch(`/api/games/${game.division}/${game.slotId}/umpire-assignments`, {
        method: 'POST',
        body: JSON.stringify({
          umpireUserId,
          sendNotification: true
        })
      });

      setToast({ message: "Umpire assigned successfully", tone: "success" });
      loadData();  // Refresh list
    } catch (err) {
      logError("Failed to assign umpire", err, { game: game.slotId, umpireUserId });
      setToast({ message: err.message || "Failed to assign umpire", tone: "error" });
    }
  }

  if (loading) {
    return <StatusCard title="Loading..." />;
  }

  return (
    <div className="umpire-assignments-section">
      <div className="section-header">
        <h2>
          Unassigned Games
          {unassignedGames.length > 0 && (
            <span className="badge badge-error">{unassignedGames.length}</span>
          )}
        </h2>

        <div className="section-filters">
          <label>
            From Date:
            <input
              type="date"
              value={dateFilter}
              onChange={(e) => setDateFilter(e.target.value)}
            />
          </label>
        </div>
      </div>

      {unassignedGames.length === 0 ? (
        <div className="alert alert-success">
          ✓ All games have umpire assignments
        </div>
      ) : (
        <div className="table-container">
          <table className="data-table">
            <thead>
              <tr>
                <th>Date</th>
                <th>Time</th>
                <th>Division</th>
                <th>Teams</th>
                <th>Field</th>
                <th>Quick Assign</th>
              </tr>
            </thead>
            <tbody>
              {unassignedGames.map(game => (
                <tr key={game.slotId}>
                  <td>{formatDate(game.gameDate)}</td>
                  <td>{game.startTime}</td>
                  <td>{game.division}</td>
                  <td>{game.homeTeamId} vs {game.awayTeamId}</td>
                  <td>{game.fieldDisplayName || game.fieldKey}</td>
                  <td>
                    <UmpireQuickAssignDropdown
                      umpires={umpires}
                      onAssign={(umpireUserId) => quickAssign(game, umpireUserId)}
                    />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {toast && (
        <Toast
          message={toast.message}
          tone={toast.tone}
          onDismiss={() => setToast(null)}
        />
      )}
    </div>
  );
}

function UmpireQuickAssignDropdown({ umpires, onAssign }) {
  const [selectedUmpire, setSelectedUmpire] = useState('');
  const [assigning, setAssigning] = useState(false);

  async function handleAssign() {
    if (!selectedUmpire) return;

    setAssigning(true);
    await onAssign(selectedUmpire);
    setAssigning(false);
    setSelectedUmpire('');
  }

  return (
    <div className="quick-assign">
      <select
        value={selectedUmpire}
        onChange={(e) => setSelectedUmpire(e.target.value)}
        disabled={assigning}
        className="select-sm"
      >
        <option value="">Select umpire...</option>
        {umpires.map(umpire => (
          <option key={umpire.umpireUserId} value={umpire.umpireUserId}>
            {umpire.name} {umpire.certificationLevel && `(${umpire.certificationLevel})`}
          </option>
        ))}
      </select>
      <button
        onClick={handleAssign}
        disabled={!selectedUmpire || assigning}
        className="btn btn-sm btn--primary"
      >
        {assigning ? '...' : 'Assign'}
      </button>
    </div>
  );
}

function formatDate(dateStr) {
  if (!dateStr) return '';
  const date = new Date(dateStr + 'T00:00:00');
  return date.toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric' });
}
