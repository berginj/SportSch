import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import StatusCard from "../components/StatusCard";
import Toast from "../components/Toast";

const DAY_OPTIONS = [
  { value: "", label: "All days" },
  { value: "Monday", label: "Monday" },
  { value: "Tuesday", label: "Tuesday" },
  { value: "Wednesday", label: "Wednesday" },
  { value: "Thursday", label: "Thursday" },
  { value: "Friday", label: "Friday" },
  { value: "Saturday", label: "Saturday" },
  { value: "Sunday", label: "Sunday" },
];

function filterSlots(slots, search, day, policy, showOnlyOpenSeats) {
  const needle = String(search || "").trim().toLowerCase();
  return (slots || [])
    .filter((slot) => (day ? slot.dayOfWeek === day : true))
    .filter((slot) => (policy ? slot.bookingPolicy === policy : true))
    .filter((slot) => (showOnlyOpenSeats ? slot.remainingCapacity > 0 : true))
    .filter((slot) => {
      if (!needle) return true;
      const haystack = [
        slot.fieldName,
        slot.assignedGroup,
        slot.assignedDivision,
        slot.assignedTeamOrEvent,
        slot.date,
      ]
        .map((value) => String(value || "").toLowerCase())
        .join(" ");
      return haystack.includes(needle);
    })
    .sort((left, right) =>
      `${left.date || ""}|${left.startTime || ""}|${left.fieldName || ""}`.localeCompare(
        `${right.date || ""}|${right.startTime || ""}|${right.fieldName || ""}`
      )
    );
}

function isActiveRequest(request) {
  return request?.status === "Pending" || request?.status === "Approved";
}

function describeRequest(request) {
  if (!request) return "";
  return `${request.date || ""} ${request.startTime || ""}-${request.endTime || ""} ${request.fieldName || ""}`.trim();
}

export default function PracticePortalPage({ me, leagueId }) {
  const [data, setData] = useState(null);
  const [seasonLabel, setSeasonLabel] = useState("");
  const [loading, setLoading] = useState(true);
  const [requestingKey, setRequestingKey] = useState("");
  const [movingRequestId, setMovingRequestId] = useState("");
  const [actingRequestId, setActingRequestId] = useState("");
  const [error, setError] = useState("");
  const [toast, setToast] = useState(null);
  const [search, setSearch] = useState("");
  const [dayFilter, setDayFilter] = useState("");
  const [policyFilter, setPolicyFilter] = useState("");
  const [showOnlyOpenSeats, setShowOnlyOpenSeats] = useState(true);

  const coachName = me?.name || me?.userDetails || "Coach";

  async function load(nextSeasonLabel = seasonLabel) {
    if (!leagueId) return;
    setLoading(true);
    setError("");
    try {
      const query = nextSeasonLabel ? `?seasonLabel=${encodeURIComponent(nextSeasonLabel)}` : "";
      const result = await apiFetch(`/api/field-inventory/practice/coach${query}`);
      setData(result);
      setSeasonLabel(result?.seasonLabel || nextSeasonLabel || "");
    } catch (e) {
      setError(e.message || "Failed to load practice space.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load("");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId]);

  const visibleSlots = useMemo(
    () => filterSlots(data?.slots || [], search, dayFilter, policyFilter, showOnlyOpenSeats),
    [data, search, dayFilter, policyFilter, showOnlyOpenSeats]
  );

  const myRequests = useMemo(
    () =>
      [...(data?.requests || [])].sort((left, right) =>
        `${right.date || ""}|${right.startTime || ""}`.localeCompare(`${left.date || ""}|${left.startTime || ""}`)
      ),
    [data]
  );

  const movingRequest = useMemo(
    () => myRequests.find((request) => request.requestId === movingRequestId) || null,
    [myRequests, movingRequestId]
  );

  async function requestSlot(slot) {
    setRequestingKey(slot.practiceSlotKey);
    setError("");
    try {
      const result = await apiFetch("/api/field-inventory/practice/requests", {
        method: "POST",
        body: JSON.stringify({
          seasonLabel,
          practiceSlotKey: slot.practiceSlotKey,
        }),
      });
      setData(result);
      setToast({
        tone: "success",
        message:
          slot.bookingPolicy === "auto_approve"
            ? "Practice space auto-approved."
            : "Practice request submitted for commissioner review.",
      });
    } catch (e) {
      setError(e.message || "Failed to request practice space.");
    } finally {
      setRequestingKey("");
    }
  }

  async function moveRequest(request, slot) {
    setActingRequestId(request.requestId);
    setError("");
    try {
      const result = await apiFetch(`/api/field-inventory/practice/requests/${encodeURIComponent(request.requestId)}/move`, {
        method: "PATCH",
        body: JSON.stringify({
          seasonLabel,
          practiceSlotKey: slot.practiceSlotKey,
          notes: `Move requested from ${describeRequest(request)}`,
        }),
      });
      setData(result);
      setMovingRequestId("");
      setToast({
        tone: "success",
        message:
          slot.bookingPolicy === "auto_approve"
            ? "Practice move completed."
            : "Practice move submitted for commissioner review.",
      });
    } catch (e) {
      setError(e.message || "Failed to move practice request.");
    } finally {
      setActingRequestId("");
    }
  }

  async function cancelRequest(request) {
    setActingRequestId(request.requestId);
    setError("");
    try {
      const result = await apiFetch(`/api/field-inventory/practice/requests/${encodeURIComponent(request.requestId)}/cancel`, {
        method: "PATCH",
      });
      setData(result);
      if (movingRequestId === request.requestId) {
        setMovingRequestId("");
      }
      setToast({ tone: "success", message: "Practice request cancelled." });
    } catch (e) {
      setError(e.message || "Failed to cancel practice request.");
    } finally {
      setActingRequestId("");
    }
  }

  if (loading && !data) {
    return <StatusCard title="Loading" message="Loading inventory-backed practice space..." />;
  }

  if (error && !data) {
    return <StatusCard tone="error" title="Unable to load practice space" message={error} />;
  }

  return (
    <div className="stack gap-4">
      {toast ? <Toast tone={toast.tone} message={toast.message} onClose={() => setToast(null)} /> : null}
      {error ? <div className="callout callout--error">{error}</div> : null}

      {movingRequest ? (
        <div className="callout callout--info">
          <div className="font-bold mb-2">Move in progress</div>
          <div className="subtle">
            Choose a replacement block for {describeRequest(movingRequest)}. Your current slot stays active until the move is approved or auto-approved.
          </div>
          <div className="mt-3">
            <button className="btn" type="button" onClick={() => setMovingRequestId("")}>
              Cancel Move
            </button>
          </div>
        </div>
      ) : null}

      <div className="callout">
        <div className="font-bold mb-2">Practice space for {coachName}</div>
        <div className="subtle">
          Request unused imported field space in 90-minute blocks. Ponytail-assigned space can auto-approve; unassigned available space goes to commissioner review. Approved or pending requests can now be moved directly from this page.
        </div>
        <div className="row row--wrap gap-3 mt-3">
          <a href="#practice-space-filters" className="link">Jump to filters</a>
          <a href="#practice-space-available" className="link">Jump to available space</a>
          <a href="#practice-space-requests" className="link">Jump to my requests</a>
          <a href="#practice-space-help" className="link">How approvals and moves work</a>
        </div>
      </div>

      <div className="card">
        <div className="card__header">
          <div>
            <div className="h2">Practice Space Summary</div>
            <div className="subtle">Committed county field inventory aligned to SportsCH fields and request policy.</div>
          </div>
          <div className="row gap-2 items-end">
            <label title="Review one committed season at a time.">
              Season
              <select value={seasonLabel} onChange={(e) => load(e.target.value)}>
                {(data?.seasons || []).map((season) => (
                  <option key={season.seasonLabel} value={season.seasonLabel}>
                    {season.seasonLabel}
                  </option>
                ))}
              </select>
            </label>
            <button className="btn" type="button" onClick={() => load(seasonLabel)} disabled={loading}>
              {loading ? "Refreshing..." : "Refresh"}
            </button>
          </div>
        </div>
        <div className="card__body">
          <div className="row row--wrap gap-3">
            <div className="pill">Team: {data?.teamName || data?.teamId || "-"}</div>
            <div className="pill">Division: {data?.division || "-"}</div>
            <div className="pill">Requestable blocks: {data?.summary?.requestableBlocks || 0}</div>
            <div className="pill">Auto-approve: {data?.summary?.autoApproveBlocks || 0}</div>
            <div className="pill">Commissioner review: {data?.summary?.commissionerReviewBlocks || 0}</div>
            <div className="pill">My active requests: {myRequests.filter(isActiveRequest).length}</div>
          </div>
        </div>
      </div>

      <div id="practice-space-filters" className="card">
        <div className="card__header">
          <div className="h2">Filters</div>
          <div className="subtle">Use filters to focus on the most useful practice space first.</div>
        </div>
        <div className="card__body">
          <div className="row row--wrap gap-3">
            <label title="Search by field name, assigned group, or imported notes.">
              <span className="row gap-1 items-center">Field search <span className="hint" title="Search imported field space by canonical field, assigned group, or imported division/team text.">?</span></span>
              <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Field, group, division..." />
            </label>
            <label title="Focus on one day of the week at a time.">
              <span className="row gap-1 items-center">Day <span className="hint" title="Use this to find your regular practice day quickly.">?</span></span>
              <select value={dayFilter} onChange={(e) => setDayFilter(e.target.value)}>
                {DAY_OPTIONS.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>
            <label title="Separate immediately bookable space from commissioner-reviewed space.">
              <span className="row gap-1 items-center">Approval path <span className="hint" title="Auto-approve means your team is confirmed immediately. Commissioner review means a league admin must approve the request first.">?</span></span>
              <select value={policyFilter} onChange={(e) => setPolicyFilter(e.target.value)}>
                <option value="">All</option>
                <option value="auto_approve">Auto-approve</option>
                <option value="commissioner_review">Commissioner review</option>
              </select>
            </label>
            <label className="inlineCheck" title="Hide reserved blocks and only show space with open capacity remaining.">
              <input type="checkbox" checked={showOnlyOpenSeats} onChange={(e) => setShowOnlyOpenSeats(e.target.checked)} />
              Open slots only
            </label>
          </div>
        </div>
      </div>

      <div id="practice-space-available" className="card">
        <div className="card__header">
          <div className="h2">Available Practice Space</div>
          <div className="subtle">Each block is 90 minutes and backed by a canonical SportsCH availability slot.</div>
        </div>
        <div className="card__body">
          <div className="stack gap-3">
            {visibleSlots.map((slot) => {
              const activeRequestForSlot = myRequests.find(
                (request) => request.slotId === slot.slotId && isActiveRequest(request)
              );
              const alreadyRequested = !!activeRequestForSlot;
              const moveTargetIsCurrent = movingRequest && movingRequest.slotId === slot.slotId;
              const slotReservedByAnotherRequest = activeRequestForSlot && activeRequestForSlot.requestId !== movingRequestId;
              const disableForStandardRequest = alreadyRequested || slot.remainingCapacity <= 0;

              return (
                <div key={slot.practiceSlotKey} className="card">
                  <div className="card__header">
                    <div>
                      <div className="h2">{slot.fieldName}</div>
                      <div className="subtle">
                        {slot.date} | {slot.dayOfWeek} | {slot.startTime}-{slot.endTime}
                      </div>
                    </div>
                    <div className="row gap-2 items-center">
                      <span className="pill" title={slot.bookingPolicyReason}>{slot.bookingPolicyLabel}</span>
                      <span className="pill" title="A canonical practice block currently supports one active team reservation.">
                        Open {slot.remainingCapacity}/{slot.capacity}
                      </span>
                    </div>
                  </div>
                  <div className="card__body">
                    <div className="row row--wrap gap-3">
                      <div title="Imported assignment context from the source workbook.">
                        <div className="subtle">Assigned group</div>
                        <div>{slot.assignedGroup || "-"}</div>
                      </div>
                      <div title="Imported division context from the source workbook.">
                        <div className="subtle">Assigned division</div>
                        <div>{slot.assignedDivision || "-"}</div>
                      </div>
                      <div title="Imported team or event text from the source workbook.">
                        <div className="subtle">Assigned team/event</div>
                        <div>{slot.assignedTeamOrEvent || "-"}</div>
                      </div>
                    </div>
                    <div className="subtle mt-3">{slot.bookingPolicyReason}</div>
                    <div className="row gap-2 mt-3 items-center">
                      {movingRequest ? (
                        <button
                          className="btn btn--primary"
                          type="button"
                          disabled={moveTargetIsCurrent || !!slotReservedByAnotherRequest || slot.remainingCapacity <= 0 || !!actingRequestId}
                          onClick={() => moveRequest(movingRequest, slot)}
                        >
                          {actingRequestId === movingRequest.requestId
                            ? "Moving..."
                            : moveTargetIsCurrent
                              ? "Current Slot"
                              : "Move Here"}
                        </button>
                      ) : (
                        <button
                          className="btn btn--primary"
                          type="button"
                          disabled={disableForStandardRequest || !!requestingKey}
                          title={alreadyRequested ? "Your team already has an active request for this block." : "Request this 90-minute practice block for your team."}
                          onClick={() => requestSlot(slot)}
                        >
                          {requestingKey === slot.practiceSlotKey
                            ? "Requesting..."
                            : alreadyRequested
                              ? "Already Requested"
                              : slot.bookingPolicy === "auto_approve"
                                ? "Book Now"
                                : "Request for Approval"}
                        </button>
                      )}
                      <span className="subtle">
                        Approved teams: {slot.approvedTeamIds.join(", ") || "None"} | Pending teams: {slot.pendingTeamIds.join(", ") || "None"}
                      </span>
                    </div>
                  </div>
                </div>
              );
            })}
            {visibleSlots.length === 0 ? <div className="muted">No practice space matches the current filter.</div> : null}
          </div>
        </div>
      </div>

      <div id="practice-space-requests" className="card">
        <div className="card__header">
          <div className="h2">My Practice Requests</div>
          <div className="subtle">Track pending approvals, approved space, moves, and cancellations.</div>
        </div>
        <div className="card__body overflow-x-auto">
          <table className="table">
            <thead>
              <tr>
                <th>Date</th>
                <th>Field</th>
                <th>Status</th>
                <th>Policy</th>
                <th>Notes</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              {myRequests.map((request) => (
                <tr key={request.requestId}>
                  <td>{request.date} {request.startTime}-{request.endTime}</td>
                  <td>
                    <div>{request.fieldName}</div>
                    {request.isMove && request.moveFromDate ? (
                      <div className="subtle">Move from {request.moveFromDate} {request.moveFromStartTime}-{request.moveFromEndTime} {request.moveFromFieldName || ""}</div>
                    ) : null}
                  </td>
                  <td><span className="pill">{request.status}</span></td>
                  <td title={request.bookingPolicy === "auto_approve" ? "This space was confirmed immediately." : "This space requires commissioner approval."}>{request.bookingPolicyLabel}</td>
                  <td>{request.notes || request.reviewReason || "-"}</td>
                  <td>
                    {isActiveRequest(request) ? (
                      <div className="row gap-2">
                        <button
                          className="btn"
                          type="button"
                          disabled={!!actingRequestId}
                          onClick={() => setMovingRequestId((current) => (current === request.requestId ? "" : request.requestId))}
                        >
                          {movingRequestId === request.requestId ? "Selecting Target..." : "Move"}
                        </button>
                        <button className="btn" type="button" disabled={!!actingRequestId} onClick={() => cancelRequest(request)}>
                          {actingRequestId === request.requestId ? "Working..." : "Cancel"}
                        </button>
                      </div>
                    ) : (
                      <span className="subtle">No action</span>
                    )}
                  </td>
                </tr>
              ))}
              {myRequests.length === 0 ? (
                <tr>
                  <td colSpan={6} className="muted">No practice requests yet.</td>
                </tr>
              ) : null}
            </tbody>
          </table>
        </div>
      </div>

      <div id="practice-space-help" className="card">
        <div className="card__header">
          <div className="h2">How This Works</div>
          <div className="subtle">Quick guidance for coaches using the normalized practice-space workflow.</div>
        </div>
        <div className="card__body stack gap-3">
          <div>
            <div className="font-bold">Auto-approve</div>
            <div className="subtle">Ponytail-assigned space can confirm immediately when capacity remains. Your team does not need commissioner review for those blocks.</div>
          </div>
          <div>
            <div className="font-bold">Commissioner review</div>
            <div className="subtle">Available but unassigned county space is requestable, but a commissioner must approve it before your team is confirmed.</div>
          </div>
          <div>
            <div className="font-bold">Move request</div>
            <div className="subtle">Use Move on an approved or pending request, then choose a replacement block above. The original slot is only released after the move is approved or auto-approved.</div>
          </div>
          <div>
            <div className="font-bold">When to cancel</div>
            <div className="subtle">Cancel a pending or approved request as soon as you no longer need the space so another team can use it.</div>
          </div>
        </div>
      </div>
    </div>
  );
}
