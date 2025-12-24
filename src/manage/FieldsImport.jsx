import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import { FIELD_STATUS } from "../lib/constants";

// Admin tool: CSV import is the ONLY fields workflow.
// Contract: POST /import/fields with required columns: fieldKey, parkName, fieldName.
// Robust backend should accept multipart/form-data (file upload) and text/csv (raw body).

const SAMPLE_CSV = `fieldKey,parkName,fieldName,displayName,address,notes,status
gunston/turf,Gunston Park,Turf,Gunston Park > Turf,,,${FIELD_STATUS.ACTIVE}
tuckahoe/field-2,Tuckahoe Park,Field 2,Tuckahoe Park > Field 2,,,${FIELD_STATUS.ACTIVE}
`;

export default function FieldsImport({ leagueId }) {
  const [fields, setFields] = useState([]);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState("");
  const [ok, setOk] = useState("");

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

  return (
    <div className="stack">
      {err ? <div className="callout callout--error">{err}</div> : null}
      {ok ? <div className="callout callout--ok">{ok}</div> : null}

      <div className="card">
        <div style={{ fontWeight: 700, marginBottom: 6 }}>Field CSV import</div>
        <div className="subtle" style={{ marginBottom: 10, lineHeight: 1.4 }}>
          Required columns: <code>fieldKey</code>, <code>parkName</code>, <code>fieldName</code>. Optional:{" "}
          <code>displayName</code>, <code>address</code>, <code>notes</code>, <code>status</code> ({FIELD_STATUS.ACTIVE}/
          {FIELD_STATUS.INACTIVE}).
        </div>

        <div className="row" style={{ alignItems: "end", gap: 12 }}>
          <label style={{ flex: 1 }}>
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

        <details style={{ marginTop: 12 }}>
          <summary style={{ cursor: "pointer" }}>Or paste CSV (fallback)</summary>
          <textarea
            value={csvText}
            onChange={(e) => setCsvText(e.target.value)}
            rows={10}
            style={{
              width: "100%",
              marginTop: 10,
              fontFamily:
                "ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, 'Liberation Mono', 'Courier New', monospace",
            }}
            disabled={!leagueId || busy}
          />
          <div className="row" style={{ marginTop: 10 }}>
            <button className="btn btn--ghost" onClick={importCsvText} disabled={!canImport}>
              {busy ? "Importing…" : "Import Pasted CSV"}
            </button>
          </div>
        </details>

        {rowErrors.length ? (
          <div style={{ marginTop: 12 }}>
            <div style={{ fontWeight: 700, marginBottom: 6 }}>Rejected rows ({rowErrors.length})</div>
            <div className="subtle" style={{ marginBottom: 8 }}>
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

        <div className="row" style={{ marginTop: 10 }}>
          <button className="btn btn--ghost" onClick={load} disabled={!leagueId || busy}>
            Reload fields
          </button>
        </div>
      </div>

      <div className="card">
        <div style={{ fontWeight: 700, marginBottom: 6 }}>Current fields ({fields.length})</div>
        {fields.length === 0 ? (
          <div className="subtle">No fields yet.</div>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Display</th>
                <th>Field Key</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {fields.map((f) => (
                <tr key={f.fieldKey}>
                  <td>{f.displayName}</td>
                  <td>
                    <code>{f.fieldKey}</code>
                  </td>
                  <td>{f.status}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
