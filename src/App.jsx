import { useMemo, useState } from "react";
import OffersPage from "./pages/OffersPage";
import CalendarPage from "./pages/CalendarPage";
import ManagePage from "./pages/ManagePage";
import HelpPage from "./pages/HelpPage";
import AccessPage from "./pages/AccessPage";
import AdminPage from "./pages/AdminPage";
import InviteAcceptPage from "./pages/InviteAcceptPage";
import PublicLanding from "./pages/PublicLanding";
import TopNav from "./components/TopNav";
import { useSession } from "./lib/useSession";

function readInviteFromUrl() {
  if (typeof window === "undefined") return null;
  const params = new URLSearchParams(window.location.search);
  const inviteId = (params.get("inviteId") || "").trim();
  const leagueId = (params.get("leagueId") || "").trim();
  if (!inviteId || !leagueId) return null;
  return { inviteId, leagueId };
}

export default function App() {
  const { me, memberships, activeLeagueId, setActiveLeagueId, refreshMe } = useSession();
  const [tab, setTab] = useState("calendar");
  const [invite, setInvite] = useState(() => readInviteFromUrl());

  const isSignedIn = !!me && me.userId && me.userId !== "UNKNOWN";
  const isGlobalAdmin = !!me?.isGlobalAdmin;
  const hasMemberships = (memberships?.length || 0) > 0;

  // When global admins have no memberships, default them into the admin view.
  const effectiveTab = useMemo(() => {
    if (!hasMemberships && isGlobalAdmin) return "admin";
    return tab;
  }, [tab, hasMemberships, isGlobalAdmin]);

  if (!me) {
    return (
      <div className="appShell">
        <div className="card">
          <h2>Loading...</h2>
        </div>
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
          <h1>GameSwap</h1>
          <p>You're not signed in yet.</p>
          <a className="btn" href="/.auth/login/aad">
            Sign in with Microsoft
          </a>
          <div className="muted" style={{ marginTop: 12 }}>
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
          <h1>GameSwap</h1>
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
      <TopNav
        tab={effectiveTab}
        setTab={setTab}
        me={me}
        leagueId={activeLeagueId}
        setLeagueId={setActiveLeagueId}
      />

      <main className="main">
        {effectiveTab === "offers" && (
          <OffersPage me={me} leagueId={activeLeagueId} setLeagueId={setActiveLeagueId} />
        )}
        {effectiveTab === "calendar" && (
          <CalendarPage me={me} leagueId={activeLeagueId} setLeagueId={setActiveLeagueId} />
        )}
        {effectiveTab === "manage" && (
          <ManagePage me={me} leagueId={activeLeagueId} setLeagueId={setActiveLeagueId} />
        )}
        {effectiveTab === "help" && <HelpPage />}
        {effectiveTab === "admin" && (
          <AdminPage me={me} leagueId={activeLeagueId} setLeagueId={setActiveLeagueId} />
        )}
      </main>
    </div>
  );
}
