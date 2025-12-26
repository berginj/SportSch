import { useMemo } from "react";

function Section({ title, children }) {
  return (
    <div className="card" style={{ marginBottom: 14 }}>
      <div className="card__header">
        <div className="h2">{title}</div>
      </div>
      <div className="card__body">{children}</div>
    </div>
  );
}

export default function HelpPage({ me, leagueId }) {
  const memberships = useMemo(() => me?.memberships || [], [me]);

  return (
    <div className="container">
      <div className="h1" style={{ marginBottom: 6 }}>
        Help
      </div>
      <div className="muted" style={{ marginBottom: 14 }}>
        This app helps leagues coordinate open game offers ("slots") and get them scheduled (all times are US/Eastern). Coaches offer slots and accept each other's offers. League admins manage setup.
      </div>

      <Section title="Select your league">
        <p>
          Use the league dropdown in the top bar. Your selection is saved in your browser and sent to the API as <code>x-league-id</code> on every request.
        </p>
        <p className="muted" style={{ marginTop: 0 }}>
          Current league: <code>{leagueId || "(none selected)"}</code>
        </p>
      </Section>

      <Section title="What this app is for">
        <p style={{ marginTop: 0 }}>
          Coaches post <b>Open</b> game offers ("slots") to the calendar. Other coaches can <b>accept</b> an open slot, and the game becomes <b>Confirmed</b> immediately on the calendar.
        </p>
      </Section>

      <Section title="Request access to a league">
        <ol style={{ margin: 0, paddingLeft: 18 }}>
          <li>Sign in (Azure AD) via the login link.</li>
          <li>Go to the Access page and pick the league you want.</li>
          <li>Select your role (Coach or Viewer) and submit.</li>
          <li>A LeagueAdmin (or global admin) approves it; refresh and re-select the league.</li>
        </ol>
      </Section>

      <Section title="Offer a slot">
        <p>
          Go to <b>Create Game Offer</b>, choose your division, then create an open slot with date/time/field. Open slots appear to other teams.
        </p>
      </Section>

      <Section title="Request a swap">
        <p>
          When you see an open slot that works for your team, click <b>Accept</b> (Calendar) or <b>Accept</b> (Create Game Offer). Add notes if helpful.
        </p>
        <p className="muted" style={{ marginTop: 0 }}>
          Acceptance immediately confirms the game and shows it as <b>Confirmed</b> on the calendar.
        </p>
      </Section>

      <Section title="Cancel a slot or confirmed game">
        <p style={{ marginTop: 0 }}>
          If plans change, either the <b>offering</b> team or the <b>accepting</b> team can cancel a confirmed game. LeagueAdmins and global admins can cancel too.
        </p>
      </Section>

      <Section title="Approve/deny an access request (admins)">
        <p style={{ marginTop: 0 }}>
          LeagueAdmins and global admins approve or deny <b>league access</b> requests on the Admin page.
        </p>
      </Section>

      <Section title="Calendar (slots + events)">
        <p>
          Calendar shows both <b>Slots</b> (open/confirmed games) and <b>Events</b> (practices, meetings, etc.). Filter by division and date range.
        </p>
      </Section>

      <Section title="Admin/Scheduler setup">
        <p>
          LeagueAdmins can import/manage fields, manage divisions, manage teams, and update league contact info. Global admins are higher-level across all leagues.
        </p>
        <p className="muted" style={{ marginBottom: 0 }}>
          Use League Management -> Fields for CSV import (fieldKey is required). Use League Management -> Teams &amp; Coaches to keep the league organized.
        </p>
      </Section>

      <Section title="Common issues + fixes">
        <ul style={{ margin: 0, paddingLeft: 18 }}>
          <li>
            <b>Forbidden / 403</b>: wrong league selected, or you don't have membership for that league.
          </li>
          <li>
            <b>COACH_TEAM_REQUIRED</b>: you're approved as Coach but not assigned to a team yet.
          </li>
          <li>
            <b>DIVISION_MISMATCH</b>: you can only accept/request games within your assigned division (exact match).
          </li>
          <li>
            <b>DOUBLE_BOOKING</b>: the game overlaps an already-confirmed game for one of the teams.
          </li>
          <li>
            <b>No slots showing</b>: check division filter and date range; confirm the league is correct.
          </li>
          <li>
            <b>Cancelled games missing</b>: the calendar hides cancelled slots by default. Turn on <b>Show cancelled</b> and refresh.
          </li>
        </ul>
        <details style={{ marginTop: 10 }}>
          <summary style={{ cursor: "pointer" }}>Show my memberships (from /api/me)</summary>
          <pre style={{ marginTop: 10, whiteSpace: "pre-wrap" }}>{JSON.stringify(memberships, null, 2)}</pre>
        </details>
      </Section>
    </div>
  );
}
