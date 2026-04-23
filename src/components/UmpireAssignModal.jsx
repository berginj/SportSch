import { useState, useEffect } from "react";
import { apiFetch } from "../lib/api";
import { logError } from "../lib/errorLogger";

export default function UmpireAssignModal({ game, onClose, onAssigned }) {
  const [umpires, setUmpires] = useState([]);
  const [selectedUmpire, setSelectedUmpire] = useState(null);
  const [conflicts, setConflicts] = useState([]);
  const [checking, setChecking] = useState(false);
  const [assigning, setAssigning] = useState(false);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    loadUmpires();
  }, []);

  useEffect(() => {
    if (selectedUmpire) {
      checkConflicts();
    } else {
      setConflicts([]);
    }
  }, [selectedUmpire]);

  async function loadUmpires() {
    try {
      const data = await apiFetch('/api/umpires?active=true');
      setUmpires(data || []);
    } catch (err) {
      logError("Failed to load umpires", err);
    } finally {
      setLoading(false);
    }
  }

  async function checkConflicts() {
    if (!selectedUmpire) return;

    setChecking(true);
    try {
      const result = await apiFetch('/api/umpires/check-conflicts', {
        method: 'POST',
        body: JSON.stringify({
          umpireUserId: selectedUmpire.umpireUserId,
          gameDate: game.gameDate,
          startTime: game.startTime,
          endTime: game.endTime,
          excludeSlotId: game.slotId
        })
      });

      setConflicts(result?.conflicts || []);
    } catch (err) {
      logError("Failed to check conflicts", err, { umpireUserId: selectedUmpire.umpireUserId });
      setConflicts([]);
    } finally {
      setChecking(false);
    }
  }

  async function handleAssign() {
    if (!selectedUmpire || conflicts.length > 0 || checking) return;

    setAssigning(true);
    try {
      await apiFetch(`/api/games/${game.division}/${game.slotId}/umpire-assignments`, {
        method: 'POST',
        body: JSON.stringify({
          umpireUserId: selectedUmpire.umpireUserId,
          sendNotification: true
        })
      });

      onAssigned();
      onClose();
    } catch (err) {
      logError("Failed to assign umpire", err, { game: game.slotId });
      alert(err.message || "Failed to assign umpire");
      setAssigning(false);
    }
  }

  const hasConflict = conflicts.length > 0;

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal modal-lg" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h2>Assign Umpire</h2>
          <button onClick={onClose} className="btn-close">×</button>
        </div>

        <div className="modal-body">
          <div className="game-summary">
            <h3>{game.homeTeamId} vs {game.awayTeamId}</h3>
            <div className="game-details">
              <span>📅 {formatDate(game.gameDate)}</span>
              <span>🕒 {game.startTime} - {game.endTime}</span>
              <span>📍 {game.fieldDisplayName || game.fieldKey}</span>
            </div>
          </div>

          {loading ? (
            <div>Loading umpires...</div>
          ) : (
            <>
              <div className="form-group">
                <label>Select Umpire *</label>
                <select
                  value={selectedUmpire?.umpireUserId || ''}
                  onChange={(e) => {
                    const umpire = umpires.find(u => u.umpireUserId === e.target.value);
                    setSelectedUmpire(umpire);
                  }}
                  className="select-input"
                >
                  <option value="">Choose an umpire...</option>
                  {umpires.map(umpire => (
                    <option key={umpire.umpireUserId} value={umpire.umpireUserId}>
                      {umpire.name}
                      {umpire.certificationLevel && ` (${umpire.certificationLevel})`}
                      {umpire.yearsExperience && ` - ${umpire.yearsExperience} yrs exp`}
                    </option>
                  ))}
                </select>
              </div>

              {checking && (
                <div className="alert alert-info">
                  🔍 Checking for conflicts...
                </div>
              )}

              {!checking && hasConflict && (
                <div className="alert alert-error">
                  <strong>⚠️ Conflict Detected</strong>
                  <p>
                    {selectedUmpire.name} is already assigned to another game:
                  </p>
                  <ul>
                    {conflicts.map((conflict, idx) => (
                      <li key={idx}>
                        {conflict.homeTeam} vs {conflict.awayTeam} —{' '}
                        {conflict.startTime}-{conflict.endTime} at {conflict.field}
                      </li>
                    ))}
                  </ul>
                  <p>
                    Please choose a different umpire or reschedule one of the games.
                  </p>
                </div>
              )}

              {!checking && selectedUmpire && !hasConflict && (
                <div className="alert alert-success">
                  ✓ {selectedUmpire.name} is available for this game
                </div>
              )}
            </>
          )}
        </div>

        <div className="modal-footer">
          <button onClick={onClose} className="btn" disabled={assigning}>
            Cancel
          </button>
          <button
            onClick={handleAssign}
            disabled={!selectedUmpire || hasConflict || checking || assigning || loading}
            className="btn btn--primary"
          >
            {assigning ? 'Assigning...' : 'Assign Umpire'}
          </button>
        </div>
      </div>
    </div>
  );
}

function formatDate(dateStr) {
  if (!dateStr) return '';
  const date = new Date(dateStr + 'T00:00:00');
  return date.toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric' });
}
