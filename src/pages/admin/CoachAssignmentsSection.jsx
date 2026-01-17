export default function CoachAssignmentsSection({
  memLoading,
  coaches,
  divisions,
  teamsByDivision,
  coachDraft,
  setDraftForCoach,
  saveCoachAssignment,
  clearCoachAssignment,
}) {
  return (
    <div className="card">
      <h3 className="m-0">Coach assignments</h3>
      <p className="muted">
        Coaches can be approved without a team. Assign teams here when you're ready.
      </p>

      {memLoading ? (
        <div className="muted">Loading memberships...</div>
      ) : coaches.length === 0 ? (
        <div className="muted">No coaches in this league yet.</div>
      ) : (
        <div className="tableWrap">
          <table className="table">
            <thead>
              <tr>
                <th>Coach</th>
                <th>Division</th>
                <th>Team</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              {coaches.map((c) => {
                const draft = coachDraft[c.userId] || { division: c.team?.division || "", teamId: c.team?.teamId || "" };
                const currentDiv = draft.division || "";
                const currentTeam = draft.teamId || "";
                const divOptions = (divisions || [])
                  .map((d) => (typeof d === "string" ? d : d.code || d.division || ""))
                  .filter(Boolean);
                const teamsForDiv = currentDiv ? (teamsByDivision.get(currentDiv) || []) : [];

                return (
                  <tr key={c.userId}>
                    <td>
                      <div className="font-semibold">{c.email || c.userId}</div>
                      <div className="muted text-xs">{c.userId}</div>
                    </td>
                    <td>
                      <select
                        value={currentDiv}
                        onChange={(e) => {
                          const v = e.target.value;
                          setDraftForCoach(c.userId, { division: v, teamId: "" });
                        }}
                        title="Set division (clears team until you select one)"
                      >
                        <option value="">(unassigned)</option>
                        {divOptions.map((d) => (
                          <option key={d} value={d}>{d}</option>
                        ))}
                      </select>
                    </td>
                    <td>
                      <select
                        value={currentTeam}
                        onChange={(e) => {
                          const v = e.target.value;
                          setDraftForCoach(c.userId, { division: currentDiv, teamId: v });
                        }}
                        disabled={!currentDiv}
                        title={!currentDiv ? "Pick a division first" : "Pick a team"}
                      >
                        <option value="">(unassigned)</option>
                        {teamsForDiv.map((t) => (
                          <option key={t.teamId} value={t.teamId}>
                            {t.name || t.teamId}
                          </option>
                        ))}
                      </select>
                    </td>
                    <td>
                      <div className="row gap-2 row--wrap">
                        <button className="btn btn--primary" onClick={() => saveCoachAssignment(c.userId)}>
                          Save
                        </button>
                        <button
                          className="btn"
                          onClick={() => clearCoachAssignment(c.userId)}
                        >
                          Clear
                        </button>
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
