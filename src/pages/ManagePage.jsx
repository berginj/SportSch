import { useMemo, useState, useEffect } from "react";
import FieldsImport from "../manage/FieldsImport";
import InvitesManager from "../manage/InvitesManager";
import SchedulerManager from "../manage/SchedulerManager";
import CommissionerHub from "../manage/CommissionerHub";
import AvailabilityManager from "../manage/AvailabilityManager";
import SlotGeneratorManager from "../manage/SlotGeneratorManager";
import LeagueSettings from "../manage/LeagueSettings";
import LeaguePicker from "../components/LeaguePicker";
import TeamsManager from "../manage/TeamsManager";
import DivisionsManager from "../manage/DivisionsManager";

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
  const memberships = useMemo(
    () => (Array.isArray(me?.memberships) ? me.memberships : []),
    [me]
  );
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
      ...(canSchedule ? [{ id: "settings", label: "League Settings" }] : []),
      { id: "invites", label: "Invites" },
      { id: "fields", label: "Fields" },
      ...(canSchedule ? [{ id: "scheduler", label: "Scheduler" }] : []),
    ],
    [canSchedule]
  );
  const tabIds = useMemo(() => new Set(tabs.map((t) => t.id)), [tabs]);
  const defaultTabId = tabs[0]?.id || "";
  const [active, setActive] = useState(() => {
    if (typeof window === "undefined") return defaultTabId;
    const params = new URLSearchParams(window.location.search);
    const next = (params.get("manageTab") || "").trim();
    if (next && tabIds.has(next)) return next;
    return defaultTabId;
  });
  const activeTabId = tabIds.has(active) ? active : defaultTabId;

  useEffect(() => {
    if (typeof window === "undefined") return;
    const onPopState = () => {
      const params = new URLSearchParams(window.location.search);
      const next = (params.get("manageTab") || defaultTabId).trim();
      const safeNext = tabIds.has(next) ? next : defaultTabId;
      setActive(safeNext);
    };
    window.addEventListener("popstate", onPopState);
    return () => window.removeEventListener("popstate", onPopState);
  }, [tabIds, defaultTabId]);

  useEffect(() => {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    if (activeTabId) params.set("manageTab", activeTabId);
    else params.delete("manageTab");
    const next = `${window.location.pathname}?${params.toString()}${window.location.hash}`;
    window.history.replaceState({}, "", next);
  }, [activeTabId]);

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
            <Pill key={t.id} active={activeTabId === t.id} onClick={() => setActive(t.id)}>
              {t.label}
            </Pill>
          ))}
          <div className="min-w-[220px]">
            <LeaguePicker leagueId={leagueId} setLeagueId={setLeagueId} me={me} label="Switch league" />
          </div>
        </div>
      </div>

      {activeTabId === "commissioner" && canSchedule && (
        <div className="card">
          <div className="card__header">
            <div className="h2">Commissioner Hub</div>
            <div className="subtle">Start the season setup wizard and build the schedule from availability.</div>
          </div>
          <div className="card__body">
            <CommissionerHub leagueId={leagueId} tableView={tableView} />
          </div>
        </div>
      )}

      {activeTabId === "fields" && (
        <div className="stack gap-4">
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
          {canSchedule && (
            <>
              <div className="card">
                <div className="card__header">
                  <div className="h2">Availability setup</div>
                  <div className="subtle">Import allocations, define recurring rules, and review generated availability.</div>
                </div>
                <div className="card__body">
                  <AvailabilityManager leagueId={leagueId} />
                </div>
              </div>
              <div className="card">
                <div className="card__header">
                  <div className="h2">Availability slots</div>
                  <div className="subtle">Generate and manage slot-level availability for scheduling.</div>
                </div>
                <div className="card__body">
                  <SlotGeneratorManager leagueId={leagueId} />
                </div>
              </div>
            </>
          )}
        </div>
      )}

      {activeTabId === "settings" && canSchedule && (
        <div className="stack gap-4">
          <div className="card">
            <div className="card__header">
              <div className="h2">League Settings</div>
              <div className="subtle">Backups, season configuration, and shared league configuration.</div>
            </div>
            <div className="card__body">
              <LeagueSettings leagueId={leagueId} />
            </div>
          </div>
          <div className="card">
            <div className="card__header">
              <div className="h2">Teams & Coaches</div>
              <div className="subtle">Upload teams and manage coach assignments.</div>
            </div>
            <div className="card__body">
              <TeamsManager leagueId={leagueId} tableView={tableView} />
            </div>
          </div>
          <div className="card">
            <div className="card__header">
              <div className="h2">Divisions</div>
              <div className="subtle">Divisions group teams, slots, and requests.</div>
            </div>
            <div className="card__body">
              <DivisionsManager leagueId={leagueId} />
            </div>
          </div>
        </div>
      )}

      {activeTabId === "invites" && (
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


      {activeTabId === "scheduler" && canSchedule && (
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
