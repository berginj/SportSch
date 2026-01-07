import { useMemo, useState } from "react";

function Section({ title, children }) {
  return (
    <div className="card mb-4">
      <div className="card__header">
        <div className="h2">{title}</div>
      </div>
      <div className="card__body">{children}</div>
    </div>
  );
}

export default function HelpPage({ me, leagueId }) {
  const memberships = useMemo(() => me?.memberships || [], [me]);
  const [layoutKey, setLayoutKey] = useState("coach");

  const layouts = [
    { id: "coach", label: "Coach hub" },
    { id: "mobile", label: "Mobile compact" },
    { id: "admin", label: "Admin ops desk" },
    { id: "filters", label: "Filters first" },
  ];

  function LayoutPreview() {
    switch (layoutKey) {
      case "mobile":
        return (
          <div className="layoutPreview layoutPreview--mobile">
            <div className="layoutPhone">
              <div className="layoutPhone__bar">Sports Scheduler</div>
              <div className="layoutPhone__section">
                <div className="layoutTitle">Today</div>
                <div className="layoutPill">Open offers</div>
                <div className="layoutList">
                  <div className="layoutItem">
                    <div className="layoutRow">
                      <div>10U Tigers @ Barcroft</div>
                      <div className="layoutBadge">Open</div>
                    </div>
                    <div className="layoutMeta">Apr 10, 6:00-7:30</div>
                    <button className="btn">Accept</button>
                  </div>
                  <div className="layoutItem">
                    <div className="layoutRow">
                      <div>Practice: 12U</div>
                      <div className="layoutBadge layoutBadge--event">Event</div>
                    </div>
                    <div className="layoutMeta">Apr 11, 5:00-6:00</div>
                  </div>
                </div>
              </div>
              <div className="layoutPhone__nav">
                <button className="btn btn--ghost">Calendar</button>
                <button className="btn btn--ghost">Offer/Request</button>
                <button className="btn btn--ghost">Teams</button>
                <button className="btn btn--ghost">More</button>
              </div>
            </div>
          </div>
        );
      case "admin":
        return (
          <div className="layoutPreview layoutPreview--admin">
            <div className="layoutHeader">
              <div>
                <div className="layoutTitle">League admin desk</div>
                <div className="layoutMeta">ARL | Power tools and daily tasks</div>
              </div>
              <div className="layoutRow">
                <button className="btn">Invite coach</button>
                <button className="btn">Import teams</button>
                <button className="btn">Create offer/request</button>
              </div>
            </div>
            <div className="layoutGrid">
              <div className="layoutPanel">
                <div className="layoutPanel__title">Today</div>
                <div className="layoutStatRow">
                  <div className="layoutStat">
                    <div className="layoutStat__value">18</div>
                    <div className="layoutStat__label">Open offers</div>
                  </div>
                  <div className="layoutStat">
                    <div className="layoutStat__value">6</div>
                    <div className="layoutStat__label">Conflicts</div>
                  </div>
                  <div className="layoutStat">
                    <div className="layoutStat__value">4</div>
                    <div className="layoutStat__label">Access requests</div>
                  </div>
                </div>
              </div>
              <div className="layoutPanel">
                <div className="layoutPanel__title">Coach assignments</div>
                <div className="layoutRow layoutRow--space">
                  <div>Coach A</div>
                  <button className="btn btn--ghost">Assign</button>
                </div>
                <div className="layoutRow layoutRow--space">
                  <div>Coach B</div>
                  <button className="btn btn--ghost">Assign</button>
                </div>
                <div className="layoutRow layoutRow--space">
                  <div>Coach C</div>
                  <button className="btn btn--ghost">Assign</button>
                </div>
              </div>
              <div className="layoutPanel">
                <div className="layoutPanel__title">Next 30 days</div>
                <div className="layoutList">
                  <div className="layoutItem">Apr 12 - Open: Tigers @ Field 1</div>
                  <div className="layoutItem">Apr 13 - Confirmed: Sharks vs Owls</div>
                  <div className="layoutItem">Apr 14 - Event: Meeting</div>
                </div>
              </div>
              <div className="layoutPanel">
                <div className="layoutPanel__title">Quick filters</div>
                <div className="layoutRow">
                  <div className="layoutPill">10U</div>
                  <div className="layoutPill">12U</div>
                  <div className="layoutPill">Open</div>
                  <div className="layoutPill">Confirmed</div>
                </div>
              </div>
            </div>
          </div>
        );
      case "filters":
        return (
          <div className="layoutPreview layoutPreview--filters">
            <div className="layoutSplit">
              <div className="layoutPanel">
                <div className="layoutPanel__title">Filters</div>
                <div className="layoutForm">
                  <label>
                    Division
                    <select>
                      <option>All</option>
                      <option>10U</option>
                      <option>12U</option>
                    </select>
                  </label>
                  <label>
                    Date range
                    <input placeholder="2026-04-01 to 2026-07-30" />
                  </label>
                  <label>
                    Status
                    <div className="layoutRow">
                      <div className="layoutPill">Open</div>
                      <div className="layoutPill">Confirmed</div>
                      <div className="layoutPill">Cancelled</div>
                    </div>
                  </label>
                  <button className="btn">Apply</button>
                </div>
              </div>
              <div className="layoutPanel">
                <div className="layoutPanel__title">Results</div>
                <div className="layoutList">
                  <div className="layoutItem">
                    <div className="layoutRow layoutRow--space">
                      <div>Apr 18 - 10U Eagles</div>
                      <div className="layoutBadge">Open</div>
                    </div>
                    <div className="layoutMeta">Barcroft Elementary</div>
                  </div>
                  <div className="layoutItem">
                    <div className="layoutRow layoutRow--space">
                      <div>Apr 20 - 12U Tigers</div>
                      <div className="layoutBadge layoutBadge--confirm">Confirmed</div>
                    </div>
                    <div className="layoutMeta">Gunston Park</div>
                  </div>
                  <div className="layoutItem">
                    <div className="layoutRow layoutRow--space">
                      <div>Apr 22 - Practice</div>
                      <div className="layoutBadge layoutBadge--event">Event</div>
                    </div>
                    <div className="layoutMeta">Lee Center</div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        );
      case "coach":
      default:
        return (
          <div className="layoutPreview layoutPreview--coach">
            <div className="layoutHero">
              <div>
                <div className="layoutTitle">Coach hub</div>
                <div className="layoutMeta">Find open offers or requests, accept, and confirm in seconds.</div>
              </div>
              <div className="layoutRow">
                <button className="btn">Create offer/request</button>
                <button className="btn btn--ghost">Subscribe</button>
              </div>
            </div>
            <div className="layoutGrid layoutGrid--two">
              <div className="layoutPanel">
                <div className="layoutPanel__title">Open offers</div>
                <div className="layoutList">
                  <div className="layoutItem">
                    <div className="layoutRow layoutRow--space">
                      <div>Apr 10 - 10U Tigers</div>
                      <div className="layoutBadge">Open</div>
                    </div>
                    <div className="layoutMeta">Barcroft Elementary</div>
                  </div>
                  <div className="layoutItem">
                    <div className="layoutRow layoutRow--space">
                      <div>Apr 12 - 10U Sharks</div>
                      <div className="layoutBadge">Open</div>
                    </div>
                    <div className="layoutMeta">Tuckahoe Park</div>
                  </div>
                </div>
              </div>
              <div className="layoutPanel">
                <div className="layoutPanel__title">My calendar</div>
                <div className="layoutList">
                  <div className="layoutItem">Apr 15 - Confirmed: Owls</div>
                  <div className="layoutItem">Apr 18 - Practice</div>
                  <div className="layoutItem">Apr 20 - Open offer</div>
                </div>
              </div>
            </div>
          </div>
        );
    }
  }

  return (
    <div className="container">
      <div className="h1 mb-2">
        Help
      </div>
      <div className="muted mb-4">
        This app helps leagues coordinate open game offers and requests ("slots") and get them scheduled (all times are US/Eastern). Coaches post slots and accept each other's offers or requests. League admins manage setup.
      </div>

      <Section title="Prototype: Commissioner season setup">
        <div className="layoutPreview layoutPreview--admin">
          <div className="layoutHeader">
            <div>
              <div className="layoutTitle">Season setup flow</div>
              <div className="layoutMeta">Guided checklist for league admins before publishing.</div>
            </div>
            <div className="layoutRow">
              <button className="btn">Start setup</button>
              <button className="btn btn--ghost">View validations</button>
            </div>
          </div>
          <div className="layoutGrid">
            <div className="layoutPanel">
              <div className="layoutPanel__title">Checklist</div>
              <div className="layoutList">
                <div className="layoutItem layoutItem--link">1. Set season dates</div>
                <div className="layoutItem layoutItem--link">2. Confirm fields and parks</div>
                <div className="layoutItem layoutItem--link">3. Create availability rules</div>
                <div className="layoutItem layoutItem--link">4. Generate slots (preview)</div>
                <div className="layoutItem layoutItem--link">5. Run validations</div>
                <div className="layoutItem layoutItem--link">6. Publish schedule</div>
              </div>
            </div>
            <div className="layoutPanel">
              <div className="layoutPanel__title">Validation blockers</div>
              <div className="layoutList">
                <div className="layoutItem">
                  <div className="layoutRow layoutRow--space">
                    <div>Division 10U missing slots</div>
                    <div className="layoutBadge">Blocked</div>
                  </div>
                  <div className="layoutMeta">Add availability for 4/12 - 5/03.</div>
                </div>
                <div className="layoutItem">
                  <div className="layoutRow layoutRow--space">
                    <div>Home/away imbalance</div>
                    <div className="layoutBadge">Warning</div>
                  </div>
                  <div className="layoutMeta">Balance games before publish.</div>
                </div>
              </div>
            </div>
            <div className="layoutPanel">
              <div className="layoutPanel__title">Quick actions</div>
              <div className="layoutRow">
                <button className="btn btn--ghost">Invite coaches</button>
                <button className="btn btn--ghost">Import teams</button>
                <button className="btn btn--ghost">Review access</button>
              </div>
            </div>
            <div className="layoutPanel">
              <div className="layoutPanel__title">Publish readiness</div>
              <div className="layoutList">
                <div className="layoutItem">Validations: 2 blockers, 3 warnings</div>
                <div className="layoutItem">Last run: 10:12 AM</div>
                <div className="layoutItem">Schedule status: Draft</div>
              </div>
            </div>
          </div>
        </div>
      </Section>

      <Section title="Prototype: Coach hub">
        <div className="layoutPreview layoutPreview--coach">
          <div className="layoutHero">
            <div>
              <div className="layoutTitle">Coach hub</div>
              <div className="layoutMeta">Quick view of open offers and your next games.</div>
            </div>
            <div className="layoutRow">
              <button className="btn">Create offer/request</button>
              <button className="btn btn--ghost">Subscribe to team calendar</button>
            </div>
          </div>
          <div className="layoutGrid layoutGrid--two">
            <div className="layoutPanel">
              <div className="layoutPanel__title">Open offers for my division</div>
              <div className="layoutList">
                <div className="layoutItem layoutItem--link">
                  <div className="layoutRow layoutRow--space">
                    <div>Apr 10 - 10U Tigers</div>
                    <div className="layoutBadge">Open</div>
                  </div>
                  <div className="layoutMeta">Barcroft Elementary</div>
                </div>
                <div className="layoutItem layoutItem--link">
                  <div className="layoutRow layoutRow--space">
                    <div>Apr 12 - 10U Sharks</div>
                    <div className="layoutBadge">Open</div>
                  </div>
                  <div className="layoutMeta">Tuckahoe Park</div>
                </div>
              </div>
            </div>
            <div className="layoutPanel">
              <div className="layoutPanel__title">My next games</div>
              <div className="layoutList">
                <div className="layoutItem">
                  <div className="layoutRow layoutRow--space">
                    <div>Apr 15 - vs Owls</div>
                    <div className="layoutBadge layoutBadge--confirm">Confirmed</div>
                  </div>
                  <div className="layoutMeta">Kenmore Field</div>
                </div>
                <div className="layoutItem">
                  <div className="layoutRow layoutRow--space">
                    <div>Apr 18 - Practice</div>
                    <div className="layoutBadge layoutBadge--event">Event</div>
                  </div>
                  <div className="layoutMeta">Drop-off 30 mins early</div>
                </div>
              </div>
            </div>
            <div className="layoutPanel">
              <div className="layoutPanel__title">Pending requests</div>
              <div className="layoutList">
                <div className="layoutItem">Apr 21 - Waiting on Sharks</div>
                <div className="layoutItem">Apr 24 - Waiting on Eagles</div>
              </div>
            </div>
            <div className="layoutPanel">
              <div className="layoutPanel__title">Context</div>
              <div className="layoutRow">
                <div className="layoutPill">Division: 10U</div>
                <div className="layoutPill">Team: Tigers</div>
                <div className="layoutPill">Role: Coach</div>
              </div>
            </div>
          </div>
        </div>
      </Section>

      <Section title="Prototype: Help tab feedback options">
        <div className="layoutPreview layoutPreview--filters">
          <div className="layoutSplit">
            <div className="layoutPanel">
              <div className="layoutPanel__title">Quick feedback</div>
              <div className="layoutRow">
                <button className="btn btn--ghost">This helped</button>
                <button className="btn btn--ghost">Still stuck</button>
                <button className="btn btn--ghost">Request feature</button>
              </div>
              <div className="layoutList mt-3">
                <div className="layoutItem">Attach current page + league to report</div>
                <div className="layoutItem">Capture last API error</div>
              </div>
            </div>
            <div className="layoutPanel">
              <div className="layoutPanel__title">Comment capture</div>
              <div className="layoutForm">
                <label>
                  Tell us what happened
                  <textarea placeholder="Steps, expected vs actual, and what you tried." />
                </label>
                <label>
                  Email (optional)
                  <input placeholder="name@domain.com" />
                </label>
                <button className="btn">Send feedback</button>
              </div>
            </div>
          </div>
        </div>
      </Section>

      <Section title="Layout options (preview)">
        <div className="row mb-3">
          {layouts.map((l) => (
            <button
              key={l.id}
              className={`btn btn--ghost ${layoutKey === l.id ? "is-active" : ""}`}
              onClick={() => setLayoutKey(l.id)}
            >
              {l.label}
            </button>
          ))}
        </div>
        <LayoutPreview />
      </Section>

      <details className="mb-4">
        <summary className="cursor-pointer font-bold">Help content</summary>
        <div className="mt-3">
          <Section title="Select your league">
            <p>
              Use the league dropdown in the top bar. Your selection is saved in your browser and sent to the API as <code>x-league-id</code> on every request.
            </p>
            <p className="muted m-0">
              Current league: <code>{leagueId || "(none selected)"}</code>
            </p>
          </Section>

          <Section title="What this app is for">
            <p className="m-0">
              Coaches post <b>Open</b> game offers or requests ("slots") to the calendar. Other coaches can <b>accept</b> an open slot, and the game becomes <b>Confirmed</b> immediately on the calendar.
            </p>
          </Section>

          <Section title="Request access to a league">
            <ol className="m-0 pl-4">
              <li>Sign in (Azure AD) via the login link.</li>
              <li>Go to the Access page and pick the league you want.</li>
              <li>Select your role (Coach or Viewer) and submit.</li>
              <li>A LeagueAdmin (or global admin) approves it; refresh and re-select the league.</li>
            </ol>
          </Section>

          <Section title="Post an offer or request">
            <p>
              Go to <b>Create Offer/Request</b>, choose your division, then create an open slot with date/time/field. Open slots appear to other teams. You can also post recurring offers or requests.
            </p>
          </Section>

          <Section title="Request a swap">
            <p>
              When you see an open slot that works for your team, click <b>Accept</b> (Calendar) or <b>Accept</b> (Create Offer/Request). Add notes if helpful.
            </p>
            <p className="muted m-0">
              Acceptance immediately confirms the game and shows it as <b>Confirmed</b> on the calendar.
            </p>
          </Section>

          <Section title="Cancel a slot or confirmed game">
            <p className="m-0">
              If plans change, either the <b>offering</b> team or the <b>accepting</b> team can cancel a confirmed game. LeagueAdmins and global admins can cancel too.
            </p>
          </Section>

          <Section title="Approve/deny an access request (admins)">
            <p className="m-0">
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
            <p className="muted m-0">
              Use League Management &gt; Fields for CSV import (fieldKey is required). Use League Management &gt; Teams &amp; Coaches to keep the league organized.
            </p>
          </Section>

          <Section title="Common issues + fixes">
            <ul className="m-0 pl-4">
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
            <details className="mt-3">
              <summary className="cursor-pointer">Show my memberships (from /api/me)</summary>
              <pre className="mt-3 whitespace-pre-wrap">{JSON.stringify(memberships, null, 2)}</pre>
            </details>
          </Section>
        </div>
      </details>
    </div>
  );
}
