import { useEffect, useState } from "react";
import { apiFetch } from "../lib/api";

export default function DivisionsManager({ leagueId }) {
  const [divisions, setDivisions] = useState([]);
  const [err, setErr] = useState("");
  const [ok, setOk] = useState("");
  const [busy, setBusy] = useState(false);

  // create form
  const [name, setName] = useState("");
  const [code, setCode] = useState("");

  async function load() {
    setErr("");
    try {
      const list = await apiFetch("/api/divisions");
      setDivisions(Array.isArray(list) ? list : []);
    } catch (e) {
      setErr(e?.message || "Failed to load divisions");
    }
  }

  useEffect(() => {
    if (!leagueId) return;
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId]);

  async function create() {
    setErr("");
    setOk("");
    if (!name.trim()) return setErr("Name is required");
    setBusy(true);
    try {
      await apiFetch("/api/divisions", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ name: name.trim(), code: code.trim() || undefined, isActive: true }),
      });
      setOk("Division created.");
      setName("");
      setCode("");
      await load();
    } catch (e) {
      setErr(e?.message || "Create failed");
    } finally {
      setBusy(false);
    }
  }

  async function updateDivision(d, patch) {
    setErr("");
    setOk("");
    setBusy(true);
    try {
      await apiFetch(`/api/divisions/${encodeURIComponent(d.code)}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(patch),
      });
      setOk("Division updated.");
      await load();
    } catch (e) {
      setErr(e?.message || "Update failed");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="stack">
      {err ? <div className="callout callout--error">{err}</div> : null}
      {ok ? <div className="callout callout--ok">{ok}</div> : null}

      <div className="card">
        <div className="font-bold mb-3">Create division</div>
        <div className="row">
          <label className="flex-2">
            Name
            <input value={name} onChange={(e) => setName(e.target.value)} placeholder="Ponytail (4th Grade)" />
          </label>
          <label className="flex-1">
            Code (optional)
            <input value={code} onChange={(e) => setCode(e.target.value)} placeholder="pony4" />
          </label>
        </div>
        <div className="row mt-3">
          <button className="btn" onClick={create} disabled={busy || !leagueId}>
            Create
          </button>
          <button className="btn btn--ghost" onClick={load} disabled={!leagueId}>
            Reload
          </button>
        </div>
      </div>

      <div className="card">
        <div className="font-bold mb-3">Divisions ({divisions.length})</div>
        {divisions.length === 0 ? (
          <div className="subtle">No divisions yet.</div>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Code</th>
                <th>Name</th>
                <th>Active</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {divisions.map((d) => (
                <tr key={d.code}>
                  <td>{d.code}</td>
                  <td>{d.name}</td>
                  <td>{String(!!d.isActive)}</td>
                  <td className="text-right">
                    <button
                      className="btn btn--ghost"
                      onClick={() => updateDivision(d, { name: d.name, isActive: !d.isActive })}
                      disabled={busy}
                    >
                      Toggle active
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
