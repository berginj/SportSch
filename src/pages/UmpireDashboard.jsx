import { useState, useEffect, useCallback } from "react";
import { apiFetch } from "../lib/api";
import { logError } from "../lib/errorLogger";
import StatusCard from "../components/StatusCard";
import UmpireAssignmentCard from "../components/UmpireAssignmentCard";
import Toast from "../components/Toast";

export default function UmpireDashboard({ leagueId, me }) {
  const [dashboard, setDashboard] = useState(null);
  const [pendingAssignments, setPendingAssignments] = useState([]);
  const [upcomingAssignments, setUpcomingAssignments] = useState([]);
  const [pastAssignments, setPastAssignments] = useState([]);
  const [loading, setLoading] = useState(true);
  const [toast, setToast] = useState(null);
  const [showPast, setShowPast] = useState(false);

  useEffect(() => {
    if (leagueId) {
      loadDashboard();
      loadAssignments();
    }
  }, [leagueId]);

  const loadDashboard = useCallback(async () => {
    if (!leagueId) return;

    try {
      const data = await apiFetch('/api/umpires/me/dashboard');
      setDashboard(data);
    } catch (err) {
      logError("Failed to load umpire dashboard", err, { leagueId });
      setToast({ message: "Failed to load dashboard", tone: "error" });
    }
  }, [leagueId]);

  const loadAssignments = useCallback(async () => {
    if (!leagueId) return;

    setLoading(true);
    try {
      const today = new Date().toISOString().split('T')[0];

      const [pending, upcoming, past] = await Promise.all([
        apiFetch('/api/umpires/me/assignments?status=Assigned'),
        apiFetch('/api/umpires/me/assignments?status=Accepted'),
        apiFetch(`/api/umpires/me/assignments?dateTo=${today}`)
      ]);

      setPendingAssignments(pending || []);
      setUpcomingAssignments(upcoming || []);
      setPastAssignments(past || []);
    } catch (err) {
      logError("Failed to load assignments", err, { leagueId });
      setToast({ message: "Failed to load assignments", tone: "error" });
    } finally {
      setLoading(false);
    }
  }, [leagueId]);

  async function handleAccept(assignmentId) {
    try {
      await apiFetch(`/api/umpire-assignments/${assignmentId}/status`, {
        method: 'PATCH',
        body: JSON.stringify({ status: 'Accepted' })
      });

      setToast({ message: "Assignment accepted! League admin notified.", tone: "success" });
      loadDashboard();
      loadAssignments();
    } catch (err) {
      logError("Failed to accept assignment", err, { assignmentId });
      setToast({ message: err.message || "Failed to accept assignment", tone: "error" });
    }
  }

  async function handleDecline(assignmentId, reason) {
    try {
      await apiFetch(`/api/umpire-assignments/${assignmentId}/status`, {
        method: 'PATCH',
        body: JSON.stringify({
          status: 'Declined',
          declineReason: reason
        })
      });

      setToast({ message: "Assignment declined. Admin has been notified.", tone: "success" });
      loadDashboard();
      loadAssignments();
    } catch (err) {
      logError("Failed to decline assignment", err, { assignmentId });
      setToast({ message: err.message || "Failed to decline assignment", tone: "error" });
    }
  }

  if (loading && !dashboard) {
    return <StatusCard title="Loading your assignments..." />;
  }

  return (
    <div className="umpire-dashboard">
      <div className="dashboard-header">
        <h1>Welcome, {dashboard?.umpire?.name || 'Umpire'}</h1>
        <p>Your officiating schedule and assignments</p>
      </div>

      {/* Stats Cards */}
      <div className="stats-grid">
        <div className="stat-card stat-card--warning">
          <div className="stat-value">{dashboard?.pendingCount || 0}</div>
          <div className="stat-label">Pending Response</div>
        </div>
        <div className="stat-card stat-card--success">
          <div className="stat-value">{dashboard?.upcomingCount || 0}</div>
          <div className="stat-label">Confirmed Games</div>
        </div>
        <div className="stat-card stat-card--info">
          <div className="stat-value">{dashboard?.thisWeek || 0}</div>
          <div className="stat-label">This Week</div>
        </div>
        <div className="stat-card stat-card--info">
          <div className="stat-value">{dashboard?.thisMonth || 0}</div>
          <div className="stat-label">This Month</div>
        </div>
      </div>

      {/* Pending Assignments (Action Required) */}
      {pendingAssignments.length > 0 && (
        <section className="section section--highlighted">
          <div className="section-header">
            <h2>⏳ Pending Assignments - Action Required</h2>
            <span className="badge badge-warning">{pendingAssignments.length}</span>
          </div>
          <div className="assignments-grid">
            {pendingAssignments.map(assignment => (
              <UmpireAssignmentCard
                key={assignment.assignmentId}
                assignment={assignment}
                onAccept={() => handleAccept(assignment.assignmentId)}
                onDecline={(reason) => handleDecline(assignment.assignmentId, reason)}
                showActions={true}
              />
            ))}
          </div>
        </section>
      )}

      {/* Upcoming Confirmed Games */}
      <section className="section">
        <div className="section-header">
          <h2>Upcoming Games</h2>
          {upcomingAssignments.length > 0 && (
            <span className="badge badge-success">{upcomingAssignments.length}</span>
          )}
        </div>

        {upcomingAssignments.length === 0 ? (
          <div className="empty-state">
            <p>No confirmed games yet.</p>
            {pendingAssignments.length > 0 && (
              <p>Accept pending assignments above to see them here.</p>
            )}
          </div>
        ) : (
          <div className="assignments-list">
            {upcomingAssignments.map(assignment => (
              <UmpireAssignmentCard
                key={assignment.assignmentId}
                assignment={assignment}
                showActions={false}
              />
            ))}
          </div>
        )}
      </section>

      {/* Past Assignments (Collapsed) */}
      {pastAssignments.length > 0 && (
        <section className="section">
          <div className="section-header">
            <button
              onClick={() => setShowPast(!showPast)}
              className="btn btn-ghost"
            >
              {showPast ? '▼' : '▶'} Past Assignments ({pastAssignments.length})
            </button>
          </div>

          {showPast && (
            <div className="assignments-list">
              {pastAssignments.map(assignment => (
                <UmpireAssignmentCard
                  key={assignment.assignmentId}
                  assignment={assignment}
                  showActions={false}
                />
              ))}
            </div>
          )}
        </section>
      )}

      {/* Quick Actions */}
      <div className="quick-actions">
        <a href="#umpire/availability" className="btn btn-outline">
          ⚙️ Manage Availability
        </a>
        <a href="#umpire/profile" className="btn btn-outline">
          👤 Edit Profile
        </a>
      </div>

      {toast && (
        <Toast
          message={toast.message}
          tone={toast.tone}
          onDismiss={() => setToast(null)}
        />
      )}
    </div>
  );
}

function formatDate(dateStr) {
  if (!dateStr) return '';
  const date = new Date(dateStr + 'T00:00:00');
  return date.toLocaleDateString('en-US', {
    weekday: 'short',
    month: 'short',
    day: 'numeric'
  });
}
