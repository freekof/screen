# Canvas overlap and off-screen windows implementation plan

> **For agentic workers:** Follow this plan task by task. Steps use checkbox syntax for tracking.

**Goal:** Allow Canvas zones to overlap and extend partially off-screen, and make one-key window placement raise windows in zone order without stealing focus.

**Architecture:** Grid geometry and grid editing remain unchanged. Canvas zones use raw signed normalized rectangles without screen clipping or spacing insets. The canvas editor allows partial off-screen placement while retaining at least 40 visible pixels and keeps labels/program selectors inside the visible area. `ApplyLayoutWindows` processes canvas zones in ascending index order and raises each window without activating it.

**Tech Stack:** C# / .NET 8 WinForms, System.Drawing, Win32 `SetWindowPos`.

---

### Task 1: Preserve raw Canvas geometry

**Files:**
- Modify: `ScreenCaptureTool/FancyZones/ZoneModels.cs`

- [ ] Branch in `GetZoneRects` on `Kind`. For Canvas, return `Rectangle.FromLTRB` values computed directly from each fraction and `workArea` without spacing insets or intersection/clipping. Keep the existing Grid path unchanged.
- [ ] Update the `ZoneRect` documentation to state Canvas ratios may be less than zero or extend beyond one.

### Task 2: Allow recoverable off-screen Canvas editing

**Files:**
- Modify: `ScreenCaptureTool/FancyZones/Editor/CanvasEditorForm.cs`

- [ ] Replace the work-area-only `ClampZoneRect` with a clamp that limits width and height to at least 40 pixels and at most three work-area dimensions, and allows x/y outside the work area while preserving at least 40 visible pixels on each axis.
- [ ] In `SyncModelFromZones`, save signed x/y and unbounded width/height ratios from the raw zone rectangles; do not clamp normalized values to `[0,1]`.
- [ ] Keep `zoneRects` as raw editable geometry and use `GetDisplayZoneRects()` for drawn zones, hit testing, labels, and handles.
- [ ] Anchor dimension labels and program ComboBoxes to the intersection of each zone with the visible work area. Hide a selector only when the visible intersection is too small to host it.
- [ ] Preserve the Canvas zone list order when reordering, deleting, adding, and saving zones; that order remains the z-order (higher index is in front).

### Task 3: Bring one-key-adjusted windows to front in zone order

**Files:**
- Modify: `ScreenCaptureTool/FancyZones/NativeMethods.cs`
- Modify: `ScreenCaptureTool/FancyZones/FancyZonesManager.cs`

- [ ] Add an `HWND_TOP` declaration using `IntPtr.Zero`.
- [ ] Add optional `bringToFront = false` to `SnapWindow`. Keep current `SWP_NOZORDER | SWP_NOACTIVATE` behavior when false. When true, call `SetWindowPos` with `HWND_TOP` and `SWP_NOACTIVATE` without `SWP_NOZORDER`.
- [ ] In `ApplyLayoutWindows`, apply selected windows in ascending zone-index order and pass `bringToFront: true`. Leave regular drag/drop snapping at the default false value.

### Task 4: Review and delegate delivery

**Files:**
- Verify the three source files above and the Canvas editor interactions.

- [ ] Run `git diff --check` and inspect the final diff; verify no Grid code path changed.
- [ ] Do not run local builds/tests/UI tests per the user's established instruction.
- [ ] Delegate commit/push to GitHub `main` and Windows Actions monitoring to the requested Gemini 3.8 Flash High agent. Report commit SHA, Actions run conclusion, and artifact link.
- [ ] User verifies that Canvas zones can overlap and remain partly visible outside the work area, and that one-key placement leaves the highest-index overlapping window on top without changing keyboard focus.
