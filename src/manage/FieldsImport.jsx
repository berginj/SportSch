import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import { FIELD_STATUS } from "../lib/constants";
import Toast from "../components/Toast";

// Admin tool: CSV import is the primary fields workflow.
// Contract: POST /import/fields with required columns: fieldKey, parkName, fieldName.
// Single-field add/edit/delete uses /api/fields.

const SAMPLE_CSV = `fieldKey,parkName,fieldName,displayName,address,city,state,notes,status
gunston/turf,Gunston Park,Turf,Gunston Park > Turf,2701 S Lang St,Arlington,VA,,${FIELD_STATUS.ACTIVE}
tuckahoe/field-2,Tuckahoe Park,Field 2,Tuckahoe Park > Field 2,123 Park Ave,Arlington,VA,,${FIELD_STATUS.ACTIVE}
`;

export default function FieldsImport({ leagueId }) {
  const [fields, setFields] = useState([]);
  const [fieldEdits, setFieldEdits] = useState({});
  const [newField, setNewField] = useState({
    fieldKey: "",
    parkName: "",
    fieldName: "",
    displayName: "",
    address: "",
    city: "",
    state: "",
    notes: "",
    status: FIELD_STATUS.ACTIVE,
  });
  const [busy, setBusy] = useState(false);
  const [savingKey, setSavingKey] = useState("");
  const [err, setErr] = useState("");
  const [ok, setOk] = useState("");
  const [toast, setToast] = useState(null);

  // Keep paste-text option as fallback
  const [csvText, setCsvText] = useState(SAMPLE_CSV);

  // File upload state
  const [file, setFile] = useState(null);

  // Optional: show server row errors if provided
  const [rowErrors, setRowErrors] = useState([]);

  async function load() {
    setErr("");
    try {
      const list = await apiFetch("/api/fields?activeOnly=false");
      setFields(Array.isArray(list) ? list : []);
      setFieldEdits((prev) => {
        const next = { ...prev };
        for (const f of list || []) {
          next[f.fieldKey] = {
            parkName: f.parkName ?? "",
            fieldName: f.fieldName ?? "",
            displayName: f.displayName ?? "",
            address: f.address ?? "",
            city: f.city ?? "",
            state: f.state ?? "",
            notes: f.notes ?? "",
            status: f.status ?? FIELD_STATUS.ACTIVE,
          };
        }
        return next;
      });
    } catch (e) {
      setErr(e?.message || "Failed to load fields");
    }
  }

  useEffect(() => {
    if (!leagueId) return;
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId]);

  const canImport = useMemo(() => !!leagueId && !busy, [leagueId, busy]);
  const canEdit = useMemo(() => !!leagueId && !busy, [leagueId, busy]);

  async function importCsvFile() {
    setErr("");
    setOk("");
    setRowErrors([]);

    if (!leagueId) return setErr("Select a league first.");
    if (!file) return setErr("Choose a CSV file to upload.");

    setBusy(true);
    try {
      const fd = new FormData();
      fd.append("file", file);

      // IMPORTANT: do NOT set Content-Type manually for multipart; browser will set boundary.
      const res = await apiFetch("/api/import/fields", {
        method: "POST",
        body: fd,
      });

      const msg = `Imported. Upserted: ${res?.upserted ?? 0}, Rejected: ${res?.rejected ?? 0}, Skipped: ${res?.skipped ?? 0}`;
      setOk(msg);
      setToast({ tone: "success", message: "Fields import complete." });

      if (Array.isArray(res?.errors) && res.errors.length) {
        setRowErrors(res.errors);
      }

      await load();
    } catch (e) {
      setErr(e?.message || "Import failed");
    } finally {
      setBusy(false);
    }
  }

  async function importCsvText() {
    setErr("");
    setOk("");
    setRowErrors([]);

    if (!leagueId) return setErr("Select a league first.");
    if (!csvText.trim()) return setErr("Paste CSV content first.");

    setBusy(true);
    try {
      const res = await apiFetch("/api/import/fields", {
        method: "POST",
        headers: { "Content-Type": "text/csv" },
        body: csvText,
      });

      const msg = `Imported. Upserted: ${res?.upserted ?? 0}, Rejected: ${res?.rejected ?? 0}, Skipped: ${res?.skipped ?? 0}`;
      setOk(msg);
      setToast({ tone: "success", message: "Fields import complete." });

      if (Array.isArray(res?.errors) && res.errors.length) {
        setRowErrors(res.errors);
      }

      await load();
    } catch (e) {
      setErr(e?.message || "Import failed");
    } finally {
      setBusy(false);
    }
  }

  function updateEdit(fieldKey, key, value) {
    setFieldEdits((prev) => ({
      ...prev,
      [fieldKey]: {
        ...prev[fieldKey],
        [key]: value,
      },
    }));
  }

  async function saveField(field) {
    setErr("");
    setOk("");

    if (!leagueId) return setErr("Select a league first.");
    const parts = (field.fieldKey || "").split("/");
    if (parts.length !== 2) return setErr("Invalid fieldKey.");

    const [parkCode, fieldCode] = parts;
    const edits = fieldEdits[field.fieldKey] || {};

    setSavingKey(field.fieldKey);
    try {
      await apiFetch(`/api/fields/${parkCode}/${fieldCode}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          parkName: edits.parkName ?? "",
          fieldName: edits.fieldName ?? "",
          displayName: edits.displayName ?? "",
          address: edits.address ?? "",
          city: edits.city ?? "",
          state: edits.state ?? "",
          notes: edits.notes ?? "",
          status: edits.status ?? FIELD_STATUS.ACTIVE,
        }),
      });

      setOk(`Saved ${field.displayName || field.fieldKey}.`);
      setToast({ tone: "success", message: "Field saved." });
      await load();
    } catch (e) {
      setErr(e?.message || "Save failed");
    } finally {
      setSavingKey("");
    }
  }

  async function createField() {
    setErr("");
    setOk("");
    if (!leagueId) return setErr("Select a league first.");

    const payload = {
      fieldKey: (newField.fieldKey || "").trim(),
      parkName: (newField.parkName || "").trim(),
      fieldName: (newField.fieldName || "").trim(),
      displayName: (newField.displayName || "").trim(),
      address: (newField.address || "").trim(),
      city: (newField.city || "").trim(),
      state: (newField.state || "").trim(),
      notes: (newField.notes || "").trim(),
      status: (newField.status || "").trim(),
    };

    if (!payload.fieldKey || !payload.parkName || !payload.fieldName) {
      setErr("fieldKey, parkName, and fieldName are required.");
      return;
    }

    setBusy(true);
    try {
      await apiFetch("/api/fields", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
      setOk(`Created ${payload.fieldKey}.`);
      setToast({ tone: "success", message: "Field created." });
      setNewField({
        fieldKey: "",
        parkName: "",
        fieldName: "",
        displayName: "",
        address: "",
        city: "",
        state: "",
        notes: "",
        status: FIELD_STATUS.ACTIVE,
      });
      await load();
    } catch (e) {
      setErr(e?.message || "Create failed");
    } finally {
      setBusy(false);
    }
  }

  async function deleteField(field) {
    setErr("");
    setOk("");
    if (!leagueId) return setErr("Select a league first.");

    const parts = (field.fieldKey || "").split("/");
    if (parts.length !== 2) return setErr("Invalid fieldKey.");
    const [parkCode, fieldCode] = parts;

    if (!window.confirm(`Delete field ${field.displayName || field.fieldKey}?`)) return;

    setSavingKey(field.fieldKey);
    try {
      await apiFetch(`/api/fields/${parkCode}/${fieldCode}`, { method: "DELETE" });
      setOk(`Deleted ${field.displayName || field.fieldKey}.`);
      setToast({ tone: "success", message: "Field deleted." });
      await load();
    } catch (e) {
      setErr(e?.message || "Delete failed");
    } finally {
      setSavingKey("");
    }
  }

  return (
    <div className="stack">
      {err ? <div className="callout callout--error">{err}</div> : null}
      {ok ? <div className="callout callout--ok">{ok}</div> : null}
      <Toast
        open={!!toast}
        tone={toast?.tone}
        message={toast?.message}
        onClose={() => setToast(null)}
      />

      <div className="card">
        <div className="font-bold mb-2">Field CSV import</div>
        <div className="subtle mb-3 leading-relaxed">
          Required columns: <code>fieldKey</code>, <code>parkName</code>, <code>fieldName</code>. Optional:{" "}
          <code>displayName</code>, <code>address</code>, <code>city</code>, <code>state</code>, <code>notes</code>,{" "}
          <code>status</code> ({FIELD_STATUS.ACTIVE}/
          {FIELD_STATUS.INACTIVE}).
        </div>

        <div className="row items-end gap-3">
          <label className="flex-1">
            CSV file
            <input
              type="file"
              accept=".csv,text/csv"
              onChange={(e) => setFile(e.target.files?.[0] || null)}
              disabled={!leagueId || busy}
            />
          </label>

          <button className="btn" onClick={importCsvFile} disabled={!canImport || !file}>
            {busy ? "Importing…" : "Upload & Import"}
          </button>
        </div>

        <details className="mt-3">
          <summary className="cursor-pointer">Or paste CSV (fallback)</summary>
          <textarea
            value={csvText}
            onChange={(e) => setCsvText(e.target.value)}
            rows={10}
            className="textareaMono"
            disabled={!leagueId || busy}
          />
          <div className="row mt-3">
            <button className="btn btn--ghost" onClick={importCsvText} disabled={!canImport}>
              {busy ? "Importing…" : "Import Pasted CSV"}
            </button>
          </div>
        </details>

        {rowErrors.length ? (
          <div className="mt-3">
            <div className="font-bold mb-2">Rejected rows ({rowErrors.length})</div>
            <div className="subtle mb-2">
              These are row numbers from the CSV (including the header row).
            </div>
            <table className="table">
              <thead>
                <tr>
                  <th>Row</th>
                  <th>Field Key</th>
                  <th>Error</th>
                </tr>
              </thead>
              <tbody>
                {rowErrors.slice(0, 50).map((x, idx) => (
                  <tr key={idx}>
                    <td>{x.row}</td>
                    <td>
                      <code>{x.fieldKey || ""}</code>
                    </td>
                    <td>{x.error}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {rowErrors.length > 50 ? <div className="subtle">Showing first 50.</div> : null}
          </div>
        ) : null}

        <div className="row mt-3">
          <button className="btn btn--ghost" onClick={load} disabled={!leagueId || busy}>
            Reload fields
          </button>
        </div>
      </div>

      <div className="card">
        <div className="font-bold mb-2">Add a single field</div>
        <div className="formGrid">
          <label>
            Field Key
            <input
              value={newField.fieldKey}
              onChange={(e) => setNewField((prev) => ({ ...prev, fieldKey: e.target.value }))}
              placeholder="park-code/field-code"
              disabled={!canEdit}
            />
          </label>
          <label>
            Park Name
            <input
              value={newField.parkName}
              onChange={(e) => setNewField((prev) => ({ ...prev, parkName: e.target.value }))}
              disabled={!canEdit}
            />
          </label>
          <label>
            Field Name
            <input
              value={newField.fieldName}
              onChange={(e) => setNewField((prev) => ({ ...prev, fieldName: e.target.value }))}
              disabled={!canEdit}
            />
          </label>
          <label>
            Display Name
            <input
              value={newField.displayName}
              onChange={(e) => setNewField((prev) => ({ ...prev, displayName: e.target.value }))}
              placeholder="Park > Field"
              disabled={!canEdit}
            />
          </label>
          <label>
            Address
            <input
              value={newField.address}
              onChange={(e) => setNewField((prev) => ({ ...prev, address: e.target.value }))}
              disabled={!canEdit}
            />
          </label>
          <label>
            City
            <input
              value={newField.city}
              onChange={(e) => setNewField((prev) => ({ ...prev, city: e.target.value }))}
              disabled={!canEdit}
            />
          </label>
          <label>
            State
            <input
              value={newField.state}
              onChange={(e) => setNewField((prev) => ({ ...prev, state: e.target.value }))}
              disabled={!canEdit}
              className="fieldInputShort"
            />
          </label>
          <label>
            Status
            <select
              value={newField.status}
              onChange={(e) => setNewField((prev) => ({ ...prev, status: e.target.value }))}
              disabled={!canEdit}
            >
              <option value={FIELD_STATUS.ACTIVE}>{FIELD_STATUS.ACTIVE}</option>
              <option value={FIELD_STATUS.INACTIVE}>{FIELD_STATUS.INACTIVE}</option>
            </select>
          </label>
        </div>
        <label className="mt-3">
          Notes
          <textarea
            value={newField.notes}
            onChange={(e) => setNewField((prev) => ({ ...prev, notes: e.target.value }))}
            disabled={!canEdit}
          />
        </label>
        <div className="row mt-3">
          <button className="btn" onClick={createField} disabled={!canEdit}>
            Add field
          </button>
        </div>
      </div>

      <div className="card">
        <div className="font-bold mb-2">Field details (edit)</div>
        <div className="subtle mb-2">Edit field metadata, then save per field.</div>
        {fields.length === 0 ? (
          <div className="subtle">No fields yet.</div>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Field</th>
                <th>Field Key</th>
                <th>Park Name</th>
                <th>Field Name</th>
                <th>Display Name</th>
                <th>Address</th>
                <th>City</th>
                <th>State</th>
                <th>Status</th>
                <th>Notes</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {fields.map((f) => (
                <tr key={f.fieldKey}>
                  <td>{f.displayName}</td>
                  <td>
                    <code>{f.fieldKey}</code>
                  </td>
                  <td>
                    <input
                      value={fieldEdits[f.fieldKey]?.parkName ?? ""}
                      onChange={(e) => updateEdit(f.fieldKey, "parkName", e.target.value)}
                      disabled={!canEdit || savingKey === f.fieldKey}
                    />
                  </td>
                  <td>
                    <input
                      value={fieldEdits[f.fieldKey]?.fieldName ?? ""}
                      onChange={(e) => updateEdit(f.fieldKey, "fieldName", e.target.value)}
                      disabled={!canEdit || savingKey === f.fieldKey}
                    />
                  </td>
                  <td>
                    <input
                      value={fieldEdits[f.fieldKey]?.displayName ?? ""}
                      onChange={(e) => updateEdit(f.fieldKey, "displayName", e.target.value)}
                      disabled={!canEdit || savingKey === f.fieldKey}
                    />
                  </td>
                  <td>
                    <input
                      value={fieldEdits[f.fieldKey]?.address ?? ""}
                      onChange={(e) => updateEdit(f.fieldKey, "address", e.target.value)}
                      disabled={!canEdit || savingKey === f.fieldKey}
                    />
                  </td>
                  <td>
                    <input
                      value={fieldEdits[f.fieldKey]?.city ?? ""}
                      onChange={(e) => updateEdit(f.fieldKey, "city", e.target.value)}
                      disabled={!canEdit || savingKey === f.fieldKey}
                    />
                  </td>
                  <td>
                    <input
                      value={fieldEdits[f.fieldKey]?.state ?? ""}
                      onChange={(e) => updateEdit(f.fieldKey, "state", e.target.value)}
                      disabled={!canEdit || savingKey === f.fieldKey}
                      className="fieldInputShort"
                    />
                  </td>
                  <td>
                    <select
                      value={fieldEdits[f.fieldKey]?.status ?? FIELD_STATUS.ACTIVE}
                      onChange={(e) => updateEdit(f.fieldKey, "status", e.target.value)}
                      disabled={!canEdit || savingKey === f.fieldKey}
                    >
                      <option value={FIELD_STATUS.ACTIVE}>{FIELD_STATUS.ACTIVE}</option>
                      <option value={FIELD_STATUS.INACTIVE}>{FIELD_STATUS.INACTIVE}</option>
                    </select>
                  </td>
                  <td>
                    <textarea
                      value={fieldEdits[f.fieldKey]?.notes ?? ""}
                      onChange={(e) => updateEdit(f.fieldKey, "notes", e.target.value)}
                      disabled={!canEdit || savingKey === f.fieldKey}
                    />
                  </td>
                  <td className="row gap-2 row--wrap">
                    <button className="btn btn--ghost" onClick={() => saveField(f)} disabled={!canEdit || savingKey}>
                      {savingKey === f.fieldKey ? "Saving..." : "Save"}
                    </button>
                    <button
                      className="btn btn--ghost"
                      onClick={() => deleteField(f)}
                      disabled={!canEdit || savingKey === f.fieldKey}
                    >
                      Delete
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}

