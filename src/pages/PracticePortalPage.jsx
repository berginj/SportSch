import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import StatusCard from "../components/StatusCard";
import Toast from "../components/Toast";

function getTodayDate() {
  return new Date().toISOString().slice(0, 10);
}

function getInitialAvailabilityDate(portalData) {
  const datedSlots = [...(portalData?.slots || [])]
    .map((slot) => String(slot?.date || "").trim())
    .filter(Boolean)
    .sort((left, right) => left.localeCompare(right));
  return datedSlots[0] || getTodayDate();
}

function buildAvailabilityQuery({ seasonLabel, date, startTime, endTime, fieldKey }) {
  const params = new URLSearchParams();
  if (seasonLabel) params.set("seasonLabel", seasonLabel);
  if (date) params.set("date", date);
  if (startTime && endTime) {
    params.set("startTime", startTime);
    params.set("endTime", endTime);
  }
  if (fieldKey) params.set("fieldKey", fieldKey);
  return params.toString();
}

function filterSlots(slots, search, policy, showOnlyOpenSeats) {
  const needle = String(search || "").trim().toLowerCase();
  return (slots || [])
    .filter((slot) => (policy ? slot.bookingPolicy === policy : true))
    .filter((slot) => (showOnlyOpenSeats ? slot.isAvailable : true))
    .filter((slot) => {
      if (!needle) return true;
      const haystack = [
        slot.fieldName,
        slot.fieldKey,
        slot.date,
        slot.startTime,
        slot.endTime,
        ...(slot.reservedTeamIds || []),
        ...(slot.pendingTeamIds || []),
        ...(slot.pendingShareTeamIds || []),
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

function describeSharing(requestOrSlot) {
  const reservedTeamIds = requestOrSlot?.reservedTeamIds || [];
  if (requestOrSlot?.openToShareField && requestOrSlot?.shareWithTeamId) {
    return `Sharing with ${requestOrSlot.shareWithTeamId}`;
  }
  if (reservedTeamIds.length > 1) {
    return `Sharing with ${reservedTeamIds.slice(1).join(", ")}`;
  }
  return "Exclusive booking";
}

function getSharePartnerName(teamOptions, teamId) {
  return teamOptions.find((team) => team.teamId === teamId)?.name || teamId;
}

export default function PracticePortalPage({ me, leagueId }) {
  const [data, setData] = useState(null);
  const [teamOptions, setTeamOptions] = useState([]);
  const [availabilityData, setAvailabilityData] = useState(null);
  const [availabilityCheck, setAvailabilityCheck] = useState(null);
  const [seasonLabel, setSeasonLabel] = useState("");
  const [availabilityDate, setAvailabilityDate] = useState("");
  const [availabilityStartTime, setAvailabilityStartTime] = useState("");
  const [availabilityEndTime, setAvailabilityEndTime] = useState("");
  const [loading, setLoading] = useState(true);
  const [availabilityLoading, setAvailabilityLoading] = useState(false);
  const [availabilityRefreshKey, setAvailabilityRefreshKey] = useState(0);
  const [requestingKey, setRequestingKey] = useState("");
  const [movingRequestId, setMovingRequestId] = useState("");
  const [actingRequestId, setActingRequestId] = useState("");
  const [error, setError] = useState("");
  const [availabilityError, setAvailabilityError] = useState("");
  const [toast, setToast] = useState(null);
  const [search, setSearch] = useState("");
  const [policyFilter, setPolicyFilter] = useState("");
  const [showOnlyOpenSeats, setShowOnlyOpenSeats] = useState(true);
  const [openToShareField, setOpenToShareField] = useState(false);
  const [shareWithTeamId, setShareWithTeamId] = useState("");

  const coachName = me?.name || me?.userDetails || "Coach";
  const sharePartnerOptions = useMemo(
    () => (teamOptions || []).filter((team) => team.teamId !== data?.teamId),
    [teamOptions, data]
  );
  const exactWindowRequested = !!availabilityStartTime && !!availabilityEndTime;

  async function load(nextSeasonLabel = seasonLabel) {
    if (!leagueId) return;
    setLoading(true);
    setError("");
    try {
      const query = nextSeasonLabel ? `?seasonLabel=${encodeURIComponent(nextSeasonLabel)}` : "";
      const result = await apiFetch(`/api/field-inventory/practice/coach${query}`);
      const resolvedSeasonLabel = result?.seasonLabel || nextSeasonLabel || "";
      setData(result);
      setSeasonLabel(resolvedSeasonLabel);
      setAvailabilityDate((current) =>
        current && resolvedSeasonLabel === seasonLabel ? current : getInitialAvailabilityDate(result)
      );
      const teamList = result?.division
        ? await apiFetch(`/api/teams?division=${encodeURIComponent(result.division)}`).catch(() => [])
        : [];
      setTeamOptions(Array.isArray(teamList) ? teamList : []);
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

  useEffect(() => {
    if (!leagueId || !availabilityDate || !seasonLabel) return;

    let cancelled = false;

    async function loadAvailability() {
      setAvailabilityLoading(true);
      setAvailabilityError("");
      try {
        const query = buildAvailabilityQuery({
          seasonLabel,
          date: availabilityDate,
          startTime: availabilityStartTime,
          endTime: availabilityEndTime,
          fieldKey: "",
        });

        const [optionsResult, checkResult] = await Promise.all([
          apiFetch(`/api/field-inventory/practice/availability/options?${query}`),
          exactWindowRequested
            ? apiFetch(`/api/field-inventory/practice/availability/check?${query}`)
            : Promise.resolve(null),
        ]);

        if (cancelled) return;
        setAvailabilityData(optionsResult);
        setAvailabilityCheck(checkResult);
      } catch (e) {
        if (cancelled) return;
        setAvailabilityError(e.message || "Failed to load availability.");
        setAvailabilityData(null);
        setAvailabilityCheck(null);
      } finally {
        if (!cancelled) {
          setAvailabilityLoading(false);
        }
      }
    }

    loadAvailability();
    return () => {
      cancelled = true;
    };
  }, [leagueId, seasonLabel, availabilityDate, availabilityStartTime, availabilityEndTime, exactWindowRequested, availabilityRefreshKey]);

  useEffect(() => {
    if (!openToShareField) {
      setShareWithTeamId("");
    }
  }, [openToShareField]);

  const visibleSlots = useMemo(
    () => filterSlots(availabilityData?.options || [], search, policyFilter, showOnlyOpenSeats),
    [availabilityData, search, policyFilter, showOnlyOpenSeats]
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

  const selectedSharePartnerName = shareWithTeamId ? getSharePartnerName(sharePartnerOptions, shareWithTeamId) : "";

  async function requestSlot(slot) {
    setRequestingKey(slot.practiceSlotKey);
    setError("");
    try {
      const result = await apiFetch("/api/field-inventory/practice/requests", {
        method: "POST",
        body: JSON.stringify({
          seasonLabel,
          practiceSlotKey: slot.practiceSlotKey,
          openToShareField,
          shareWithTeamId: openToShareField ? shareWithTeamId : null,
        }),
      });
      setData(result);
      setAvailabilityRefreshKey((current) => current + 1);
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
          openToShareField,
          shareWithTeamId: openToShareField ? shareWithTeamId : null,
        }),
      });
      setData(result);
      setAvailabilityRefreshKey((current) => current + 1);
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
      setAvailabilityRefreshKey((current) => current + 1);
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
      {availabilityError ? <div className="callout callout--error">{availabilityError}</div> : null}

      {movingRequest ? (
        <div className="callout callout--info">
          <div className="font-bold mb-2">Move in progress</div>
          <div className="subtle">
            Choose a replacement block for {describeRequest(movingRequest)}. Your current slot stays active until the move is approved or auto-approved.
          </div>
          <div className="mt-2 subtle">
            Share setting for this move: {openToShareField && selectedSharePartnerName ? `share with ${selectedSharePartnerName}` : "exclusive booking"}
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
          Query canonical availability for a specific date and optional time window, then book or move a practice request from the returned options. Shared bookings can reserve a named partner team when the slot is marked shareable.
        </div>
        <div className="row row--wrap gap-3 mt-3">
          <a href="#practice-space-filters" className="link">Jump to filters</a>
          <a href="#practice-space-available" className="link">Jump to available space</a>
          <a href="#practice-space-requests" className="link">Jump to my requests</a>
          <a href="#practice-space-help" className="link">How approvals and sharing work</a>
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
          <div className="h2">Availability Query</div>
          <div className="subtle">Search by date first, then narrow to an exact window if you need a yes/no availability check.</div>
        </div>
        <div className="card__body stack gap-3">
          <div className="row row--wrap gap-3">
            <label title="Query one day of canonical availability at a time.">
              Date
              <input type="date" value={availabilityDate} onChange={(e) => setAvailabilityDate(e.target.value)} />
            </label>
            <label title="Optional. Fill both start and end to check an exact window.">
              Start
              <input type="time" value={availabilityStartTime} onChange={(e) => setAvailabilityStartTime(e.target.value)} />
            </label>
            <label title="Optional. Fill both start and end to check an exact window.">
              End
              <input type="time" value={availabilityEndTime} onChange={(e) => setAvailabilityEndTime(e.target.value)} />
            </label>
            <label title="Search by field name or team IDs already attached to a slot.">
              Search
              <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Field, team, date..." />
            </label>
            <label title="Separate immediately bookable space from commissioner-reviewed space.">
              Approval path
              <select value={policyFilter} onChange={(e) => setPolicyFilter(e.target.value)}>
                <option value="">All</option>
                <option value="auto_approve">Auto-approve</option>
                <option value="commissioner_review">Commissioner review</option>
              </select>
            </label>
            <label className="inlineCheck" title="Hide windows that are already full or blocked by an active request.">
              <input type="checkbox" checked={showOnlyOpenSeats} onChange={(e) => setShowOnlyOpenSeats(e.target.checked)} />
              Open slots only
            </label>
          </div>

          <div className="row row--wrap gap-3 items-end">
            <label className="inlineCheck" title="Reserve the slot for your team plus one named partner team.">
              <input
                type="checkbox"
                checked={openToShareField}
                onChange={(e) => setOpenToShareField(e.target.checked)}
                disabled={sharePartnerOptions.length === 0}
              />
              Book as shared practice
            </label>
            <label title="Choose the partner team that will share this reservation.">
              Share with
              <select
                value={shareWithTeamId}
                onChange={(e) => setShareWithTeamId(e.target.value)}
                disabled={!openToShareField || sharePartnerOptions.length === 0}
              >
                <option value="">Select team</option>
                {sharePartnerOptions.map((team) => (
                  <option key={team.teamId} value={team.teamId}>
                    {team.name} ({team.teamId})
                  </option>
                ))}
              </select>
            </label>
            <div className="subtle">
              {openToShareField
                ? selectedSharePartnerName
                  ? `Requests and moves will reserve this slot for ${data?.teamName || data?.teamId} and ${selectedSharePartnerName}.`
                  : "Choose a partner team before booking a shared practice."
                : "Requests and moves will reserve the slot only for your team."}
            </div>
          </div>

          {exactWindowRequested && availabilityCheck ? (
            <div className={`callout ${availabilityCheck.available ? "callout--ok" : "callout--info"}`}>
              <div className="font-bold mb-1">
                {availabilityCheck.available ? "Exact window available" : "Exact window unavailable"}
              </div>
              <div className="subtle">
                {availabilityCheck.date} {availabilityCheck.startTime}-{availabilityCheck.endTime} returned {availabilityCheck.matchingOptionCount} matching option{availabilityCheck.matchingOptionCount === 1 ? "" : "s"}.
              </div>
            </div>
          ) : null}
        </div>
      </div>

      <div id="practice-space-available" className="card">
        <div className="card__header">
          <div>
            <div className="h2">Available Practice Space</div>
            <div className="subtle">Options below are returned from the canonical practice availability query for the selected day and time window.</div>
          </div>
          <div className="subtle">{availabilityLoading ? "Refreshing availability..." : `${visibleSlots.length} option${visibleSlots.length === 1 ? "" : "s"}`}</div>
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
              const shareSelectionInvalid = openToShareField && !shareWithTeamId;
              const disableForStandardRequest = alreadyRequested || !slot.isAvailable || shareSelectionInvalid;

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
                      <span className="pill">{slot.bookingPolicyLabel}</span>
                      <span className="pill" title={slot.shareable ? "This slot supports up to two teams on a shared booking." : "This slot can only be booked exclusively."}>
                        {slot.shareable ? `Shareable ${slot.reservedTeamIds.length}/${slot.maxTeamsPerBooking}` : "Exclusive"}
                      </span>
                      <span className="pill" title={slot.isAvailable ? "Bookable right now." : "Blocked by an active reservation or pending request."}>
                        {slot.isAvailable ? "Available" : "Unavailable"}
                      </span>
                    </div>
                  </div>
                  <div className="card__body">
                    <div className="row row--wrap gap-3">
                      <div>
                        <div className="subtle">Field key</div>
                        <div>{slot.fieldKey || "-"}</div>
                      </div>
                      <div>
                        <div className="subtle">Reserved teams</div>
                        <div>{slot.reservedTeamIds.join(", ") || "None"}</div>
                      </div>
                      <div>
                        <div className="subtle">Pending teams</div>
                        <div>{slot.pendingTeamIds.join(", ") || "None"}</div>
                      </div>
                      <div>
                        <div className="subtle">Pending share partners</div>
                        <div>{slot.pendingShareTeamIds.join(", ") || "None"}</div>
                      </div>
                    </div>
                    <div className="subtle mt-3">{slot.bookingPolicy === "auto_approve" ? "This option confirms immediately when booked." : "This option requires commissioner approval before it is confirmed."}</div>
                    <div className="subtle mt-2">{describeSharing(slot)}</div>
                    <div className="row gap-2 mt-3 items-center">
                      {movingRequest ? (
                        <button
                          className="btn btn--primary"
                          type="button"
                          disabled={moveTargetIsCurrent || !!slotReservedByAnotherRequest || !slot.isAvailable || !!actingRequestId || shareSelectionInvalid}
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
                          title={
                            alreadyRequested
                              ? "Your team already has an active request for this block."
                              : shareSelectionInvalid
                                ? "Choose a partner team before sending a shared booking request."
                                : "Request this practice block for your team."
                          }
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
                        {openToShareField && selectedSharePartnerName
                          ? `This request will share the field with ${selectedSharePartnerName}.`
                          : "This request will reserve the field only for your team."}
                      </span>
                    </div>
                  </div>
                </div>
              );
            })}
            {!availabilityLoading && visibleSlots.length === 0 ? <div className="muted">No practice space matches the current query.</div> : null}
          </div>
        </div>
      </div>

      <div id="practice-space-requests" className="card">
        <div className="card__header">
          <div className="h2">My Practice Requests</div>
          <div className="subtle">Track pending approvals, approved space, moves, sharing, and cancellations.</div>
        </div>
        <div className="card__body overflow-x-auto">
          <table className="table">
            <thead>
              <tr>
                <th>Date</th>
                <th>Field</th>
                <th>Status</th>
                <th>Policy</th>
                <th>Booking</th>
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
                  <td>
                    <div>{describeSharing(request)}</div>
                    <div className="subtle">{request.reservedTeamIds?.join(", ") || request.teamId}</div>
                  </td>
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
                  <td colSpan={7} className="muted">No practice requests yet.</td>
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
            <div className="font-bold">Exact availability check</div>
            <div className="subtle">Enter a date plus start and end time to get a yes/no answer for that exact window, along with the matching canonical options.</div>
          </div>
          <div>
            <div className="font-bold">Shared booking</div>
            <div className="subtle">Use Book as shared practice to reserve the slot for your team plus one named partner team when the slot is shareable.</div>
          </div>
          <div>
            <div className="font-bold">Auto-approve</div>
            <div className="subtle">Ponytail-assigned space can confirm immediately while the block is still available. Your team does not need commissioner review for those blocks.</div>
          </div>
          <div>
            <div className="font-bold">Commissioner review</div>
            <div className="subtle">Available but unassigned county space is requestable, but a commissioner must approve it before your team is confirmed.</div>
          </div>
          <div>
            <div className="font-bold">Move request</div>
            <div className="subtle">Use Move on an approved or pending request, then query a replacement date above. The original slot is only released after the move is approved or auto-approved.</div>
          </div>
        </div>
      </div>
    </div>
  );
}
