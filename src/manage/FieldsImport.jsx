import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import { FIELD_STATUS } from "../lib/constants";
import Toast from "../components/Toast";

// Admin tool: CSV import is the ONLY fields workflow.
// Contract: POST /import/fields with required columns: fieldKey, parkName, fieldName.
// Robust backend should accept multipart/form-data (file upload) and text/csv (raw body).

const SAMPLE_CSV = `fieldKey,parkName,fieldName,displayName,address,city,state,notes,status
gunston/turf,Gunston Park,Turf,Gunston Park > Turf,2701 S Lang St,Arlington,VA,,${FIELD_STATUS.ACTIVE}
tuckahoe/field-2,Tuckahoe Park,Field 2,Tuckahoe Park > Field 2,123 Park Ave,Arlington,VA,,${FIELD_STATUS.ACTIVE}
`;

export default function FieldsImport({ leagueId }) {
  const [fields, setFields] = useState([]);
  const [fieldEdits, setFieldEdits] = useState({});
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
            address: f.address ?? "",
            city: f.city ?? "",
            state: f.state ?? "",
            notes: f.notes ?? "",
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
          address: edits.address ?? "",
          city: edits.city ?? "",
          state: edits.state ?? "",
          notes: edits.notes ?? "",
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
        <div className="font-bold mb-2">Field details (edit)</div>
        <div className="subtle mb-2">
          Address details can be edited after import. Changes are saved per field.
        </div>
        {fields.length === 0 ? (
          <div className="subtle">No fields yet.</div>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Field</th>
                <th>Field Key</th>
                <th>Address</th>
                <th>City</th>
                <th>State</th>
                <th>Status</th>
                <th></th>
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
                  <td>{f.status}</td>
                  <td>
                    <button className="btn btn--ghost" onClick={() => saveField(f)} disabled={!canEdit || savingKey}>
                      {savingKey === f.fieldKey ? "Saving..." : "Save"}
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

