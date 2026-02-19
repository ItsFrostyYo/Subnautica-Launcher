# MainWindow + Overlay Design Review (Validated v2)

**Date**: 2026-02-19  
**Reviewed Files**: `UI/MainWindow.xaml`, `UI/LauncherOverlayWindow.xaml`, `UI/MainWindow.xaml.cs`, `UI/LauncherOverlayWindow.xaml.cs`, `UI/Styles/CommonStyles.xaml`, `App.xaml`

## Executive Verdict
The original review contains useful direction, but it overstates severity in several places and includes stale findings. The launcher has a solid visual identity and good functional coverage. The main gaps are style/resource plumbing, accessibility basics, and maintainability consistency.

## What I Agree With
1. Shared style resources are not wired cleanly enough yet (`App.xaml` has no merged dictionaries).
2. Accessibility is under-served (focus visibility, automation names, keyboard clarity).
3. Color and spacing tokens are not centralized.
4. Main + overlay duplicate style logic (`DarkComboBox`, local button templates).
5. Overlay and main both use many hardcoded values that are harder to maintain.
6. Social/action buttons should have tooltips and clearer semantics.
7. View transitions and polish can be improved later.

## What I Partially Disagree With (or Reprioritize)
1. `LauncherOverlayWindow.xaml` width `1920` is not the runtime size problem by itself.
   - Runtime sizing is already applied in `LauncherOverlayWindow.ApplyOverlaySizing()`.
   - Keep as a fallback value, but still worth cleaning.
2. "Critical" contrast claims are too broad without actual measured contrast per control/state.
   - Needs targeted testing, not blanket severity.
3. "No loading state" is true for some flows, but not the immediate UX blocker compared with accessibility and style consistency.
4. MVVM migration is valuable, but too large for immediate UI iteration. This should be long-term.

## Validated Priority List

### P0 (Do First)
1. Wire global style dictionaries in `App.xaml`.
2. Add consistent keyboard focus visuals.
3. Add automation names on window chrome buttons and key action buttons.
4. Add dropdown max-height defaults to prevent giant popups.

### P1 (Do Next)
1. Consolidate duplicate button and combo styles between main/overlay.
2. Add tooltips for social/media buttons and ambiguous controls.
3. Normalize a small token set for typography and spacing.

### P2 (Later)
1. Introduce subtle transitions for tab/view changes.
2. Introduce a lightweight design token dictionary (`Colors.xaml`, `Typography.xaml`).
3. Consider incremental MVVM extraction for high-churn views.

## Implementation Plan (Safe Iteration)

### Phase 1 - Safety + Accessibility + Resource Wiring
- Merge `UI/Styles/CommonStyles.xaml` via `App.xaml`.
- Add `FocusVisualStyle` and keyboard-friendly focus cues.
- Add `AutomationProperties.Name` for titlebar and icon-only actions.
- Add `MaxDropDownHeight` on custom combo styles.
- Add tooltips on social buttons.

### Phase 2 - Consistency Cleanup
- Reduce duplicated style blocks in main/overlay.
- Move repeated colors into named resources.
- Keep behavior unchanged.

### Phase 3 - Optional UX Polish
- Add lightweight fade transitions between views.
- Add optional loading indicators for longer-running operations.

## Acceptance Criteria
1. Build succeeds with no XAML/resource lookup errors.
2. Keyboard tab/focus is visually clear on interactive controls.
3. Overlay and main window behavior remains unchanged.
4. No regressions in launch/install/reset workflows.

## Phase 1 + 2 Status In This Branch
Implemented:
1. `App.xaml` now merges `UI/Styles/CommonStyles.xaml`.
2. `RoundedButtonBase` now includes keyboard focus visuals and a visible focused border state.
3. `DarkComboBox` styles now define `MaxDropDownHeight` and keyboard-focus border cues in:
   - `UI/MainWindow.xaml`
   - `UI/LauncherOverlayWindow.xaml`
   - `UI/Subnautica100TrackerCustomizeWindow.xaml`
4. Title bar window controls in `UI/MainWindow.xaml` now include automation names and tooltips.
5. Social link buttons now include tooltips in both main and overlay windows.
6. Main + overlay now use shared color tokens in `UI/Styles/CommonStyles.xaml` for primary button/status/panel/divider colors.
7. Main + overlay `ComboBox` variants are centralized in shared styles:
   - `MainDarkComboBox`
   - `OverlayDarkComboBox`
8. Overlay controls that regressed were restored in the main settings UI:
   - Startup mode selector
   - Overlay hotkey capture
   - Overlay transparency slider
   - Direct "Open Launcher Overlay" button
9. Overlay runtime plumbing restored for usability:
   - Overlay window lifecycle management from `MainWindow`
   - Global overlay toggle hotkey registration/handling
   - Overlay settings persistence (`StartupMode`, `OverlayToggleKey`, `OverlayToggleModifiers`, `OverlayPanelOpacity`)

Still planned:
1. Phase 3 motion/loading polish (optional).

## Notes
- The launcher is already functionally strong. This review focuses on maintainability and usability upgrades without changing your current flow.
- Changes should be iterative and reversible with save-points between phases.
