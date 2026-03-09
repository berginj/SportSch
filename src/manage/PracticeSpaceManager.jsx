import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import Toast from "../components/Toast";

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

export default function PracticeSpaceManager({ leagueId }) {
  const [data, setData] = useState(null);
  const [seasonLabel, setSeasonLabel] = useState("");
  const [loading, setLoading] = useState(true);
  const [savingKey, setSavingKey] = useState("");
  const [error, setError] = useState("");
  const [toast, setToast] = useState(null);
  const [search, setSearch] = useState("");
  const [policyFilter, setPolicyFilter] = useState("");
  const [issueFilter, setIssueFilter] = useState("");
  const [sortBy, setSortBy] = useState("date");
  const [requestStatusFilter, setRequestStatusFilter] = useState("Pending");
  const [divisionDrafts, setDivisionDrafts] = useState({});
  const [teamDrafts, setTeamDrafts] = useState({});
  const [policyDrafts, setPolicyDrafts] = useState({});

  async function load(nextSeasonLabel = seasonLabel) {
    if (!leagueId) return;
    setLoading(true);
    setError("");
    try {
      const query = nextSeasonLabel ? `?seasonLabel=${encodeURIComponent(nextSeasonLabel)}` : "";
      const result = await apiFetch(`/api/field-inventory/practice/admin${query}`);
      setData(result);
      setSeasonLabel(result?.seasonLabel || nextSeasonLabel || "");
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
      setData(result);
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
      setData(result);
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
      setData(result);
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
      setData(result);
      setToast({ tone: "success", message: action === "approve" ? "Practice request approved." : "Practice request rejected." });
    } catch (e) {
      setError(e.message || `Failed to ${action} practice request.`);
    } finally {
      setSavingKey("");
    }
  }

  if (loading && !data) {
    return <div className="muted">Loading practice space review...</div>;
  }

  return (
    <div className="stack gap-4">
      {toast ? <Toast tone={toast.tone} message={toast.message} onClose={() => setToast(null)} /> : null}
      {error ? <div className="callout callout--error">{error}</div> : null}

      <div className="callout">
        <div className="font-bold mb-2">What this replaces</div>
        <div className="subtle">
          This is the new admin control surface for imported practice space. Review canonical field/division/team alignment, set booking policy, and approve commissioner-reviewed requests from one place.
        </div>
      </div>

      <div className="card">
        <div className="card__header">
          <div>
            <div className="h2">Practice Space Admin</div>
            <div className="subtle">Committed field inventory normalized into actionable practice space.</div>
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
            <div className="pill">Rows: {data?.summary?.totalRecords || 0}</div>
            <div className="pill">Requestable 90m blocks: {data?.summary?.requestableBlocks || 0}</div>
            <div className="pill">Auto-approve: {data?.summary?.autoApproveBlocks || 0}</div>
            <div className="pill">Commissioner review: {data?.summary?.commissionerReviewBlocks || 0}</div>
            <div className="pill">Pending requests: {data?.summary?.pendingRequests || 0}</div>
            <div className="pill">Unmapped divisions: {data?.summary?.unmappedDivisions || 0}</div>
            <div className="pill">Unmapped teams: {data?.summary?.unmappedTeams || 0}</div>
            <div className="pill">Policy gaps: {data?.summary?.unmappedPolicies || 0}</div>
          </div>
        </div>
      </div>

      <div className="card">
        <div className="card__header">
          <div className="h2">Request Queue</div>
          <div className="subtle">Commissioner-reviewed requests stay here until approved or rejected.</div>
        </div>
        <div className="card__body">
          <div className="row row--wrap gap-3 mb-3">
            <label title="Filter the queue by request status.">
              Status
              <select value={requestStatusFilter} onChange={(e) => setRequestStatusFilter(e.target.value)}>
                <option value="">All</option>
                <option value="Pending">Pending</option>
                <option value="Approved">Approved</option>
                <option value="Rejected">Rejected</option>
                <option value="Cancelled">Cancelled</option>
              </select>
            </label>
          </div>
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
                    <td><span className="pill">{request.status}</span></td>
                    <td>{request.bookingPolicyLabel}</td>
                    <td>{request.notes || request.reviewReason || ""}</td>
                    <td>
                      <div className="row gap-2">
                        {request.status === "Pending" ? (
                          <>
                            <button className="btn btn--primary" type="button" disabled={!!savingKey} onClick={() => reviewRequest(request.requestId, "approve")}>
                              {savingKey === `approve:${request.requestId}` ? "Approving..." : "Approve"}
                            </button>
                            <button className="btn" type="button" disabled={!!savingKey} onClick={() => reviewRequest(request.requestId, "reject")}>
                              {savingKey === `reject:${request.requestId}` ? "Rejecting..." : "Reject"}
                            </button>
                          </>
                        ) : (
                          <span className="subtle">No action</span>
                        )}
                      </div>
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
          <div className="subtle">Align imported text to canonical divisions and teams, then set reusable booking rules for requestable groups.</div>
        </div>
        <div className="card__body">
          <div className="row row--wrap gap-3 mb-3">
            <label title="Search fields, groups, raw assigned values, and mapped values.">
              Search
              <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Field, group, division, team..." />
            </label>
            <label title="Focus on one booking policy at a time.">
              Policy
              <select value={policyFilter} onChange={(e) => setPolicyFilter(e.target.value)}>
                <option value="">All</option>
                <option value="auto_approve">Auto-approve</option>
                <option value="commissioner_review">Commissioner review</option>
                <option value="not_requestable">Not requestable</option>
              </select>
            </label>
            <label title="Only show unresolved mapping or policy problems when needed.">
              Issues
              <select value={issueFilter} onChange={(e) => setIssueFilter(e.target.value)}>
                <option value="">All</option>
                <option value="division_unmapped">Division unmapped</option>
                <option value="team_unmapped">Team unmapped</option>
                <option value="policy_unmapped">Policy unmapped</option>
                <option value="field_unmapped">Field unmapped</option>
              </select>
            </label>
            <label title="Sort the review grid by date, field, or policy.">
              Sort by
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
                      <td>
                        <div className="row row--wrap gap-2">
                          <label title="Map imported division text to a canonical SportsCH division.">
                            <span className="row gap-1 items-center">Division <span className="hint" title="Save a reusable mapping when the workbook uses shorthand or non-canonical naming.">?</span></span>
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
                        <div className="row row--wrap gap-2 mt-2">
                          <label title="Map imported team text to a canonical SportsCH team.">
                            <span className="row gap-1 items-center">Team <span className="hint" title="Team mapping stays optional when the imported row is describing a clinic, event, or generic group use.">?</span></span>
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
                      <td>
                        <div className="row row--wrap gap-2">
                          <label title="Set how coaches can use this imported space.">
                            <span className="row gap-1 items-center">Booking policy <span className="hint" title="Ponytail space can auto-approve. Unassigned available space should normally require commissioner review.">?</span></span>
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
                        </div>
                        <div className="subtle mt-2">{row.bookingPolicyReason}</div>
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
