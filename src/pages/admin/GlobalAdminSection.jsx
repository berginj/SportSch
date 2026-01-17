const ROLE_OPTIONS = ["LeagueAdmin", "Coach", "Viewer"];

export default function GlobalAdminSection({
  globalErr,
  globalOk,
  newLeague,
  setNewLeague,
  createLeague,
  globalLoading,
  loadGlobalLeagues,
  globalLeagues,
  deleteLeague,
  seasonLeagueId,
  setSeasonLeagueId,
  seasonDraft,
  setSeasonDraft,
  blackoutsDraft,
  setBlackoutsDraft,
  saveSeasonConfig,
  applySeasonFromLeague,
  userSearch,
  setUserSearch,
  loadUsers,
  usersLoading,
  userDraft,
  setUserDraft,
  saveUser,
  users,
  memberSearch,
  setMemberSearch,
  memberLeague,
  setMemberLeague,
  memberRole,
  setMemberRole,
  loadAllMemberships,
  membersLoadingAll,
  membersAll,
}) {
  return (
    <div className="card mt-4">
      <h3 className="m-0">Global admin: leagues</h3>
      <p className="muted">
        Create new leagues and review existing ones. This is global admin only.
      </p>
      {globalErr ? <div className="callout callout--error">{globalErr}</div> : null}
      {globalOk ? <div className="callout callout--ok">{globalOk}</div> : null}

      <div className="row gap-3 row--wrap mb-3">
        <label className="flex-1 min-w-[160px]">
          League ID
          <input
            value={newLeague.leagueId}
            onChange={(e) => setNewLeague((p) => ({ ...p, leagueId: e.target.value }))}
            placeholder="ARL"
          />
        </label>
        <label className="flex-[2] min-w-[220px]">
          League name
          <input
            value={newLeague.name}
            onChange={(e) => setNewLeague((p) => ({ ...p, name: e.target.value }))}
            placeholder="Arlington"
          />
        </label>
        <button className="btn btn--primary" onClick={createLeague} disabled={globalLoading}>
          {globalLoading ? "Saving..." : "Create league"}
        </button>
        <button className="btn" onClick={loadGlobalLeagues} disabled={globalLoading}>
          Refresh leagues
        </button>
      </div>

      {globalLoading ? (
        <div className="muted">Loading...</div>
      ) : globalLeagues.length === 0 ? (
        <div className="muted">No leagues yet.</div>
      ) : (
        <div className="tableWrap">
          <table className="table">
            <thead>
              <tr>
                <th>League ID</th>
                <th>Name</th>
                <th>Timezone</th>
                <th>Status</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {globalLeagues.map((l) => (
                <tr key={l.leagueId}>
                  <td><code>{l.leagueId}</code></td>
                  <td>{l.name}</td>
                  <td>{l.timezone}</td>
                  <td>{l.status}</td>
                  <td className="text-right">
                    <button className="btn btn--ghost" onClick={() => deleteLeague(l)} disabled={globalLoading}>
                      Delete
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div className="card mt-4">
        <h4 className="m-0">Season settings</h4>
        <p className="muted">Set league season dates, blackout windows, and default game length.</p>
        <div className="row gap-3 row--wrap mb-3">
          <label className="min-w-[220px]">
            League
            <select value={seasonLeagueId} onChange={(e) => setSeasonLeagueId(e.target.value)}>
              {globalLeagues.map((l) => (
                <option key={l.leagueId} value={l.leagueId}>
                  {l.name ? `${l.name} (${l.leagueId})` : l.leagueId}
                </option>
              ))}
            </select>
          </label>
          <label className="min-w-[180px]">
            Game length (minutes)
            <input
              type="number"
              min="1"
              value={seasonDraft.gameLengthMinutes}
              onChange={(e) => setSeasonDraft((p) => ({ ...p, gameLengthMinutes: e.target.value }))}
            />
          </label>
        </div>
        <div className="grid2 mb-3">
          <label>
            Spring start
            <input
              value={seasonDraft.springStart}
              onChange={(e) => setSeasonDraft((p) => ({ ...p, springStart: e.target.value }))}
              placeholder="YYYY-MM-DD"
            />
          </label>
          <label>
            Spring end
            <input
              value={seasonDraft.springEnd}
              onChange={(e) => setSeasonDraft((p) => ({ ...p, springEnd: e.target.value }))}
              placeholder="YYYY-MM-DD"
            />
          </label>
          <label>
            Fall start
            <input
              value={seasonDraft.fallStart}
              onChange={(e) => setSeasonDraft((p) => ({ ...p, fallStart: e.target.value }))}
              placeholder="YYYY-MM-DD"
            />
          </label>
          <label>
            Fall end
            <input
              value={seasonDraft.fallEnd}
              onChange={(e) => setSeasonDraft((p) => ({ ...p, fallEnd: e.target.value }))}
              placeholder="YYYY-MM-DD"
            />
          </label>
        </div>

        <div className="mb-3">
          <div className="font-bold mb-2">Blackout windows</div>
          {blackoutsDraft.length === 0 ? <div className="muted">No blackouts yet.</div> : null}
          {blackoutsDraft.map((b, idx) => (
            <div key={`${b.startDate}-${b.endDate}-${idx}`} className="row gap-2 row--wrap mb-2">
              <input
                className="min-w-[160px]"
                value={b.startDate}
                onChange={(e) => {
                  const next = [...blackoutsDraft];
                  next[idx] = { ...next[idx], startDate: e.target.value };
                  setBlackoutsDraft(next);
                }}
                placeholder="Start (YYYY-MM-DD)"
              />
              <input
                className="min-w-[160px]"
                value={b.endDate}
                onChange={(e) => {
                  const next = [...blackoutsDraft];
                  next[idx] = { ...next[idx], endDate: e.target.value };
                  setBlackoutsDraft(next);
                }}
                placeholder="End (YYYY-MM-DD)"
              />
              <input
                className="min-w-[220px]"
                value={b.label}
                onChange={(e) => {
                  const next = [...blackoutsDraft];
                  next[idx] = { ...next[idx], label: e.target.value };
                  setBlackoutsDraft(next);
                }}
                placeholder="Label (optional)"
              />
              <button
                className="btn"
                type="button"
                onClick={() => setBlackoutsDraft((prev) => prev.filter((_, i) => i !== idx))}
              >
                Remove
              </button>
            </div>
          ))}
          <button
            className="btn btn--ghost"
            type="button"
            onClick={() => setBlackoutsDraft((prev) => [...prev, { startDate: "", endDate: "", label: "" }])}
          >
            Add blackout
          </button>
        </div>

        <div className="row gap-2">
          <button className="btn btn--primary" onClick={saveSeasonConfig} disabled={globalLoading}>
            Save season settings
          </button>
          <button
            className="btn"
            onClick={() => {
              const league = globalLeagues.find((l) => l.leagueId === seasonLeagueId);
              if (league) applySeasonFromLeague(league);
            }}
            disabled={globalLoading}
          >
            Reset
          </button>
        </div>
      </div>

      <div className="card mt-4">
        <h4 className="m-0">User admin</h4>
        <p className="muted">Set a user's home league and home-league role.</p>
        <div className="row gap-3 row--wrap mb-3">
          <label className="min-w-[220px]">
            Search
            <input
              value={userSearch}
              onChange={(e) => setUserSearch(e.target.value)}
              placeholder="userId or email"
            />
          </label>
          <button className="btn" onClick={loadUsers} disabled={usersLoading}>
            {usersLoading ? "Loading..." : "Refresh"}
          </button>
        </div>

        <div className="grid2 mb-3">
          <label>
            User ID
            <input
              value={userDraft.userId}
              onChange={(e) => setUserDraft((p) => ({ ...p, userId: e.target.value }))}
              placeholder="aad|..."
            />
          </label>
          <label>
            Email
            <input
              value={userDraft.email}
              onChange={(e) => setUserDraft((p) => ({ ...p, email: e.target.value }))}
              placeholder="name@domain.com"
            />
          </label>
          <label>
            Home league
            <select
              value={userDraft.homeLeagueId}
              onChange={(e) => setUserDraft((p) => ({ ...p, homeLeagueId: e.target.value }))}
            >
              <option value="">(none)</option>
              {globalLeagues.map((l) => (
                <option key={l.leagueId} value={l.leagueId}>
                  {l.name ? `${l.name} (${l.leagueId})` : l.leagueId}
                </option>
              ))}
            </select>
          </label>
          <label>
            Home role
            <select
              value={userDraft.role}
              onChange={(e) => setUserDraft((p) => ({ ...p, role: e.target.value }))}
            >
              <option value="">(leave unchanged)</option>
              {ROLE_OPTIONS.map((role) => (
                <option key={role} value={role}>{role}</option>
              ))}
            </select>
          </label>
        </div>
        <div className="row gap-2">
          <button className="btn btn--primary" onClick={saveUser}>
            Save user
          </button>
          <button className="btn" onClick={() => setUserDraft({ userId: "", email: "", homeLeagueId: "", role: "" })}>
            Clear
          </button>
        </div>

        {users.length === 0 ? (
          <div className="muted mt-3">No user profiles yet.</div>
        ) : (
          <div className="tableWrap mt-3">
            <table className="table">
              <thead>
                <tr>
                  <th>User</th>
                  <th>Email</th>
                  <th>Home league</th>
                  <th>Home role</th>
                </tr>
              </thead>
              <tbody>
                {users.map((u) => (
                  <tr key={u.userId}>
                    <td><code>{u.userId}</code></td>
                    <td>{u.email || ""}</td>
                    <td>{u.homeLeagueId || ""}</td>
                    <td>{u.homeLeagueRole || ""}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div className="card mt-4">
        <h4 className="m-0">Memberships (all leagues)</h4>
        <p className="muted">Review all memberships across leagues. Use filters to narrow results.</p>
        <div className="row gap-3 row--wrap mb-3">
          <label className="min-w-[200px]">
            Search
            <input
              value={memberSearch}
              onChange={(e) => setMemberSearch(e.target.value)}
              placeholder="userId or email"
            />
          </label>
          <label className="min-w-[180px]">
            League
            <select value={memberLeague} onChange={(e) => setMemberLeague(e.target.value)}>
              <option value="">All leagues</option>
              {globalLeagues.map((l) => (
                <option key={l.leagueId} value={l.leagueId}>
                  {l.name ? `${l.name} (${l.leagueId})` : l.leagueId}
                </option>
              ))}
            </select>
          </label>
          <label className="min-w-[160px]">
            Role
            <select value={memberRole} onChange={(e) => setMemberRole(e.target.value)}>
              <option value="">All roles</option>
              {ROLE_OPTIONS.map((role) => (
                <option key={role} value={role}>{role}</option>
              ))}
            </select>
          </label>
          <button className="btn" onClick={loadAllMemberships} disabled={membersLoadingAll}>
            {membersLoadingAll ? "Loading..." : "Refresh"}
          </button>
        </div>

        {membersAll.length === 0 ? (
          <div className="muted">No memberships found.</div>
        ) : (
          <div className="tableWrap">
            <table className="table">
              <thead>
                <tr>
                  <th>User</th>
                  <th>Email</th>
                  <th>League</th>
                  <th>Role</th>
                  <th>Division</th>
                  <th>Team</th>
                </tr>
              </thead>
              <tbody>
                {membersAll.map((m) => (
                  <tr key={`${m.userId}-${m.leagueId}-${m.role}`}>
                    <td><code>{m.userId}</code></td>
                    <td>{m.email || ""}</td>
                    <td>{m.leagueId || ""}</td>
                    <td>{m.role || ""}</td>
                    <td>{m.team?.division || ""}</td>
                    <td>{m.team?.teamId || ""}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
