import { useEffect, useState, useCallback } from 'react';
import { apiFetch } from '../lib/api';
import StatusCard from '../components/StatusCard';

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
          return slot.status === 'Open' &&
                 slot.division === division &&
                 slot.offeringTeamId !== teamId; // Not my own offers
        }).length;

      // Count my open offers
      const myOpenOffers = (Array.isArray(slots) ? slots : [])
        .filter(slot => {
          return slot.status === 'Open' && slot.offeringTeamId === teamId;
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
          <h1 className="text-2xl font-bold mb-4">Welcome, Coach!</h1>
          <div className="callout callout--info mb-4">
            <strong>Team Assignment Needed</strong>
            <p className="mt-2">
              You're registered as a coach, but you haven't been assigned to a team yet.
              Contact your league administrator to get assigned to your team.
            </p>
          </div>
          <QuickActionsPanel setTab={setTab} hasTeam={false} />
        </div>
      </div>
    );
  }

  return (
    <div className="page">
      <div className="mb-6">
        <h1 className="text-3xl font-bold mb-2">Coach Dashboard</h1>
        <p className="text-gray-600">Welcome back! Here's what's happening with your team.</p>
      </div>

      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        {/* Team Summary Card */}
        <TeamSummaryCard team={dashboard.team} />

        {/* Action Items Card */}
        <ActionItemsCard
          openOffersInDivision={dashboard.openOffersInDivision}
          myOpenOffers={dashboard.myOpenOffers}
          upcomingGames={dashboard.upcomingGames}
          setTab={setTab}
        />

        {/* Quick Actions */}
        <QuickActionsPanel setTab={setTab} hasTeam={true} />
      </div>

      {/* Upcoming Games Section */}
      {dashboard.upcomingGames.length > 0 && (
        <div className="card mt-6">
          <h2 className="text-xl font-bold mb-4">Upcoming Games</h2>
          <div className="space-y-3">
            {dashboard.upcomingGames.map((game, idx) => (
              <GameCard key={game.slotId || idx} game={game} teamId={teamId} />
            ))}
          </div>
          <button
            className="btn btn--ghost mt-4"
            onClick={() => setTab('calendar')}
          >
            View Full Schedule
          </button>
        </div>
      )}

      {dashboard.upcomingGames.length === 0 && (
        <div className="card mt-6">
          <h2 className="text-xl font-bold mb-3">Upcoming Games</h2>
          <div className="callout callout--info">
            <p>No upcoming games scheduled in the next 30 days.</p>
            <p className="mt-2">
              Check the <button className="link" onClick={() => setTab('calendar')}>Calendar</button> for available game slots.
            </p>
          </div>
        </div>
      )}
    </div>
  );
}

function TeamSummaryCard({ team }) {
  return (
    <div className="card bg-gradient-to-br from-blue-50 to-blue-100 border-blue-200">
      <div className="flex items-start justify-between mb-3">
        <h2 className="text-lg font-bold text-blue-900">My Team</h2>
        <span className="badge badge--primary">{team.division}</span>
      </div>
      <div className="text-2xl font-bold text-blue-900 mb-1">
        {team.name || team.teamId}
      </div>
      {team.name && team.name !== team.teamId && (
        <div className="text-sm text-blue-700">Team ID: {team.teamId}</div>
      )}
      {team.primaryContact && (
        <div className="mt-3 pt-3 border-t border-blue-200 text-sm text-blue-800">
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
      <h2 className="text-lg font-bold mb-4">Action Items</h2>
      {!hasActions && (
        <p className="text-gray-600">No action items at this time.</p>
      )}
      {hasActions && (
        <div className="space-y-3">
          {openOffersInDivision > 0 && (
            <div className="flex items-center justify-between p-3 bg-green-50 border border-green-200 rounded">
              <div>
                <div className="font-semibold text-green-900">
                  {openOffersInDivision} New {openOffersInDivision === 1 ? 'Offer' : 'Offers'}
                </div>
                <div className="text-sm text-green-700">Available in your division</div>
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
            <div className="flex items-center justify-between p-3 bg-blue-50 border border-blue-200 rounded">
              <div>
                <div className="font-semibold text-blue-900">
                  {myOpenOffers} Open {myOpenOffers === 1 ? 'Offer' : 'Offers'}
                </div>
                <div className="text-sm text-blue-700">Your slots still available</div>
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
            <div className="flex items-center justify-between p-3 bg-yellow-50 border border-yellow-200 rounded">
              <div>
                <div className="font-semibold text-yellow-900">Game Tomorrow</div>
                <div className="text-sm text-yellow-700">
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
      <h2 className="text-lg font-bold mb-4">Quick Actions</h2>
      <div className="space-y-2">
        {hasTeam && (
          <button
            className="btn btn--primary w-full justify-center"
            onClick={() => setTab('calendar')}
            title="Offer a game slot to other teams"
          >
            <span className="mr-2">📅</span>
            Offer a Game Slot
          </button>
        )}
        <button
          className="btn w-full justify-center"
          onClick={() => setTab('calendar')}
          title="Browse available game slots"
        >
          <span className="mr-2">🔍</span>
          Browse Available Slots
        </button>
        <button
          className="btn w-full justify-center"
          onClick={() => setTab('calendar')}
          title="View your team's schedule"
        >
          <span className="mr-2">📆</span>
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
    <div className="flex items-center justify-between p-3 bg-gray-50 border border-gray-200 rounded hover:bg-gray-100 transition-colors">
      <div className="flex-1">
        <div className="font-semibold">
          {formatGameDate(game.gameDate)} at {formatTime(game.startTime)}
        </div>
        <div className="text-sm text-gray-600">
          {vsText} • {game.displayName || game.fieldKey}
        </div>
      </div>
      {isHome && (
        <span className="badge badge--sm badge--success">Home</span>
      )}
      {isAway && (
        <span className="badge badge--sm">Away</span>
      )}
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
