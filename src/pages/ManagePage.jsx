import { Suspense, lazy, useMemo, useState, useEffect } from "react";
import LeaguePicker from "../components/LeaguePicker";

const FieldsImport = lazy(() => import("../manage/FieldsImport"));
const InvitesManager = lazy(() => import("../manage/InvitesManager"));
const SchedulerManager = lazy(() => import("../manage/SchedulerManager"));
const CommissionerHub = lazy(() => import("../manage/CommissionerHub"));
const AvailabilityManager = lazy(() => import("../manage/AvailabilityManager"));
const SlotGeneratorManager = lazy(() => import("../manage/SlotGeneratorManager"));
const LeagueSettings = lazy(() => import("../manage/LeagueSettings"));
const TeamsManager = lazy(() => import("../manage/TeamsManager"));
const DivisionsManager = lazy(() => import("../manage/DivisionsManager"));
const PracticeRequestsManager = lazy(() => import("../manage/PracticeRequestsManager"));
const CoachLinksGenerator = lazy(() => import("../manage/CoachLinksGenerator"));

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
      ...(canSchedule ? [{ id: "scheduler", label: "Scheduler" }] : []),
      ...(canSchedule ? [{ id: "coach-links", label: "Coach Links" }] : []),
      ...(canSchedule ? [{ id: "practice-requests", label: "Practice Requests" }] : []),
      { id: "fields", label: "Fields" },
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
  const sectionFallback = <div className="muted">Loading section...</div>;

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
          <div className="w-full min-[740px]:w-auto min-[740px]:min-w-[220px]">
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
            <Suspense fallback={sectionFallback}>
              <CommissionerHub leagueId={leagueId} tableView={tableView} />
            </Suspense>
          </div>
        </div>
      )}

      {activeTabId === "fields" && (
        <div className="stack gap-4">
          {canSchedule && (
            <>
              <div className="card">
                <div className="card__header">
                  <div className="h2">Availability setup</div>
                  <div className="subtle">Import allocations, define recurring rules, and review generated availability.</div>
                </div>
                <div className="card__body">
                  <Suspense fallback={sectionFallback}>
                    <AvailabilityManager leagueId={leagueId} />
                  </Suspense>
                </div>
              </div>
              <div className="card">
                <div className="card__header">
                  <div className="h2">Availability slots</div>
                  <div className="subtle">Generate and manage slot-level availability for scheduling.</div>
                </div>
                <div className="card__body">
                  <Suspense fallback={sectionFallback}>
                    <SlotGeneratorManager leagueId={leagueId} />
                  </Suspense>
                </div>
              </div>
            </>
          )}
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
                <Suspense fallback={sectionFallback}>
                  <FieldsImport leagueId={leagueId} me={me} tableView={tableView} />
                </Suspense>
              </div>
            </div>
          </div>
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
              <Suspense fallback={sectionFallback}>
                <LeagueSettings leagueId={leagueId} />
              </Suspense>
            </div>
          </div>
          <div className="card">
            <div className="card__header">
              <div className="h2">Teams & Coaches</div>
              <div className="subtle">Upload teams and manage coach assignments.</div>
            </div>
            <div className="card__body">
              <Suspense fallback={sectionFallback}>
                <TeamsManager leagueId={leagueId} tableView={tableView} />
              </Suspense>
            </div>
          </div>
          <div className="card">
            <div className="card__header">
              <div className="h2">Divisions</div>
              <div className="subtle">Divisions group teams, slots, and requests.</div>
            </div>
            <div className="card__body">
              <Suspense fallback={sectionFallback}>
                <DivisionsManager leagueId={leagueId} />
              </Suspense>
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
            <Suspense fallback={sectionFallback}>
              <InvitesManager leagueId={leagueId} me={me} />
            </Suspense>
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
            <Suspense fallback={sectionFallback}>
              <SchedulerManager leagueId={leagueId} />
            </Suspense>
          </div>
        </div>
      )}

      {activeTabId === "coach-links" && canSchedule && (
        <div className="card">
          <div className="card__header">
            <div className="h2">Coach Onboarding Links</div>
            <div className="subtle">Generate personalized onboarding links for all coaches to complete team setup.</div>
          </div>
          <div className="card__body">
            <Suspense fallback={sectionFallback}>
              <CoachLinksGenerator leagueId={leagueId} />
            </Suspense>
          </div>
        </div>
      )}

      {activeTabId === "practice-requests" && canSchedule && (
        <div className="card">
          <div className="card__header">
            <div className="h2">Practice Slot Requests</div>
            <div className="subtle">Review and approve/reject practice slot requests from coaches.</div>
          </div>
          <div className="card__body">
            <Suspense fallback={sectionFallback}>
              <PracticeRequestsManager leagueId={leagueId} />
            </Suspense>
          </div>
        </div>
      )}
    </div>
  );
}
