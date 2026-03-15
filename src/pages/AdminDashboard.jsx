import { useEffect, useState, useCallback } from 'react';
import { apiFetch } from '../lib/api';
import StatusCard from '../components/StatusCard';

/**
 * AdminDashboard - League admin dashboard with health metrics and quick actions
 * Shows:
 * - League health metrics (pending access requests, unassigned coaches, schedule coverage)
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
      const data = await apiFetch('/api/admin/dashboard');
      setMetrics({
        pendingRequests: Number(data?.pendingRequests || 0),
        unassignedCoaches: Number(data?.unassignedCoaches || 0),
        totalCoaches: Number(data?.totalCoaches || 0),
        scheduleCoverage: Number(data?.scheduleCoverage || 0),
        upcomingGames: Number(data?.upcomingGames || 0),
        totalSlots: Number(data?.totalSlots || 0),
        confirmedSlots: Number(data?.confirmedSlots || 0),
        openSlots: Number(data?.openSlots || 0),
        divisions: Number(data?.divisions || 0),
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
      <div className="card">
        <div className="card__header">
          <div className="h1">League Admin Dashboard</div>
          <div className="subtle">Manage your league and monitor key metrics.</div>
        </div>
        <div className="layoutStatRow">
          <div className="layoutStat">
            <div className="layoutStat__value">{metrics.divisions}</div>
            <div className="layoutStat__label">Divisions</div>
          </div>
          <div className="layoutStat">
            <div className="layoutStat__value">{metrics.totalCoaches}</div>
            <div className="layoutStat__label">Coaches</div>
          </div>
          <div className="layoutStat">
            <div className="layoutStat__value">{metrics.openSlots}</div>
            <div className="layoutStat__label">Open slots</div>
          </div>
          <div className="layoutStat">
            <div className="layoutStat__value">{metrics.confirmedSlots}</div>
            <div className="layoutStat__label">Confirmed games</div>
          </div>
        </div>
      </div>

      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
        <MetricCard
          title="Pending Access"
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

      <div className="card">
        <div className="card__header">
          <div className="h2">Quick Actions</div>
        </div>
        <div className="grid gap-3 md:grid-cols-2 lg:grid-cols-4">
          <button
            className="btn btn--primary w-full justify-center"
            onClick={() => onNavigate && onNavigate('access-requests')}
          >
            Access Requests
          </button>
          <button
            className="btn w-full justify-center"
            onClick={() => onNavigate && onNavigate('coaches')}
          >
            Assign Coaches
          </button>
          <button
            className="btn w-full justify-center"
            onClick={() => onNavigate && onNavigate('import')}
          >
            Import CSV
          </button>
          <button
            className="btn w-full justify-center"
            onClick={() => window.location.hash = '#manage'}
          >
            League Setup
          </button>
        </div>
      </div>

      <div className="grid gap-4 md:grid-cols-2">
        <div className="card">
          <div className="card__header">
            <div className="h2">League Overview</div>
          </div>
          <div className="grid gap-2 text-sm">
            <div className="row row--between">
              <span className="subtle">Divisions:</span>
              <span className="font-semibold">{metrics.divisions}</span>
            </div>
            <div className="row row--between">
              <span className="subtle">Total Coaches:</span>
              <span className="font-semibold">{metrics.totalCoaches}</span>
            </div>
            <div className="row row--between">
              <span className="subtle">Open Slots:</span>
              <span className="font-semibold">{metrics.openSlots}</span>
            </div>
            <div className="row row--between">
              <span className="subtle">Confirmed Games:</span>
              <span className="font-semibold">{metrics.confirmedSlots}</span>
            </div>
          </div>
        </div>

        <div className="card">
          <div className="card__header">
            <div className="h2">Quick Links</div>
          </div>
          <div className="grid gap-2">
            <button
              className="layoutItem layoutItem--link"
              onClick={() => window.location.hash = '#manage'}
            >
              <div className="font-semibold text-sm">Teams & Divisions</div>
              <div className="subtle">Manage teams and division setup</div>
            </button>
            <button
              className="layoutItem layoutItem--link"
              onClick={() => window.location.hash = '#manage'}
            >
              <div className="font-semibold text-sm">Fields</div>
              <div className="subtle">Manage field locations</div>
            </button>
            <button
              className="layoutItem layoutItem--link"
              onClick={() => window.location.hash = '#calendar'}
            >
              <div className="font-semibold text-sm">Schedule</div>
              <div className="subtle">View calendar and manage games</div>
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

function MetricCard({ title, value, subtitle, color = 'gray', actionLabel, onAction }) {
  const toneBadges = {
    blue: 'statusBadge status-scheduled',
    green: 'statusBadge status-confirmed',
    yellow: 'statusBadge status-open',
    red: 'statusBadge status-cancelled',
    gray: 'statusBadge',
  };
  const toneLabels = {
    blue: 'review',
    green: 'healthy',
    yellow: 'watch',
    red: 'urgent',
    gray: 'info',
  };

  return (
    <div className="card">
      <div className="grid gap-2 h-full">
        <div className="row row--between">
          <div className="subtle font-semibold">{title}</div>
          <span className={toneBadges[color] || toneBadges.gray}>{toneLabels[color] || toneLabels.gray}</span>
        </div>
        <div className="h1">{value}</div>
        <div className="subtle">{subtitle}</div>
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
