import { useEffect, useState, useCallback } from 'react';
import { apiFetch } from '../lib/api';
import StatusCard from '../components/StatusCard';

function isGameSlot(slot) {
  if (!slot || slot.isAvailability) return false;
  const gameType = String(slot.gameType || '').trim().toLowerCase();
  return gameType !== 'practice';
}

function isOpenOfferSlot(slot) {
  if (!isGameSlot(slot)) return false;
  if (String(slot.status || '').trim() !== 'Open') return false;
  return String(slot.gameType || '').trim().toLowerCase() !== 'request';
}

/**
 * Coach Dashboard - Personalized home page for coaches
 * Shows:
 * - Team summary
 * - Upcoming games
 * - Action items (new offers, pending requests)
 * - Quick actions
 */
export default function CoachDashboard({ me, leagueId, setTab }) {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [dashboard, setDashboard] = useState(null);

  // Get coach's team assignment
  const membership = (me?.memberships || []).find(m => m.leagueId === leagueId && m.role === 'Coach');
  const team = membership?.team;
  const division = team?.division;
  const teamId = team?.teamId;

  const loadDashboard = useCallback(async () => {
    if (!leagueId) {
      setError('No league selected');
      setLoading(false);
      return;
    }

    setLoading(true);
    setError('');

    try {
      // For now, fetch data from existing endpoints
      // TODO: Create dedicated /coach/dashboard endpoint for better performance
      const [slots, teams] = await Promise.all([
        apiFetch(`/api/slots?dateFrom=${getTodayDate()}&dateTo=${getDateInDays(30)}`).catch(() => []),
        division ? apiFetch(`/api/teams?division=${division}`).catch(() => []) : Promise.resolve([])
      ]);

      // Filter slots for team's upcoming games (confirmed and where team is playing)
      const upcomingGames = (Array.isArray(slots) ? slots : [])
        .filter(slot => {
          if (!isGameSlot(slot)) return false;
          if (slot.status !== 'Confirmed') return false;
          if (!teamId) return false;
          return slot.offeringTeamId === teamId || slot.confirmedTeamId === teamId ||
                 slot.homeTeamId === teamId || slot.awayTeamId === teamId;
        })
        .sort((a, b) => {
          const dateA = `${a.gameDate} ${a.startTime}`;
          const dateB = `${b.gameDate} ${b.startTime}`;
          return dateA.localeCompare(dateB);
        })
        .slice(0, 5); // Next 5 games

      // Count open offers in division
      const openOffersInDivision = (Array.isArray(slots) ? slots : [])
        .filter(slot => {
          return isOpenOfferSlot(slot) &&
                 slot.division === division &&
                 slot.offeringTeamId !== teamId; // Not my own offers
        }).length;

      // Count my open offers
      const myOpenOffers = (Array.isArray(slots) ? slots : [])
        .filter(slot => {
          return isOpenOfferSlot(slot) && slot.offeringTeamId === teamId;
        }).length;

      // Find team details
      const teamDetails = (Array.isArray(teams) ? teams : [])
        .find(t => t.division === division && t.teamId === teamId);

      setDashboard({
        team: teamDetails || { teamId, division, name: teamId },
        upcomingGames,
        openOffersInDivision,
        myOpenOffers,
      });
    } catch (err) {
      setError(err.message || 'Failed to load dashboard');
    } finally {
      setLoading(false);
    }
  }, [leagueId, division, teamId]);

  useEffect(() => {
    loadDashboard();
  }, [loadDashboard]);

  if (loading) {
    return (
      <div className="page">
        <StatusCard title="Loading Dashboard" message="Loading your coach dashboard..." />
      </div>
    );
  }

  if (error) {
    return (
      <div className="page">
        <StatusCard title="Error" message={error} variant="error" />
        <button className="btn mt-3" onClick={loadDashboard}>
          Retry
        </button>
      </div>
    );
  }

  // No team assigned yet
  if (!team) {
    return (
      <div className="page">
        <div className="card">
          <div className="card__header">
            <div className="h1">Welcome, Coach</div>
            <div className="subtle">Your coach account is active and waiting on team assignment.</div>
          </div>
          <div className="callout callout--info mb-4">
            <div className="font-semibold">Team assignment needed</div>
            <div className="mt-2">
              You're registered as a coach, but you haven't been assigned to a team yet.
              Contact your league administrator to get assigned to your team.
            </div>
          </div>
          <QuickActionsPanel setTab={setTab} hasTeam={false} />
        </div>
      </div>
    );
  }

  return (
    <div className="page">
      <div className="card">
        <div className="card__header">
          <div className="h1">Coach Dashboard</div>
          <div className="subtle">Welcome back. Here is what is happening with your team.</div>
        </div>
        <div className="layoutStatRow">
          <div className="layoutStat">
            <div className="layoutStat__value">{dashboard.upcomingGames.length}</div>
            <div className="layoutStat__label">Upcoming games</div>
          </div>
          <div className="layoutStat">
            <div className="layoutStat__value">{dashboard.openOffersInDivision}</div>
            <div className="layoutStat__label">Open offers in division</div>
          </div>
          <div className="layoutStat">
            <div className="layoutStat__value">{dashboard.myOpenOffers}</div>
            <div className="layoutStat__label">Your open offers</div>
          </div>
          <div className="layoutStat">
            <div className="layoutStat__value">{dashboard.team?.division || '-'}</div>
            <div className="layoutStat__label">Division</div>
          </div>
        </div>
      </div>

      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        <TeamSummaryCard team={dashboard.team} />

        <ActionItemsCard
          openOffersInDivision={dashboard.openOffersInDivision}
          myOpenOffers={dashboard.myOpenOffers}
          upcomingGames={dashboard.upcomingGames}
          setTab={setTab}
        />

        <QuickActionsPanel setTab={setTab} hasTeam={true} />
      </div>

      {dashboard.upcomingGames.length > 0 && (
        <div className="card">
          <div className="card__header">
            <div className="h2">Upcoming Games</div>
          </div>
          <div className="grid gap-3">
            {dashboard.upcomingGames.map((game, idx) => (
              <GameCard key={game.slotId || idx} game={game} teamId={teamId} />
            ))}
          </div>
          <button className="btn btn--ghost" onClick={() => setTab('calendar')}>
            View Full Schedule
          </button>
        </div>
      )}

      {dashboard.upcomingGames.length === 0 && (
        <div className="card">
          <div className="card__header">
            <div className="h2">Upcoming Games</div>
          </div>
          <div className="callout callout--info">
            <div>No upcoming games scheduled in the next 30 days.</div>
            <div className="mt-2">
              Check the <button className="link" onClick={() => setTab('calendar')}>Calendar</button> for available game slots.
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function TeamSummaryCard({ team }) {
  return (
    <div className="card">
      <div className="card__header">
        <div className="h2">My Team</div>
        <span className="statusBadge">{team.division}</span>
      </div>
      <div className="h1 mb-1">
        {team.name || team.teamId}
      </div>
      {team.name && team.name !== team.teamId && (
        <div className="subtle">Team ID: {team.teamId}</div>
      )}
      {team.primaryContact && (
        <div className="callout mt-3">
          <div className="font-semibold">Primary Contact</div>
          {team.primaryContact.name && <div>{team.primaryContact.name}</div>}
          {team.primaryContact.email && (
            <a href={`mailto:${team.primaryContact.email}`} className="link">
              {team.primaryContact.email}
            </a>
          )}
          {team.primaryContact.phone && <div>{team.primaryContact.phone}</div>}
        </div>
      )}
    </div>
  );
}

function ActionItemsCard({ openOffersInDivision, myOpenOffers, upcomingGames, setTab }) {
  const hasActions = openOffersInDivision > 0 || myOpenOffers > 0 || upcomingGames.length > 0;

  return (
    <div className="card">
      <div className="card__header">
        <div className="h2">Action Items</div>
      </div>
      {!hasActions && (
        <div className="muted">No action items at this time.</div>
      )}
      {hasActions && (
        <div className="grid gap-3">
          {openOffersInDivision > 0 && (
            <div className="callout callout--ok row row--between row--wrap">
              <div>
                <div className="font-semibold">
                  {openOffersInDivision} New {openOffersInDivision === 1 ? 'Offer' : 'Offers'}
                </div>
                <div className="subtle">Available in your division</div>
              </div>
              <button
                className="btn btn--sm btn--primary"
                onClick={() => setTab('calendar')}
              >
                Review
              </button>
            </div>
          )}

          {myOpenOffers > 0 && (
            <div className="callout callout--info row row--between row--wrap">
              <div>
                <div className="font-semibold">
                  {myOpenOffers} Open {myOpenOffers === 1 ? 'Offer' : 'Offers'}
                </div>
                <div className="subtle">Your slots still available</div>
              </div>
              <button
                className="btn btn--sm btn--ghost"
                onClick={() => setTab('calendar')}
              >
                View
              </button>
            </div>
          )}

          {upcomingGames.length > 0 && upcomingGames[0] && isWithinDays(upcomingGames[0].gameDate, 1) && (
            <div className="callout callout--warning row row--between row--wrap">
              <div>
                <div className="font-semibold">Game Tomorrow</div>
                <div className="subtle">
                  {upcomingGames[0].startTime} at {upcomingGames[0].displayName || upcomingGames[0].fieldKey}
                </div>
              </div>
              <button
                className="btn btn--sm btn--ghost"
                onClick={() => setTab('calendar')}
              >
                Details
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

function QuickActionsPanel({ setTab, hasTeam }) {
  return (
    <div className="card">
      <div className="card__header">
        <div className="h2">Quick Actions</div>
      </div>
      <div className="grid gap-2">
        {hasTeam && (
          <button
            className="btn btn--primary w-full justify-center"
            onClick={() => setTab('calendar')}
            title="Offer a game slot to other teams"
          >
            Offer a Game Slot
          </button>
        )}
        <button
          className="btn w-full justify-center"
          onClick={() => setTab('calendar')}
          title="Browse available game slots"
        >
          Browse Available Slots
        </button>
        <button
          className="btn w-full justify-center"
          onClick={() => setTab('calendar')}
          title="View your team's schedule"
        >
          View Team Schedule
        </button>
      </div>
    </div>
  );
}

function GameCard({ game, teamId }) {
  const isHome = game.homeTeamId === teamId;
  const isAway = game.awayTeamId === teamId;
  const opponent = isHome ? game.awayTeamId : game.homeTeamId;
  const vsText = opponent ? (isHome ? `vs ${opponent}` : `@ ${opponent}`) : 'TBD';

  return (
    <div className="layoutPanel row row--between row--wrap">
      <div className="min-w-0">
        <div className="font-semibold">
          {formatGameDate(game.gameDate)} at {formatTime(game.startTime)}
        </div>
        <div className="subtle">
          {vsText} - {game.displayName || game.fieldKey}
        </div>
      </div>
      {isHome ? <span className="statusBadge status-confirmed">Home</span> : null}
      {isAway ? <span className="statusBadge">Away</span> : null}
    </div>
  );
}

// Helper functions
function getTodayDate() {
  const now = new Date();
  return now.toISOString().split('T')[0];
}

function getDateInDays(days) {
  const date = new Date();
  date.setDate(date.getDate() + days);
  return date.toISOString().split('T')[0];
}

function isWithinDays(dateStr, days) {
  const gameDate = new Date(dateStr);
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const diffTime = gameDate - today;
  const diffDays = Math.ceil(diffTime / (1000 * 60 * 60 * 24));
  return diffDays <= days && diffDays >= 0;
}

function formatGameDate(dateStr) {
  const date = new Date(dateStr + 'T00:00:00');
  return date.toLocaleDateString('en-US', {
    weekday: 'short',
    month: 'short',
    day: 'numeric'
  });
}

function formatTime(timeStr) {
  const [hours, minutes] = timeStr.split(':');
  const hour = parseInt(hours, 10);
  const ampm = hour >= 12 ? 'PM' : 'AM';
  const displayHour = hour === 0 ? 12 : hour > 12 ? hour - 12 : hour;
  return `${displayHour}:${minutes} ${ampm}`;
}
