# FancyZones editor alignment implementation plan

> **For agentic workers:** Follow this plan task by task. The owner requested no local build/UI testing and delegated push plus GitHub Actions tracking to the Gemini 4 Argon High agent.

**Goal:** Keep layout controls inside their window, make snap rectangles match the editor wireframes without an extra outer-edge inset, and activate zones only while Shift and the right mouse button are both held during a window drag.

**Architecture:** `ZoneLayout.GetZoneRects` remains the canonical geometry shared by the snap manager and the editors. Work-area edges stay flush; spacing is applied only to internal/zone boundaries. Canvas editing retains unsnapped relative model rectangles but renders and hit-tests the same effective rectangles used for snapping. A low-level mouse hook tracks and suppresses right-button events only during the system window move loop, preventing a context menu from interrupting the new chord.

**Tech Stack:** C# / .NET 8 WinForms, existing Win32 P/Invoke helpers, `System.Drawing`.

---

### Task 1: Fix editor window client-area sizing

**Files:**
- Modify: `ScreenCaptureTool/FancyZones/Editor/LayoutEditorForm.cs`

- [ ] Set `AutoScaleMode = AutoScaleMode.Dpi` and `AutoScaleDimensions = new SizeF(96F, 96F)` before creating child controls.
- [ ] Replace the outer `Size = new Size(720, 520)` assignment with an explicit `ClientSize = new Size(720, 520)` so the bottom-most controls at Y=468..504 fit inside the client area.
- [ ] Anchor the layout list and preview to the top/left as appropriate; keep bottom action buttons at fixed client-relative Y positions. Keep `FormBorderStyle.FixedDialog` so this explicit client layout cannot be shrunk by dragging.

### Task 2: Make edge-touching zone geometry flush and share it with grid editor

**Files:**
- Modify: `ScreenCaptureTool/FancyZones/ZoneModels.cs`
- Modify: `ScreenCaptureTool/FancyZones/Editor/GridEditorForm.cs`
- Modify: `ScreenCaptureTool/FancyZones/Editor/LayoutEditorForm.cs`

- [ ] In `GetZoneRects`, stop shrinking the whole work area before applying fractions. For each zone, add a half-spacing inset only on sides that border another zone or lie inside a free-form canvas; do not inset sides whose normalized coordinate touches the work-area edge (tolerance `1e-6`). Split odd spacing as `lowInset = spacing / 2` and `highInset = spacing - lowInset` so adjacent cells leave exactly `spacing` pixels between them.
- [ ] Keep the existing 1-pixel fallback for degenerate rectangles and clamp produced rectangles to the supplied work area.
- [ ] Add a `spacing` constructor parameter/field to `GridEditorForm`; use it in `RecalcZones()` instead of the hard-coded `4`.
- [ ] Pass `settings.FancyZonesSpacing` from `LayoutEditorForm` when constructing the grid editor. Runtime and grid-editor outlines then use the same pixel rectangles.
- [ ] Scale spacing in the miniature layout preview by the ratio of preview size to its monitor work area rather than dividing by a magic constant.

### Task 3: Render canvas wireframes from effective snap rectangles

**Files:**
- Modify: `ScreenCaptureTool/FancyZones/Editor/CanvasEditorForm.cs`
- Modify: `ScreenCaptureTool/FancyZones/Editor/LayoutEditorForm.cs`

- [ ] Add a `spacing` constructor argument to `CanvasEditorForm` and pass the configured value from `LayoutEditorForm`.
- [ ] Keep `zoneRects` as editable raw pixel rectangles and persist ratios from those raw rectangles only.
- [ ] Add `GetDisplayZoneRects()`: synchronize raw rectangles to the cloned model, call `layout.GetZoneRects(localWorkArea, spacing)`, and return its effective rectangles in form-local coordinates.
- [ ] Draw effective rectangles and their handles. Use effective rectangles for hit-testing/hover, but keep drag deltas applied to raw `zoneRects` so saving does not bake spacing into coordinates or double-apply it at runtime.
- [ ] Keep Z-order hit-testing from last canvas zone to first and preserve the toolbar's add/delete/reorder behavior.

### Task 4: Require Shift plus right-button hold during window movement

**Files:**
- Modify: `ScreenCaptureTool/FancyZones/NativeMethods.cs`
- Modify: `ScreenCaptureTool/FancyZones/FancyZonesManager.cs`

- [ ] Add low-level mouse-hook constants, delegate, `SetWindowsHookEx`, `UnhookWindowsHookEx`, and `CallNextHookEx` declarations for `WH_MOUSE_LL` and right-button down/up messages.
- [ ] Install the hook in `FancyZonesManager.Start()` and root its callback delegate in a field; unhook it in `Stop()` and `Dispose()`.
- [ ] During an active tracked move loop, update a `rightButtonDown` field on right-button down/up and return nonzero from the low-level hook for those messages so they do not open a target app context menu. Chain every other event with `CallNextHookEx`.
- [ ] Show zones only when `IsActivatorDown()` and `rightButtonDown` are both true. The default activation key remains Shift; a configured alternate modifier remains supported with the same right-button chord.
- [ ] Hide the overlay when either input is released and clear activation state when the move-size loop ends or is cancelled. Preserve existing Ctrl-based multi-zone selection and unsnapped-window filtering.
- [ ] Before snapping in `OnMoveSizeEnd`, re-check the activation chord so a release immediately before the end event cannot snap using a stale highlight.

### Task 5: Inspect, stage, delegate push/build, and review CI

**Files:**
- Verify all files above and the approved design file `docs/superpowers/specs/2026-11-05-fancyzones-editor-alignment-design.md`.

- [ ] Review `git diff` and run `git diff --check`; confirm no legacy data files were removed.
- [ ] Do not run local `dotnet build`, tests, or GUI interaction tests, following the owner's request.
- [ ] Hand the final reviewed changes to the user-requested `google/gemini-4-argon-high` general agent to commit/push to `main`, trigger the Windows Actions workflow, poll it to completion, inspect any failure logs, and report the artifact/run URL.
- [ ] If the Windows workflow fails, fix only the reported issue, re-run `git diff --check`, and hand the updated commit/push/build cycle to the same agent.
- [ ] The user verifies on the resulting EXE: buttons are reachable, a full-work-area zone has no extra outer strip, window frame meets the displayed wireframe, and Shift+right-button shows/hides zones while left-dragging.
