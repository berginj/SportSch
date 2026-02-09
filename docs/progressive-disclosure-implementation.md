# Progressive Disclosure Implementation Guide

**Goal**: Reduce cognitive load by collapsing lightly-used or setup-phase-only features by default, while keeping frequently-used features visible.

## Overview

This guide implements a smart collapsible system that:
- Hides setup-only features once league is in active season
- Collapses advanced/rarely-used features by default
- Maintains good UX with clear labels and visual hierarchy
- Remembers user preferences (future enhancement)

---

## New Components Created

### 1. `CollapsibleSection.jsx`
Reusable component for progressive disclosure with:
- Expand/collapse animation
- Badge support (e.g., "Setup Only", "Advanced", "Rare")
- Icon support
- Subtitle for context
- onChange callback for tracking

**Usage**:
```jsx
<CollapsibleSection
  title="Divisions"
  subtitle="Create and manage divisions (setup phase only)"
  badge="Setup Only"
  badgeColor="blue"
  defaultExpanded={false}
  icon="📂"
>
  <DivisionsManager leagueId={leagueId} />
</CollapsibleSection>
```

### 2. `useLeaguePhase` Hook
Smart hook that determines league phase (setup/active/complete) and suggests which features should be expanded.

**Usage**:
```jsx
const leaguePhase = useLeaguePhase(leagueData);
const disclosure = useFeatureDisclosure(leaguePhase);

<CollapsibleSection
  title="Teams Manager"
  defaultExpanded={disclosure.teams}
>
  <TeamsManager />
</CollapsibleSection>
```

---

## Priority 1: High-Impact Changes

### A. ManagePage.jsx - Settings Tab

**Current Structure** (3 cards stacked):
```jsx
<div className="card">
  <div className="card__header">League Settings</div>
  <div className="card__body"><LeagueSettings /></div>
</div>
<div className="card">
  <div className="card__header">Teams & Coaches</div>
  <div className="card__body"><TeamsManager /></div>
</div>
<div className="card">
  <div className="card__header">Divisions</div>
  <div className="card__body"><DivisionsManager /></div>
</div>
```

**Proposed Structure**:
```jsx
import CollapsibleSection from '../components/CollapsibleSection';
import { useLeaguePhase, useFeatureDisclosure } from '../lib/useLeaguePhase';

// In component:
const leaguePhase = useLeaguePhase({ seasonStart, seasonEnd, divisionCount, teamCount, fieldCount });
const disclosure = useFeatureDisclosure(leaguePhase);

<div className="stack gap-4">
  {/* League Settings - Collapsed after initial setup */}
  <CollapsibleSection
    title="League Settings"
    subtitle="Game length, season dates, and league-wide configuration"
    badge={leaguePhase.setupProgress < 30 ? "Setup Required" : "Configured"}
    badgeColor={leaguePhase.setupProgress < 30 ? "yellow" : "green"}
    defaultExpanded={disclosure.leagueSettings}
    icon="⚙️"
  >
    <LeagueSettings leagueId={leagueId} />
  </CollapsibleSection>

  {/* Teams - Important during setup and roster changes */}
  <CollapsibleSection
    title="Teams & Coaches"
    subtitle="Upload teams and manage coach assignments"
    badge="Setup Phase"
    badgeColor="blue"
    defaultExpanded={disclosure.teams}
    icon="👥"
  >
    <TeamsManager leagueId={leagueId} tableView={tableView} />
  </CollapsibleSection>

  {/* Divisions - Setup only, rarely changed */}
  <CollapsibleSection
    title="Divisions"
    subtitle="Divisions group teams, slots, and requests (rarely modified after setup)"
    badge="Setup Only"
    badgeColor="blue"
    defaultExpanded={disclosure.divisions}
    icon="📂"
  >
    <DivisionsManager leagueId={leagueId} />
  </CollapsibleSection>
</div>
```

**Impact**: Reduces vertical scrolling from ~2000px to ~400px when collapsed during active season.

---

### B. ManagePage.jsx - Fields Tab

**Current Structure** (3 cards):
```jsx
<div className="card">
  <div className="card__header">Availability setup</div>
  <div className="card__body"><AvailabilityManager /></div>
</div>
<div className="card">
  <div className="card__header">Availability slots</div>
  <div className="card__body"><SlotGeneratorManager /></div>
</div>
<div className="card">
  <div className="card__header">Fields</div>
  <div className="card__body"><FieldsImport /></div>
</div>
```

**Proposed Structure**:
```jsx
<div className="stack gap-4">
  {/* Fields Import - Setup phase, occasionally updated */}
  <CollapsibleSection
    title="Fields"
    subtitle="Import fields via CSV (setup phase, then occasional updates)"
    badge="Setup Phase"
    badgeColor="blue"
    defaultExpanded={disclosure.fields}
    icon="🏟️"
  >
    <FieldsImport leagueId={leagueId} me={me} tableView={tableView} />
  </CollapsibleSection>

  {/* Availability Rules - Setup phase, complex workflow */}
  <CollapsibleSection
    title="Availability Setup"
    subtitle="Import allocations, define recurring rules, and review generated availability"
    badge="Setup Phase"
    badgeColor="blue"
    defaultExpanded={disclosure.fields && leaguePhase.setupProgress < 50}
    icon="📅"
  >
    <AvailabilityManager leagueId={leagueId} />
  </CollapsibleSection>

  {/* Slot Generator - Advanced, used once after rules defined */}
  <CollapsibleSection
    title="Availability Slots"
    subtitle="Generate and manage slot-level availability for scheduling (run after rules defined)"
    badge="Advanced"
    badgeColor="purple"
    defaultExpanded={disclosure.slotGenerator}
    icon="🎯"
  >
    <SlotGeneratorManager leagueId={leagueId} />
  </CollapsibleSection>
</div>
```

**Impact**: Most complex setup workflow. Collapsing reduces cognitive load significantly during active season.

---

### C. LeagueSettings.jsx - Internal Sections

**Current Structure**:
- Backup: Always visible (1 card)
- Season Reset: `<details>` (collapsed) ✓
- League Season Settings: Always visible (large form)
- Division Overrides: `<details>` (collapsed) ✓
- Field Blackouts: `<details>` (collapsed) ✓
- Availability Insights: `<details>` (collapsed) ✓

**Proposed Changes**:

#### Change 1: Wrap League Season Settings in CollapsibleSection

**Location**: `LeagueSettings.jsx`, lines 103-202

**Before**:
```jsx
<div className="card mb-6">
  <h3 className="text-lg font-bold mb-4">League Season Configuration</h3>
  <div className="grid md:grid-cols-2 gap-4">
    {/* 8 form inputs for season config */}
  </div>
  <button className="btn btn--primary mt-4" onClick={saveLeagueSettings}>
    Save League Settings
  </button>
</div>
```

**After**:
```jsx
<CollapsibleSection
  title="League Season Configuration"
  subtitle="Game length, season dates, and league-wide blackouts (set once during setup)"
  badge={hasSeasonDates ? "Configured" : "Setup Required"}
  badgeColor={hasSeasonDates ? "green" : "yellow"}
  defaultExpanded={!hasSeasonDates} // Expand if not configured, collapse once set
  icon="📆"
  className="mb-6"
>
  <div className="grid md:grid-cols-2 gap-4">
    {/* Same form inputs */}
  </div>
  <button className="btn btn--primary mt-4" onClick={saveLeagueSettings}>
    Save League Settings
  </button>
</CollapsibleSection>
```

**Impact**: Reduces clutter after initial setup. Critical info still accessible with one click.

#### Change 2: Convert native `<details>` to CollapsibleSection for consistency

**Before** (Division Overrides, lines 210-283):
```jsx
<details className="card mb-6">
  <summary className="cursor-pointer p-4 font-semibold">
    Division Season Overrides
  </summary>
  <div className="p-4 pt-0">
    {/* Override form */}
  </div>
</details>
```

**After**:
```jsx
<CollapsibleSection
  title="Division Season Overrides"
  subtitle="Set different season dates per division (optional, rarely used)"
  badge="Rare"
  badgeColor="gray"
  defaultExpanded={false}
  icon="🔀"
  className="mb-6"
>
  {/* Override form */}
</CollapsibleSection>
```

**Benefit**: Consistent UI/UX across all collapsible sections, better badge support.

---

## Priority 2: Medium-Impact Changes

### D. AvailabilityManager.jsx - Exception Forms

**Problem**: Exception forms appear inline within expanded table rows, creating deep nesting.

**Current Structure** (lines 120-180):
```jsx
<button onClick={() => setShowExceptions(!showExceptions)}>
  {showExceptions ? 'Hide' : 'Show'} Exceptions ({exceptionCount})
</button>

{showExceptions && (
  <div className="mt-3 p-3 bg-gray-50 rounded border">
    {/* Exception list table */}
    {/* Exception form (always visible when showExceptions=true) */}
    <div className="mt-4 p-3 bg-white rounded border">
      <h4>Add Exception</h4>
      {/* 4 date inputs + reason textarea */}
      <button onClick={addException}>Add Exception</button>
    </div>
  </div>
)}
```

**Proposed Structure**:
```jsx
import CollapsibleSection from '../components/CollapsibleSection';

<button onClick={() => setShowExceptions(!showExceptions)}>
  {showExceptions ? 'Hide' : 'Show'} Exceptions ({exceptionCount})
</button>

{showExceptions && (
  <div className="mt-3">
    {/* Exception list table - always visible when section expanded */}
    {exceptions.length > 0 && (
      <table className="table mb-4">
        {/* Exception rows */}
      </table>
    )}

    {/* Add exception form - collapsible */}
    <CollapsibleSection
      title="Add New Exception"
      subtitle="Create a date range exception to this availability rule"
      badge="Optional"
      badgeColor="gray"
      defaultExpanded={exceptions.length === 0} // Auto-expand if no exceptions yet
      icon="➕"
      className="mt-3"
      headerClassName="bg-white" // Nested background differentiation
    >
      <div className="grid gap-3">
        {/* 4 date inputs + reason textarea */}
        <button className="btn btn--primary" onClick={addException}>
          Add Exception
        </button>
      </div>
    </CollapsibleSection>
  </div>
)}
```

**Impact**: Reduces form clutter when viewing exceptions. Users can see existing exceptions without the "Add" form taking space.

---

### E. AdminPage.jsx - CSV Import Tab

**Current Structure**: Two large sections (Slots import + Teams import) always visible

**Proposed Structure**:
```jsx
<div className="stack gap-4">
  <CollapsibleSection
    title="Import Slots from CSV"
    subtitle="Upload slot data in bulk (advanced feature for mass imports)"
    badge="Advanced"
    badgeColor="purple"
    defaultExpanded={false}
    icon="📊"
  >
    {/* Slot import form */}
  </CollapsibleSection>

  <CollapsibleSection
    title="Import Teams from CSV"
    subtitle="Upload team data in bulk (alternative to Teams Manager)"
    badge="Advanced"
    badgeColor="purple"
    defaultExpanded={false}
    icon="👥"
  >
    {/* Team import form */}
  </CollapsibleSection>
</div>
```

**Impact**: Reduces admin page complexity. CSV import is power-user feature, rarely needed.

---

## Priority 3: Future Enhancements

### F. Persist Collapse State to localStorage

**Goal**: Remember user's expand/collapse preferences across sessions.

**Implementation**:
```jsx
import { useState, useEffect } from 'react';

function usePersistedCollapse(key, defaultValue) {
  const storageKey = `collapse:${key}`;

  const [isExpanded, setIsExpanded] = useState(() => {
    const stored = localStorage.getItem(storageKey);
    return stored !== null ? stored === 'true' : defaultValue;
  });

  useEffect(() => {
    localStorage.setItem(storageKey, String(isExpanded));
  }, [isExpanded, storageKey]);

  return [isExpanded, setIsExpanded];
}

// Usage in CollapsibleSection:
export default function CollapsibleSection({ id, defaultExpanded, ...props }) {
  const [isExpanded, setIsExpanded] = usePersistedCollapse(id, defaultExpanded);
  // ... rest of component
}
```

**Benefit**: User collapses "Divisions" once, stays collapsed on future visits.

---

### G. "Expand All" / "Collapse All" Controls

**Location**: Top of ManagePage Settings/Fields tabs

**Implementation**:
```jsx
const [expandAll, setExpandAll] = useState(false);

<div className="flex justify-end gap-2 mb-4">
  <button
    className="btn btn--sm"
    onClick={() => setExpandAll(true)}
  >
    Expand All Sections
  </button>
  <button
    className="btn btn--sm"
    onClick={() => setExpandAll(false)}
  >
    Collapse All Sections
  </button>
</div>

<CollapsibleSection
  title="Teams"
  defaultExpanded={expandAll || disclosure.teams}
  onChange={(expanded) => {/* track individual state */}}
>
  <TeamsManager />
</CollapsibleSection>
```

**Benefit**: Power users can see everything at once when debugging or reviewing configuration.

---

### H. Setup Progress Indicator

**Goal**: Show users how far along they are in league setup.

**Location**: ManagePage header or Commissioner Hub

**Implementation**:
```jsx
const leaguePhase = useLeaguePhase(leagueData);

{leaguePhase.isSetup && (
  <div className="card mb-4 bg-blue-50 border-blue-200">
    <div className="card__body">
      <div className="flex items-center justify-between mb-2">
        <span className="font-semibold">League Setup Progress</span>
        <span className="text-sm text-gray-600">{leaguePhase.setupProgress}% Complete</span>
      </div>
      <div className="w-full bg-gray-200 rounded-full h-2">
        <div
          className="bg-blue-500 h-2 rounded-full transition-all"
          style={{ width: `${leaguePhase.setupProgress}%` }}
        />
      </div>
      <div className="mt-2 text-sm text-gray-700">
        {leaguePhase.setupProgress < 30 && "Start by configuring your league settings and divisions"}
        {leaguePhase.setupProgress >= 30 && leaguePhase.setupProgress < 60 && "Next: Import fields and teams"}
        {leaguePhase.setupProgress >= 60 && leaguePhase.setupProgress < 90 && "Almost there! Set up availability and generate your schedule"}
        {leaguePhase.setupProgress >= 90 && "Setup complete! You're ready for the season"}
      </div>
    </div>
  </div>
)}
```

**Benefit**: Guides first-time commissioners through setup sequence.

---

## Implementation Checklist

### Phase 1: Core Components (This PR)
- [x] Create `CollapsibleSection.jsx` component
- [x] Create `useLeaguePhase.js` hook
- [ ] Write documentation (this file)

### Phase 2: High-Impact Areas (Next PR)
- [ ] Wrap ManagePage Settings tab cards in CollapsibleSection
- [ ] Wrap ManagePage Fields tab cards in CollapsibleSection
- [ ] Wrap LeagueSettings.jsx "League Season Configuration" in CollapsibleSection
- [ ] Convert LeagueSettings `<details>` to CollapsibleSection for consistency

### Phase 3: Medium-Impact Areas
- [ ] Add collapsible exception form in AvailabilityManager.jsx
- [ ] Wrap AdminPage CSV import sections in CollapsibleSection
- [ ] Add CollapsibleSection to FieldsImport.jsx (refactor existing collapse)

### Phase 4: Polish & Enhancements
- [ ] Implement localStorage persistence for collapse states
- [ ] Add "Expand All / Collapse All" controls
- [ ] Add setup progress indicator
- [ ] Add analytics tracking for which sections users expand (optional)

---

## Testing Checklist

### Functional Tests
- [ ] CollapsibleSection expands/collapses correctly
- [ ] Icons and badges display properly
- [ ] Nested CollapsibleSections work (exception forms)
- [ ] Touch targets are 44px+ on mobile
- [ ] Keyboard navigation works (Tab, Enter, Space)
- [ ] Screen readers announce expand/collapse state

### Visual Tests
- [ ] Collapsed sections have clear affordance (chevron icon)
- [ ] Badge colors are distinct and accessible
- [ ] Transitions are smooth (no layout jank)
- [ ] Works on mobile (320px width)
- [ ] Works on tablet (768px width)
- [ ] Works on desktop (1920px width)

### Integration Tests
- [ ] useLeaguePhase returns correct phase based on date logic
- [ ] useFeatureDisclosure suggests correct defaults
- [ ] Expand/collapse doesn't break lazy-loaded components (Suspense)
- [ ] Form state is preserved when collapsing/expanding sections

---

## Accessibility Considerations

### ARIA Attributes
```jsx
<button
  aria-expanded={isExpanded}
  aria-controls={`section-${id}`}
>
  <div id={`section-${id}`} role="region" aria-labelledby={`header-${id}`}>
    {children}
  </div>
</button>
```

### Keyboard Support
- `Tab`: Navigate to collapse button
- `Enter` or `Space`: Toggle expand/collapse
- `Tab` (when expanded): Navigate into content

### Focus Management
- When expanding, don't auto-focus first input (let user continue tabbing)
- When collapsing, return focus to toggle button

### Screen Reader Support
- Announce state: "Divisions, button, collapsed"
- Announce change: "Divisions, button, expanded"

---

## Rollout Strategy

### Option A: Feature Flag (Recommended)
```jsx
const ENABLE_PROGRESSIVE_DISCLOSURE = true; // Toggle via env var

{ENABLE_PROGRESSIVE_DISCLOSURE ? (
  <CollapsibleSection {...props}>
    {content}
  </CollapsibleSection>
) : (
  <div className="card">
    {content}
  </div>
)}
```

**Benefit**: Easy rollback if users report confusion.

### Option B: Gradual Rollout
1. Week 1: Ship components, enable on "Advanced" features only (Backups, CSV Import)
2. Week 2: Enable on Setup Phase features (Divisions, Fields)
3. Week 3: Enable on all identified sections
4. Week 4: Monitor feedback, iterate

### Option C: User Preference Toggle
Add a "Show all sections by default" checkbox in user settings.

---

## Success Metrics

### Quantitative
- **Page scroll depth**: Expect 40-60% reduction on ManagePage Settings/Fields tabs
- **Time to first action**: Measure if users find features faster (GA event tracking)
- **Support tickets**: Monitor for "Can't find X feature" tickets

### Qualitative
- **User feedback**: Survey commissioners on new layout
- **Usability testing**: Watch first-time commissioners navigate setup
- **Heat maps**: Track which sections users expand most

---

## FAQ

**Q: What if a user can't find a feature because it's collapsed?**
A: Use clear titles, subtitles, and badges. Consider adding a search/filter for sections.

**Q: Should we collapse everything by default?**
A: No. Frequently-used features (Commissioner Hub, Scheduler) should stay expanded. Use `useFeatureDisclosure` hook for smart defaults.

**Q: What about mobile vs desktop defaults?**
A: Mobile benefits more from collapsing. Consider `defaultExpanded={!isMobile && disclosure.feature}`.

**Q: How do we handle nested collapse sections?**
A: Ensure visual hierarchy (borders, background colors, indentation). Parent → child relationship should be clear.

**Q: Should we animate the expand/collapse?**
A: Yes, but keep it subtle (200-300ms). Respect `prefers-reduced-motion`.

---

## Related Files

- `src/components/CollapsibleSection.jsx` - Main component
- `src/lib/useLeaguePhase.js` - Phase detection hook
- `src/pages/ManagePage.jsx` - Primary usage
- `src/manage/LeagueSettings.jsx` - Complex nested case
- `src/manage/AvailabilityManager.jsx` - Exception form collapse
- `src/pages/admin/AdminPage.jsx` - CSV import collapse

---

## Notes

- Keep existing `<details>` elements for Season Reset (dangerous action, should be hidden)
- Don't collapse error/warning messages (always visible)
- Don't collapse empty states (e.g., "No teams yet - upload CSV")
- Consider animated height transitions (requires `max-height` or JS measurement)

**End of Implementation Guide**
