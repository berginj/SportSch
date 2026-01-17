import { useEffect, useState, useCallback } from 'react';
import { apiFetch } from '../lib/api';
import StatusCard from '../components/StatusCard';

/**
 * AdminDashboard - League admin dashboard with health metrics and quick actions
 * Shows:
 * - League health metrics (pending requests, unassigned coaches, schedule coverage)
 * - Quick action buttons
 * - Recent activity feed (future enhancement)
 */
export default function AdminDashboard({ leagueId, onNavigate }) {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [metrics, setMetrics] = useState(null);

  const loadMetrics = useCallback(async () => {
    if (!leagueId) {
      setError('No league selected');
      setLoading(false);
      return;
    }

    setLoading(true);
    setError('');

    try {
      // Fetch metrics from multiple endpoints
      // TODO: Create dedicated /admin/dashboard/metrics endpoint for better performance
      const [accessRequests, memberships, slots, divisions] = await Promise.all([
        apiFetch('/api/accessrequests?status=Pending').catch(() => []),
        apiFetch('/api/memberships').catch(() => []),
        apiFetch('/api/slots').catch(() => []),
        apiFetch('/api/divisions').catch(() => []),
      ]);

      // Calculate metrics
      const pendingRequests = Array.isArray(accessRequests) ? accessRequests.length : 0;

      const coaches = (Array.isArray(memberships) ? memberships : [])
        .filter(m => m.role === 'Coach');
      const unassignedCoaches = coaches.filter(m => !m.team || !m.team.teamId).length;

      const allSlots = Array.isArray(slots) ? slots : [];
      const confirmedSlots = allSlots.filter(s => s.status === 'Confirmed').length;
      const openSlots = allSlots.filter(s => s.status === 'Open').length;
      const totalSlots = allSlots.length;
      const scheduleCoverage = totalSlots > 0
        ? Math.round((confirmedSlots / totalSlots) * 100)
        : 0;

      // Upcoming games (next 7 days)
      const today = new Date();
      const nextWeek = new Date(today);
      nextWeek.setDate(today.getDate() + 7);
      const todayStr = formatDateYMD(today);
      const nextWeekStr = formatDateYMD(nextWeek);

      const upcomingGames = allSlots
        .filter(s => s.status === 'Confirmed' && s.gameDate >= todayStr && s.gameDate <= nextWeekStr)
        .length;

      setMetrics({
        pendingRequests,
        unassignedCoaches,
        totalCoaches: coaches.length,
        scheduleCoverage,
        upcomingGames,
        totalSlots,
        confirmedSlots,
        openSlots,
        divisions: Array.isArray(divisions) ? divisions.length : 0,
      });
    } catch (err) {
      setError(err.message || 'Failed to load metrics');
    } finally {
      setLoading(false);
    }
  }, [leagueId]);

  useEffect(() => {
    loadMetrics();
  }, [loadMetrics]);

  if (loading) {
    return (
      <div className="page">
        <StatusCard title="Loading Dashboard" message="Loading league metrics..." />
      </div>
    );
  }

  if (error) {
    return (
      <div className="page">
        <StatusCard title="Error" message={error} variant="error" />
        <button className="btn mt-3" onClick={loadMetrics}>
          Retry
        </button>
      </div>
    );
  }

  return (
    <div className="page">
      <div className="mb-6">
        <h1 className="text-3xl font-bold mb-2">League Admin Dashboard</h1>
        <p className="text-gray-600">Manage your league and monitor key metrics.</p>
      </div>

      {/* Health Metrics */}
      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4 mb-6">
        <MetricCard
          title="Pending Requests"
          value={metrics.pendingRequests}
          subtitle={metrics.pendingRequests === 1 ? 'access request' : 'access requests'}
          color={metrics.pendingRequests > 0 ? 'blue' : 'gray'}
          actionLabel={metrics.pendingRequests > 0 ? 'Review' : null}
          onAction={() => onNavigate && onNavigate('access-requests')}
        />

        <MetricCard
          title="Unassigned Coaches"
          value={metrics.unassignedCoaches}
          subtitle={`of ${metrics.totalCoaches} total coaches`}
          color={metrics.unassignedCoaches > 0 ? 'yellow' : 'green'}
          actionLabel={metrics.unassignedCoaches > 0 ? 'Assign' : null}
          onAction={() => onNavigate && onNavigate('coaches')}
        />

        <MetricCard
          title="Schedule Coverage"
          value={`${metrics.scheduleCoverage}%`}
          subtitle={`${metrics.confirmedSlots} of ${metrics.totalSlots} slots`}
          color={metrics.scheduleCoverage >= 80 ? 'green' : metrics.scheduleCoverage >= 50 ? 'yellow' : 'red'}
          actionLabel="Generate"
          onAction={() => window.location.hash = '#manage'}
        />

        <MetricCard
          title="Upcoming Games"
          value={metrics.upcomingGames}
          subtitle="in the next 7 days"
          color="gray"
        />
      </div>

      {/* Quick Actions Panel */}
      <div className="card mb-6">
        <h2 className="text-xl font-bold mb-4">Quick Actions</h2>
        <div className="grid gap-3 md:grid-cols-2 lg:grid-cols-4">
          <button
            className="btn btn--primary w-full justify-center"
            onClick={() => onNavigate && onNavigate('access-requests')}
          >
            <span className="mr-2">👥</span>
            Access Requests
          </button>
          <button
            className="btn w-full justify-center"
            onClick={() => onNavigate && onNavigate('coaches')}
          >
            <span className="mr-2">🎯</span>
            Assign Coaches
          </button>
          <button
            className="btn w-full justify-center"
            onClick={() => onNavigate && onNavigate('import')}
          >
            <span className="mr-2">📄</span>
            Import CSV
          </button>
          <button
            className="btn w-full justify-center"
            onClick={() => window.location.hash = '#manage'}
          >
            <span className="mr-2">⚙️</span>
            League Setup
          </button>
        </div>
      </div>

      {/* Additional Info */}
      <div className="grid gap-4 md:grid-cols-2">
        <div className="card">
          <h3 className="text-lg font-bold mb-3">League Overview</h3>
          <div className="space-y-2 text-sm">
            <div className="flex justify-between">
              <span className="text-gray-600">Divisions:</span>
              <span className="font-semibold">{metrics.divisions}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-gray-600">Total Coaches:</span>
              <span className="font-semibold">{metrics.totalCoaches}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-gray-600">Open Slots:</span>
              <span className="font-semibold">{metrics.openSlots}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-gray-600">Confirmed Games:</span>
              <span className="font-semibold">{metrics.confirmedSlots}</span>
            </div>
          </div>
        </div>

        <div className="card">
          <h3 className="text-lg font-bold mb-3">Quick Links</h3>
          <div className="space-y-2">
            <button
              className="w-full text-left px-3 py-2 rounded hover:bg-gray-100 transition-colors"
              onClick={() => window.location.hash = '#manage'}
            >
              <div className="font-semibold text-sm">Teams & Divisions</div>
              <div className="text-xs text-gray-600">Manage teams and division setup</div>
            </button>
            <button
              className="w-full text-left px-3 py-2 rounded hover:bg-gray-100 transition-colors"
              onClick={() => window.location.hash = '#manage'}
            >
              <div className="font-semibold text-sm">Fields</div>
              <div className="text-xs text-gray-600">Manage field locations</div>
            </button>
            <button
              className="w-full text-left px-3 py-2 rounded hover:bg-gray-100 transition-colors"
              onClick={() => window.location.hash = '#calendar'}
            >
              <div className="font-semibold text-sm">Schedule</div>
              <div className="text-xs text-gray-600">View calendar and manage games</div>
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

function MetricCard({ title, value, subtitle, color = 'gray', actionLabel, onAction }) {
  const colorClasses = {
    blue: 'bg-blue-50 border-blue-200 text-blue-900',
    green: 'bg-green-50 border-green-200 text-green-900',
    yellow: 'bg-yellow-50 border-yellow-200 text-yellow-900',
    red: 'bg-red-50 border-red-200 text-red-900',
    gray: 'bg-gray-50 border-gray-200 text-gray-900',
  };

  const valueColorClasses = {
    blue: 'text-blue-600',
    green: 'text-green-600',
    yellow: 'text-yellow-600',
    red: 'text-red-600',
    gray: 'text-gray-600',
  };

  return (
    <div className={`card ${colorClasses[color]}`}>
      <div className="flex flex-col h-full">
        <div className="text-sm font-semibold mb-2">{title}</div>
        <div className={`text-3xl font-bold ${valueColorClasses[color]} mb-1`}>
          {value}
        </div>
        <div className="text-xs opacity-75 mb-3">{subtitle}</div>
        {actionLabel && onAction && (
          <button
            className="btn btn--sm btn--ghost mt-auto"
            onClick={onAction}
          >
            {actionLabel}
          </button>
        )}
      </div>
    </div>
  );
}

function formatDateYMD(date) {
  const yyyy = date.getFullYear();
  const mm = String(date.getMonth() + 1).padStart(2, '0');
  const dd = String(date.getDate()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}`;
}
