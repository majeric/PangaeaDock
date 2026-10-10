# Captured-pointer transport for the evaluation preview

2026-10-10. Implemented in `src/Pangaea.Dock.Avalonia/DockDragging.cs`.

The preview uses Avalonia pointer capture on the source DockSurface. Each pointer move converts
source-local position to a screen PixelPoint, then into registered host-local positions using
PointToClient. Host-local target evaluation supplies a pure immutable engine plan; the preview
uses the same arrangement function as committed controls. Capture is released only after clearing
the active drag session so the expected capture-loss event cannot cancel an already committed drop.

No desktop hooks, OS data transfer, arbitrary serialized drag payloads or native window moving is
used. External shell/file drag-drop is not intercepted by this transport. The source is focused on
capture so Escape reaches the drag handler even if keyboard focus started in a toolbar.

Headless tests drive actual pointer events rather than calling the move engine alone. They cover
merge/split preview equality, resize, cancellation and host creation. Native Windows and macOS
capture, monitor topology, mixed DPI, GPU/native-content reparenting and OS z-order behavior remain
release gates. The registered-host ordering is a preview implementation, not a verified OS z-order
query. Use the explicit Float command; floating by release onto bare desktop is not implemented.

This decision is provisional for the evaluation version. Introduce an IDockDragTransport adapter
before adding an OS drag-session fallback if native tests require it; do not alter Core to work
around a platform input limitation.
