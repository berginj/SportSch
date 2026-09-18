import { useEffect, useState } from "react";
import { useLeagueApi } from "../lib/useLeagueApi";
import { useModalFocus } from "../lib/useModalFocus";

export default function PracticeRequestModal(props) {
  return props.isOpen ? <PracticeBookingDialog {...props} /> : null;
}

function PracticeBookingDialog({ onClose, initialData = {}, leagueId, onSuccess }) {
  const apiFetch = useLeagueApi(leagueId);
  const [date, setDate] = useState(initialData.date || "");
  const [context, setContext] = useState(null);
  const [options, setOptions] = useState([]);
  const [selectedKey, setSelectedKey] = useState("");
  const [notes, setNotes] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const modalRef = useModalFocus(true, () => { if (!saving) onClose(); });
  const selected = options.find((slot) => slot.practiceSlotKey === selectedKey);

  useEffect(() => {
    let active = true;
    apiFetch("/api/field-inventory/practice/coach")
      .then((result) => { if (active) { setContext(result); setLoading(false); } })
      .catch((e) => { if (active) { setError(e.message); setLoading(false); } });
    return () => { active = false; };
  }, [apiFetch]);

  useEffect(() => {
    if (!context?.seasonLabel || !date) return;
    let active = true;
    setLoading(true);
    setError("");
    setOptions([]);
    setSelectedKey("");
    const query = new URLSearchParams({ seasonLabel: context.seasonLabel, date });
    apiFetch(`/api/field-inventory/practice/availability/options?${query}`)
      .then((result) => { if (active) setOptions((result?.options || []).filter((slot) => slot.isAvailable)); })
      .catch((e) => { if (active) setError(e.message); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [apiFetch, context?.seasonLabel, date]);

  async function submit(event) {
    event.preventDefault();
    if (!selected || saving || loading) return;
    setSaving(true);
    setError("");
    try {
      const result = await apiFetch("/api/field-inventory/practice/requests", {
        method: "POST",
        body: JSON.stringify({ seasonLabel: context.seasonLabel, practiceSlotKey: selectedKey, notes }),
      });
      const saved = [...(result?.requests || [])].reverse().find((request) => request.practiceSlotKey === selectedKey && ["Pending", "Approved"].includes(request.status));
      if (!saved) throw new Error("The request response could not be verified. Check your practice requests before trying again.");
      await onSuccess({ ...saved, autoApproved: saved.status === "Approved" });
      onClose();
    } catch (e) { setError(e.message || "Practice request failed."); }
    finally { setSaving(false); }
  }

  return <div className="modalOverlay" role="presentation">
    <section ref={modalRef} className="modal" role="dialog" aria-modal="true" aria-labelledby="practice-booking-title" tabIndex={-1}>
      <h2 id="practice-booking-title">Request practice space</h2>
      <p>Choose available inventory for your team. The league policy determines whether commissioner review is required.</p>
      <form onSubmit={submit} className="stack gap-3">
        <label>Date<input type="date" value={date} onChange={(e) => setDate(e.target.value)} required disabled={saving} /></label>
        <label>Available field and time<select value={selectedKey} onChange={(e) => setSelectedKey(e.target.value)} required disabled={loading || saving}>
          <option value="">Select an available practice time</option>
          {options.map((slot) => <option key={slot.practiceSlotKey} value={slot.practiceSlotKey}>{slot.fieldName} · {slot.startTime}–{slot.endTime} · {slot.bookingPolicyLabel || slot.bookingPolicy}</option>)}
        </select></label>
        {loading ? <p role="status">Checking available practice space…</p> : !options.length && !error ? <p>No available practice space for this date. Choose another date or contact your commissioner.</p> : null}
        {selected ? <p>{selected.bookingPolicy === "auto_approve" ? "This time can be confirmed when you submit, if it remains available." : "Your request will reserve this time pending commissioner review."}</p> : null}
        <label>Notes<textarea value={notes} onChange={(e) => setNotes(e.target.value)} maxLength={2000} disabled={saving} /></label>
        {error ? <p role="alert">{error}</p> : null}
        <div className="modal__actions">
          <button type="button" className="btn" onClick={onClose} disabled={saving}>Cancel</button>
          <button type="submit" className="btn btn--primary" disabled={!selected || loading || saving} aria-busy={saving}>{saving ? "Submitting…" : "Request practice"}</button>
        </div>
      </form>
    </section>
  </div>;
}
