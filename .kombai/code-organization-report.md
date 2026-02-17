# Subnautica Launcher - Code Organization Analysis Report

## Executive Summary

This report provides a comprehensive analysis of the codebase structure, identifies code duplications, unused files, and provides recommendations for better organization and consolidation.

---

## 1. Current Project Structure

```
SubnauticaLauncher/
├── Core/              ✅ Core application logic (App, Logger, Paths)
├── UI/                ✅ User interface windows and dialogs
├── Enums/             ✅ Enumeration types
├── Versions/          ✅ Subnautica version management
├── BelowZero/         ⚠️  Below Zero specific code (has duplication issues)
├── Updates/           ✅ Update checking and downloading
├── Installer/         ✅ Installation services
├── Macros/            ✅ Reset macro services
├── Memory/            ✅ Memory reading for game state
├── Gameplay/          ✅ Gameplay tracking and events
├── Explosion/         ✅ Explosion reset functionality
├── Converters/        ⚠️  WPF value converters (has duplication)
├── Settings/          ✅ Launcher settings
├── Properties/        ✅ .NET project properties
├── Assets/            ✅ Images and icons
├── tools/             ✅ External tools (AutoHotkey, SNLUpdater)
└── scripts/           ✅ Build scripts
```

---

## 2. Critical Issues Found

### 🔴 MAJOR DUPLICATIONS

#### A. Identical Enums (100% duplicate)
- **Files:**
  - `Enums/VersionStatus.cs`
  - `Enums/BZVersionStatus.cs`
- **Issue:** Both enums have IDENTICAL values (Idle, Switching, Launching, Launched, Active)
- **Impact:** Unnecessary code duplication, harder to maintain
- **Recommendation:** **Combine into single `VersionStatus` enum** with a generic type parameter or use a single enum for both games

#### B. Nearly Identical Classes (95% duplicate)
1. **InstalledVersion classes:**
   - `Versions/InstalledVersion.cs`
   - `BelowZero/BZInstalledVersion.cs`
   - **Difference:** Only the Status property type differs (VersionStatus vs BZVersionStatus)
   - **Recommendation:** Merge into single generic class or unified class

2. **VersionInstallDefinition classes:**
   - `Versions/VersionInstallDefinition.cs`
   - `BelowZero/BZVersionInstallDefinition.cs`
   - **Difference:** Only AppId and DepotId constants differ (264710/264712 vs 848450/848452)
   - **Recommendation:** Use single class with AppId/DepotId as instance properties or static factory methods

3. **DeleteVersionDialog (100% XAML duplicate):**
   - `UI/DeleteVersionDialog.xaml` + `.xaml.cs`
   - `BelowZero/BZDeleteVersionDialog.xaml` + `.xaml.cs`
   - **Issue:** XAML is IDENTICAL except x:Class attribute. Code-behind is also identical except enum type
   - **Recommendation:** **DELETE BelowZero version**, keep only in UI/ folder, use single generic dialog

4. **Converter classes (95% duplicate):**
   - `Converters/ActiveColorConverter.cs`
   - `Converters/BZActiveColorConverter.cs`
   - **Difference:** Only the enum type differs
   - **Recommendation:** Merge into single converter that handles both enum types

#### C. Naming Inconsistency
- **Files:**
  - `Installer/DepotDownloaderService.cs` (class name: `BZDepotDownloaderService` ❌ WRONG!)
  - `BelowZero/BZDepotDownloaderService.cs` (class name: `BZDepotDownloaderService` ✅ CORRECT)
- **Issue:** The file in Installer/ has the WRONG class name. Should be `DepotDownloaderService`
- **Recommendation:** Fix the class name in `Installer/DepotDownloaderService.cs`

---

## 3. File Organization Issues

### ⚠️ Misplaced Files

| Current Location | File | Should Be In | Reason |
|-----------------|------|--------------|--------|
| `Macros/` | `DisplayInfo.cs` | Create `Display/` folder | Uses namespace `SubnauticaLauncher.Display` |
| `BelowZero/` | `BZDeleteVersionDialog.xaml[.cs]` | `UI/` | It's a UI component, should be with other dialogs |
| `BelowZero/BZResetMacro/` | `BZResetMacroService.cs` | `Macros/` | It's a macro service, belongs with other macros |
| Root `/` | `TaskKill.txt` | `scripts/` or `tools/` | It's a utility script |
| Root `/` | `AssemblyInfo.cs` | `Properties/` | Standard .NET convention |

---

## 4. Unused Files to Remove

| File | Reason | Keep? |
|------|--------|-------|
| `TaskKill.txt` | Utility file, but should be moved to scripts/ | MOVE to scripts/ |
| `Updates/PossibleUpdates.txt` | User requested to keep | ✅ KEEP |

**Note:** No truly unused/dead code files found. All C# files appear to be referenced and used.

---

## 5. Recommended File Consolidations

### Priority 1: Critical Consolidations (Eliminate Duplication)

#### A. Merge Enums
**Create:** `Enums/VersionStatus.cs` (single unified enum)
```csharp
namespace SubnauticaLauncher.Enums
{
    public enum VersionStatus
    {
        Idle,
        Switching,
        Launching,
        Launched,
        Active
    }
}
```
**Delete:** `Enums/BZVersionStatus.cs`
**Update:** All references to `BZVersionStatus` → `VersionStatus`

#### B. Merge Converters
**Create:** `Converters/VersionStatusColorConverter.cs` (handles both)
```csharp
public class VersionStatusColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not VersionStatus status)
            return Brushes.White;

        return status switch
        {
            VersionStatus.Active => Brushes.LimeGreen,
            VersionStatus.Launched => Brushes.Red,
            VersionStatus.Launching => Brushes.Orange,
            VersionStatus.Switching => Brushes.Yellow,
            _ => Brushes.White
        };
    }
    // ...
}
```
**Delete:** 
- `Converters/ActiveColorConverter.cs`
- `Converters/BZActiveColorConverter.cs`

#### C. Consolidate Delete Dialogs
**Keep:** `UI/DeleteVersionDialog.xaml[.cs]`
**Modify:** Make it generic to support both games (pass game type as parameter)
**Delete:** 
- `BelowZero/BZDeleteVersionDialog.xaml`
- `BelowZero/BZDeleteVersionDialog.xaml.cs`

#### D. Merge Version Classes
**Option 1 - Factory Pattern:**
```csharp
// Versions/VersionInstallDefinition.cs
public sealed class VersionInstallDefinition
{
    public string Id { get; }
    public string DisplayName { get; }
    public long ManifestId { get; }
    public int AppId { get; }
    public int DepotId { get; }

    // Factory methods
    public static VersionInstallDefinition ForSubnautica(string id, string displayName, long manifestId)
        => new(id, displayName, manifestId, 264710, 264712);

    public static VersionInstallDefinition ForBelowZero(string id, string displayName, long manifestId)
        => new(id, displayName, manifestId, 848450, 848452);

    private VersionInstallDefinition(string id, string displayName, long manifestId, int appId, int depotId)
    {
        Id = id;
        DisplayName = displayName;
        ManifestId = manifestId;
        AppId = appId;
        DepotId = depotId;
    }
}
```
**Delete:** `BelowZero/BZVersionInstallDefinition.cs`

#### E. Merge InstalledVersion Classes
**Keep:** `Versions/InstalledVersion.cs` (already uses unified `VersionStatus` enum after merge)
**Delete:** `BelowZero/BZInstalledVersion.cs`

### Priority 2: File Reorganization

#### A. Create Display Folder
```
Display/
└── DisplayInfo.cs
```
Move `Macros/DisplayInfo.cs` → `Display/DisplayInfo.cs` (namespace already correct)

#### B. Consolidate UI Components
Move:
- `BelowZero/BZDeleteVersionDialog.*` → `UI/` (or delete if using unified dialog)

#### C. Consolidate Macros
Move:
- `BelowZero/BZResetMacro/BZResetMacroService.cs` → `Macros/BZResetMacroService.cs`
- Delete empty `BelowZero/BZResetMacro/` folder

#### D. Move Utility Files
Move:
- `TaskKill.txt` → `scripts/TaskKill.txt`
- `AssemblyInfo.cs` → `Properties/AssemblyInfo.cs`

### Priority 3: Fix Naming Issues

**File:** `Installer/DepotDownloaderService.cs`
**Fix:** Change class name from `BZDepotDownloaderService` to `DepotDownloaderService`

---

## 6. Projected File Count Reduction

| Category | Before | After | Reduction |
|----------|--------|-------|-----------|
| Enum files | 11 | 10 | -1 file |
| Converter files | 2 | 1 | -1 file |
| Dialog files | 4 files (2 .xaml + 2 .cs) | 2 files | -2 files |
| Version classes | 4 | 2 | -2 files |
| Service classes | 2 | 2 (but fixed naming) | 0 (but cleaner) |
| **Total** | **~90 files** | **~84 files** | **-6 files** |

**Additional benefits:**
- Reduced code duplication by ~40% in affected areas
- Clearer separation of concerns
- Easier maintenance
- Better namespace organization

---

## 7. BelowZero Folder Assessment

### Current Contents:
```
BelowZero/
├── BZDeleteVersionDialog.xaml[.cs]    → MOVE to UI/ or DELETE (duplicate)
├── BZDepotDownloaderService.cs        → KEEP (game-specific logic)
├── BZInstalledVersion.cs              → DELETE (merge with InstalledVersion)
├── BZVersionInstallDefinition.cs      → DELETE (merge with VersionInstallDefinition)
├── BZVersionLoader.cs                 → KEEP (game-specific logic)
├── BZVersionRegistry.cs               → KEEP (game-specific logic)
└── BZResetMacro/
    └── BZResetMacroService.cs         → MOVE to Macros/
```

### After Cleanup:
```
BelowZero/
├── BZDepotDownloaderService.cs        ✅ Game-specific installation
├── BZVersionLoader.cs                 ✅ Game-specific version loading
└── BZVersionRegistry.cs               ✅ Game-specific version registry
```

**Result:** BelowZero/ folder becomes focused ONLY on Below Zero specific logic, not duplicating common functionality.

---

## 8. Implementation Priority

### Phase 1: Quick Wins (Low Risk)
1. ✅ Move `TaskKill.txt` → `scripts/`
2. ✅ Move `AssemblyInfo.cs` → `Properties/`
3. ✅ Fix class name in `Installer/DepotDownloaderService.cs`
4. ✅ Create `Display/` folder and move `DisplayInfo.cs`
5. ✅ Move `BZResetMacroService.cs` → `Macros/`

### Phase 2: Consolidation (Medium Risk - Requires Testing)
6. ✅ Merge `VersionStatus` and `BZVersionStatus` enums
7. ✅ Update all references to use unified `VersionStatus`
8. ✅ Merge converter classes into single `VersionStatusColorConverter`
9. ✅ Update XAML files to use new converter

### Phase 3: Major Refactoring (Higher Risk - Requires Thorough Testing)
10. ✅ Merge `VersionInstallDefinition` classes using factory pattern
11. ✅ Merge `InstalledVersion` classes
12. ✅ Consolidate Delete dialog (make generic or delete BZ version)
13. ✅ Update all references throughout codebase
14. ✅ Full regression testing

---

## 9. Additional Recommendations

### Code Quality Improvements
1. **Consistent Naming:** Ensure all BZ-specific classes have "BZ" prefix
2. **Namespace Alignment:** Ensure file locations match namespaces
3. **Documentation:** Add XML documentation comments to public APIs
4. **Unit Tests:** Consider adding unit tests for core logic

### Future Architecture Considerations
1. **Game Abstraction:** Consider creating an interface `IGame` with implementations for Subnautica and Below Zero
2. **Dependency Injection:** Could simplify testing and reduce coupling
3. **MVVM Pattern:** Some UI code could benefit from proper ViewModel separation

---

## 10. Summary

The codebase is generally well-organized with clear separation of concerns. However, there are significant opportunities to:

- **Reduce duplication** by ~6 files and eliminate redundant code
- **Improve organization** by aligning namespaces with folder structure
- **Simplify maintenance** by consolidating game-specific logic using factory patterns or interfaces

**Estimated effort:** 4-6 hours for complete implementation and testing

**Risk level:** Medium (requires careful testing of version management and UI dialogs)

**Benefit:** Cleaner, more maintainable codebase with less duplication
