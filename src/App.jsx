import { Suspense, lazy, useEffect, useMemo, useState } from "react";
import StatusCard from "./components/StatusCard";
import {
  readHashValue,
  readLocationSearchParams,
  replaceLocation,
  updateLocationSearch,
  subscribeToLocationChanges,
} from "./lib/locationState";
import { useSession } from "./lib/useSession";
import { trackPageView } from "./lib/telemetry";
import { useKeyboardShortcuts, COMMON_SHORTCUTS } from "./lib/hooks/useKeyboardShortcuts";
import { THEME_MODE, THEME_STORAGE_KEY } from "./lib/constants";

const TopNav = lazy(() => import("./components/TopNav"));
const OffersPage = lazy(() => import("./pages/OffersPage"));
const CalendarPage = lazy(() => import("./pages/CalendarPage"));
const ManagePage = lazy(() => import("./pages/ManagePage"));
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

const VALID_TABS = new Set(["home", "calendar", "offers", "manage", "admin", "debug", "practice", "coach-setup", "settings", "notifications"]);

function readInviteFromUrl() {
  if (typeof window === "undefined") return null;
  const params = readLocationSearchParams();
  const inviteId = (params.get("inviteId") || "").trim();
  const leagueId = (params.get("leagueId") || "").trim();
  if (!inviteId || !leagueId) return null;
  return { inviteId, leagueId };
}

function readTabFromHash() {
  return readHashValue({ fallback: "home", validValues: VALID_TABS });
}

function readThemePreference() {
  if (typeof window === "undefined") return THEME_MODE.SYSTEM;
  const stored = (window.localStorage.getItem(THEME_STORAGE_KEY) || "").trim().toLowerCase();
  if (stored === THEME_MODE.DARK || stored === THEME_MODE.LIGHT || stored === THEME_MODE.SYSTEM) return stored;
  return THEME_MODE.SYSTEM;
}

function readSystemPrefersDark() {
  if (typeof window === "undefined") return false;
  if (typeof window.matchMedia !== "function") return false;
  return window.matchMedia("(prefers-color-scheme: dark)").matches;
}

export default function App() {
  const { me, memberships, leagueId, setLeagueId, refreshMe } = useSession();
  const [tab, setTab] = useState(() => readTabFromHash());
  const [invite, setInvite] = useState(() => readInviteFromUrl());
  const [showShortcuts, setShowShortcuts] = useState(false);
  const [themeMode, setThemeMode] = useState(() => readThemePreference());
  const [systemPrefersDark, setSystemPrefersDark] = useState(() => readSystemPrefersDark());
  const tableView = "A";
  const theme = useMemo(
    () => (themeMode === THEME_MODE.SYSTEM ? (systemPrefersDark ? THEME_MODE.DARK : THEME_MODE.LIGHT) : themeMode),
    [themeMode, systemPrefersDark]
  );

  const isSignedIn = !!me && me.userId && me.userId !== "UNKNOWN";
  const isGlobalAdmin = !!me?.isGlobalAdmin;
  const hasMemberships = (memberships?.length || 0) > 0;
  const activeMembership = useMemo(() => {
    const id = (leagueId || "").trim();
    if (!id) return null;
    return (memberships || []).find((m) => (m?.leagueId || "").trim() === id) || null;
  }, [memberships, leagueId]);
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
  // but allow navigation to non-league-specific pages (debug, admin, settings).
  const effectiveTab = useMemo(() => {
    const nonLeaguePages = new Set(["debug", "admin", "settings", "notifications"]);
    const hasLeagueContext = !!(leagueId || "").trim();
    if (tab === "manage" && !canManage) return "home";
    if (!hasMemberships && isGlobalAdmin && !hasLeagueContext && !nonLeaguePages.has(tab)) return "admin";
    return tab;
  }, [tab, hasMemberships, isGlobalAdmin, canManage, leagueId]);

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
      replaceLocation({ hash: nextHash });
    }
  }, [effectiveTab]);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const onHashChange = () => {
      const next = readTabFromHash();
      setTab((prev) => (prev === next ? prev : next));
    };
    return subscribeToLocationChanges(onHashChange, { hashchange: true, popstate: false });
  }, []);

  useEffect(() => {
    if (typeof document === "undefined") return;
    document.documentElement.setAttribute("data-theme", theme);
    if (typeof window !== "undefined") {
      window.localStorage.setItem(THEME_STORAGE_KEY, themeMode);
    }
  }, [theme, themeMode]);

  useEffect(() => {
    if (typeof window === "undefined") return;
    if (typeof window.matchMedia !== "function") return;
    const mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
    const onChange = () => setSystemPrefersDark(mediaQuery.matches);
    onChange();
    if (typeof mediaQuery.addEventListener === "function") {
      mediaQuery.addEventListener("change", onChange);
      return () => mediaQuery.removeEventListener("change", onChange);
    }
    if (typeof mediaQuery.addListener === "function") {
      mediaQuery.addListener(onChange);
      return () => mediaQuery.removeListener(onChange);
    }
    return undefined;
  }, []);

  function toggleTheme() {
    setThemeMode((prev) => {
      if (prev === THEME_MODE.SYSTEM) return THEME_MODE.LIGHT;
      if (prev === THEME_MODE.LIGHT) return THEME_MODE.DARK;
      return THEME_MODE.SYSTEM;
    });
  }

  if (!me) {
    return (
      <div className="appShell">
        <StatusCard title="Loading" message="Loading your session..." />
      </div>
    );
  }

  if (invite) {
    const clearInvite = () => {
      updateLocationSearch((params) => {
        params.delete("inviteId");
        params.delete("leagueId");
      }, { hash: "" });
      setInvite(null);
    };

    return (
      <Suspense fallback={pageFallback}>
        <InviteAcceptPage
          invite={invite}
          me={me}
          refreshMe={refreshMe}
          setLeagueId={setLeagueId}
          onDone={clearInvite}
        />
      </Suspense>
    );
  }

  // Not signed in: show the authenticated sign-in landing.
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
            leagueId={leagueId}
            setLeagueId={setLeagueId}
            refreshMe={refreshMe}
          />
        </Suspense>
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
          leagueId={leagueId}
          setLeagueId={setLeagueId}
          theme={theme}
          themeMode={themeMode}
          onToggleTheme={toggleTheme}
        />
      </Suspense>

      <main id="main-content" className="main">
        <Suspense fallback={pageFallback}>
          {effectiveTab === "home" && (
            <HomePage
              me={me}
              leagueId={leagueId}
              setLeagueId={setLeagueId}
              setTab={setTab}
            />
          )}
          {effectiveTab === "offers" && (
            <OffersPage me={me} leagueId={leagueId} setLeagueId={setLeagueId} />
          )}
          {effectiveTab === "calendar" && (
            <CalendarPage me={me} leagueId={leagueId} setLeagueId={setLeagueId} />
          )}
          {effectiveTab === "manage" && (
            <ManagePage
              me={me}
              leagueId={leagueId}
              setLeagueId={setLeagueId}
              tableView={tableView}
            />
          )}
          {effectiveTab === "admin" && (
            <AdminPage me={me} leagueId={leagueId} setLeagueId={setLeagueId} />
          )}
          {effectiveTab === "debug" && (
            isGlobalAdmin ? (
              <DebugPage me={me} leagueId={leagueId} />
            ) : (
              <div className="card">
                <h2>Debug</h2>
                <p className="muted">You do not have access to this page.</p>
              </div>
            )
          )}
          {effectiveTab === "practice" && (
            <PracticePortalPage me={me} leagueId={leagueId} />
          )}
          {effectiveTab === "coach-setup" && (
            <CoachOnboardingPage me={me} leagueId={leagueId} setTab={setTab} />
          )}
          {effectiveTab === "settings" && (
            <NotificationSettingsPage leagueId={leagueId} />
          )}
          {effectiveTab === "notifications" && (
            <NotificationCenterPage leagueId={leagueId} />
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
