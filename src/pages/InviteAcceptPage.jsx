import { useEffect, useState } from "react";
import { apiFetch } from "../lib/api";

export default function InviteAcceptPage({ invite, me, refreshMe, setLeagueId, onDone }) {
  const signedIn = !!me?.userId && me.userId !== "UNKNOWN";
  const [status, setStatus] = useState("idle");
  const [err, setErr] = useState("");
  const [result, setResult] = useState(null);

  useEffect(() => {
    if (!signedIn || status !== "idle") return;
    acceptInvite();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [signedIn, status]);

  function loginUrl() {
    if (typeof window === "undefined") return "/.auth/login/aad";
    const returnTo = `${window.location.pathname}${window.location.search}`;
    return `/.auth/login/aad?post_login_redirect_uri=${encodeURIComponent(returnTo)}`;
  }

  async function acceptInvite() {
    setErr("");
    setStatus("submitting");
    try {
      const res = await apiFetch("/api/invites/accept", {
        method: "POST",
        body: JSON.stringify({ leagueId: invite.leagueId, inviteId: invite.inviteId }),
      });
      setResult(res);
      setStatus("success");
      if (setLeagueId) setLeagueId(invite.leagueId);
      if (refreshMe) await refreshMe();
    } catch (e) {
      setErr(e?.message || "Invite acceptance failed.");
      setStatus("error");
    }
  }

  function finish() {
    if (onDone) onDone();
  }

  return (
    <div className="appShell">
      <div className="card max-w-640 mx-auto mt-10">
        <h2>Accept invite</h2>
        <div className="subtle mb-3">
          League: <b>{invite.leagueId}</b>
        </div>
        {err ? <div className="callout callout--error">{err}</div> : null}

        {!signedIn ? (
          <div className="stack">
            <div className="subtle">Sign in to accept your invite.</div>
            <a className="btn" href={loginUrl()}>
              Sign in with Microsoft
            </a>
          </div>
        ) : null}

        {signedIn && status === "submitting" ? <div className="subtle">Accepting invite...</div> : null}
        {signedIn && status === "success" ? (
          <div className="stack">
            <div className="callout callout--ok">
              You now have access as <b>{result?.role || "member"}</b>.
            </div>
            <button className="btn primary" onClick={finish}>
              Go to app
            </button>
          </div>
        ) : null}
        {signedIn && status === "error" ? (
          <div className="stack">
            <button className="btn" onClick={acceptInvite}>
              Try again
            </button>
          </div>
        ) : null}
      </div>
    </div>
  );
}
