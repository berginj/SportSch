# Import AGSA xlsx File - Quick Start

The xlsx file is in the repo but NOT yet imported to the database.
Follow these steps to import it so you can view it on the web.

---

## ❌ **CURRENT STATE**

**File Location:** `docs/2026 AGSA Spring Field Grid (1).xlsx` (157KB)
**Status:**
- ✅ In git repo
- ✅ Used in automated tests (fixture)
- ❌ **NOT imported to live database**
- ❌ **NOT viewable on web yet**

**To View on Web:** You must import it first (5 minutes)

---

## ✅ **HOW TO IMPORT IT NOW (5 Minutes)**

### **Step 1: Open Field Inventory Import**
```
1. Go to your app: https://softball.sports.fifthseasonadvisors.com
2. Sign in
3. Click "Manage" tab
4. Click "Field Inventory Import" tab
```

### **Step 2: Upload the File**
```
Method: Direct File Upload (simplest)

1. In "Upload Workbook" section
2. Click "Choose file" button
3. Navigate to your local repo:
   C:\Users\berginjohn\App\SportSch\docs\
4. Select: "2026 AGSA Spring Field Grid (1).xlsx"
5. Click "Upload Workbook" button
6. Wait ~3-5 seconds
7. System shows: "Workbook uploaded. Select tabs and parse a preview."
```

**Or use deployed version:**
```
The file is in your GitHub repo, so it's deployed to the static web app.
You could access it via:
https://softball.sports.fifthseasonadvisors.com/docs/2026%20AGSA%20Spring%20Field%20Grid%20(1).xlsx

But the import system needs local upload or Google Sheets URL.
```

### **Step 3: Select Tabs**
```
System auto-detects tabs:
☑️ Spring 316-522 (Parser: season_weekday_grid)
☑️ Spring 525-619 (Parser: season_weekday_grid)
☑️ Weekends (Parser: weekend_grid)

All should be checked by default. Leave as-is.
```

### **Step 4: Parse Preview**
```
1. Enter Season Label: "Spring 2026"
2. Click "Parse Preview" button
3. Wait ~10-20 seconds (parsing excel)
4. See results:
   - Summary: X records staged
   - Warnings: Field mapping issues
   - Review Queue: Unmapped fields
   - Staged Records: All parsed slots
```

### **Step 5: View in Calendar**
```
In Staged Records section:
1. Click "📅 Calendar View" button
2. See week-by-week field availability
3. Toggle to "📋 Table View" for details
4. Review the data
```

### **Step 6: Map Unmapped Fields**
```
If Review Queue shows unmapped fields:
1. For each unmapped field:
   - Select canonical field from dropdown
   - Or: Create new field
   - Click "Save Mapping"
2. Reparse to apply mappings
```

### **Step 7: Commit to Live Database**
```
1. Click "Dry Run Upsert" button
2. Review counts:
   - Create: X new records
   - Update: Y existing records
   - Delete: Z obsolete records
3. If looks good:
   - Click "Run Upsert" button
4. Wait ~5-10 seconds
5. See success message
6. **Data is now in live database!**
```

---

## 🎯 **AFTER IMPORT - WHERE TO VIEW**

### **Option 1: Field Inventory Import Page**
**Path:** Manage → Field Inventory Import
**What:** Latest imported data
**Views:** Calendar or Table toggle

### **Option 2: Calendar Page** (If integrated)
**Path:** Calendar tab
**What:** All scheduled games + availability
**Note:** Field inventory may not show here yet (depends on integration)

### **Option 3: Fields Page**
**Path:** Manage → Fields
**What:** Field definitions
**Note:** This is field metadata, not availability schedule

---

## ⚠️ **IMPORTANT NOTES**

### **The xlsx File Is:**
- ✅ In your git repo
- ✅ Deployed to static web app
- ✅ Used in automated tests
- ❌ **NOT automatically imported on startup**

### **To Make It Viewable:**
- **You MUST run the import workflow** (steps above)
- One-time process (5 minutes)
- After import, data is in database
- Then it's viewable on web

### **Why Not Auto-Import?**
- Safe by design (no auto-mutations)
- Requires admin approval
- Allows field mapping review
- Prevents accidental overwrites

---

## 🚀 **QUICK ACTION PLAN**

**To View on Web Right Now:**
```
1. Go to app
2. Manage → Field Inventory Import
3. Upload the xlsx file (from docs/ folder)
4. Parse preview
5. Click "📅 Calendar View"
6. View the data! ✅

Optional (to persist):
7. Map any unmapped fields
8. Click "Run Upsert"
9. Data now in database permanently
```

**Time:** 5 minutes for steps 1-6 (viewing)
**Time:** 10-15 minutes total (if importing to database)

---

## ✅ **ANSWER TO YOUR QUESTION**

**"Have we successfully done that in a way I can go view it on the web?"**

**Answer:**
- ✅ **System is ready** (field inventory import works)
- ✅ **File is in repo** (docs/2026 AGSA Spring Field Grid (1).xlsx)
- ✅ **Calendar view added** (just deployed!)
- ❌ **NOT imported yet** (you need to run the workflow)

**Next Step:**
Go to Field Inventory Import page and upload the file. You'll see it immediately in calendar view (no database commit needed for preview).

**Then:** If you want it persistent, click "Run Upsert" to commit to database.

---

**The system is 100% ready. You just need to upload the file through the UI!** 🎯

**Everything is deployed. Try it now and let me know what you see!** 🚀

**Session: 727k tokens - complete!**