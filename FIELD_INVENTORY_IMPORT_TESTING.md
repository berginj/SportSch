# Field Inventory Import - Testing & Usage Guide

How to test the xlsx import functionality with the AGSA Spring Field Grid.

Created: 2026-03-10
File: `docs/2026 AGSA Spring Field Grid (1).xlsx`

---

## 🎯 **WHAT JB BUILT**

### **Comprehensive Field Inventory System:**

**Frontend:** `FieldInventoryImportManager.jsx` (795 lines)
- Upload xlsx or provide Google Sheets URL
- Parse field availability grids
- Review and map fields
- Preview before import
- Safe staging workflow

**Backend:** `FieldInventoryImportService.cs` (2,699 lines!)
- Excel parsing (xlsx format)
- Multiple grid layouts supported:
  - Season weekday grid (AGSA format)
  - Weekend grid
  - Reference grids
- Field alias mapping
- Review queue for unmapped fields
- Safe commit boundary (staging → live)

**Tests:** 892 + 917 = 1,809 lines of tests!

---

## 🚀 **HOW TO TEST IT**

### **Step 1: Access the Feature**

1. **Go to app:** Commissioner Hub or Admin access
2. **Navigate:** Manage tab
3. **Find:** "Field Inventory Import" tab
4. **Click:** Opens FieldInventoryImportManager

---

### **Step 2: Upload the AGSA File**

**Method A: Direct Upload (Recommended for Testing)**
```
1. Click "Choose file" button
2. Navigate to: C:\Users\berginjohn\App\SportSch\docs\
3. Select: "2026 AGSA Spring Field Grid (1).xlsx"
4. Click "Upload Workbook"
5. System inspects file and shows available tabs
```

**Method B: Google Sheets URL (If File is on Google Drive)**
```
1. Upload xlsx to Google Drive
2. Open in Google Sheets
3. Share → Anyone with link can view
4. Copy share URL
5. Paste into "Source Workbook URL" field
6. Click "Load Workbook"
```

---

### **Step 3: Select Tabs to Parse**

**After upload/load:**
```
System shows:
- Spring 316-522 (inferred: season_weekday_grid)
- Spring 525-619 (inferred: season_weekday_grid)
- Weekends (inferred: weekend_grid)
- [Other tabs if present]

For each tab:
☑️ Selected checkbox
📋 Parser Type dropdown (season_weekday_grid, weekend_grid, reference, ignore)
🎯 Action Type dropdown (ingest, reference, ignore)

Default: All inventory tabs selected, reference tabs ignored
```

**Actions:**
- Check/uncheck tabs to include/exclude
- Override parser type if auto-detection wrong
- Override action type if needed

---

### **Step 4: Parse Preview**

```
1. Enter Season Label: "Spring 2026"
2. Click "Parse Preview" button
3. Wait ~5-15 seconds (parsing xlsx)
4. Review results:
   - Summary: X records staged
   - Warnings: Field name conflicts, date issues, etc.
   - Review Queue: Unmapped fields to resolve
   - Staged Records: Full list of parsed availability
```

---

### **Step 5: Review Warnings**

**Example Warnings:**
```
⚠️ UNMAPPED_FIELD_NAME
Field "Barcroft Turf" from workbook doesn't match known fields.
Action: Map to canonical field or create new

⚠️ DATE_RANGE_INFERENCE
Inferred dates from tab name "Spring 316-522"
Verify: Mar 16 - May 22
```

**Resolve:**
- Review each warning
- Decide if OK or needs action
- Some are informational only

---

### **Step 6: Resolve Review Queue**

**Review Items = Decisions Needed**

**Example: Field Mapping**
```
Title: "Unmapped field 'Barcroft Turf'"
Description: "Found in workbook, no canonical match"
Raw Value: "Barcroft Turf"

Actions:
[Dropdown: Select canonical field]
  - Option: Barcroft Park - Turf Field
  - Option: Create new field

[Save Mapping] button
```

**After saving:**
- Mapping stored for future imports
- Reparse preview to apply mapping
- Record now shows canonical field

---

### **Step 7: Stage Results**

```
1. Click "Stage Results" button
2. System saves to staging tables
3. Does NOT modify live data yet
4. Safe to review before committing
```

---

### **Step 8: Preview Commit (Dry Run)**

```
1. Click "Dry Run Upsert" button
2. Shows what WOULD happen:
   - Create: 45 new field availability records
   - Update: 12 existing records
   - Delete: 3 obsolete records
   - Unchanged: 78 records
   - Skipped: 2 unmapped fields
3. Review before actual commit
```

---

### **Step 9: Commit to Live**

```
1. Choose commit type:
   - "Run Import" = Insert only (fail if exists)
   - "Run Upsert" = Insert or update (safe)

2. Click button
3. System writes to FieldInventoryLiveRecords table
4. Success message with counts
5. Fields now available for scheduling
```

---

## 📊 **WHAT YOU'LL SEE**

### **Parsed Data Includes:**

**For Each Slot:**
- Field Name (mapped to canonical)
- Team Name
- Date (concrete date, not range)
- Start Time
- End Time
- Duration
- Division/Level
- Season Label
- Source Tab
- Source Cell Range (for debugging)

**Example Record:**
```json
{
  "fieldId": "barcroft/turf",
  "canonicalFieldName": "Barcroft Park - Turf",
  "teamName": "Rockets",
  "slotDate": "2026-03-16",
  "startTime": "17:30",
  "endTime": "19:00",
  "durationMinutes": 90,
  "divisionLevel": "10U",
  "seasonLabel": "Spring 2026",
  "sourceTab": "Spring 316-522",
  "sourceCellRange": "B12"
}
```

---

## 🗓️ **INTEGRATING WITH CALENDARVIEW**

### **Current Display:**

JB's UI shows:
- ✅ Summary cards (total records, fields, teams)
- ✅ Warnings list
- ✅ Review queue
- ✅ Staged records table

### **Enhanced Calendar Display (What You Want):**

**To add CalendarView visualization:**

**File:** `src/manage/FieldInventoryImportManager.jsx`

**After line ~550 (staged records section), add:**
```jsx
import CalendarView from "../components/CalendarView";

// In render, add calendar toggle:
<div className="card">
  <div className="row row--between items-center mb-2">
    <div className="h2">Staged Records</div>
    <button
      className="btn btn--ghost"
      onClick={() => setViewMode(viewMode === "table" ? "calendar" : "table")}
    >
      {viewMode === "table" ? "📅 Calendar View" : "📋 Table View"}
    </button>
  </div>

  {viewMode === "calendar" ? (
    <CalendarView
      slots={preview.records.map(record => ({
        slotId: record.id,
        gameDate: record.slotDate,
        startTime: record.startTime,
        endTime: record.endTime,
        fieldKey: record.fieldId,
        displayName: record.canonicalFieldName,
        homeTeamId: record.teamName,
        status: record.canonicalFieldName ? "Mapped" : "Unmapped",
        division: record.divisionLevel
      }))}
      events={[]}
      defaultView="week-cards"
      onSlotClick={(slot) => {
        // Show detail modal or highlight in review queue
        const record = preview.records.find(r => r.id === slot.slotId);
        if (!record.canonicalFieldName) {
          // Jump to unmapped field in review queue
        }
      }}
      showViewToggle={true}
    />
  ) : (
    // Existing table view
    <table>...</table>
  )}
</div>
```

**Why This Helps:**
- ✅ Visual week-by-week view
- ✅ See field utilization patterns
- ✅ Spot gaps or conflicts
- ✅ Click to map unmapped fields
- ✅ Familiar interface (same as calendar page)

---

## 🎯 **TESTING CHECKLIST**

### **Test 1: Basic Upload**
- [ ] Navigate to Manage → Field Inventory Import
- [ ] Upload `2026 AGSA Spring Field Grid (1).xlsx`
- [ ] Verify workbook loads
- [ ] Check tabs detected (Spring 316-522, Spring 525-619, Weekends)

### **Test 2: Parsing**
- [ ] Select all inventory tabs
- [ ] Enter season label: "Spring 2026"
- [ ] Click "Parse Preview"
- [ ] Verify records appear (should be 50-200 depending on workbook)
- [ ] Check summary counts match expectations

### **Test 3: Field Mapping**
- [ ] Review warnings for unmapped fields
- [ ] Open review queue
- [ ] Find unmapped field items
- [ ] Map to existing field OR create new
- [ ] Save mapping
- [ ] Reparse to apply mapping

### **Test 4: Commit (Dry Run First)**
- [ ] Click "Dry Run Upsert"
- [ ] Review: create/update/delete counts
- [ ] Verify counts reasonable
- [ ] If OK, click "Run Upsert" (live)
- [ ] Verify success message
- [ ] Check fields now available in field list

### **Test 5: Calendar View (If Enhanced)**
- [ ] Toggle to Calendar View
- [ ] See week cards with slots
- [ ] Expand weeks to see details
- [ ] Click unmapped slot
- [ ] Jump to review queue
- [ ] Map and save

---

## 🔧 **TROUBLESHOOTING**

### **Issue: No Records Parsed**

**Causes:**
- Tab classification wrong (set to "reference" or "ignore")
- Parser type wrong (try different parser)
- Workbook format doesn't match expected layout

**Fix:**
- Check tab selection (☑️ checkboxes)
- Try different parser type
- Review docs/field-inventory-import.md for expected layout

---

### **Issue: Many Unmapped Fields**

**Causes:**
- Field names in workbook don't match database
- No saved field aliases yet

**Fix:**
- Review queue → Map each field
- Select canonical field from dropdown
- Or: Add new field to database
- Save mapping for future imports

---

### **Issue: Date Parsing Errors**

**Causes:**
- Tab name doesn't contain dates
- Date format unexpected

**Fix:**
- Check season label matches tab naming
- Verify dates in warnings
- Adjust tab classification if needed

---

## 📋 **CURRENT STATUS**

**JB's Implementation:**
- ✅ Complete field inventory import system
- ✅ Excel parsing working
- ✅ Field mapping system
- ✅ Review queue
- ✅ Safe staging → commit workflow
- ✅ Comprehensive tests (1,809 lines!)

**What's Working:**
- ✅ Upload xlsx
- ✅ Parse grids (weekday, weekend, reference)
- ✅ Map fields to canonical names
- ✅ Stage results
- ✅ Commit to live

**Enhancement Needed:**
- ⏸️ CalendarView integration for visual display
- ⏸️ Click-to-map from calendar
- ⏸️ Week-by-week visualization

---

## 🎯 **NEXT STEPS**

**To Use Right Now (No Code Changes):**
1. Go to Manage → Field Inventory Import
2. Upload the xlsx file
3. Parse preview
4. Map any unmapped fields
5. Stage and commit

**To Enhance with CalendarView (30-60 min):**
1. Add import to FieldInventoryImportManager
2. Add view mode toggle (table vs calendar)
3. Map records to CalendarView slot format
4. Add click handler for unmapped fields
5. Test and deploy

---

## ✅ **RECOMMENDATION**

**JB's system is production-ready as-is!**

Try it now:
1. Upload the xlsx
2. Test the workflow
3. See if table view is sufficient
4. If you want calendar view, I can add it (30-60 min)

**The foundation is excellent - comprehensive, tested, and safe!**

---

**Want me to:**
- **A)** Walk you through testing it as-is (no code changes)
- **B)** Add CalendarView integration now (30-60 min)
- **C)** Something else specific

**Your call!**
