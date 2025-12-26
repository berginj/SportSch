import { useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import { ROLE } from "../lib/constants";

function normalizeRole(role) {
  return (role || "").trim();
}

export default function InvitesManager({ leagueId, me }) {
  const memberships = Array.isArray(me?.memberships) ? me.memberships : [];
  const isGlobalAdmin = !!me?.isGlobalAdmin;

  const role = useMemo(() => {
    const inLeague = memberships.filter((m) => (m?.leagueId || "").trim() === (leagueId || "").trim());
    const roles = inLeague.map((m) => normalizeRole(m?.role));
    if (roles.includes(ROLE.LEAGUE_ADMIN)) return ROLE.LEAGUE_ADMIN;
    if (roles.includes(ROLE.COACH)) return ROLE.COACH;
    return roles.includes(ROLE.VIEWER) ? ROLE.VIEWER : "";
  }, [memberships, leagueId]);

  const canInvite = isGlobalAdmin || role === ROLE.LEAGUE_ADMIN;

  const [inviteEmail, setInviteEmail] = useState("");
  const [inviteRole, setInviteRole] = useState(ROLE.COACH);
  const [teamDivision, setTeamDivision] = useState("");
  const [teamId, setTeamId] = useState("");
  const [expiresHours, setExpiresHours] = useState("168");
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState("");
  const [ok, setOk] = useState("");
  const [inviteUrl, setInviteUrl] = useState("");

  async function createInvite() {
    setErr("");
    setOk("");
    setInviteUrl("");

    if (!leagueId) return setErr("Select a league first.");
    if (!inviteEmail.trim()) return setErr("Email is required.");

    const isCoach = inviteRole === ROLE.COACH;
    if (isCoach && ((teamDivision && !teamId) || (!teamDivision && teamId)))
      return setErr("Team assignment requires both division and teamId.");

    const payload = {
      leagueId,
      inviteEmail: inviteEmail.trim(),
      role: inviteRole,
      expiresHours: expiresHours ? Number(expiresHours) : undefined,
      team: isCoach && teamDivision && teamId ? { division: teamDivision.trim(), teamId: teamId.trim() } : null,
    };

    setBusy(true);
    try {
      const res = await apiFetch("/api/admin/invites", {
        method: "POST",
        body: JSON.stringify(payload),
      });
      setOk(`Invite created for ${res?.inviteEmail || inviteEmail.trim()}.`);
      if (res?.acceptUrl) setInviteUrl(res.acceptUrl);
    } catch (e) {
      setErr(e?.message || "Invite failed.");
    } finally {
      setBusy(false);
    }
  }

  async function copyInviteUrl() {
    if (!inviteUrl) return;
    try {
      await navigator.clipboard.writeText(inviteUrl);
      setOk("Invite link copied.");
    } catch {
      prompt("Copy the invite link:", inviteUrl);
    }
  }

  return (
    <div className="stack">
      {err ? <div className="callout callout--error">{err}</div> : null}
      {ok ? <div className="callout callout--ok">{ok}</div> : null}

      {!canInvite ? (
        <div className="callout callout--error">Only League Admins can send invites.</div>
      ) : (
        <div className="stack">
          <div className="grid2">
            <label title="Email of the person you are inviting.">
              Email
              <input value={inviteEmail} onChange={(e) => setInviteEmail(e.target.value)} placeholder="coach@example.com" />
            </label>
            <label title="Role granted by this invite.">
              Role
              <select value={inviteRole} onChange={(e) => setInviteRole(e.target.value)}>
                <option value={ROLE.COACH}>Coach</option>
                <option value={ROLE.LEAGUE_ADMIN}>LeagueAdmin</option>
                <option value={ROLE.VIEWER}>Viewer</option>
              </select>
            </label>
            <label title="How long the invite link remains valid.">
              Expires (hours)
              <input
                value={expiresHours}
                onChange={(e) => setExpiresHours(e.target.value)}
                placeholder="168"
              />
            </label>
            <label title="League that will be granted.">
              League
              <input value={leagueId || ""} disabled />
            </label>
            {inviteRole === ROLE.COACH ? (
              <>
                <label title="Optional division for coach assignment.">
                  Team division (optional)
                  <input value={teamDivision} onChange={(e) => setTeamDivision(e.target.value)} placeholder="10U" />
                </label>
                <label title="Optional team ID for coach assignment.">
                  Team ID (optional)
                  <input value={teamId} onChange={(e) => setTeamId(e.target.value)} placeholder="TIGERS" />
                </label>
              </>
            ) : null}
          </div>
          <div className="row mt-3">
            <button className="btn primary" onClick={createInvite} disabled={busy} title="Create a magic link for this invite.">
              {busy ? "Sending..." : "Create invite"}
            </button>
          </div>

          {inviteUrl ? (
            <div className="card mt-3">
              <div className="font-bold mb-2">Magic link</div>
              <div className="subtle mb-2">
                Send this link to the recipient. They will sign in and immediately receive access.
              </div>
              <div className="row">
                <input value={inviteUrl} readOnly />
                <button className="btn btn--ghost" onClick={copyInviteUrl} title="Copy the invite URL.">
                  Copy
                </button>
              </div>
            </div>
          ) : null}
        </div>
      )}
    </div>
  );
}
