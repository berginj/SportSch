import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import Toast from "../components/Toast";
import PracticeSpaceComparisonCalendar from "../components/PracticeSpaceComparisonCalendar";
import { buildPracticeSpaceComparison, derivePracticeSpaceDateRange, filterPracticeSpaceComparison } from "../lib/practiceSpaceCompare";

function filterRows(rows, search, policy, issue, sortBy) {
  const needle = String(search || "").trim().toLowerCase();
  const list = (rows || []).filter((row) => {
    if (policy && row.bookingPolicy !== policy) return false;
    if (issue && !(row.mappingIssues || []).includes(issue)) return false;
    if (!needle) return true;

    const haystack = [
      row.fieldName,
      row.rawFieldName,
      row.assignedGroup,
      row.rawAssignedDivision,
      row.rawAssignedTeamOrEvent,
      row.canonicalDivisionCode,
      row.canonicalTeamName,
      row.date,
    ]
      .map((value) => String(value || "").toLowerCase())
      .join(" ");

    return haystack.includes(needle);
  });

  return list.sort((left, right) => {
    if (sortBy === "field") return String(left.fieldName || "").localeCompare(String(right.fieldName || ""));
    if (sortBy === "policy") return String(left.bookingPolicy || "").localeCompare(String(right.bookingPolicy || ""));
    return `${left.date || ""}|${left.startTime || ""}|${left.fieldName || ""}`.localeCompare(
      `${right.date || ""}|${right.startTime || ""}|${right.fieldName || ""}`
    );
  });
}

function filterRequests(requests, status) {
  if (!status) return requests || [];
  return (requests || []).filter((request) => request.status === status);
}

function humanizeCompareState(value) {
  const state = String(value || "").trim();
  if (state === "normalized") return "Normalized";
  if (state === "missing") return "Missing";
  if (state === "conflict") return "Conflict";
  if (state === "blocked") return "Blocked";
  return state || "Unknown";
}

function summarizeNormalization(result) {
  if (!result) return "";
  return [
    `${result.createdBlocks || 0} created`,
    `${result.updatedBlocks || 0} updated`,
    `${result.alreadyNormalizedBlocks || 0} already normalized`,
    `${result.conflictBlocks || 0} conflicts`,
    `${result.blockedBlocks || 0} blocked`,
  ].join(" · ");
}

function summarizeNormalizationText(result) {
  if (!result) return "";
  return [
    `${result.createdBlocks || 0} created`,
    `${result.updatedBlocks || 0} updated`,
    `${result.alreadyNormalizedBlocks || 0} already normalized`,
    `${result.conflictBlocks || 0} conflicts`,
    `${result.blockedBlocks || 0} blocked`,
  ].join(" | ");
}

export default function PracticeSpaceManager({ leagueId }) {
  const [data, setData] = useState(null);
  const [seasonLabel, setSeasonLabel] = useState("");
  const [loading, setLoading] = useState(true);
  const [savingKey, setSavingKey] = useState("");
  const [normalizeBusy, setNormalizeBusy] = useState("");
  const [error, setError] = useState("");
  const [toast, setToast] = useState(null);
  const [search, setSearch] = useState("");
  const [compareSearch, setCompareSearch] = useState("");
  const [compareDateFrom, setCompareDateFrom] = useState("");
  const [compareDateTo, setCompareDateTo] = useState("");
  const [compareStateFilter, setCompareStateFilter] = useState("");
  const [compareIssueFilter, setCompareIssueFilter] = useState("");
  const [compareFieldFilter, setCompareFieldFilter] = useState("");
  const [policyFilter, setPolicyFilter] = useState("");
  const [issueFilter, setIssueFilter] = useState("");
  const [sortBy, setSortBy] = useState("date");
  const [requestStatusFilter, setRequestStatusFilter] = useState("Pending");
  const [divisionDrafts, setDivisionDrafts] = useState({});
  const [teamDrafts, setTeamDrafts] = useState({});
  const [policyDrafts, setPolicyDrafts] = useState({});

  function applyAdminView(nextData, resetRange = false) {
    setData(nextData);
    setSeasonLabel(nextData?.seasonLabel || "");
    if (resetRange || !compareDateFrom || !compareDateTo) {
      const range = derivePracticeSpaceDateRange(nextData?.rows || [], nextData?.slots || []);
      setCompareDateFrom(range.dateFrom);
      setCompareDateTo(range.dateTo);
    }
    if (resetRange) setCompareFieldFilter("");
  }

  async function load(nextSeasonLabel = seasonLabel) {
    if (!leagueId) return;
    setLoading(true);
    setError("");
    try {
      const query = nextSeasonLabel ? `?seasonLabel=${encodeURIComponent(nextSeasonLabel)}` : "";
      const result = await apiFetch(`/api/field-inventory/practice/admin${query}`);
      applyAdminView(result, true);
    } catch (e) {
      setError(e.message || "Failed to load practice space admin view.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load("");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [leagueId]);

  const filteredRows = useMemo(
    () => filterRows(data?.rows || [], search, policyFilter, issueFilter, sortBy),
    [data, search, policyFilter, issueFilter, sortBy]
  );

  const visibleRequests = useMemo(
    () => filterRequests(data?.requests || [], requestStatusFilter),
    [data, requestStatusFilter]
  );

  const comparison = useMemo(
    () => buildPracticeSpaceComparison(data?.rows || [], data?.slots || [], data?.normalization || null),
    [data]
  );

  const compareFieldOptions = useMemo(
    () =>
      Array.from(new Map((comparison.items || []).map((item) => [item.fieldId, item.fieldName])).entries())
        .filter(([fieldId]) => !!fieldId)
        .map(([fieldId, fieldName]) => ({ fieldId, fieldName }))
        .sort((left, right) => left.fieldName.localeCompare(right.fieldName)),
    [comparison]
  );

  const filteredCompareItems = useMemo(
    () =>
      filterPracticeSpaceComparison(comparison.items || [], {
        search: compareSearch,
        dateFrom: compareDateFrom,
        dateTo: compareDateTo,
        compareState: compareStateFilter,
        issue: compareIssueFilter,
        fieldId: compareFieldFilter,
      }),
    [comparison, compareSearch, compareDateFrom, compareDateTo, compareStateFilter, compareIssueFilter, compareFieldFilter]
  );

  async function saveDivisionMapping(row) {
    const canonicalDivisionCode = (divisionDrafts[row.recordId] || row.canonicalDivisionCode || "").trim();
    if (!canonicalDivisionCode || !row.rawAssignedDivision) return;
    setSavingKey(`division:${row.recordId}`);
    try {
      const result = await apiFetch("/api/field-inventory/practice/mappings/divisions", {
        method: "POST",
        body: JSON.stringify({
          rawDivisionName: row.rawAssignedDivision,
          canonicalDivisionCode,
        }),
      });
      applyAdminView(result);
      setToast({ tone: "success", message: `Saved division mapping for ${row.rawAssignedDivision}.` });
    } catch (e) {
      setError(e.message || "Failed to save division mapping.");
    } finally {
      setSavingKey("");
    }
  }

  async function saveTeamMapping(row) {
    const divisionCode = (divisionDrafts[row.recordId] || row.canonicalDivisionCode || "").trim();
    const canonicalTeamId = (teamDrafts[row.recordId] || row.canonicalTeamId || "").trim();
    if (!divisionCode || !canonicalTeamId || !row.rawAssignedTeamOrEvent) return;
    const option = (data?.canonicalTeams || []).find((team) => team.divisionCode === divisionCode && team.teamId === canonicalTeamId);
    setSavingKey(`team:${row.recordId}`);
    try {
      const result = await apiFetch("/api/field-inventory/practice/mappings/teams", {
        method: "POST",
        body: JSON.stringify({
          rawTeamName: row.rawAssignedTeamOrEvent,
          canonicalDivisionCode: divisionCode,
          canonicalTeamId,
          canonicalTeamName: option?.teamName || canonicalTeamId,
        }),
      });
      applyAdminView(result);
      setToast({ tone: "success", message: `Saved team mapping for ${row.rawAssignedTeamOrEvent}.` });
    } catch (e) {
      setError(e.message || "Failed to save team mapping.");
    } finally {
      setSavingKey("");
    }
  }

  async function savePolicy(row) {
    const bookingPolicy = (policyDrafts[row.recordId] || row.bookingPolicy || "").trim();
    if (!bookingPolicy || !row.assignedGroup) return;
    setSavingKey(`policy:${row.recordId}`);
    try {
      const result = await apiFetch("/api/field-inventory/practice/policies", {
        method: "POST",
        body: JSON.stringify({
          rawGroupName: row.assignedGroup,
          bookingPolicy,
        }),
      });
      applyAdminView(result);
      setToast({ tone: "success", message: `Saved booking policy for ${row.assignedGroup}.` });
    } catch (e) {
      setError(e.message || "Failed to save policy.");
    } finally {
      setSavingKey("");
    }
  }

  async function reviewRequest(requestId, action) {
    setSavingKey(`${action}:${requestId}`);
    try {
      const result = await apiFetch(`/api/field-inventory/practice/requests/${encodeURIComponent(requestId)}/${action}`, {
        method: "PATCH",
        body: JSON.stringify({
          reason: action === "approve" ? "Approved by commissioner" : "Rejected by commissioner",
        }),
      });
      applyAdminView(result);
      setToast({ tone: "success", message: action === "approve" ? "Practice request approved." : "Practice request rejected." });
    } catch (e) {
      setError(e.message || `Failed to ${action} practice request.`);
    } finally {
      setSavingKey("");
    }
  }

  async function runNormalization({ dryRun = false, dateFrom = compareDateFrom, dateTo = compareDateTo, fieldId = compareFieldFilter } = {}) {
    setNormalizeBusy(dryRun ? "preview" : "apply");
    setError("");
    try {
      const result = await apiFetch("/api/field-inventory/practice/normalize", {
        method: "POST",
        body: JSON.stringify({
          seasonLabel,
          dateFrom,
          dateTo,
          fieldId: fieldId || null,
          dryRun,
        }),
      });
      applyAdminView(result?.adminView || data);
      setToast({
        tone: dryRun ? "info" : "success",
        message: `${dryRun ? "Normalization preview" : "Normalization complete"}: ${summarizeNormalizationText(result?.result)}`,
      });
    } catch (e) {
      setError(e.message || "Failed to normalize committed practice availability.");
    } finally {
      setNormalizeBusy("");
    }
  }

  if (loading && !data) {
    return <div className="muted">Loading practice space review...</div>;
  }

  return (
    <div className="stack gap-4">
      {toast ? <Toast tone={toast.tone} message={toast.message} onClose={() => setToast(null)} /> : null}
      {error ? <div className="callout callout--error">{error}</div> : null}

      <div className="card">
        <div className="card__header">
          <div>
            <div className="h2">Practice Space Admin</div>
            <div className="subtle">Normalize committed workbook inventory into canonical availability, then review requests from the same screen.</div>
          </div>
          <div className="row gap-2 items-end">
            <label>
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
        <div className="card__body row row--wrap gap-3">
          <div className="pill">Rows: {data?.summary?.totalRecords || 0}</div>
          <div className="pill">90m blocks: {data?.normalization?.candidateBlocks || 0}</div>
          <div className="pill">Normalized: {data?.normalization?.normalizedBlocks || 0}</div>
          <div className="pill">Missing: {data?.normalization?.missingBlocks || 0}</div>
          <div className="pill">Conflicts: {data?.normalization?.conflictBlocks || 0}</div>
          <div className="pill">Blocked: {data?.normalization?.blockedBlocks || 0}</div>
          <div className="pill">Pending requests: {data?.summary?.pendingRequests || 0}</div>
        </div>
      </div>

      <div className="card">
        <div className="card__header">
          <div>
            <div className="h2">Availability Normalization</div>
            <div className="subtle">Preview or apply canonical slot backfill, then inspect conflicts and mapping gaps at the block level.</div>
          </div>
          <div className="row gap-2 items-end">
            <label>
              From
              <input type="date" value={compareDateFrom} onChange={(e) => setCompareDateFrom(e.target.value)} />
            </label>
            <label>
              To
              <input type="date" value={compareDateTo} onChange={(e) => setCompareDateTo(e.target.value)} />
            </label>
            <button className="btn" type="button" onClick={() => runNormalization({ dryRun: true })} disabled={!!normalizeBusy || !compareDateFrom || !compareDateTo}>
              {normalizeBusy === "preview" ? "Previewing..." : "Preview Normalize"}
            </button>
            <button className="btn btn--primary" type="button" onClick={() => runNormalization({ dryRun: false })} disabled={!!normalizeBusy || !compareDateFrom || !compareDateTo}>
              {normalizeBusy === "apply" ? "Normalizing..." : "Normalize Missing"}
            </button>
          </div>
        </div>
        <div className="card__body stack gap-4">
          <div className="row row--wrap gap-3">
            <div className="pill">Candidates: {comparison.summary.candidateCount}</div>
            <div className="pill">Normalized: {comparison.summary.normalizedCount}</div>
            <div className="pill">Missing: {comparison.summary.missingCount}</div>
            <div className="pill">Conflicts: {comparison.summary.conflictCount}</div>
            <div className="pill">Blocked: {comparison.summary.blockedCount}</div>
            <div className="pill">Issue rows: {comparison.summary.issueCount}</div>
          </div>

          <div className="row row--wrap gap-3">
            <label>
              Search
              <input value={compareSearch} onChange={(e) => setCompareSearch(e.target.value)} placeholder="Field, issue, division..." />
            </label>
            <label>
              Field
              <select value={compareFieldFilter} onChange={(e) => setCompareFieldFilter(e.target.value)}>
                <option value="">All fields</option>
                {compareFieldOptions.map((field) => (
                  <option key={field.fieldId} value={field.fieldId}>{field.fieldName}</option>
                ))}
              </select>
            </label>
            <label>
              State
              <select value={compareStateFilter} onChange={(e) => setCompareStateFilter(e.target.value)}>
                <option value="">All</option>
                <option value="normalized">Normalized</option>
                <option value="missing">Missing</option>
                <option value="conflict">Conflict</option>
                <option value="blocked">Blocked</option>
              </select>
            </label>
            <label>
              Issue
              <select value={compareIssueFilter} onChange={(e) => setCompareIssueFilter(e.target.value)}>
                <option value="">All</option>
                <option value="division_unmapped">Division unmapped</option>
                <option value="team_unmapped">Team unmapped</option>
                <option value="policy_unmapped">Policy unmapped</option>
                <option value="field_unmapped">Field unmapped</option>
                <option value="legacy_slot_id">Legacy slot id</option>
                <option value="manual_overlap">Overlap on canonical availability</option>
                <option value="cross_division_overlap">Cross-division overlap</option>
                <option value="slot_already_in_use">Already converted to practice</option>
                <option value="imported_not_requestable">Imported block unavailable</option>
              </select>
            </label>
          </div>

          <PracticeSpaceComparisonCalendar items={filteredCompareItems} mode="compare" />

          <div className="overflow-x-auto">
            <table className="table">
              <thead>
                <tr>
                  <th>When</th>
                  <th>Field</th>
                  <th>State</th>
                  <th>Division</th>
                  <th>Policy</th>
                  <th>Issues</th>
                  <th>Action</th>
                </tr>
              </thead>
              <tbody>
                {filteredCompareItems.map((item) => (
                  <tr key={item.key}>
                    <td>
                      <div>{item.date}</div>
                      <div className="subtle">{item.dayOfWeek} {item.startTime}-{item.endTime}</div>
                    </td>
                    <td>
                      <div className="font-bold">{item.fieldName}</div>
                      <div className="subtle">{item.fieldId}</div>
                    </td>
                    <td><span className="pill">{humanizeCompareState(item.compareState)}</span></td>
                    <td>
                      <div>{item.slot?.division || "-"}</div>
                      <div className="subtle">{item.slot?.slotId || "No canonical slot yet"}</div>
                    </td>
                    <td>
                      <div>{item.slot?.bookingPolicyLabel || "-"}</div>
                      <div className="subtle">{item.slot?.bookingPolicyReason || "-"}</div>
                    </td>
                    <td>
                      {item.issueFlags.length ? (
                        <div className="row row--wrap gap-2">
                          {item.issueFlags.map((issue) => <span key={issue} className="pill">{issue.replaceAll("_", " ")}</span>)}
                        </div>
                      ) : (
                        <span className="muted">No issues</span>
                      )}
                    </td>
                    <td>
                      {item.compareState === "missing" ? (
                        <button
                          className="btn btn--primary"
                          type="button"
                          disabled={!!normalizeBusy}
                          onClick={() => runNormalization({ dryRun: false, dateFrom: item.date, dateTo: item.date, fieldId: item.fieldId })}
                        >
                          {normalizeBusy === "apply" ? "Normalizing..." : "Normalize Day"}
                        </button>
                      ) : (
                        <span className="subtle">No action</span>
                      )}
                    </td>
                  </tr>
                ))}
                {filteredCompareItems.length === 0 ? (
                  <tr>
                    <td colSpan={7} className="muted">No normalization rows match the current filter.</td>
                  </tr>
                ) : null}
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h2">Request Queue</div>
          <div className="subtle">Pending requests and move requests stay here until approved or rejected.</div>
        </div>
        <div className="card__body stack gap-3">
          <label>
            Status
            <select value={requestStatusFilter} onChange={(e) => setRequestStatusFilter(e.target.value)}>
              <option value="">All</option>
              <option value="Pending">Pending</option>
              <option value="Approved">Approved</option>
              <option value="Rejected">Rejected</option>
              <option value="Cancelled">Cancelled</option>
            </select>
          </label>
          <div className="overflow-x-auto">
            <table className="table">
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Field</th>
                  <th>Team</th>
                  <th>Status</th>
                  <th>Policy</th>
                  <th>Notes</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {visibleRequests.map((request) => (
                  <tr key={request.requestId}>
                    <td>{request.date} {request.startTime}-{request.endTime}</td>
                    <td>{request.fieldName}</td>
                    <td>{request.teamName || request.teamId}</td>
                    <td>
                      <div className="row row--wrap gap-2">
                        <span className="pill">{request.status}</span>
                        {request.isMove ? <span className="pill">Move</span> : null}
                      </div>
                    </td>
                    <td>{request.bookingPolicyLabel}</td>
                    <td>
                      <div>{request.notes || request.reviewReason || "-"}</div>
                      {request.isMove && request.moveFromDate ? (
                        <div className="subtle">
                          From {request.moveFromDate} {request.moveFromStartTime}-{request.moveFromEndTime} {request.moveFromFieldName ? `at ${request.moveFromFieldName}` : ""}
                        </div>
                      ) : null}
                    </td>
                    <td>
                      {request.status === "Pending" ? (
                        <div className="row gap-2">
                          <button className="btn btn--primary" type="button" disabled={!!savingKey} onClick={() => reviewRequest(request.requestId, "approve")}>
                            {savingKey === `approve:${request.requestId}` ? "Approving..." : "Approve"}
                          </button>
                          <button className="btn" type="button" disabled={!!savingKey} onClick={() => reviewRequest(request.requestId, "reject")}>
                            {savingKey === `reject:${request.requestId}` ? "Rejecting..." : "Reject"}
                          </button>
                        </div>
                      ) : (
                        <span className="subtle">No action</span>
                      )}
                    </td>
                  </tr>
                ))}
                {visibleRequests.length === 0 ? (
                  <tr>
                    <td colSpan={7} className="muted">No practice-space requests in this filter.</td>
                  </tr>
                ) : null}
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h2">Imported Inventory Review</div>
          <div className="subtle">Resolve mapping and policy gaps before normalizing more availability.</div>
        </div>
        <div className="card__body stack gap-3">
          <div className="row row--wrap gap-3">
            <label>
              Search
              <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Field, group, division, team..." />
            </label>
            <label>
              Policy
              <select value={policyFilter} onChange={(e) => setPolicyFilter(e.target.value)}>
                <option value="">All</option>
                <option value="auto_approve">Auto-approve</option>
                <option value="commissioner_review">Commissioner review</option>
                <option value="not_requestable">Not requestable</option>
              </select>
            </label>
            <label>
              Issues
              <select value={issueFilter} onChange={(e) => setIssueFilter(e.target.value)}>
                <option value="">All</option>
                <option value="division_unmapped">Division unmapped</option>
                <option value="team_unmapped">Team unmapped</option>
                <option value="policy_unmapped">Policy unmapped</option>
                <option value="field_unmapped">Field unmapped</option>
              </select>
            </label>
            <label>
              Sort
              <select value={sortBy} onChange={(e) => setSortBy(e.target.value)}>
                <option value="date">Date</option>
                <option value="field">Field</option>
                <option value="policy">Policy</option>
              </select>
            </label>
          </div>

          <div className="overflow-x-auto">
            <table className="table">
              <thead>
                <tr>
                  <th>When</th>
                  <th>Field</th>
                  <th>Imported assignment</th>
                  <th>Canonical mapping</th>
                  <th>Policy</th>
                  <th>Capacity</th>
                  <th>Issues</th>
                </tr>
              </thead>
              <tbody>
                {filteredRows.map((row) => {
                  const divisionCode = divisionDrafts[row.recordId] || row.canonicalDivisionCode || "";
                  const teamOptions = (data?.canonicalTeams || []).filter((team) => !divisionCode || team.divisionCode === divisionCode);
                  return (
                    <tr key={row.recordId}>
                      <td>
                        <div>{row.date}</div>
                        <div className="subtle">{row.dayOfWeek} {row.startTime}-{row.endTime}</div>
                      </td>
                      <td>
                        <div className="font-bold">{row.fieldName || row.rawFieldName}</div>
                        <div className="subtle">{row.availabilityStatus} / {row.utilizationStatus}</div>
                      </td>
                      <td>
                        <div>Group: {row.assignedGroup || "-"}</div>
                        <div>Division: {row.rawAssignedDivision || "-"}</div>
                        <div>Team/Event: {row.rawAssignedTeamOrEvent || "-"}</div>
                      </td>
                      <td className="stack gap-2">
                        <div className="row row--wrap gap-2">
                          <label>
                            Division
                            <select value={divisionCode} onChange={(e) => setDivisionDrafts((prev) => ({ ...prev, [row.recordId]: e.target.value }))}>
                              <option value="">Unmapped</option>
                              {(data?.canonicalDivisions || []).map((division) => (
                                <option key={division.code} value={division.code}>{division.name} ({division.code})</option>
                              ))}
                            </select>
                          </label>
                          {row.rawAssignedDivision ? (
                            <button className="btn" type="button" disabled={!!savingKey} onClick={() => saveDivisionMapping(row)}>
                              {savingKey === `division:${row.recordId}` ? "Saving..." : "Save Division"}
                            </button>
                          ) : null}
                        </div>
                        <div className="row row--wrap gap-2">
                          <label>
                            Team
                            <select value={teamDrafts[row.recordId] || row.canonicalTeamId || ""} onChange={(e) => setTeamDrafts((prev) => ({ ...prev, [row.recordId]: e.target.value }))}>
                              <option value="">Unmapped</option>
                              {teamOptions.map((team) => (
                                <option key={`${team.divisionCode}|${team.teamId}`} value={team.teamId}>{team.teamName} ({team.teamId})</option>
                              ))}
                            </select>
                          </label>
                          {row.rawAssignedTeamOrEvent ? (
                            <button className="btn" type="button" disabled={!!savingKey || !divisionCode} onClick={() => saveTeamMapping(row)}>
                              {savingKey === `team:${row.recordId}` ? "Saving..." : "Save Team"}
                            </button>
                          ) : null}
                        </div>
                      </td>
                      <td className="stack gap-2">
                        <label>
                          Booking policy
                          <select value={policyDrafts[row.recordId] || row.bookingPolicy} onChange={(e) => setPolicyDrafts((prev) => ({ ...prev, [row.recordId]: e.target.value }))}>
                            <option value="auto_approve">Auto-approve</option>
                            <option value="commissioner_review">Commissioner review</option>
                            <option value="not_requestable">Not requestable</option>
                          </select>
                        </label>
                        {row.assignedGroup ? (
                          <button className="btn" type="button" disabled={!!savingKey} onClick={() => savePolicy(row)}>
                            {savingKey === `policy:${row.recordId}` ? "Saving..." : "Save Policy"}
                          </button>
                        ) : null}
                        <div className="subtle">{row.bookingPolicyReason}</div>
                      </td>
                      <td>
                        <div>{row.requestableBlockCount} blocks</div>
                        <div className="subtle">Approved {row.approvedTeamCount} / Pending {row.pendingTeamCount}</div>
                      </td>
                      <td>
                        {(row.mappingIssues || []).length ? (
                          <div className="row row--wrap gap-2">
                            {row.mappingIssues.map((issue) => (
                              <span key={issue} className="pill">{issue}</span>
                            ))}
                          </div>
                        ) : (
                          <span className="subtle">Aligned</span>
                        )}
                      </td>
                    </tr>
                  );
                })}
                {filteredRows.length === 0 ? (
                  <tr>
                    <td colSpan={7} className="muted">No imported inventory rows match this filter.</td>
                  </tr>
                ) : null}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  );
}
