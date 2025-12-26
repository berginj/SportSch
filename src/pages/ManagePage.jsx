import { useMemo, useState } from "react";
import FieldsImport from "../manage/FieldsImport";
import DivisionsManager from "../manage/DivisionsManager";
import InvitesManager from "../manage/InvitesManager";

function Pill({ active, children, onClick }) {
  return (
    <button
      className={`btn btn--ghost ${active ? "" : ""}`}
      style={{ borderColor: active ? "var(--accent)" : "var(--border)", color: active ? "var(--accent)" : "var(--text)" }}
      onClick={onClick}
    >
      {children}
    </button>
  );
}

export default function ManagePage({ leagueId, me }) {
  const tabs = useMemo(
    () => [
      { id: "fields", label: "Fields" },
      { id: "divisions", label: "Divisions" },
      { id: "invites", label: "Invites" },
      { id: "notes", label: "Notes" }
    ],
    []
  );
  const [active, setActive] = useState("fields");

  return (
    <div className="container">
      <div className="card" style={{ marginBottom: 14 }}>
        <div className="card__header">
          <div className="h2">Manage</div>
          <div className="subtle">
            League: <b>{leagueId || "(none selected)"}</b>
          </div>
        </div>
        <div className="card__body" style={{ display: "flex", gap: 10, flexWrap: "wrap" }}>
          {tabs.map((t) => (
            <Pill key={t.id} active={active === t.id} onClick={() => setActive(t.id)}>
              {t.label}
            </Pill>
          ))}
        </div>
      </div>

      {active === "fields" && (
        <div className="card">
          <div className="card__header">
            <div className="h2">Fields</div>
            <div className="subtle">Import fields via CSV (the only supported fields workflow).</div>
          </div>
          <div className="card__body">
            <div className="callout">
              <div style={{ fontWeight: 700, marginBottom: 6 }}>CSV rules</div>
              <div className="subtle" style={{ lineHeight: 1.45 }}>
                CSV must include <b>fieldKey</b> + <b>parkName</b> + <b>fieldName</b> (and optionally displayName, address, notes, status). DisplayName should be what you want coaches to see.
                Keep it consistent (example: <code>Tuckahoe Park &gt; Field 2</code>). fieldKey is stable and is how slots reference a field.
              </div>
            </div>
            <div className="tableWrap" style={{ marginTop: 12 }}>
              <FieldsImport leagueId={leagueId} me={me} />
            </div>
          </div>
        </div>
      )}

      {active === "divisions" && (
        <div className="card">
          <div className="card__header">
            <div className="h2">Divisions</div>
            <div className="subtle">Divisions are used to group slots and requests (e.g., “Ponytail 4th Grade”).</div>
          </div>
          <div className="card__body">
            <DivisionsManager leagueId={leagueId} />
          </div>
        </div>
      )}

      {active === "invites" && (
        <div className="card">
          <div className="card__header">
            <div className="h2">Invites</div>
            <div className="subtle">Send a magic link to grant access without a request.</div>
          </div>
          <div className="card__body">
            <InvitesManager leagueId={leagueId} me={me} />
          </div>
        </div>
      )}

      {active === "notes" && (
        <div className="card">
          <div className="card__header">
            <div className="h2">Notes</div>
            <div className="subtle">Admin-ish reminders (not secret; just pragmatic).</div>
          </div>
          <div className="card__body" style={{ lineHeight: 1.5 }}>
            <ul style={{ margin: 0, paddingLeft: 18 }}>
              <li>
                <b>League selection persists</b> via <code>gameswap_leagueId</code> in localStorage. Every API call sends <code>x-league-id</code>.
              </li>
              <li>
                If the UI says <b>No league access</b>, add a row in <code>GameSwapMemberships</code> for your UserId + LeagueId.
              </li>
              <li>
                The portal is currently built for speed, not for perfect security. Auth will tighten later (EasyAuth now, Entra later).
              </li>
            </ul>
          </div>
        </div>
      )}
    </div>
  );
}
