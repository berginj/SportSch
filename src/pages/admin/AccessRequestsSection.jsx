import { useState, useCallback } from "react";
import LeaguePicker from "../../components/LeaguePicker";

const ROLE_OPTIONS = ["LeagueAdmin", "Coach", "Viewer"];

function requestSelectionKey(request) {
  const userId = String(request?.userId || "").trim();
  const leagueId = String(request?.leagueId || "").trim();
  if (!userId) return "";
  return leagueId ? `${leagueId}::${userId}` : userId;
}

export default function AccessRequestsSection({
  leagueId,
  setLeagueId,
  me,
  isGlobalAdmin,
  accessStatus,
  setAccessStatus,
  accessScope,
  setAccessScope,
  accessLeagueFilter,
  setAccessLeagueFilter,
  accessLeagueOptions,
  loading,
  err,
  sorted,
  load,
  loadMembershipsAndTeams,
  memLoading,
  approve,
  deny,
  bulkApproveRequests,
  bulkDenyRequests,
}) {
  const accessAll = accessScope === "all";

  // Bulk selection state
  const [selectedItems, setSelectedItems] = useState(new Set());
  const [bulkRole, setBulkRole] = useState("Coach");
  const [bulkProcessing, setBulkProcessing] = useState(false);

  // Toggle single item selection
  const toggleSelection = useCallback((selectionKey) => {
    if (!selectionKey) return;
    setSelectedItems(prev => {
      const next = new Set(prev);
      if (next.has(selectionKey)) {
        next.delete(selectionKey);
      } else {
        next.add(selectionKey);
      }
      return next;
    });
  }, []);

  // Toggle all items
  const toggleSelectAll = useCallback(() => {
    if (selectedItems.size === sorted.length) {
      setSelectedItems(new Set());
    } else {
      setSelectedItems(new Set(sorted.map((r) => requestSelectionKey(r)).filter(Boolean)));
    }
  }, [selectedItems.size, sorted]);

  // Bulk approve
  const bulkApprove = useCallback(async () => {
    if (selectedItems.size === 0) return;

    setBulkProcessing(true);
    try {
      const itemsToApprove = sorted.filter((r) => selectedItems.has(requestSelectionKey(r)));
      let completed = true;

      if (typeof bulkApproveRequests === "function") {
        completed = await bulkApproveRequests(itemsToApprove, bulkRole, true);
      } else {
        for (const item of itemsToApprove) {
          try {
            await approve(item, bulkRole, true); // Skip reload after each
          } catch (err) {
            console.error('Failed to approve:', requestSelectionKey(item), err);
          }
        }
      }

      if (!completed) return;
      setSelectedItems(new Set());
      await load(); // Reload once at the end
    } finally {
      setBulkProcessing(false);
    }
  }, [selectedItems, sorted, bulkRole, bulkApproveRequests, approve, load]);

  // Bulk deny
  const bulkDeny = useCallback(async () => {
    if (selectedItems.size === 0) return;

    setBulkProcessing(true);
    try {
      const itemsToDeny = sorted.filter((r) => selectedItems.has(requestSelectionKey(r)));
      let completed = true;

      if (typeof bulkDenyRequests === "function") {
        completed = await bulkDenyRequests(itemsToDeny, true);
      } else {
        for (const item of itemsToDeny) {
          try {
            await deny(item, true); // Skip reload after each
          } catch (err) {
            console.error('Failed to deny:', requestSelectionKey(item), err);
          }
        }
      }

      if (!completed) return;
      setSelectedItems(new Set());
      await load(); // Reload once at the end
    } finally {
      setBulkProcessing(false);
    }
  }, [selectedItems, sorted, bulkDenyRequests, deny, load]);

  return (
    <div className="card">
      <div className="card__header">
        <div className="h2">Admin: access requests</div>
        <div className="subtle">
          {accessAll
            ? "Approve or deny access requests across all leagues."
            : "Approve or deny access requests for the currently selected league."}
        </div>
      </div>
      <div className="row gap-3 row--wrap mb-2">
        <LeaguePicker leagueId={leagueId} setLeagueId={setLeagueId} me={me} label="League" />
        {isGlobalAdmin ? (
          <label className="min-w-[160px]">
            Scope
            <select value={accessScope} onChange={(e) => setAccessScope(e.target.value)}>
              <option value="league">Current league</option>
              <option value="all">All leagues</option>
            </select>
          </label>
        ) : null}
        {isGlobalAdmin && accessAll ? (
          <label className="min-w-[180px]">
            League filter
            <select value={accessLeagueFilter} onChange={(e) => setAccessLeagueFilter(e.target.value)}>
              <option value="">All leagues</option>
              {accessLeagueOptions.map((l) => (
                <option key={l.leagueId} value={l.leagueId}>
                  {l.name ? `${l.name} (${l.leagueId})` : l.leagueId}
                </option>
              ))}
            </select>
          </label>
        ) : null}
        <label className="min-w-[160px]">
          Status
          <select value={accessStatus} onChange={(e) => setAccessStatus(e.target.value)}>
            <option value="Pending">Pending</option>
            <option value="Approved">Approved</option>
            <option value="Denied">Denied</option>
          </select>
        </label>
      </div>

      <div className="row gap-3 row--wrap">
        <button className="btn" onClick={load} disabled={loading} title="Refresh access requests.">
          Refresh
        </button>
        <button className="btn" onClick={loadMembershipsAndTeams} disabled={memLoading} title="Refresh memberships and teams.">
          Refresh members/teams
        </button>
      </div>

      {err && <div className="callout callout--error">{err}</div>}

      {/* Bulk Action Toolbar */}
      {selectedItems.size > 0 && accessStatus === 'Pending' && (
        <div className="callout callout--info mb-4">
          <div className="row row--between row--wrap gap-4">
            <div className="row row--wrap gap-3">
              <span className="font-semibold">
                {selectedItems.size} {selectedItems.size === 1 ? 'request' : 'requests'} selected
              </span>
              <label className="inlineCheck inlineCheck--compact">
                <span className="subtle">Assign role:</span>
                <select
                  value={bulkRole}
                  onChange={(e) => setBulkRole(e.target.value)}
                  disabled={bulkProcessing}
                >
                  {ROLE_OPTIONS.map((role) => (
                    <option key={role} value={role}>{role}</option>
                  ))}
                </select>
              </label>
            </div>
            <div className="row gap-2 row--wrap">
              <button
                className="btn btn--primary btn--sm"
                onClick={bulkApprove}
                disabled={bulkProcessing}
              >
                {bulkProcessing ? 'Processing...' : `Approve ${selectedItems.size}`}
              </button>
              <button
                className="btn btn--sm"
                onClick={bulkDeny}
                disabled={bulkProcessing}
              >
                {bulkProcessing ? 'Processing...' : `Deny ${selectedItems.size}`}
              </button>
              <button
                className="btn btn--sm btn--ghost"
                onClick={() => setSelectedItems(new Set())}
                disabled={bulkProcessing}
              >
                Clear
              </button>
            </div>
          </div>
        </div>
      )}

      {loading ? (
        <div className="muted">Loading...</div>
      ) : sorted.length === 0 ? (
        <div className="muted">No {accessStatus.toLowerCase()} requests.</div>
      ) : (
        <div className="tableWrap">
          <table className="table">
            <thead>
              <tr>
                {accessStatus === 'Pending' && (
                  <th className="w-12">
                    <input
                      type="checkbox"
                      checked={selectedItems.size === sorted.length && sorted.length > 0}
                      onChange={toggleSelectAll}
                      aria-label="Select all"
                    />
                  </th>
                )}
                {accessAll ? <th>League</th> : null}
                <th>User</th>
                <th>Requested role</th>
                <th>Notes</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {sorted.map((r) => {
                const selectionKey = requestSelectionKey(r);
                return (
                <tr key={selectionKey || r.userId} className={selectedItems.has(selectionKey) ? 'tableRow--selected' : ''}>
                  {accessStatus === 'Pending' && (
                    <td>
                      <input
                        type="checkbox"
                        checked={selectedItems.has(selectionKey)}
                        onChange={() => toggleSelection(selectionKey)}
                        aria-label={`Select ${r.email || r.userId}${r.leagueId ? ` in ${r.leagueId}` : ''}`}
                      />
                    </td>
                  )}
                  {accessAll ? (
                    <td>
                      <code>{r.leagueId}</code>
                    </td>
                  ) : null}
                  <td>
                    <div className="font-semibold">{r.email || r.userId}</div>
                    <div className="muted text-xs">{r.userId}</div>
                  </td>
                  <td>
                    <select
                      defaultValue={r.requestedRole || "Viewer"}
                      onChange={(e) => approve(r, e.target.value)}
                      title="Pick a role to approve"
                      disabled={accessStatus !== 'Pending'}
                    >
                      {ROLE_OPTIONS.map((x) => (
                        <option key={x} value={x}>
                          {x}
                        </option>
                      ))}
                    </select>
                    <div className="muted text-xs">{r.updatedUtc || r.createdUtc || ""}</div>
                  </td>
                  <td className="max-w-[320px]">
                    <div className="whitespace-pre-wrap">{r.notes || ""}</div>
                  </td>
                  <td>
                    {accessStatus === 'Pending' ? (
                      <div className="row gap-2 row--wrap">
                        <button className="btn btn--primary btn--sm" onClick={() => approve(r)}>
                          Approve
                        </button>
                        <button className="btn btn--sm" onClick={() => deny(r)}>
                          Deny
                        </button>
                      </div>
                    ) : (
                      <span className="statusBadge">{accessStatus}</span>
                    )}
                  </td>
                </tr>
              )})}
            </tbody>
          </table>
        </div>
      )}

      <details className="mt-4">
        <summary>Notes</summary>
        <ul>
          <li>Approving creates (or updates) a membership record for this league and marks the request Approved.</li>
          <li>Deny marks the request Denied.</li>
        </ul>
      </details>
    </div>
  );
}
