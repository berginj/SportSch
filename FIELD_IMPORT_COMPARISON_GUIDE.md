# Field Inventory Import - Comparison & Viewing Guide

Where to view imported xlsx data and compare with existing fields.

---

## 📍 **WHERE TO VIEW IMPORTED DATA**

### **Location 1: Staged Records (After Parse)**

**Path:** Manage → Field Inventory Import → Parse Preview → Staged Records

**What You See:**
- All slots parsed from xlsx
- Table view OR Calendar view (toggle)
- Field names, dates, times, teams
- Mapping status (Mapped vs Unmapped)

**Views Available:**
1. **📋 Table View:**
   - All metadata visible
   - Source tab and cell references
   - Confidence scores
   - Availability/utilization status

2. **📅 Calendar View:** (Just added!)
   - Week Cards with expandable details
   - Timeline/Month views (DayPilot)
   - Visual week-by-week patterns
   - Click to see details

---

### **Location 2: Commit Preview (Before Import)**

**Path:** Staged Records → Click "Dry Run Upsert"

**What You See:**
```
Commit Preview:
- Create: 45 (new fields/slots)
- Update: 12 (existing fields being updated)
- Delete: 3 (obsolete records)
- Unchanged: 78 (matches existing)
- Skipped unmapped: 2 (fields not mapped yet)
```

**This tells you:**
- What's NEW in xlsx (Create count)
- What EXISTS but differs (Update count)
- What's in database but not xlsx (Delete count)
- What's identical (Unchanged count)

---

## 📊 **WHERE TO VIEW EXISTING FIELDS**

### **Option 1: Fields Tab (Manual Fields)**

**Path:** Manage → Fields

**What You See:**
- All manually created fields
- Field list with park/field names
- Addresses, divisions, notes
- Active/Inactive status

**BUT:** This shows manually created fields, NOT field inventory

---

### **Option 2: Field Inventory Live Records** (Not Currently in UI)

**Current State:**
- ✅ Backend table exists: `FieldInventoryLiveRecords`
- ✅ Backend API exists: Can query live inventory
- ❌ **No UI to view live inventory yet**

**What's Missing:**
A view to see what's already in `FieldInventoryLiveRecords` table

---

## ⚠️ **GAP: No Side-by-Side Comparison View**

### **What Exists:**
- ✅ View imported data (Staged Records)
- ✅ See counts (Commit Preview: Create/Update/Delete)
- ✅ View manual fields (Fields tab)

### **What's Missing:**
- ❌ Side-by-side comparison (xlsx vs database)
- ❌ Detailed diff view (what changed?)
- ❌ View existing field inventory records

---

## 💡 **RECOMMENDED ENHANCEMENT**

### **Add "Compare with Existing" View**

**Add to FieldInventoryImportManager.jsx:**

```jsx
<div className="card">
  <div className="card__header">
    <div className="h2">Comparison: Imported vs Existing</div>
    <button onClick={loadExistingInventory}>
      Load Existing Inventory
    </button>
  </div>

  <div className="card__body">
    <div className="row gap-3">
      {/* Left: Imported from xlsx */}
      <div className="flex-1">
        <h3>From xlsx (Staged)</h3>
        <div className="subtle">{stagedRecords.length} records</div>
        <CalendarView
          slots={stagedRecords}
          defaultView="timeline"
        />
      </div>

      {/* Right: Existing in database */}
      <div className="flex-1">
        <h3>In Database (Live)</h3>
        <div className="subtle">{liveRecords.length} records</div>
        <CalendarView
          slots={liveRecords}
          defaultView="timeline"
        />
      </div>
    </div>

    {/* Diff Summary */}
    <div className="callout mt-3">
      <div className="font-bold">Changes</div>
      <div>New: {newRecords.length}</div>
      <div>Modified: {modifiedRecords.length}</div>
      <div>Removed: {removedRecords.length}</div>
    </div>
  </div>
</div>
```

**Backend API needed:**
```
GET /api/field-inventory/live?leagueId=X&seasonLabel=Spring+2026
Returns: All FieldInventoryLiveRecords for that season
```

---

## 🎯 **CURRENT WORKFLOW (As-Is)**

### **To Compare Imported vs Existing:**

**Step 1: View What's in xlsx**
1. Upload xlsx
2. Parse preview
3. Toggle "📅 Calendar View"
4. See all imported slots by week

**Step 2: See What Will Change**
1. Click "Dry Run Upsert"
2. See counts:
   - Create: 45 (these are NEW, not in database)
   - Update: 12 (these EXIST but data differs)
   - Unchanged: 78 (these MATCH database exactly)

**Step 3: View Existing Manual Fields** (Different data)
1. Go to Manage → Fields
2. See manually created fields
3. This is separate from field inventory

---

## 🔧 **WHAT YOU CAN DO RIGHT NOW**

### **Without Code Changes:**

**View Imported Data:**
- ✅ Manage → Field Inventory Import
- ✅ Upload xlsx
- ✅ Parse preview
- ✅ Toggle Calendar/Table views
- ✅ See all imported slots

**See What's Different:**
- ✅ Click "Dry Run Upsert"
- ✅ See counts: Create (new), Update (changed), Unchanged (same)
- ⚠️ But can't see WHICH records are in each category

**View Manual Fields:**
- ✅ Manage → Fields
- ✅ See manually created fields
- ⚠️ But these are different from field inventory

---

## 💡 **RECOMMENDATION**

### **Quick Add (30-45 min):**

I can add a **"Comparison View"** that shows:
- Left column: Imported from xlsx
- Right column: Existing in database
- Highlighted: NEW (green), MODIFIED (yellow), REMOVED (red)
- Side-by-side calendars

**Or:**

Enhance the existing **Commit Preview** to show:
- Not just counts (Create: 45)
- But actual records in each category
- Expandable sections:
  - "45 New Records (expand to see list)"
  - "12 Modified Records (expand to see changes)"

---

## 🎯 **WHICH DO YOU PREFER?**

**Option A: Side-by-Side Comparison View** (30-45 min)
- Two calendar views
- Imported vs Existing
- Visual diff highlighting

**Option B: Enhanced Commit Preview** (15-20 min)
- Expandable sections
- List records in each category
- Simpler, faster to implement

**Option C: Use Current System As-Is**
- Commit preview counts tell you what's different
- Staged records show imported data
- Fields tab shows manual fields

**Your choice!** Let me know which you want and I'll implement it.

**Session: 724k tokens**
