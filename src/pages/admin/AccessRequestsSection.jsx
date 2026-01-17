import LeaguePicker from "../../components/LeaguePicker";

const ROLE_OPTIONS = ["LeagueAdmin", "Coach", "Viewer"];

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
}) {
  const accessAll = accessScope === "all";

  return (
    <div className="card">
      <h2>Admin: access requests</h2>
      <p className="muted">
        {accessAll
          ? "Approve or deny access requests across all leagues."
          : "Approve or deny access requests for the currently selected league."}
      </p>
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

      {err && <div className="error">{err}</div>}
      {loading ? (
        <div className="muted">Loading...</div>
      ) : sorted.length === 0 ? (
        <div className="muted">No {accessStatus.toLowerCase()} requests.</div>
      ) : (
        <div className="tableWrap">
          <table className="table">
            <thead>
              <tr>
                {accessAll ? <th>League</th> : null}
                <th>User</th>
                <th>Requested role</th>
                <th>Notes</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {sorted.map((r) => (
                <tr key={r.userId}>
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
                    <div className="row gap-2 row--wrap">
                      <button className="btn btn--primary" onClick={() => approve(r)}>
                        Approve
                      </button>
                      <button className="btn" onClick={() => deny(r)}>
                        Deny
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
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
