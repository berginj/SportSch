import { Suspense, lazy, useEffect, useMemo, useState } from "react";
import StatusCard from "./components/StatusCard";
import { useSession } from "./lib/useSession";
import { trackPageView } from "./lib/telemetry";
import { useKeyboardShortcuts, COMMON_SHORTCUTS } from "./lib/hooks/useKeyboardShortcuts";

const TopNav = lazy(() => import("./components/TopNav"));
const OffersPage = lazy(() => import("./pages/OffersPage"));
const CalendarPage = lazy(() => import("./pages/CalendarPage"));
const SchedulePage = lazy(() => import("./pages/SchedulePage"));
const ManagePage = lazy(() => import("./pages/ManagePage"));
const HelpPage = lazy(() => import("./pages/HelpPage"));
const AccessPage = lazy(() => import("./pages/AccessPage"));
const AdminPage = lazy(() => import("./pages/AdminPage"));
const InviteAcceptPage = lazy(() => import("./pages/InviteAcceptPage"));
const HomePage = lazy(() => import("./pages/HomePage"));
const DebugPage = lazy(() => import("./pages/DebugPage"));
const PracticePortalPage = lazy(() => import("./pages/PracticePortalPage"));
const CoachOnboardingPage = lazy(() => import("./pages/CoachOnboardingPage"));
const NotificationSettingsPage = lazy(() => import("./pages/NotificationSettingsPage"));
const NotificationCenterPage = lazy(() => import("./pages/NotificationCenterPage"));
const KeyboardShortcutsModal = lazy(() => import("./components/KeyboardShortcutsModal"));

const VALID_TABS = new Set(["home", "calendar", "schedule", "offers", "manage", "admin", "debug", "help", "practice", "coach-setup", "settings", "notifications"]);

function readInviteFromUrl() {
  if (typeof window === "undefined") return null;
  const params = new URLSearchParams(window.location.search);
  const inviteId = (params.get("inviteId") || "").trim();
  const leagueId = (params.get("leagueId") || "").trim();
  if (!inviteId || !leagueId) return null;
  return { inviteId, leagueId };
}

function readTabFromHash() {
  if (typeof window === "undefined") return "home";
  const hash = (window.location.hash || "").replace("#", "").trim();
  return VALID_TABS.has(hash) ? hash : "home";
}

export default function App() {
  const { me, memberships, activeLeagueId, setActiveLeagueId, refreshMe } = useSession();
  const [tab, setTab] = useState(() => readTabFromHash());
  const [invite, setInvite] = useState(() => readInviteFromUrl());
  const [showShortcuts, setShowShortcuts] = useState(false);
  const tableView = "A";

  const isSignedIn = !!me && me.userId && me.userId !== "UNKNOWN";
  const isGlobalAdmin = !!me?.isGlobalAdmin;
  const hasMemberships = (memberships?.length || 0) > 0;
  const activeMembership = useMemo(() => {
    const id = (activeLeagueId || "").trim();
    if (!id) return null;
    return (memberships || []).find((m) => (m?.leagueId || "").trim() === id) || null;
  }, [memberships, activeLeagueId]);
  const activeRole = (activeMembership?.role || "").trim();
  const canManage = isGlobalAdmin || activeRole === "LeagueAdmin";
  const pageFallback = <StatusCard title="Loading" message="Loading page..." />;

  // Keyboard shortcuts
  useKeyboardShortcuts({
    [COMMON_SHORTCUTS.GO_HOME]: () => setTab('home'),
    [COMMON_SHORTCUTS.GO_CALENDAR]: () => setTab('calendar'),
    [COMMON_SHORTCUTS.GO_MANAGE]: () => canManage && setTab('manage'),
    [COMMON_SHORTCUTS.GO_ADMIN]: () => isGlobalAdmin && setTab('admin'),
    [COMMON_SHORTCUTS.HELP]: () => setShowShortcuts(true),
    [COMMON_SHORTCUTS.ESCAPE]: () => setShowShortcuts(false),
  }, isSignedIn && hasMemberships);

  // When global admins have no memberships, default them into the admin view,
  // but allow navigation to non-league-specific pages (help, debug, admin).
  const effectiveTab = useMemo(() => {
    const nonLeaguePages = new Set(["help", "debug", "admin", "settings", "notifications"]);
    const hasLeagueContext = !!(activeLeagueId || "").trim();
    if (tab === "manage" && !canManage) return "home";
    if (!hasMemberships && isGlobalAdmin && !hasLeagueContext && !nonLeaguePages.has(tab)) return "admin";
    return tab;
  }, [tab, hasMemberships, isGlobalAdmin, canManage, activeLeagueId]);

  useEffect(() => {
    if (!me) return;
    const name = `tab:${effectiveTab}`;
    const uri = `${window.location.pathname}#${effectiveTab}`;
    trackPageView(name, uri);
  }, [effectiveTab, me]);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const nextHash = `#${effectiveTab}`;
    if (window.location.hash !== nextHash) {
      window.history.replaceState({}, "", `${window.location.pathname}${window.location.search}${nextHash}`);
    }
  }, [effectiveTab]);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const onHashChange = () => {
      const next = readTabFromHash();
      setTab((prev) => (prev === next ? prev : next));
    };
    window.addEventListener("hashchange", onHashChange);
    return () => window.removeEventListener("hashchange", onHashChange);
  }, []);

  if (!me) {
    return (
      <div className="appShell">
        <StatusCard title="Loading" message="Loading your session..." />
      </div>
    );
  }

  if (invite) {
    const clearInvite = () => {
      if (typeof window !== "undefined") {
        const url = new URL(window.location.href);
        url.searchParams.delete("inviteId");
        url.searchParams.delete("leagueId");
        window.history.replaceState({}, "", url.pathname + url.search);
      }
      setInvite(null);
    };

    return (
      <Suspense fallback={pageFallback}>
        <InviteAcceptPage
          invite={invite}
          me={me}
          refreshMe={refreshMe}
          setLeagueId={setActiveLeagueId}
          onDone={clearInvite}
        />
      </Suspense>
    );
  }

  // Not signed in: show public landing with recent offers + sign-up.
  if (!isSignedIn) {
    return (
      <div className="appShell">
        <div className="card">
          <h1>Sports Scheduler</h1>
          <p>You're not signed in yet.</p>
          <div className="stack gap-2">
            <a className="btn" href="/.auth/login/aad">
              Sign in with Microsoft
            </a>
            <a className="btn" href="/.auth/login/google">
              Sign in with Google
            </a>
          </div>
          <div className="muted mt-3">
            After signing in, come right back here.
          </div>
        </div>
      </div>
    );
  }

  // Signed in but no memberships: show access request workflow.
  if (!hasMemberships && !isGlobalAdmin) {
    return (
      <div className="appShell">
        <div className="card">
          <h1>Sports Scheduler</h1>
          <p>You're signed in, but you don't have access to any leagues yet.</p>
        </div>
        <Suspense fallback={pageFallback}>
          <AccessPage
            me={me}
            leagueId={activeLeagueId}
            setLeagueId={setActiveLeagueId}
            refreshMe={refreshMe}
          />
        </Suspense>
        <div className="card">
          <Suspense fallback={pageFallback}>
            <HelpPage minimal />
          </Suspense>
        </div>
      </div>
    );
  }

  return (
    <div className="app">
      <a href="#main-content" className="skip-link">
        Skip to main content
      </a>
      <Suspense fallback={null}>
        <TopNav
          tab={effectiveTab}
          setTab={setTab}
          me={me}
          leagueId={activeLeagueId}
          setLeagueId={setActiveLeagueId}
        />
      </Suspense>

      <main id="main-content" className="main">
        <Suspense fallback={pageFallback}>
          {effectiveTab === "home" && (
            <HomePage
              me={me}
              leagueId={activeLeagueId}
              setLeagueId={setActiveLeagueId}
              setTab={setTab}
            />
          )}
          {effectiveTab === "offers" && (
            <OffersPage me={me} leagueId={activeLeagueId} setLeagueId={setActiveLeagueId} />
          )}
          {effectiveTab === "calendar" && (
            <CalendarPage me={me} leagueId={activeLeagueId} setLeagueId={setActiveLeagueId} />
          )}
          {effectiveTab === "schedule" && (
            <SchedulePage me={me} leagueId={activeLeagueId} setLeagueId={setActiveLeagueId} />
          )}
          {effectiveTab === "manage" && (
            <ManagePage
              me={me}
              leagueId={activeLeagueId}
              setLeagueId={setActiveLeagueId}
              tableView={tableView}
            />
          )}
          {effectiveTab === "help" && <HelpPage />}
          {effectiveTab === "admin" && (
            <AdminPage me={me} leagueId={activeLeagueId} setLeagueId={setActiveLeagueId} />
          )}
          {effectiveTab === "debug" && (
            isGlobalAdmin ? (
              <DebugPage me={me} leagueId={activeLeagueId} />
            ) : (
              <div className="card">
                <h2>Debug</h2>
                <p className="muted">You do not have access to this page.</p>
              </div>
            )
          )}
          {effectiveTab === "practice" && (
            <PracticePortalPage me={me} leagueId={activeLeagueId} />
          )}
          {effectiveTab === "coach-setup" && (
            <CoachOnboardingPage me={me} leagueId={activeLeagueId} />
          )}
          {effectiveTab === "settings" && (
            <NotificationSettingsPage leagueId={activeLeagueId} />
          )}
          {effectiveTab === "notifications" && (
            <NotificationCenterPage leagueId={activeLeagueId} />
          )}
        </Suspense>
      </main>

      {showShortcuts ? (
        <Suspense fallback={null}>
          <KeyboardShortcutsModal
            isOpen={showShortcuts}
            onClose={() => setShowShortcuts(false)}
          />
        </Suspense>
      ) : null}
    </div>
  );
}
