import SeasonWizard from "./SeasonWizard";

export default function CommissionerHub({ leagueId, tableView = "A" }) {
  return (
    <div className="stack gap-4">
      <div className="card">
        <div className="card__header">
          <div className="h3">Season setup wizard</div>
          <div className="subtle">Plan backwards from pool play and bracket, then schedule regular season games.</div>
        </div>
        <div className="card__body">
          <SeasonWizard leagueId={leagueId} tableView={tableView} />
        </div>
      </div>
    </div>
  );
}
