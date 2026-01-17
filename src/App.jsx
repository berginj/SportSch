import { useEffect, useMemo, useState } from "react";
import OffersPage from "./pages/OffersPage";
import CalendarPage from "./pages/CalendarPage";
import SchedulePage from "./pages/SchedulePage";
import ManagePage from "./pages/ManagePage";
import HelpPage from "./pages/HelpPage";
import AccessPage from "./pages/AccessPage";
import AdminPage from "./pages/AdminPage";
import InviteAcceptPage from "./pages/InviteAcceptPage";
import HomePage from "./pages/HomePage";
import DebugPage from "./pages/DebugPage";
import PracticePortalPage from "./pages/PracticePortalPage";
import NotificationSettingsPage from "./pages/NotificationSettingsPage";
import TopNav from "./components/TopNav";
import StatusCard from "./components/StatusCard";
import KeyboardShortcutsModal from "./components/KeyboardShortcutsModal";
import { useSession } from "./lib/useSession";
import { trackPageView } from "./lib/telemetry";
import { useKeyboardShortcuts, COMMON_SHORTCUTS } from "./lib/hooks/useKeyboardShortcuts";

const VALID_TABS = new Set(["home", "calendar", "schedule", "offers", "manage", "admin", "debug", "help", "practice", "settings"]);

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

  // Keyboard shortcuts
  useKeyboardShortcuts({
    [COMMON_SHORTCUTS.GO_HOME]: () => setTab('home'),
    [COMMON_SHORTCUTS.GO_CALENDAR]: () => setTab('calendar'),
    [COMMON_SHORTCUTS.GO_MANAGE]: () => setTab('manage'),
    [COMMON_SHORTCUTS.GO_ADMIN]: () => isGlobalAdmin && setTab('admin'),
    [COMMON_SHORTCUTS.HELP]: () => setShowShortcuts(true),
    [COMMON_SHORTCUTS.ESCAPE]: () => setShowShortcuts(false),
  }, isSignedIn && hasMemberships);

  // When global admins have no memberships, default them into the admin view.
  const effectiveTab = useMemo(() => {
    if (!hasMemberships && isGlobalAdmin) return "admin";
    return tab;
  }, [tab, hasMemberships, isGlobalAdmin]);

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
      <InviteAcceptPage
        invite={invite}
        me={me}
        refreshMe={refreshMe}
        setLeagueId={setActiveLeagueId}
        onDone={clearInvite}
      />
    );
  }

  // Not signed in: show public landing with recent offers + sign-up.
  if (!isSignedIn) {
    return (
      <div className="appShell">
        <div className="card">
          <h1>Sports Scheduler</h1>
          <p>You're not signed in yet.</p>
          <a className="btn" href="/.auth/login/aad">
            Sign in with Microsoft
          </a>
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
        <AccessPage
          me={me}
          leagueId={activeLeagueId}
          setLeagueId={setActiveLeagueId}
          refreshMe={refreshMe}
        />
        <div className="card">
          <HelpPage minimal />
        </div>
      </div>
    );
  }

  return (
    <div className="app">
      <a href="#main-content" className="skip-link">
        Skip to main content
      </a>
      <TopNav
        tab={effectiveTab}
        setTab={setTab}
        me={me}
        leagueId={activeLeagueId}
        setLeagueId={setActiveLeagueId}
      />

      <main id="main-content" className="main">
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
        {effectiveTab === "settings" && (
          <NotificationSettingsPage leagueId={activeLeagueId} />
        )}
      </main>

      <KeyboardShortcutsModal
        isOpen={showShortcuts}
        onClose={() => setShowShortcuts(false)}
      />
    </div>
  );
}
