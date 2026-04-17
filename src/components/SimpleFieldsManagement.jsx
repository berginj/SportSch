import { useEffect, useState } from "react";
import { apiFetch } from "../lib/api";
import Toast from "./Toast";

/**
 * Simplified field management component.
 * Replaces the complex field inventory import system for most leagues (< 20 fields).
 * CSV import available in accordion for larger leagues.
 */
export default function SimpleFieldsManagement({ leagueId }) {
  const [fields, setFields] = useState([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [toast, setToast] = useState(null);
  const [showCsvImport, setShowCsvImport] = useState(false);

  useEffect(() => {
    loadFields();
  }, [leagueId]);

  const loadFields = async () => {
    setLoading(true);
    try {
      const data = await apiFetch("/api/fields");
      setFields(Array.isArray(data) ? data : []);
    } catch (err) {
      setToast({ message: `Failed to load fields: ${err.message}`, type: "error" });
    } finally {
      setLoading(false);
    }
  };

  const addField = () => {
    setFields([...fields, {
      fieldKey: "",
      fieldName: "",
      parkName: "",
      displayName: "",
      address: "",
      notes: "",
      active: true,
      isNew: true
    }]);
  };

  const removeField = (idx) => {
    setFields(fields.filter((_, i) => i !== idx));
  };

  const updateField = (idx, property, value) => {
    const updated = [...fields];
    updated[idx] = { ...updated[idx], [property]: value };
    setFields(updated);
  };

  const saveFields = async () => {
    setSaving(true);
    try {
      // Save each field
      for (const field of fields) {
        if (field.isNew) {
          // Create new field
          await apiFetch("/api/fields", {
            method: "POST",
            body: JSON.stringify({
              fieldKey: field.fieldKey || field.fieldName.replace(/\s+/g, '_').toUpperCase(),
              fieldName: field.fieldName,
              parkName: field.parkName,
              displayName: field.displayName || field.fieldName,
              address: field.address,
              notes: field.notes,
              active: field.active !== false
            })
          });
        } else {
          // Update existing field
          await apiFetch(`/api/fields/${field.fieldKey}`, {
            method: "PATCH",
            body: JSON.stringify({
              fieldName: field.fieldName,
              parkName: field.parkName,
              displayName: field.displayName,
              address: field.address,
              notes: field.notes,
              active: field.active
            })
          });
        }
      }

      setToast({ message: "Fields saved successfully!", type: "success" });
      await loadFields(); // Reload to get server state
    } catch (err) {
      setToast({ message: `Failed to save fields: ${err.message}`, type: "error" });
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return <div className="card"><div className="subtle">Loading fields...</div></div>;
  }

  return (
    <div className="stack">
      <Toast
        open={!!toast}
        message={toast?.message}
        tone={toast?.type === "error" ? "error" : "success"}
        onClose={() => setToast(null)}
      />

      <div className="card">
        <div className="row row--between mb-3">
          <div>
            <h3 className="font-bold">Fields</h3>
            <p className="text-sm text-gray-600 dark:text-gray-400 mt-1">
              Add the fields/parks where games are played.
            </p>
          </div>
          <button className="btn btn--primary" onClick={addField}>
            + Add Field
          </button>
        </div>

        {fields.length === 0 ? (
          <div className="callout callout--info">
            <p>No fields added yet. Click "Add Field" to get started.</p>
            <p className="text-sm mt-2">
              Most leagues need 3-10 fields. You can add them inline here or use CSV import for 20+ fields.
            </p>
          </div>
        ) : (
          <div className="stack gap-3">
            {fields.map((field, idx) => (
              <div
                key={idx}
                className="border border-gray-200 dark:border-gray-700 rounded-lg p-4"
              >
                <div className="grid2 gap-3">
                  <label>
                    Field Name *
                    <input
                      value={field.fieldName || ""}
                      onChange={(e) => updateField(idx, "fieldName", e.target.value)}
                      placeholder="Field A, North Park Field, etc."
                      required
                    />
                  </label>

                  <label>
                    Park/Location Name
                    <input
                      value={field.parkName || ""}
                      onChange={(e) => updateField(idx, "parkName", e.target.value)}
                      placeholder="City Park, Recreation Center, etc."
                    />
                  </label>

                  <label>
                    Display Name (shown to coaches)
                    <input
                      value={field.displayName || ""}
                      onChange={(e) => updateField(idx, "displayName", e.target.value)}
                      placeholder="Leave blank to use Field Name"
                    />
                  </label>

                  <label>
                    Address
                    <input
                      value={field.address || ""}
                      onChange={(e) => updateField(idx, "address", e.target.value)}
                      placeholder="123 Main St, City, State"
                    />
                  </label>

                  <label className="col-span-2">
                    Notes (optional)
                    <input
                      value={field.notes || ""}
                      onChange={(e) => updateField(idx, "notes", e.target.value)}
                      placeholder="Parking instructions, field conditions, etc."
                    />
                  </label>

                  <div className="flex items-center gap-2">
                    <label className="flex items-center gap-2 cursor-pointer">
                      <input
                        type="checkbox"
                        checked={field.active !== false}
                        onChange={(e) => updateField(idx, "active", e.target.checked)}
                      />
                      <span>Available for scheduling</span>
                    </label>
                  </div>

                  <div className="flex justify-end">
                    <button
                      className="btn btn--ghost text-red-600"
                      onClick={() => removeField(idx)}
                    >
                      Remove Field
                    </button>
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}

        <div className="row gap-3 mt-4">
          <button
            className="btn btn--primary"
            onClick={saveFields}
            disabled={saving}
          >
            {saving ? "Saving..." : "Save All Fields"}
          </button>
          <button className="btn" onClick={loadFields}>
            Cancel Changes
          </button>
        </div>

        {/* CSV Import (Advanced) */}
        <details className="mt-4 border-t border-gray-200 dark:border-gray-700 pt-4">
          <summary className="cursor-pointer text-sm font-medium text-gray-700 dark:text-gray-300">
            Advanced: Bulk Import from CSV (for 20+ fields)
          </summary>
          <div className="mt-3 p-4 bg-gray-50 dark:bg-gray-800 rounded-lg">
            <p className="text-sm text-gray-600 dark:text-gray-400 mb-3">
              Upload a CSV file with columns: fieldKey, fieldName, parkName, address, active
            </p>
            <div className="row gap-3">
              <button
                className="btn btn--secondary"
                onClick={() => {
                  const csvContent = "fieldKey,fieldName,parkName,displayName,address,active\nFIELD_A,Field A,North Park,North Park Field A,123 Main St,true\n";
                  const blob = new Blob([csvContent], { type: "text/csv" });
                  const url = window.URL.createObjectURL(blob);
                  const a = document.createElement("a");
                  a.href = url;
                  a.download = "fields_template.csv";
                  a.click();
                }}
              >
                Download CSV Template
              </button>
              <input
                type="file"
                accept=".csv"
                onChange={async (e) => {
                  const file = e.target.files?.[0];
                  if (!file) return;

                  try {
                    const formData = new FormData();
                    formData.append("file", file);

                    await apiFetch("/api/fields/import", {
                      method: "POST",
                      body: formData
                    });

                    setToast({ message: "CSV imported successfully!", type: "success" });
                    await loadFields();
                  } catch (err) {
                    setToast({ message: `CSV import failed: ${err.message}`, type: "error" });
                  }
                }}
              />
            </div>
          </div>
        </details>
      </div>
    </div>
  );
}
