import { useMemo, useState, useEffect } from "react";
import FieldsImport from "../manage/FieldsImport";
import DivisionsManager from "../manage/DivisionsManager";
import InvitesManager from "../manage/InvitesManager";
import TeamsManager from "../manage/TeamsManager";
import SchedulerManager from "../manage/SchedulerManager";
import CommissionerHub from "../manage/CommissionerHub";
import AvailabilityManager from "../manage/AvailabilityManager";
import SlotGeneratorManager from "../manage/SlotGeneratorManager";
import LeaguePicker from "../components/LeaguePicker";

function Pill({ active, children, onClick }) {
  return (
    <button
      className={`btn btn--ghost ${active ? "is-active" : ""}`}
      type="button"
      onClick={onClick}
    >
      {children}
    </button>
  );
}

export default function ManagePage({ leagueId, me, setLeagueId, tableView }) {
  const memberships = Array.isArray(me?.memberships) ? me.memberships : [];
  const isGlobalAdmin = !!me?.isGlobalAdmin;
  const isLeagueAdmin = useMemo(() => {
    if (!leagueId) return false;
    return memberships.some(
      (m) => (m?.leagueId || "").trim() === leagueId && (m?.role || "").trim() === "LeagueAdmin"
    );
  }, [leagueId, memberships]);
  const canSchedule = isGlobalAdmin || isLeagueAdmin;

  const tabs = useMemo(
    () => [
      ...(canSchedule ? [{ id: "commissioner", label: "Commissioner Hub" }] : []),
      { id: "teams", label: "Teams & Coaches" },
      { id: "invites", label: "Invites" },
      { id: "notes", label: "Notes" },
      { id: "divisions", label: "Divisions" },
      { id: "fields", label: "Fields" },
      ...(canSchedule ? [{ id: "availability", label: "Field Availability" }] : []),
      ...(canSchedule ? [{ id: "slots", label: "Field Slots" }] : []),
      ...(canSchedule ? [{ id: "scheduler", label: "Scheduler" }] : []),
    ],
    [canSchedule]
  );
  const tabIds = useMemo(() => new Set(tabs.map((t) => t.id)), [tabs]);
  const [active, setActive] = useState(() => {
    if (typeof window === "undefined") return "teams";
    const params = new URLSearchParams(window.location.search);
    const next = (params.get("manageTab") || "").trim();
    if (next && tabIds.has(next)) return next;
    return canSchedule ? "commissioner" : "teams";
  });

  useEffect(() => {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    const next = (params.get("manageTab") || "teams").trim();
    const safeNext = tabIds.has(next) ? next : "teams";
    if (safeNext !== active) setActive(safeNext);
  }, [leagueId, tabIds]);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const onPopState = () => {
      const params = new URLSearchParams(window.location.search);
      const next = (params.get("manageTab") || "teams").trim();
      const safeNext = tabIds.has(next) ? next : "teams";
      if (safeNext !== active) setActive(safeNext);
    };
    window.addEventListener("popstate", onPopState);
    return () => window.removeEventListener("popstate", onPopState);
  }, [active, tabIds]);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    if (active) params.set("manageTab", active);
    else params.delete("manageTab");
    const next = `${window.location.pathname}?${params.toString()}${window.location.hash}`;
    window.history.replaceState({}, "", next);
  }, [active]);

  return (
    <div className="container">
      <div className="card mb-4">
        <div className="card__header">
          <div className="h2">League Management</div>
          <div className="subtle">
            League: <b>{leagueId || "(none selected)"}</b>
          </div>
        </div>
        <div className="card__body row row--wrap items-end gap-3">
          {tabs.map((t) => (
            <Pill key={t.id} active={active === t.id} onClick={() => setActive(t.id)}>
              {t.label}
            </Pill>
          ))}
          <div className="min-w-[220px]">
            <LeaguePicker leagueId={leagueId} setLeagueId={setLeagueId} me={me} label="Switch league" />
          </div>
        </div>
      </div>

      {active === "teams" && (
        <div className="card">
          <div className="card__header">
            <div className="h2">Teams & Coaches</div>
            <div className="subtle">Upload teams and manage coach assignments.</div>
          </div>
          <div className="card__body">
            <TeamsManager leagueId={leagueId} tableView={tableView} />
          </div>
        </div>
      )}

      {active === "commissioner" && canSchedule && (
        <div className="stack gap-4">
          <div className="card">
            <div className="card__header">
              <div className="h2">Commissioner Hub</div>
              <div className="subtle">Season settings, blackouts, and scheduling tools in one place.</div>
            </div>
            <div className="card__body">
              <CommissionerHub leagueId={leagueId} />
            </div>
          </div>

          <div className="card">
            <div className="card__header">
              <div className="h2">Scheduling</div>
              <div className="subtle">Preview schedules here. Generate slots in the Field Slots tab first.</div>
            </div>
            <div className="card__body">
              <SchedulerManager leagueId={leagueId} />
            </div>
          </div>
        </div>
      )}

      {active === "fields" && (
        <div className="card">
          <div className="card__header">
            <div className="h2">Fields</div>
            <div className="subtle">Import fields via CSV (the only supported fields workflow).</div>
          </div>
          <div className="card__body">
            <div className="callout">
              <div className="font-bold mb-2">CSV rules</div>
              <div className="subtle leading-relaxed">
                CSV must include <b>fieldKey</b> + <b>parkName</b> + <b>fieldName</b> (and optionally displayName, address, notes, status). DisplayName should be what you want coaches to see.
                Keep it consistent (example: <code>Tuckahoe Park &gt; Field 2</code>). fieldKey is stable and is how slots reference a field.
              </div>
            </div>
            <div className="mt-3">
              <FieldsImport leagueId={leagueId} me={me} tableView={tableView} />
            </div>
          </div>
        </div>
      )}

      {active === "availability" && canSchedule && (
        <div className="card">
          <div className="card__header">
            <div className="h2">Field Availability</div>
            <div className="subtle">Create recurring rules and add exclusions for fields.</div>
          </div>
          <div className="card__body">
            <AvailabilityManager leagueId={leagueId} />
          </div>
        </div>
      )}

      {active === "slots" && canSchedule && (
        <div className="card">
          <div className="card__header">
            <div className="h2">Field Slots</div>
            <div className="subtle">Generate availability slots separately from schedule generation.</div>
          </div>
          <div className="card__body">
            <SlotGeneratorManager leagueId={leagueId} />
          </div>
        </div>
      )}

      {active === "divisions" && (
        <div className="card">
          <div className="card__header">
            <div className="h2">Divisions</div>
            <div className="subtle">Divisions are used to group slots and requests (e.g., "Ponytail 4th Grade").</div>
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
          <div className="card__body leading-relaxed">
            <ul className="m-0 pl-4">
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

      {active === "scheduler" && canSchedule && (
        <div className="card">
          <div className="card__header">
            <div className="h2">Scheduler</div>
            <div className="subtle">Auto-assign matchups to open slots for a division.</div>
          </div>
          <div className="card__body">
            <SchedulerManager leagueId={leagueId} />
          </div>
        </div>
      )}
    </div>
  );
}
