# FancyZones editor alignment and activation design

## Scope

Correct the three reported interactions in the existing FancyZones implementation:

1. Keep every layout-management button inside its form at supported DPI scales.
2. Make the visible zone outlines and the rectangles used for snapping share the same geometry. A snapped window's outer frame must meet the editor's zone outline, without an additional runtime inset. Zones touching the work-area edges must reach those edges; configured spacing remains between adjacent zones.
3. While a window is being moved with the left mouse button, show the zone overlay only while both the configured activation modifier (Shift by default) and the right mouse button are held. Releasing either hides the overlay. Keep the existing Ctrl multi-zone selection behavior.

## Design

- Make the layout editor's client area explicit and position/anchor its bottom controls relative to the client area rather than relying on the outer form size.
- Use `ZoneLayout.GetZoneRects` as the canonical geometry for runtime snapping and editor wireframes. Pass the configured spacing into the grid editor. For canvas layouts, keep editable model rectangles separate from their spacing-adjusted display/snap rectangles so saving does not accumulate spacing.
- Treat a work-area edge as a real layout boundary, not as a spacing gutter. Apply spacing to internal zone boundaries while keeping edge-touching zones flush with the work area.
- Extend the drag activation check to require both the configured modifier and the right mouse button. Preserve the window's left-button move loop and existing multi-zone modifier behavior.

## Verification

- Review the client-area layout and coordinate transforms for both grid and canvas editors.
- Confirm the activation condition is true only when the modifier and right button are simultaneously down during a window move.
- Push the change and use the Windows GitHub Actions workflow to compile and publish; the user requested that push/build tracking be delegated to Gemini 4 Argon High.
- Do not run a local build or UI test; perform manual interaction validation from the resulting artifact if needed.
