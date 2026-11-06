# Canvas overlap, off-screen placement, and window z-order design

## Goal

Allow canvas layouts to place zones partially outside the monitor work area and overlap other zones/windows. Applying a layout must raise assigned windows in zone order so a higher-numbered zone's window appears above lower-numbered zone windows, without taking keyboard focus.

## Scope

- Free positioning and overlap apply only to `LayoutKind.Canvas` layouts.
- Grid layouts retain their current row/column split behavior and non-overlapping geometry.
- Canvas zone ordering is the z-order: later/higher-index zones are rendered over earlier zones. The existing “置顶选中” behavior moves a zone to the end of the ordered list and therefore gives it the highest number/frontmost visual layer.
- On “一键调整窗口”, assigned windows are placed and raised in ascending zone index order. `SetWindowPos(HWND_TOP, ... SWP_NOACTIVATE)` raises each window without explicitly activating it; the final/highest-index zone is therefore frontmost where zones overlap.
- Canvas zone geometry remains un-clipped in persisted normalized coordinates. A canvas zone may extend beyond the screen; the editor keeps a minimum visible portion available for selecting and moving it. On-screen dimensions/program controls are anchored to the visible intersection when a zone is partially out of bounds.
- Canvas spacing is not applied to free rectangles, preserving the exact rectangle shown by the editor. Grid spacing behavior remains unchanged.

## Current constraints found

- `CanvasEditorForm.ClampZoneRect` clamps x/y and dimensions to `localWorkArea`, so zones cannot be moved/resized beyond it.
- `SyncModelFromZones` clamps x/y/width/height ratios to `[0,1]`, which would discard off-screen geometry even if the pixel rectangle were permitted.
- `ZoneLayout.GetZoneRects` applies spacing insets and intersects every zone with the work area; this clips canvas zones and changes their bounds.
- `FancyZonesManager.ApplyLayoutWindows` calls `SnapWindow`, which uses `SWP_NOZORDER`, so it intentionally leaves the window stack unchanged.
- `CanvasEditorForm` draws dimensions and places per-zone program selectors relative to the raw zone origin; after allowing off-screen placement those controls need to use the visible part of the zone.

## Design

1. In `ZoneLayout.GetZoneRects`, branch on layout kind. Keep the existing inset/intersection algorithm for Grid. For Canvas, map the saved ratios directly to a pixel rectangle and do not inset or intersect with the work area.
2. In `CanvasEditorForm`, allow signed/out-of-range normalized x/y and width/height values. Bound rectangle size to a safe multiple of the work area, and constrain moves/resizes only enough to keep a small visible portion (at least 40 px on each axis) inside the work area. Preserve overlap and list order. Compute visible zone intersection for labels and app combo boxes so controls remain usable when a zone extends off-screen.
3. In `FancyZonesManager.SnapWindow`, add an optional bring-to-front parameter. Ordinary drag/drop snapping keeps its current z-order behavior. `ApplyLayoutWindows` requests bring-to-front and processes zone bindings in ascending index order; use `HWND_TOP` with `SWP_NOACTIVATE` (no `SWP_NOZORDER`) so larger zone indices end above smaller indices without stealing keyboard focus.
4. Retain the existing “置顶选中” canvas operation; it already reorders the selected zone to the end, matching the list index/z-order model.

## Acceptance criteria

- A Canvas zone can be dragged partly past any work-area edge and resized to partly exceed it; at least 40 px remain visible on both axes so it can be recovered.
- Canvas zones can overlap. The higher-numbered canvas zone is drawn over lower-numbered ones and remains pickable via reverse-order hit testing.
- Saving/reopening a canvas layout preserves negative or >1 normalized coordinates and does not clip or spacing-shrink the zone.
- The visible dimensions and app selector stay on-screen for partially off-screen zones.
- “一键调整窗口” places and raises selected windows in zone-number order; the highest-numbered overlapping zone's window is frontmost and the operation does not request foreground activation.
- Grid layouts continue using their current grid constraints and spacing behavior.

## Verification

- Review the code changes and `git diff --check`.
- No local build or UI test was requested; validate compilation and publishing through the GitHub Actions Windows workflow after push.
- Verify in the built app: canvas zones can be overlapped and left partly off-screen; saving/reopening preserves them; a one-key adjustment of overlapping assigned programs leaves the higher-numbered zone window in front.
