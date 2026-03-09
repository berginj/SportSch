import { Suspense, lazy, useMemo, useState, useEffect } from "react";
import LeaguePicker from "../components/LeaguePicker";
import CollapsibleSection from "../components/CollapsibleSection";

const FieldsImport = lazy(() => import("../manage/FieldsImport"));
const InvitesManager = lazy(() => import("../manage/InvitesManager"));
const CommissionerHub = lazy(() => import("../manage/CommissionerHub"));
const AvailabilityManager = lazy(() => import("../manage/AvailabilityManager"));
const SlotGeneratorManager = lazy(() => import("../manage/SlotGeneratorManager"));
const LeagueSettings = lazy(() => import("../manage/LeagueSettings"));
const TeamsManager = lazy(() => import("../manage/TeamsManager"));
const DivisionsManager = lazy(() => import("../manage/DivisionsManager"));
const PracticeSpaceManager = lazy(() => import("../manage/PracticeSpaceManager"));
const CoachLinksGenerator = lazy(() => import("../manage/CoachLinksGenerator"));
const FieldInventoryImportManager = lazy(() => import("../manage/FieldInventoryImportManager"));

const PRACTICE_SPACE_TAB_ID = "practice-space";
const LEGACY_TAB_IDS = {
  "practice-requests": PRACTICE_SPACE_TAB_ID,
};

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
      ...(canSchedule ? [{ id: "coach-links", label: "Coach Links" }] : []),
      ...(canSchedule ? [{ id: PRACTICE_SPACE_TAB_ID, label: "Practice Space Admin" }] : []),
      ...(canSchedule ? [{ id: "field-inventory", label: "Field Inventory Import" }] : []),
      { id: "fields", label: "Fields" },
    ],
    [canSchedule]
  );
  const tabIds = useMemo(() => new Set(tabs.map((t) => t.id)), [tabs]);
  const defaultTabId = tabs[0]?.id || "";
  const resolveTabId = (value) => {
    const next = (value || "").trim();
    return LEGACY_TAB_IDS[next] || next;
  };
  const [active, setActive] = useState(() => {
    if (typeof window === "undefined") return defaultTabId;
    const params = new URLSearchParams(window.location.search);
    const next = resolveTabId(params.get("manageTab"));
    if (next && tabIds.has(next)) return next;
    return defaultTabId;
  });
  const activeTabId = tabIds.has(active) ? active : defaultTabId;
  const sectionFallback = <div className="muted">Loading section...</div>;

  useEffect(() => {
    if (typeof window === "undefined") return;
    const onPopState = () => {
      const params = new URLSearchParams(window.location.search);
      const next = resolveTabId(params.get("manageTab") || defaultTabId);
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
        <div className="card__body stack gap-3">
          <div className="controlBand">
            <div className="tabs">
              {tabs.map((t) => (
                <button
                  key={t.id}
                  className={`tabBtn ${activeTabId === t.id ? "active" : ""}`}
                  type="button"
                  onClick={() => setActive(t.id)}
                >
                  {t.label}
                </button>
              ))}
            </div>
          </div>
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
          <CollapsibleSection
            title="Fields"
            subtitle="Import fields via CSV (setup phase, then occasional updates)"
            badge="Setup Phase"
            badgeColor="blue"
            defaultExpanded={true}
            storageKey="manage-fields-import"
          >
            <div className="callout mb-3">
              <div className="font-bold mb-2">CSV rules</div>
              <div className="subtle leading-relaxed">
                CSV must include <b>fieldKey</b> + <b>parkName</b> + <b>fieldName</b> (and optionally displayName, address, notes, status). DisplayName should be what you want coaches to see.
                Keep it consistent (example: <code>Tuckahoe Park &gt; Field 2</code>). fieldKey is stable and is how slots reference a field.
              </div>
            </div>
            <Suspense fallback={sectionFallback}>
              <FieldsImport leagueId={leagueId} me={me} tableView={tableView} />
            </Suspense>
          </CollapsibleSection>

          {canSchedule && (
            <>
              <CollapsibleSection
                title="Availability Setup"
                subtitle="Import allocations, define recurring rules, and review generated availability"
                badge="Setup Phase"
                badgeColor="blue"
                defaultExpanded={false}
                storageKey="manage-availability-setup"
              >
                <Suspense fallback={sectionFallback}>
                  <AvailabilityManager leagueId={leagueId} />
                </Suspense>
              </CollapsibleSection>

              <CollapsibleSection
                title="Availability Slots"
                subtitle="Generate and manage slot-level availability for scheduling (run after rules defined)"
                badge="Advanced"
                badgeColor="purple"
                defaultExpanded={false}
                storageKey="manage-availability-slots"
              >
                <Suspense fallback={sectionFallback}>
                  <SlotGeneratorManager leagueId={leagueId} />
                </Suspense>
              </CollapsibleSection>
            </>
          )}
        </div>
      )}

      {activeTabId === "settings" && canSchedule && (
        <div className="stack gap-4">
          <CollapsibleSection
            title="League Settings"
            subtitle="Backups, season configuration, and shared league configuration"
            badge="Setup Phase"
            badgeColor="blue"
            defaultExpanded={false}
            storageKey="manage-league-settings"
          >
            <Suspense fallback={sectionFallback}>
              <LeagueSettings leagueId={leagueId} />
            </Suspense>
          </CollapsibleSection>

          <CollapsibleSection
            title="Teams & Coaches"
            subtitle="Upload teams and manage coach assignments (used during setup and roster changes)"
            badge="Setup Phase"
            badgeColor="blue"
            defaultExpanded={true}
            storageKey="manage-teams-coaches"
          >
            <Suspense fallback={sectionFallback}>
              <TeamsManager leagueId={leagueId} tableView={tableView} />
            </Suspense>
          </CollapsibleSection>

          <CollapsibleSection
            title="Divisions"
            subtitle="Divisions group teams, slots, and requests (rarely modified after setup)"
            badge="Setup Only"
            badgeColor="blue"
            defaultExpanded={false}
            storageKey="manage-divisions"
          >
            <Suspense fallback={sectionFallback}>
              <DivisionsManager leagueId={leagueId} />
            </Suspense>
          </CollapsibleSection>
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

      {activeTabId === PRACTICE_SPACE_TAB_ID && canSchedule && (
        <div className="card">
          <div className="card__header">
            <div className="h2">Practice Space Admin</div>
            <div className="subtle">Review imported field space, align canonical mappings, and manage coach practice requests.</div>
          </div>
          <div className="card__body">
            <Suspense fallback={sectionFallback}>
              <PracticeSpaceManager leagueId={leagueId} />
            </Suspense>
          </div>
        </div>
      )}

      {activeTabId === "field-inventory" && canSchedule && (
        <div className="card">
          <div className="card__header">
            <div className="h2">Field Inventory Import</div>
            <div className="subtle">Load a public county workbook, preview normalized inventory, review mappings, and explicitly stage or import results.</div>
          </div>
          <div className="card__body">
            <Suspense fallback={sectionFallback}>
              <FieldInventoryImportManager leagueId={leagueId} />
            </Suspense>
          </div>
        </div>
      )}
    </div>
  );
}
