import { useState, useCallback, useEffect } from "react";
import { apiFetch } from "../../lib/api";
import { logError } from "../../lib/errorLogger";
import StatusCard from "../../components/StatusCard";
import Toast from "../../components/Toast";

export default function UmpireRosterSection({ leagueId }) {
  const [umpires, setUmpires] = useState([]);
  const [loading, setLoading] = useState(true);
  const [editingUmpire, setEditingUmpire] = useState(null);
  const [showAddModal, setShowAddModal] = useState(false);
  const [toast, setToast] = useState(null);
  const [activeOnly, setActiveOnly] = useState(true);

  useEffect(() => {
    loadUmpires();
  }, [leagueId, activeOnly]);

  const loadUmpires = useCallback(async () => {
    if (!leagueId) return;

    setLoading(true);
    try {
      const data = await apiFetch(`/api/umpires?active=${activeOnly}`);
      setUmpires(data || []);
    } catch (err) {
      logError("Failed to load umpires", err, { leagueId });
      setToast({ message: "Failed to load umpires", tone: "error" });
    } finally {
      setLoading(false);
    }
  }, [leagueId, activeOnly]);

  async function handleCreate(umpireData) {
    try {
      await apiFetch('/api/umpires', {
        method: 'POST',
        body: JSON.stringify(umpireData)
      });
      setToast({ message: "Umpire created successfully", tone: "success" });
      setShowAddModal(false);
      loadUmpires();
    } catch (err) {
      logError("Failed to create umpire", err, { leagueId });
      setToast({ message: err.message || "Failed to create umpire", tone: "error" });
    }
  }

  async function handleUpdate(umpireUserId, updates) {
    try {
      await apiFetch(`/api/umpires/${umpireUserId}`, {
        method: 'PATCH',
        body: JSON.stringify(updates)
      });
      setToast({ message: "Umpire updated successfully", tone: "success" });
      setEditingUmpire(null);
      loadUmpires();
    } catch (err) {
      logError("Failed to update umpire", err, { umpireUserId });
      setToast({ message: err.message || "Failed to update umpire", tone: "error" });
    }
  }

  async function handleDeactivate(umpire) {
    const reassignGames = confirm(
      `Deactivate ${umpire.name}?\n\nChoose OK to cancel their future game assignments, or Cancel to deactivate without reassigning.`
    );

    try {
      await apiFetch(`/api/umpires/${umpire.umpireUserId}?reassignGames=${reassignGames}`, {
        method: 'DELETE'
      });
      setToast({ message: `${umpire.name} deactivated`, tone: "success" });
      loadUmpires();
    } catch (err) {
      logError("Failed to deactivate umpire", err, { umpireUserId: umpire.umpireUserId });
      setToast({ message: err.message || "Failed to deactivate umpire", tone: "error" });
    }
  }

  if (loading) {
    return <StatusCard title="Loading umpires..." />;
  }

  return (
    <div className="umpire-roster-section">
      <div className="section-header">
        <h2>Umpire Roster</h2>
        <div className="section-actions">
          <label className="checkbox-label">
            <input
              type="checkbox"
              checked={activeOnly}
              onChange={(e) => setActiveOnly(e.target.checked)}
            />
            Active only
          </label>
          <button onClick={() => setShowAddModal(true)} className="btn btn--primary">
            + Add Umpire
          </button>
        </div>
      </div>

      {umpires.length === 0 ? (
        <div className="empty-state">
          <p>No umpires found. Click "+ Add Umpire" to create one.</p>
        </div>
      ) : (
        <div className="table-container">
          <table className="data-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Certification</th>
                <th>Phone</th>
                <th>Email</th>
                <th>Experience</th>
                <th>Status</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {umpires.map(umpire => (
                <tr key={umpire.umpireUserId}>
                  <td><strong>{umpire.name}</strong></td>
                  <td>{umpire.certificationLevel || '—'}</td>
                  <td>{umpire.phone}</td>
                  <td>{umpire.email}</td>
                  <td>{umpire.yearsExperience ? `${umpire.yearsExperience} yrs` : '—'}</td>
                  <td>
                    <span className={`badge ${umpire.isActive ? 'badge-success' : 'badge-secondary'}`}>
                      {umpire.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                  <td>
                    <div className="action-buttons">
                      <button onClick={() => setEditingUmpire(umpire)} className="btn btn-sm">
                        Edit
                      </button>
                      {umpire.isActive && (
                        <button onClick={() => handleDeactivate(umpire)} className="btn btn-sm btn-danger">
                          Deactivate
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {showAddModal && (
        <UmpireFormModal
          onSave={handleCreate}
          onClose={() => setShowAddModal(false)}
        />
      )}

      {editingUmpire && (
        <UmpireFormModal
          umpire={editingUmpire}
          onSave={(data) => handleUpdate(editingUmpire.umpireUserId, data)}
          onClose={() => setEditingUmpire(null)}
        />
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

function UmpireFormModal({ umpire, onSave, onClose }) {
  const isEdit = !!umpire;
  const [formData, setFormData] = useState({
    name: umpire?.name || '',
    email: umpire?.email || '',
    phone: umpire?.phone || '',
    certificationLevel: umpire?.certificationLevel || '',
    yearsExperience: umpire?.yearsExperience || '',
    notes: umpire?.notes || ''
  });
  const [saving, setSaving] = useState(false);

  function handleChange(field, value) {
    setFormData(prev => ({ ...prev, [field]: value }));
  }

  async function handleSubmit(e) {
    e.preventDefault();

    if (!formData.name.trim()) {
      alert('Name is required');
      return;
    }

    if (!formData.email.trim()) {
      alert('Email is required');
      return;
    }

    if (!formData.phone.trim()) {
      alert('Phone is required');
      return;
    }

    setSaving(true);
    await onSave({
      name: formData.name.trim(),
      email: formData.email.trim(),
      phone: formData.phone.trim(),
      certificationLevel: formData.certificationLevel.trim() || null,
      yearsExperience: formData.yearsExperience ? parseInt(formData.yearsExperience) : null,
      notes: formData.notes.trim() || null
    });
    setSaving(false);
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h2>{isEdit ? 'Edit Umpire' : 'Add Umpire'}</h2>
          <button onClick={onClose} className="btn-close">×</button>
        </div>

        <form onSubmit={handleSubmit} className="modal-body">
          <div className="form-group">
            <label>Name *</label>
            <input
              type="text"
              value={formData.name}
              onChange={(e) => handleChange('name', e.target.value)}
              required
              placeholder="John Doe"
            />
          </div>

          <div className="form-group">
            <label>Email *</label>
            <input
              type="email"
              value={formData.email}
              onChange={(e) => handleChange('email', e.target.value)}
              required
              placeholder="john@example.com"
              disabled={isEdit}  // Can't change email after creation
            />
          </div>

          <div className="form-group">
            <label>Phone *</label>
            <input
              type="tel"
              value={formData.phone}
              onChange={(e) => handleChange('phone', e.target.value)}
              required
              placeholder="(555) 123-4567"
            />
          </div>

          <div className="form-row">
            <div className="form-group">
              <label>Certification Level</label>
              <input
                type="text"
                value={formData.certificationLevel}
                onChange={(e) => handleChange('certificationLevel', e.target.value)}
                placeholder="Level 1, Level 2, Certified, etc."
              />
            </div>

            <div className="form-group">
              <label>Years Experience</label>
              <input
                type="number"
                min="0"
                max="50"
                value={formData.yearsExperience}
                onChange={(e) => handleChange('yearsExperience', e.target.value)}
                placeholder="5"
              />
            </div>
          </div>

          <div className="form-group">
            <label>Notes (Admin Only)</label>
            <textarea
              value={formData.notes}
              onChange={(e) => handleChange('notes', e.target.value)}
              placeholder="Internal notes about this umpire..."
              rows={3}
            />
          </div>

          <div className="modal-actions">
            <button type="button" onClick={onClose} className="btn" disabled={saving}>
              Cancel
            </button>
            <button type="submit" className="btn btn--primary" disabled={saving}>
              {saving ? 'Saving...' : (isEdit ? 'Update' : 'Create')}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
