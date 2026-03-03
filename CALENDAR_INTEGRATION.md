# CalendarView Component Integration Guide

## Overview

The new `CalendarView` component provides two clean calendar display options:
- **Week Cards** (Option 1): Compact expandable cards showing week summaries
- **Agenda List** (Option 4): Chronological mobile-friendly list

## Component Location

- **Component**: `src/components/CalendarView.jsx`
- **Styles**: `src/components/CalendarView.css`
- **Status**: ✅ Created, committed, ready to integrate

## Where to Apply

### 1. CalendarPage (Primary Calendar)
**File**: `src/pages/CalendarPage.jsx`
**Current**: Linear timeline list (lines 958-1095)
**Replace with**: CalendarView component

**Before:**
```jsx
<div className="stack">
  {timeline.map((it) => {
    // 137 lines of rendering logic per item
  })}
</div>
```

**After:**
```jsx
<CalendarView
  slots={slots.filter(/* current filters */)}
  events={events}
  defaultView="week-cards"
  onSlotClick={handleSlotClick}
  onEventClick={handleEventClick}
  showViewToggle={true}
/>
```

### 2. SeasonWizard Preview - Calendar View
**File**: `src/manage/SeasonWizard.jsx`
**Current**: Timeline + calendar grid (lines 6269-6417)
**Keep current** for administrative detail, optionally add CalendarView as alternate

**Option A - Replace:**
```jsx
<CalendarView
  slots={previewCollections.regularAssignments}
  defaultView="week-cards"
  onSlotClick={setSelectedExplainGameKey}
  showViewToggle={true}
/>
```

**Option B - Add as Alternative Tab:**
```jsx
<div className="row gap-2">
  <button onClick={() => setPreviewView("detailed")}>Detailed Grid</button>
  <button onClick={() => setPreviewView("calendar")}>Calendar View</button>
</div>

{previewView === "calendar" ? (
  <CalendarView ... />
) : (
  /* existing detailed grid */
)}
```

### 3. SchedulePage (Optional)
**File**: `src/pages/SchedulePage.jsx`
**Current**: Unknown (need to check implementation)
**Could benefit from**: CalendarView for coach schedule viewing

### 4. CoachDashboard (Optional)
**File**: `src/pages/CoachDashboard.jsx` or similar
**Use case**: Show upcoming games for coach's team in agenda view

---

## Integration Steps (Recommended Order)

### Phase 1: CalendarPage (Primary Integration)
**Estimated effort**: 2-3 hours

1. **Import component:**
   ```jsx
   import CalendarView from "../components/CalendarView";
   ```

2. **Transform data to match component API:**
   ```jsx
   const calendarSlots = slots.map(slot => ({
     slotId: slot.slotId,
     gameDate: slot.gameDate,
     startTime: slot.startTime,
     endTime: slot.endTime,
     fieldKey: slot.fieldKey,
     displayName: slot.displayName,
     parkName: slot.parkName,
     fieldName: slot.fieldName,
     homeTeamId: slot.homeTeamId,
     awayTeamId: slot.awayTeamId,
     offeringTeamId: slot.offeringTeamId,
     confirmedTeamId: slot.confirmedTeamId,
     status: slot.status,
     gameType: slot.gameType,
     isExternalOffer: slot.isExternalOffer,
     isAvailability: slot.isAvailability,
     division: slot.division,
   }));
   ```

3. **Add slot click handler:**
   ```jsx
   const handleSlotClick = (slot) => {
     // Open edit dialog or show details
     if (canAcceptSlot(slot)) {
       // Show accept flow
     } else if (role === "LeagueAdmin") {
       // Show edit dialog
     }
   };
   ```

4. **Replace current timeline rendering:**
   - Remove lines 958-1095 (current timeline.map)
   - Replace with `<CalendarView .../>`

5. **Test thoroughly:**
   - Click slots to ensure handlers work
   - Test expand/collapse
   - Test view toggle
   - Verify filters still work
   - Test on mobile

### Phase 2: SeasonWizard Preview (Optional Enhancement)
**Estimated effort**: 1-2 hours

Add as a supplementary view tab alongside existing detailed grid.

### Phase 3: Other Pages (As Needed)
**Estimated effort**: 1 hour each

---

## Component API Reference

### Props

| Prop | Type | Default | Description |
|------|------|---------|-------------|
| `slots` | `Array<Slot>` | `[]` | Array of slot objects |
| `events` | `Array<Event>` | `[]` | Array of event objects |
| `defaultView` | `"week-cards" \| "agenda"` | `"week-cards"` | Initial view mode |
| `onSlotClick` | `(slot) => void` | `undefined` | Callback when slot is clicked |
| `onEventClick` | `(event) => void` | `undefined` | Callback when event is clicked |
| `showViewToggle` | `boolean` | `true` | Show/hide view mode toggle buttons |

### Slot Object Shape

```typescript
{
  slotId: string,
  gameDate: string,        // YYYY-MM-DD
  startTime: string,       // HH:MM
  endTime: string,         // HH:MM
  fieldKey: string,
  displayName?: string,
  parkName?: string,
  fieldName?: string,
  homeTeamId?: string,
  awayTeamId?: string,
  offeringTeamId?: string,
  confirmedTeamId?: string,
  status: string,          // "Open", "Confirmed", "Cancelled"
  gameType?: string,       // "practice", "game", etc.
  isExternalOffer?: boolean,
  isAvailability?: boolean,
  division?: string,
}
```

### Event Object Shape

```typescript
{
  eventId: string,
  date: string,            // YYYY-MM-DD
  title: string,
  description?: string,
}
```

---

## Layout Specifications

### Week Cards View (Option 1)

**Collapsed State:**
- Height: ~60px per week
- Shows: Week range, totals, 7-day micro grid
- Day cells show: Game count (e.g., "2g") or "-" if none

**Expanded State:**
- Height: Variable (~100-400px depending on games)
- Shows: Full day-by-day breakdown
- Each game: Time, field, matchup
- Grouped by day, then by field

**Space Savings:**
- 10 weeks collapsed: ~600px (vs ~2000px in current list)
- Progressive disclosure: expand only what you need

### Agenda View (Option 4)

**Layout:**
- Groups by day (header)
- Then by field (subheader)
- Then individual games
- Fully linear, no grid

**Space:**
- ~80-120px per game
- More than Week Cards but better for mobile
- No horizontal scroll

**Mobile Optimization:**
- Single column
- Large touch targets
- Clear hierarchy

---

## Visual Design

### Color Coding

**Week Cards:**
- Background: Light gray (#f9fafb)
- Border: Standard (#e5e7eb)
- Hover: Slight darken (#f3f4f6)
- Expanded background: #fafafa

**Day Cells:**
- Games: Blue (#1d4ed8)
- Open: Green (#059669)
- None: Gray text

**Agenda Items:**
- Border-left: 3px blue (#3b82f6)
- Hover: Transform right slightly
- Events: Purple accent (#8b5cf6)

### Typography

- Week dates: 0.95rem, font-weight 600
- Stats: 0.85rem, color #6b7280
- Day labels: 0.8rem, font-weight 600
- Game times: 0.85-0.9rem, font-weight 600

---

## Testing Checklist

Before integrating into production pages:

- [ ] Week cards expand/collapse correctly
- [ ] Day summaries show accurate counts
- [ ] Agenda view groups by field properly
- [ ] View toggle saves to localStorage
- [ ] Slot click handlers fire correctly
- [ ] Event click handlers fire correctly
- [ ] Mobile responsive (test at 375px, 768px)
- [ ] Keyboard accessible
- [ ] Works with empty data
- [ ] Works with 100+ games
- [ ] Week calculation is correct (Monday start)
- [ ] Multi-year seasons work

---

## Migration Strategy

### Recommended Approach:

1. **Don't replace existing calendar immediately**
2. **Add CalendarView as a new view option first**
3. **Let users try both side-by-side**
4. **Gather feedback**
5. **Then decide which to make default**

### Feature Flag Approach:

```jsx
const [useNewCalendar, setUseNewCalendar] = useState(false);

{useNewCalendar ? (
  <CalendarView slots={slots} events={events} />
) : (
  /* existing calendar code */
)}

<button onClick={() => setUseNewCalendar(!useNewCalendar)}>
  {useNewCalendar ? "Use Classic View" : "Try New Calendar"}
</button>
```

This allows safe rollback if issues arise.

---

## Performance Considerations

- Week grouping is memoized (useMemo)
- Expand/collapse uses Set for O(1) lookup
- Only re-renders changed weeks
- LocalStorage access is try-caught

---

## Future Enhancements

1. **Quick Jump Navigation**
   - "This Week" button
   - "Next Week" / "Previous Week" arrows
   - Date picker to jump to specific week

2. **Bulk Actions**
   - Expand/collapse all weeks
   - Print view mode
   - Export visible range

3. **Filtering**
   - Filter by team
   - Filter by field
   - Filter by status
   - Search box

4. **Visual Enhancements**
   - Utilization heat colors
   - Conflict indicators
   - Weather warnings
   - Late-season highlighting

---

**Status**: Component ready, not yet integrated
**Next Step**: Choose integration point (CalendarPage recommended)
**Risk**: Low - component is isolated, doesn't affect existing code
