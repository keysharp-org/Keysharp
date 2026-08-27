// GNOME Shell extension for Keysharp integration.
// Exposes window management and mouse simulation via D-Bus so that
// Keysharp can operate on GNOME Wayland where foreign-client access
// to compositor state is normally forbidden.
//
// Requires GNOME Shell 45+ (ESM extension format).
// D-Bus service name : io.github.keysharp.GnomeShell
// Object path        : /io/github/keysharp/GnomeShell
// Interface          : io.github.keysharp.GnomeShell1

import GLib from 'gi://GLib';
import Gio from 'gi://Gio';
import Meta from 'gi://Meta';
import Mtk from 'gi://Mtk';
import Clutter from 'gi://Clutter';
import Cogl from 'gi://Cogl';
import GdkPixbuf from 'gi://GdkPixbuf';
import Shell from 'gi://Shell';
import St from 'gi://St';
import CairoGI from 'gi://cairo';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import * as Config from 'resource:///org/gnome/shell/misc/config.js';

const SHELL_MAJOR = Number.parseInt(Config.PACKAGE_VERSION.split('.')[0], 10);

const SERVICE_NAME = 'io.github.keysharp.GnomeShell';

// How many compositor frames to wait for a reserved window to become placeable.
// Retries are timer-driven, so this is a real time budget: 40 x 16ms.
const PLACEMENT_MAX_TRIES = 40;
const PLACEMENT_RETRY_MS = 16;
// How long the actor watchdog stays armed after placement; the observed yank fight lasts ~150ms.
const PLACEMENT_WATCH_MS = 600;

// How long a reservation stays live if the window it was meant for never arrives.
const PLACEMENT_TTL_MS = 2000;

const OBJECT_PATH  = '/io/github/keysharp/GnomeShell';

const DBUS_IFACE_XML = `
<node>
  <interface name="io.github.keysharp.GnomeShell1">

    <!-- Returns JSON: { ok, windows: [ { id, title, appId, pid,
         frame:{x,y,width,height}, client:{x,y,width,height},
         active, minimized, maximized, visible, transparency } ... ] }
         Windows are ordered bottom-to-top (index 0 = lowest z-order). -->
    <method name="GetWindowList">
      <arg type="b" direction="in"  name="includeHidden"/>
      <arg type="s" direction="out" name="json"/>
    </method>

    <!-- Returns JSON: { ok, window: { ... } | null } -->
    <method name="GetActiveWindow">
      <arg type="s" direction="out" name="json"/>
    </method>

    <!-- Global pointer position in compositor coordinates. -->
    <method name="GetCursorPosition">
      <arg type="i" direction="out" name="x"/>
      <arg type="i" direction="out" name="y"/>
    </method>

    <!-- Work area (primary monitor minus panels/struts), in screen coordinates. -->
    <method name="GetWorkArea">
      <arg type="i" direction="out" name="x"/>
      <arg type="i" direction="out" name="y"/>
      <arg type="i" direction="out" name="width"/>
      <arg type="i" direction="out" name="height"/>
    </method>

    <!-- Clipboard bridge: Wayland has no focus-independent clipboard access for a background app, and
         Mutter exposes no data-control protocol, so the shell (which owns the selection) reads/writes it
         via MetaSelection and notifies of changes via the MetaSelection owner-changed signal. Content is
         raw MIME-type <-> bytes so every format (text, image, html, uri-list, ...) round-trips. -->
    <method name="GetClipboardMimetypes">
      <arg type="as" direction="out" name="mimetypes"/>
    </method>
    <!-- Read one MIME type's bytes (async: MetaSelection.transfer_async). "" bytes = absent/empty. -->
    <method name="GetClipboardContent">
      <arg type="s" direction="in" name="mimetype"/>
      <arg type="ay" direction="out" name="bytes"/>
    </method>
    <!-- Replace the whole selection with a single MIME type's bytes. -->
    <method name="SetClipboardContent">
      <arg type="s" direction="in" name="mimetype"/>
      <arg type="ay" direction="in" name="bytes"/>
      <arg type="b" direction="out" name="ok"/>
    </method>
    <!-- UTF-8 text convenience (the common A_Clipboard path). -->
    <method name="GetClipboardText">
      <arg type="s" direction="out" name="text"/>
    </method>
    <method name="SetClipboardText">
      <arg type="s" direction="in" name="text"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <!-- Register this client as an overlay owner. ownerKey = "pid:starttime"; busName = the caller's
         unique D-Bus name. Lets the shell reap the caller's overlays when its process/connection dies. -->
    <method name="RegisterHighlightOwner">
      <arg type="s" direction="in" name="ownerKey"/>
      <arg type="s" direction="in" name="busName"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <!-- Window handle is the stable_sequence uint32 of Meta.Window. ok = false when no such window. -->
    <method name="FocusWindow">
      <arg type="t" direction="in" name="handle"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <!-- Raise the window to the top of the stack. -->
    <method name="RaiseWindow">
      <arg type="t" direction="in" name="handle"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <!-- Lower the window to the bottom of the stack. -->
    <method name="LowerWindow">
      <arg type="t" direction="in" name="handle"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <method name="ReserveWindow">
      <arg type="i" direction="in" name="pid"/>
      <arg type="t" direction="in" name="cookie"/>
      <arg type="i" direction="in" name="x"/>
      <arg type="i" direction="in" name="y"/>
      <arg type="i" direction="in" name="ttlMs"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <method name="GetReservedWindow">
      <arg type="i" direction="in" name="pid"/>
      <arg type="t" direction="in" name="cookie"/>
      <arg type="s" direction="out" name="id"/>
    </method>

    <!-- Pass x = INT32_MIN (-2147483648) to leave position unchanged;
         width/height <= 0 to leave size unchanged. -->
    <method name="MoveResizeWindow">
      <arg type="t" direction="in" name="handle"/>
      <arg type="i" direction="in" name="x"/>
      <arg type="i" direction="in" name="y"/>
      <arg type="i" direction="in" name="width"/>
      <arg type="i" direction="in" name="height"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <!-- Same as MoveResizeWindow but the window is identified by its X11 window id
         (get_xwindow), for X11 sessions where the caller has an XID, not a stable_sequence. -->
    <method name="MoveResizeWindowByXid">
      <arg type="t" direction="in" name="xid"/>
      <arg type="i" direction="in" name="x"/>
      <arg type="i" direction="in" name="y"/>
      <arg type="i" direction="in" name="width"/>
      <arg type="i" direction="in" name="height"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <!-- state: 0 = normal, 1 = minimized, 2 = maximized. -->
    <method name="SetWindowState">
      <arg type="t" direction="in" name="handle"/>
      <arg type="i" direction="in" name="state"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <!-- above: true = keep above all others, false = clear keep-above. -->
    <method name="SetWindowAbove">
      <arg type="t" direction="in" name="handle"/>
      <arg type="b" direction="in" name="above"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <!-- decorated: true = show the titlebar/frame, false = hide it (the GNOME counterpart of KWin's
         noBorder, used by borderless overlays and WinSetStyle -WS_CAPTION). -->
    <method name="SetWindowDecorated">
      <arg type="t" direction="in" name="handle"/>
      <arg type="b" direction="in" name="decorated"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <!-- opacity: 0 (fully transparent) .. 255 (fully opaque). -->
    <method name="SetWindowOpacity">
      <arg type="t" direction="in" name="handle"/>
      <arg type="i" direction="in" name="opacity"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <method name="CloseWindow">
      <arg type="t" direction="in" name="handle"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <!-- Force-kill the window's client, falling back to a graceful close. -->
    <method name="KillWindow">
      <arg type="t" direction="in" name="handle"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <!-- Mouse simulation via Clutter.VirtualInputDevice. ok = false when no virtual pointer exists. -->
    <method name="SendMouseMoveAbsolute">
      <arg type="i" direction="in" name="x"/>
      <arg type="i" direction="in" name="y"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <method name="SendMouseMoveRelative">
      <arg type="i" direction="in" name="dx"/>
      <arg type="i" direction="in" name="dy"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <!-- button: 1=left, 2=middle, 3=right (X11 convention). -->
    <method name="SendMouseButton">
      <arg type="u" direction="in" name="button"/>
      <arg type="b" direction="in" name="pressed"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <!-- delta in 120-unit wheel increments (positive = up/right).
         vertical: true = vertical scroll, false = horizontal. -->
    <method name="SendMouseScroll">
      <arg type="i" direction="in" name="delta"/>
      <arg type="b" direction="in" name="vertical"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <!-- Capture a screen region and return raw PNG bytes. No flash, no
         file is written. Runs entirely inside the compositor process.
         Returns an empty array on failure. -->
    <method name="CaptureArea">
      <arg type="i" direction="in"  name="x"/>
      <arg type="i" direction="in"  name="y"/>
      <arg type="i" direction="in"  name="width"/>
      <arg type="i" direction="in"  name="height"/>
      <arg type="ay" direction="out" name="pngData"/>
    </method>

    <!-- Capture a single window's full contents (handle = stable_sequence, as in
         GetWindowList) and return raw PNG bytes. Reads the window actor's own
         backing buffer via meta_window_actor_get_image, so the result is correct
         even when the window is occluded by other windows. No flash, no file is
         left behind. Returns an empty array on failure (e.g. unknown handle or a
         minimized window with no live texture). -->
    <method name="CaptureWindow">
      <arg type="t"  direction="in"  name="handle"/>
      <arg type="ay" direction="out" name="pngData"/>
    </method>

    <!-- Draw (or update) a click-through rectangle-outline overlay on the compositor stage — the
         GNOME counterpart of the wlr-layer-shell highlight Keysharp uses on KWin/wlroots (GNOME has
         no layer-shell, so the overlay has to live inside the shell process). id is a caller-chosen
         token so the same overlay can be moved/resized or hidden later; x/y/width/height are in global
         compositor coordinates; color is an "RRGGBB" hex string; thickness is the outline width in px.
         The overlay is non-reactive and excluded from the shell input region, so it never eats clicks. -->
    <method name="ShowHighlight">
      <arg type="u" direction="in" name="id"/>
      <arg type="s" direction="in" name="ownerKey"/>
      <arg type="s" direction="in" name="busName"/>
      <arg type="i" direction="in" name="x"/>
      <arg type="i" direction="in" name="y"/>
      <arg type="i" direction="in" name="width"/>
      <arg type="i" direction="in" name="height"/>
      <arg type="s" direction="in" name="color"/>
      <arg type="i" direction="in" name="thickness"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <!-- Remove the overlay previously created with the given id. Unknown ids are ignored. -->
    <method name="HideHighlight">
      <arg type="u" direction="in" name="id"/>
      <arg type="s" direction="in" name="ownerKey"/>
      <arg type="s" direction="in" name="busName"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <method name="ShowImageOverlay">
      <arg type="u" direction="in" name="id"/>
      <arg type="s" direction="in" name="ownerKey"/>
      <arg type="s" direction="in" name="busName"/>
      <arg type="i" direction="in" name="x"/>
      <arg type="i" direction="in" name="y"/>
      <arg type="i" direction="in" name="width"/>
      <arg type="i" direction="in" name="height"/>
      <arg type="ay" direction="in" name="pngData"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <!-- The same frame, handed over as shared memory rather than an encoded PNG. The client writes
         premultiplied BGRA into a file both processes map, so an animated overlay costs one texture
         upload per frame instead of a PNG encode, a multi-megabyte D-Bus payload and a PNG decode.
         pixelWidth/pixelHeight/stride describe the buffer; width/height stay the on-screen size. -->
    <method name="ShowImageOverlayShm">
      <arg type="u" direction="in" name="id"/>
      <arg type="s" direction="in" name="ownerKey"/>
      <arg type="s" direction="in" name="busName"/>
      <arg type="i" direction="in" name="x"/>
      <arg type="i" direction="in" name="y"/>
      <arg type="i" direction="in" name="width"/>
      <arg type="i" direction="in" name="height"/>
      <arg type="s" direction="in" name="shmPath"/>
      <arg type="i" direction="in" name="pixelWidth"/>
      <arg type="i" direction="in" name="pixelHeight"/>
      <arg type="i" direction="in" name="stride"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <method name="MoveImageOverlay">
      <arg type="u" direction="in" name="id"/>
      <arg type="s" direction="in" name="ownerKey"/>
      <arg type="s" direction="in" name="busName"/>
      <arg type="i" direction="in" name="x"/>
      <arg type="i" direction="in" name="y"/>
      <arg type="i" direction="in" name="width"/>
      <arg type="i" direction="in" name="height"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <method name="HideImageOverlay">
      <arg type="u" direction="in" name="id"/>
      <arg type="s" direction="in" name="ownerKey"/>
      <arg type="s" direction="in" name="busName"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <!-- True when the shell can draw a click-through tooltip on the caller's behalf. -->
    <method name="SupportsTooltip">
      <arg type="b" direction="out" name="ok"/>
    </method>

    <!-- Draw or update a click-through tooltip. slot identifies it per owner; empty text clears it.
         x/y are global compositor coordinates. -->
    <method name="ShowTooltip">
      <arg type="i" direction="in" name="slot"/>
      <arg type="s" direction="in" name="ownerKey"/>
      <arg type="s" direction="in" name="busName"/>
      <arg type="s" direction="in" name="text"/>
      <arg type="i" direction="in" name="x"/>
      <arg type="i" direction="in" name="y"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <!-- Remove the tooltip in the given slot for this owner. -->
    <method name="HideTooltip">
      <arg type="i" direction="in" name="slot"/>
      <arg type="s" direction="in" name="ownerKey"/>
      <arg type="s" direction="in" name="busName"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <!-- Emitted whenever the focused window changes. Carries the same
         JSON as GetActiveWindow. -->
    <signal name="ActiveWindowChanged">
      <arg type="s" name="json"/>
    </signal>

    <!-- Generic window-event stream consumed by Keysharp's WinEvent.
         type is one of: create, close, active, title, minimize, restore, move.
         json is the affected window's info (the same shape as GetActiveWindow's
         window object); for "close" it carries at least { id }. -->
    <signal name="WindowEvent">
      <arg type="s" name="type"/>
      <arg type="s" name="json"/>
    </signal>

    <!-- Fires on every clipboard change: text is the UTF-8 text (or ""), mimetypes lists the available MIME
         types, so a listener can classify text/other/empty and read any format on demand. -->
    <signal name="ClipboardChanged">
      <arg type="s" name="text"/>
      <arg type="as" name="mimetypes"/>
    </signal>

  </interface>
</node>`;

// Window state protocol values sent by the C# Wayland bridge.
const STATE_NORMAL    = 0;
const STATE_MINIMIZED = 1;
const STATE_MAXIMIZED = 2;

// Sentinel used by MoveResizeWindow to mean "don't change this axis".
const INT32_MIN = -2147483648;

// Grace period after a client's D-Bus name drops before its overlays are reaped, so a client that
// merely reconnected (new unique name, then re-registers) does not lose its overlays. See
// _handleOverlayConnectionLost. Mirrors the Cinnamon extension.
const OVERLAY_RECONNECT_GRACE_MS = 2000;

export default class KeysharpExtension {
    constructor() {
        this._dbusImpl     = null;
        this._busNameId    = 0;
        this._vPointer     = null;
        this._focusId      = null;
        this._winCreatedId = null;
        this._mapId = 0;
        this._placements = [];
        this._placed = {};
        this._placementSources = new Set();
        // Overlays are keyed by "<ownerKey>:<id>" (see _overlayKey) so ids never collide across clients.
        // Each entry carries its owner so a dead client's overlays can be reaped:
        //   highlight  entry = { edges:[St.Widget...], ownerKey, ownerPid, ownerStartTime, busName }
        //   image      entry = { actor:Clutter.Actor,  ownerKey, ownerPid, ownerStartTime, busName }
        //   tooltip    entry = { actor:St.Label,       ownerKey, ownerPid, ownerStartTime, busName }
        this._highlights   = new Map();
        this._imageOverlays = new Map();
        this._tooltips     = new Map();
        // Owner-death cleanup: subscription id for NameOwnerChanged, the periodic /proc liveness sweep
        // source id (0 = idle), and per-owner reconnect-grace timers keyed by ownerKey.
        this._overlayNameWatchId = 0;
        this._overlayCleanupId = 0;
        this._overlayReconnectTimers = new Map();
        this._clipboard = null;
        this._clipboardSelectionId = null;
    }

    enable() {
        // Virtual pointer for mouse simulation.
        try {
            const seat = Clutter.get_default_backend().get_default_seat();
            this._vPointer = seat.create_virtual_device(
                Clutter.InputDeviceType.POINTER_DEVICE);
        } catch (e) {
            logError(e, 'Keysharp: could not create virtual pointer device');
        }

        // Export the D-Bus object first, then own the well-known name. Guard the whole D-Bus bring-up: if
        // export() or bus_own_name() throws, tear down whatever came up (exported object, name watch, virtual
        // pointer) and bail instead of leaving a half-initialized zombie extension — mirroring the Cinnamon
        // extension's bus_own_name guard.
        // NOTE: Gio.DBus.session is a Gio.DBusConnection — it has no own_name()
        // method. The correct API is the free function Gio.bus_own_name().
        try {
            this._dbusImpl = Gio.DBusExportedObject.wrapJSObject(DBUS_IFACE_XML, this);
            this._dbusImpl.export(Gio.DBus.session, OBJECT_PATH);

            // Watch for client D-Bus connection names dropping off the bus. When one that owns overlays
            // disappears, _handleNameOwnerChanged arms a short reconnect grace timer and then reaps them.
            this._overlayNameWatchId = Gio.DBus.session.signal_subscribe(
                'org.freedesktop.DBus',
                'org.freedesktop.DBus',
                'NameOwnerChanged',
                '/org/freedesktop/DBus',
                null,
                Gio.DBusSignalFlags.NONE,
                (_conn, _sender, _path, _iface, _signal, parameters) => this._handleNameOwnerChanged(parameters));

            this._busNameId = Gio.bus_own_name(
                Gio.BusType.SESSION,
                SERVICE_NAME,
                Gio.BusNameOwnerFlags.NONE,
                null,   // bus_acquired_closure  (not needed; object is already exported)
                null,   // name_acquired_closure
                null);  // name_lost_closure
        } catch (e) {
            logError(e, 'Keysharp: could not export D-Bus service / own name');

            if (this._overlayNameWatchId !== 0) {
                try { Gio.DBus.session.signal_unsubscribe(this._overlayNameWatchId); } catch (_e) {}
                this._overlayNameWatchId = 0;
            }
            if (this._busNameId !== 0) {
                try { Gio.bus_unown_name(this._busNameId); } catch (_e) {}
                this._busNameId = 0;
            }
            if (this._dbusImpl !== null) {
                try { this._dbusImpl.unexport(); } catch (_e) {}
                this._dbusImpl = null;
            }
            this._vPointer = null;
            // Don't wire up focus/window-created signals against a service that never came up.
            return;
        }

        // Emit ActiveWindowChanged (back-compat) and the generic WindowEvent('active')
        // whenever the compositor focus changes.
        this._focusId = global.display.connect('notify::focus-window', () => {
            this._emitActiveWindowChanged();
            const win = global.display.get_focus_window();
            if (win && this._isTrackedWindow(win))
                this._emitWindowEvent('active', win);
        });

        // The moment a reserved window can be placed WITHOUT a visible jump: 'map' fires as the actor is
        // about to be presented, before its first frame reaches the screen, so a move here is never seen.
        // The timer loop in _placeOnMap stays as the safety net for a map that slips past this.
        this._mapId = global.window_manager.connect('map', (_wm, actor) => {
            try {
                const win = actor ? actor.get_meta_window() : null;

                if (win && win.__ksPlaceNow)
                    win.__ksPlaceNow();
            } catch (_e) {
            }
        });

        // Emit WindowEvent('create') for newly mapped windows and hook their per-window
        // signals (title/minimize/move/close).
        this._winCreatedId = global.display.connect('window-created', (_disp, win) => {
            if (!win || !this._isTrackedWindow(win))
                return;
            this._placeOnMap(win);
            this._hookWindow(win);
            this._emitWindowEvent('create', win);
        });

        // Hook windows that already exist at enable time, without re-emitting create.
        for (const actor of global.get_window_actors()) {
            const win = this._liveMetaWindow(actor);
            if (win && this._isTrackedWindow(win))
                this._hookWindow(win);
        }

        // Clipboard change notification: the shell owns the selection, so MetaSelection's owner-changed fires
        // for every clipboard change regardless of which client (or none) is focused — the focus-independent
        // path a background app otherwise can't get on Wayland (Mutter exposes no data-control protocol).
        try {
            this._clipboard = St.Clipboard.get_default();
            this._clipboardSelectionId = global.display.get_selection().connect('owner-changed',
                (_selection, selectionType, _source) => {
                    if (selectionType === Meta.SelectionType.SELECTION_CLIPBOARD)
                        this._emitClipboardChanged();
                });
        } catch (e) {
            logError(e, 'Keysharp: could not hook clipboard selection');
        }
    }

    disable() {
        if (this._clipboardSelectionId !== null) {
            try { global.display.get_selection().disconnect(this._clipboardSelectionId); } catch (_e) {}
            this._clipboardSelectionId = null;
        }
        this._clipboard = null;

        // Tear down any overlays still on screen, plus the owner-death cleanup machinery.
        for (const key of [...this._highlights.keys()])
            this._removeHighlightByKey(key);

        for (const key of [...this._imageOverlays.keys()])
            this._removeImageOverlayByKey(key);

        for (const key of [...this._tooltips.keys()])
            this._removeTooltipByKey(key);

        this._stopOverlayCleanupTimer();
        this._cancelAllOverlayReconnectTimers();

        if (this._overlayNameWatchId !== 0) {
            try { Gio.DBus.session.signal_unsubscribe(this._overlayNameWatchId); } catch (_e) {}
            this._overlayNameWatchId = 0;
        }

        if (this._winCreatedId !== null) {
            global.display.disconnect(this._winCreatedId);
            this._winCreatedId = null;
        }

        if (this._mapId) {
            global.window_manager.disconnect(this._mapId);
            this._mapId = 0;
        }

        for (const id of this._placementSources) {
            try { GLib.source_remove(id); } catch (_e) {}
        }

        this._placementSources.clear();

        for (const actor of global.get_window_actors()) {
            const win = actor.get_meta_window();
            if (win)
                this._unhookWindow(win);
        }

        if (this._focusId !== null) {
            global.display.disconnect(this._focusId);
            this._focusId = null;
        }

        if (this._busNameId !== 0) {
            Gio.bus_unown_name(this._busNameId);
            this._busNameId = 0;
        }

        if (this._dbusImpl !== null) {
            this._dbusImpl.unexport();
            this._dbusImpl = null;
        }

        this._vPointer = null;
    }

    // ----------------------------------------------------------------
    // D-Bus method implementations
    // ----------------------------------------------------------------

    GetWindowList(includeHidden) {
        try {
            const actors  = global.get_window_actors();
            const windows = [];

            for (let i = 0; i < actors.length; i++) {
                const win = this._liveMetaWindow(actors[i]);
                if (!win || !this._isTrackedWindow(win))
                    continue;
                if (!includeHidden && win.minimized)
                    continue;
                windows.push(this._windowInfo(win));
            }

            return JSON.stringify({ok: true, windows});
        } catch (e) {
            return JSON.stringify({ok: false, error: String(e), windows: []});
        }
    }

    GetActiveWindow() {
        try {
            const win = global.display.get_focus_window();
            return JSON.stringify({
                ok: true,
                window: (win && this._isLiveWindow(win) && this._isTrackedWindow(win))
                    ? this._windowInfo(win)
                    : null,
            });
        } catch (e) {
            return JSON.stringify({ok: false, window: null});
        }
    }

    // Two out-parameters map to a two-element array in GJS D-Bus dispatch.
    GetCursorPosition() {
        const [x, y] = global.get_pointer();
        return [Math.round(x), Math.round(y)];
    }

    GetWorkArea() {
        // Work area of the primary monitor, with panels/struts already subtracted by Mutter — the
        // panel-excluded usable area a foreign Wayland client cannot compute for itself.
        const idx = Main.layoutManager.primaryIndex;
        const wa = Main.layoutManager.getWorkAreaForMonitor(idx);
        return [Math.round(wa.x), Math.round(wa.y), Math.round(wa.width), Math.round(wa.height)];
    }

    // --- Clipboard --------------------------------------------------------------------------------------
    // MetaSelection is the focus-independent, all-MIME path (St.Clipboard alone can only *get* text):
    // get_mimetypes lists the types, transfer_async streams one type's bytes, and a MetaSelectionSourceMemory
    // owner writes one type.
    _selectionObj() {
        return global.display.get_selection();
    }

    _mimetypes() {
        try {
            return this._selectionObj().get_mimetypes(Meta.SelectionType.SELECTION_CLIPBOARD) || [];
        } catch (_e) {
            return [];
        }
    }

    GetClipboardMimetypes() {
        return this._mimetypes();
    }

    // Async: MetaSelection.transfer_async streams the content of one MIME type into a memory output stream.
    GetClipboardContentAsync(params, invocation) {
        const [mimetype] = params;
        try {
            const stream = Gio.MemoryOutputStream.new_resizable();
            this._selectionObj().transfer_async(Meta.SelectionType.SELECTION_CLIPBOARD, String(mimetype), -1, stream, null,
                (sel, res) => {
                    let bytes = new Uint8Array(0);
                    try {
                        sel.transfer_finish(res);
                        stream.close(null);
                        const data = stream.steal_as_bytes().get_data();
                        if (data)
                            bytes = data instanceof Uint8Array ? data : new Uint8Array(data);
                    } catch (_e) {
                    }
                    try { invocation.return_value(new GLib.Variant('(ay)', [bytes])); } catch (_e2) {}
                });
        } catch (_e) {
            try { invocation.return_value(new GLib.Variant('(ay)', [new Uint8Array(0)])); } catch (_e2) {}
        }
    }

    SetClipboardContent(mimetype, bytes) {
        try {
            const data = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes || []);
            const source = Meta.SelectionSourceMemory.new(String(mimetype), new GLib.Bytes(data));
            this._selectionObj().set_owner(Meta.SelectionType.SELECTION_CLIPBOARD, source);
            return true;
        } catch (e) {
            logError(e, 'Keysharp: SetClipboardContent failed');
            return false;
        }
    }

    // Text conveniences (the common A_Clipboard path). Read via St.Clipboard.get_text (async); write goes
    // through SetClipboardContent so the owner is a proper MetaSelection source.
    GetClipboardTextAsync(_params, invocation) {
        try {
            (this._clipboard || St.Clipboard.get_default()).get_text(St.ClipboardType.CLIPBOARD, (_cb, text) => {
                try { invocation.return_value(new GLib.Variant('(s)', [text || ''])); } catch (_e) {}
            });
        } catch (_e) {
            try { invocation.return_value(new GLib.Variant('(s)', [''])); } catch (_e2) {}
        }
    }

    SetClipboardText(text) {
        return this.SetClipboardContent('text/plain;charset=utf-8', new TextEncoder().encode(String(text || '')));
    }

    _emitClipboardChanged() {
        if (!this._dbusImpl)
            return;

        const mimetypes = this._mimetypes();
        try {
            (this._clipboard || St.Clipboard.get_default()).get_text(St.ClipboardType.CLIPBOARD, (_cb, text) => {
                if (!this._dbusImpl)
                    return;
                try {
                    this._dbusImpl.emit_signal('ClipboardChanged', new GLib.Variant('(sas)', [text || '', mimetypes]));
                } catch (_e) {}
            });
        } catch (_e) {
        }
    }

    FocusWindow(handle) {
        const win = this._findWindow(handle);
        if (!win) return false;
        if (win.minimized)
            win.unminimize();
        Main.activateWindow(win);
        return true;
    }

    RaiseWindow(handle) {
        const win = this._findWindow(handle);
        if (!win)
            return false;

        try {
            if (typeof win.raise === 'function') {
                win.raise();
                return true;
            }
        } catch (_e) {
        }

        // Mutter versions without a public raise(): activating brings it to the front.
        return this.FocusWindow(handle);
    }

    LowerWindow(handle) {
        const win = this._findWindow(handle);
        if (!win)
            return false;

        try {
            if (typeof win.lower_with_transients === 'function') {
                win.lower_with_transients();
                return true;
            }
        } catch (_e) {
        }

        try {
            if (typeof win.lower === 'function') {
                win.lower();
                return true;
            }
        } catch (_e) {
        }

        return false;
    }

    _moveResizeWin(win, x, y, width, height) {
        if (!win) return false;

        // Moving a window that has no monitor yet recurses to death inside
        // move_resize_internal -> update_monitor -> wayland_update_main_monitor. Reachable here because a
        // reserved window is correlated as soon as it has geometry, which can precede its monitor.
        if (win.get_monitor() < 0)
            return false;

        // A maximized (or tiled) window ignores move/resize until it is restored — mirror how dragging its
        // titlebar first unmaximizes it. Unmaximize BEFORE reading the frame rect so an unchanged-size move
        // keeps the restored size, not the maximized one.
        if (win.maximized_horizontally || win.maximized_vertically)
            win.unmaximize(Meta.MaximizeFlags.HORIZONTAL | Meta.MaximizeFlags.VERTICAL);

        // Read the current frame rect so we can substitute unchanged axes.
        const frame = win.get_frame_rect();
        const nx = (x !== INT32_MIN) ? x : frame.x;
        const ny = (y !== INT32_MIN) ? y : frame.y;
        const nw = (width  > 0) ? width  : frame.width;
        const nh = (height > 0) ? height : frame.height;

        // move_resize_frame is the single Mutter call for both move and resize;
        // win.resize() does not exist as a standalone public API in Mutter/GJS.
        // user_op = true marks this a user action, so Mutter applies the looser "keep-minimal on
        // screen" constraint of an interactive drag (allowing off-screen placement) instead of the
        // strict fully-on-screen clamp it forces on app ConfigureRequests. It does not steal focus.
        win.move_resize_frame(true, nx, ny, nw, nh);
        return true;
    }

    // Claim the NEXT window this pid creates under a caller-chosen cookie, so it can ask afterwards which
    // compositor window its own toplevel became. Consumed in creation order: Show() is synchronous, so the
    // oldest live reservation for a pid belongs to the next window it maps. The pid is the only identity
    // available this early - title, app_id and geometry are all still empty when a window is created, which
    // is exactly why a client cannot recognise its own window without this.
    ReserveWindow(pid, cookie, x, y, ttlMs) {
        if (pid <= 0)
            return false;

        try {
            this._sweepPlacements();
            this._placements.push({
                pid:     pid,
                cookie:  cookie,
                x:       x,
                y:       y,
                expires: GLib.get_monotonic_time() / 1000 + (ttlMs > 0 ? ttlMs : PLACEMENT_TTL_MS)
            });
            return true;
        } catch (_e) {
            return false;
        }
    }

    // The compositor window a reservation ended up on, or '' if it has not been consumed (or has expired).
    // Keyed by pid too: the cookie is a per-process pointer, so two Keysharp processes can mint the same one.
    GetReservedWindow(pid, cookie) {
        try {
            const now = GLib.get_monotonic_time() / 1000;
            const hit = this._placed[pid + ':' + cookie];

            if (!hit)
                return '';

            delete this._placed[pid + ':' + cookie];
            return hit.expires > now ? hit.id : '';
        } catch (_e) {
            return '';
        }
    }

    _sweepPlacements() {
        const now = GLib.get_monotonic_time() / 1000;
        this._placements = this._placements.filter(p => p.expires > now);

        for (const key in this._placed) {
            if (this._placed[key].expires <= now)
                delete this._placed[key];
        }
    }

    // Consume a reservation for a just-created window: record which compositor window it became, and place
    // it if a position was reserved.
    //
    // The placement is what a client cannot do for itself. It has to wait for the window to exist, learn which
    // one is its own, then move it - by which time the window has been painted where the compositor chose. Here
    // the move happens before it is ever painted.
    //
    // Two hazards, both learned the hard way. A window has no monitor at creation, and moving it then recurses
    // to death inside move_resize_internal -> update_monitor -> wayland_update_main_monitor, so wait for one.
    // And Muffin emits the geometry signals from INSIDE move_resize_internal, so moving from a signal handler
    // re-enters it: the move is posted to run before the next paint instead, outside that call stack.
    _placeOnMap(win) {
        let p = null;

        try {
            this._sweepPlacements();
            const pid = this._clientPid(win, true);

            if (pid <= 0 || this._placements.length === 0)
                return;

            const i = this._placements.findIndex(q => q.pid === pid);

            if (i < 0)
                return;

            p = this._placements.splice(i, 1)[0];

            if (p.cookie)
                this._placed[p.pid + ':' + p.cookie] = {id: String(win.get_stable_sequence()), expires: p.expires};
        } catch (_e) {
            return;
        }

        if (!p || (p.x === INT32_MIN && p.y === INT32_MIN))
            return;

        // Wait for the window to become placeable, then move it and hold it there. Never move before
        // get_monitor() answers: a monitorless window recurses to death through
        // move_resize_internal -> update_monitor -> wayland_update_main_monitor.
        let left = PLACEMENT_MAX_TRIES;
        let moved = 0;
        let stable = 0;

        // Every source is tracked so disable() can cancel it: a disabled extension must stop moving windows.
        const defer = (ms, fn) => {
            let id = 0;
            id = GLib.timeout_add(GLib.PRIORITY_DEFAULT, ms, () => {
                this._placementSources.delete(id);
                fn();
                return GLib.SOURCE_REMOVE;
            });
            this._placementSources.add(id);
            return id;
        };

        const retry = () => defer(PLACEMENT_RETRY_MS, attempt);

        // The compositor's lazy geometry sync can yank the painted actor to a stale position while the
        // window's own buffer rect keeps saying where it belongs. Correcting from notify:: runs synchronously
        // inside whatever moved the property - BEFORE the next paint - so a yank never reaches the screen,
        // unlike a timer correction which can be a frame late. Snapping to the live buffer rect also cannot
        // fight a legitimate move, which updates the buffer rect with it.
        // The yank pathology was measured on Muffin; on a compositor that keeps the actor synced, the >1px
        // gate leaves this inert. If windows ever stutter for ~600ms after Show (fractional scaling is the
        // plausible trigger), suspect this block first.
        let watchIds = [];
        let fixing = false;
        let fixes = 0;

        const disarmWatch = (actor) => {
            for (const id of watchIds) {
                try { actor.disconnect(id); } catch (_e) {}
            }

            watchIds = [];
        };

        const armWatch = (actor) => {
            if (watchIds.length)
                return;

            const fix = () => {
                if (fixing || fixes > 60)
                    return;

                try {
                    const br = win.get_buffer_rect();

                    if (Math.abs(actor.x - br.x) > 1 || Math.abs(actor.y - br.y) > 1) {
                        fixing = true;
                        fixes++;
                        actor.set_position(br.x, br.y);
                        fixing = false;
                    }
                } catch (_e) {
                }
            };

            try {
                watchIds.push(actor.connect('notify::x', fix));
                watchIds.push(actor.connect('notify::y', fix));

                defer(PLACEMENT_WATCH_MS, () => disarmWatch(actor));
            } catch (_e) {
            }
        };

        const attempt = () => {
            try {
                if (--left <= 0) {
                    delete win.__ksPlaceNow;
                    return;
                }

                if (win.get_monitor() < 0) {
                    retry();
                    return;
                }

                const f = win.get_frame_rect();
                const tx = p.x !== INT32_MIN ? p.x : f.x;
                const ty = p.y !== INT32_MIN ? p.y : f.y;

                if (moved > 0 && f.x === tx && f.y === ty) {
                    // The rect being right is only half the job: the ACTOR - the thing actually painted - is
                    // synced to it lazily and can still be yanked to a stale position tens of ms later. Only
                    // finish once it exists and has sat at the window's buffer rect for two consecutive
                    // ticks; a rect that converged before the actor existed would otherwise paint at its
                    // spawn position unwatched.
                    const held = win.get_compositor_private();

                    if (!held) {
                        retry();
                        return;
                    }

                    armWatch(held);

                    const br = win.get_buffer_rect();

                    if (Math.abs(held.x - br.x) > 1 || Math.abs(held.y - br.y) > 1) {
                        held.set_position(br.x, br.y);
                        stable = 0;
                        retry();
                        return;
                    }

                    if (++stable < 2) {
                        retry();
                        return;
                    }

                    delete win.__ksPlaceNow;
                    return;
                }

                // Position only, never a size. The size the client chose is already on its way, and a resize
                // it never asked for is at best held until the client acks it - the position with it - and at
                // worst is what crashes the compositor when issued this early.
                win.move_frame(true, tx, ty);

                // Carry the actor along: the buffer rect is where it belongs now that the rect has moved.
                const a2 = win.get_compositor_private();

                if (a2) {
                    const nbr = win.get_buffer_rect();
                    a2.set_position(nbr.x, nbr.y);
                    armWatch(a2);
                }

                moved++;
                stable = 0;
                retry();
            } catch (_e) {
                delete win.__ksPlaceNow;
            }
        };

        win.__ksPlaceNow = attempt;

        // Deferred out of the window-created signal, but only just: an idle fires long before anything is
        // presented, and pre-show moves both seed the rect and mean the map-time correction is tiny.
        defer(0, attempt);
    }

    MoveResizeWindow(handle, x, y, width, height) {
        return this._moveResizeWin(this._findWindow(handle), x, y, width, height);
    }

    // Move by X11 window id. On an X11 session the caller identifies windows by their XID
    // (XQueryTree / _NET_CLIENT_LIST), not the Mutter stable_sequence, so route the move through the
    // compositor this way to reach off-screen placement a raw XMoveWindow can't (Mutter clamps that
    // on screen).
    MoveResizeWindowByXid(xid, x, y, width, height) {
        return this._moveResizeWin(this._findWindowByXid(xid), x, y, width, height);
    }

    SetWindowState(handle, state) {
        const win = this._findWindow(handle);
        if (!win) return false;

        if (state === STATE_MINIMIZED) {
            win.minimize();
        } else if (state === STATE_MAXIMIZED) {
            win.unminimize();
            win.maximize(Meta.MaximizeFlags.HORIZONTAL | Meta.MaximizeFlags.VERTICAL);
        } else {
            win.unminimize();
            win.unmaximize(Meta.MaximizeFlags.HORIZONTAL | Meta.MaximizeFlags.VERTICAL);
        }
        return true;
    }

    SetWindowAbove(handle, above) {
        const win = this._findWindow(handle);
        if (!win) return false;

        // Mutter exposes make_above()/unmake_above(); guard with is_above() so a redundant
        // call doesn't throw on compositor versions that are strict about state transitions.
        if (above) {
            if (!win.is_above())
                win.make_above();
        } else {
            if (win.is_above())
                win.unmake_above();
        }
        return true;
    }

    CloseWindow(handle) {
        const win = this._findWindow(handle);
        if (!win)
            return false;
        win.delete(global.get_current_time());
        return true;
    }

    KillWindow(handle) {
        const win = this._findWindow(handle);
        if (!win)
            return false;

        try {
            if (typeof win.kill === 'function') {
                win.kill();
                return true;
            }
        } catch (_e) {
        }

        // No kill() on this Mutter version: fall back to a graceful close.
        try {
            win.delete(global.get_current_time());
            return true;
        } catch (_e) {
            return false;
        }
    }

    // Hide/show a window's titlebar — the GNOME counterpart of KWin's noBorder.
    //
    // Mutter does not server-side-decorate Wayland clients (unlike KWin), so a borderless overlay made
    // with GTK decorated=false already has no titlebar here and this is a no-op for it. There is also no
    // public Mutter API to toggle decorations for a Wayland client (its decorations are client-side, drawn
    // by the app itself), so the only case we can act on is an XWayland (X11) window, whose Motif decoration
    // hint Mutter honours. We set that hint via xprop when available; otherwise we return gracefully so the
    // caller treats it as best-effort.
    SetWindowDecorated(handle, decorated) {
        const win = this._findWindow(handle);
        if (!win)
            return false;

        try {
            const isX11 = (typeof win.get_client_type === 'function')
                ? (win.get_client_type() === Meta.WindowClientType.X11)
                : (typeof win.get_xwindow === 'function' && win.get_xwindow() !== 0);

            // Wayland client: decorations are client-side; nothing the compositor can remove. The window
            // exists and there is nothing to do, so this is best-effort success, not a failure.
            if (!isX11 || typeof win.get_xwindow !== 'function')
                return true;

            const xid = win.get_xwindow();
            if (!xid)
                return true;

            // _MOTIF_WM_HINTS: flags=2 (MWM_HINTS_DECORATIONS), decorations field 0 = none / 1 = all.
            const decor = decorated ? 1 : 0;
            GLib.spawn_command_line_async(
                `xprop -id ${xid} -f _MOTIF_WM_HINTS 32c ` +
                `-set _MOTIF_WM_HINTS "2, 0, ${decor}, 0, 0"`);
            return true;
        } catch (e) {
            logError(e, 'Keysharp: SetWindowDecorated failed');
            return false;
        }
    }

    // Set the window's opacity by setting it on its compositor actor. opacity is 0 (transparent)..255 (opaque).
    SetWindowOpacity(handle, opacity) {
        const win = this._findWindow(handle);
        if (!win)
            return false;

        const value = Math.max(0, Math.min(255, Math.round(Number(opacity))));

        try {
            const actor = (typeof win.get_compositor_private === 'function')
                ? win.get_compositor_private()
                : null;

            if (!actor)
                return false;

            if (typeof actor.set_opacity === 'function')
                actor.set_opacity(value);
            else
                actor.opacity = value;

            return true;
        } catch (_e) {
            return false;
        }
    }

    SendMouseMoveAbsolute(x, y) {
        if (!this._vPointer) return false;
        this._vPointer.notify_absolute_motion(GLib.get_monotonic_time(), x, y);
        return true;
    }

    SendMouseMoveRelative(dx, dy) {
        if (!this._vPointer) return false;
        const [cx, cy] = global.get_pointer();
        this._vPointer.notify_absolute_motion(
            GLib.get_monotonic_time(), cx + dx, cy + dy);
        return true;
    }

    SendMouseButton(button, pressed) {
        if (!this._vPointer) return false;
        this._vPointer.notify_button(
            GLib.get_monotonic_time(),
            button,
            pressed ? Clutter.ButtonState.PRESSED : Clutter.ButtonState.RELEASED);
        return true;
    }

    SendMouseScroll(delta, vertical) {
        if (!this._vPointer) return false;

        const notches = Math.max(1, Math.abs(Math.round(delta / 120)));
        let dir;

        if (vertical)
            dir = delta > 0 ? Clutter.ScrollDirection.UP : Clutter.ScrollDirection.DOWN;
        else
            dir = delta > 0 ? Clutter.ScrollDirection.RIGHT : Clutter.ScrollDirection.LEFT;

        const time = GLib.get_monotonic_time();
        for (let i = 0; i < notches; i++)
            this._vPointer.notify_discrete_scroll(time, dir, Clutter.ScrollSource.WHEEL);
        return true;
    }

    // Screen capture is the one genuinely privileged operation here — Wayland normally blocks foreign
    // screenshotting — so it is the ONLY method that is permission-gated. It may be invoked exclusively by
    // the root-owned setuid keysharp-helper, which enforces the user's screen-capture consent
    // (via the trust store) before it ever calls us. Everything else on this interface stays open to any
    // same-user caller. These use the async D-Bus form (FooAsync(params, invocation)) solely so we can read
    // the caller's identity off the invocation; the capture itself is synchronous.
    // Truly async: the D-Bus reply is sent from the screenshot callback, so the shell's main loop
    // keeps running normally in between — no nested main-context pump inside the compositor (which
    // would re-dispatch input/D-Bus re-entrantly and unwind concurrent captures in LIFO order).
    CaptureAreaAsync(params, invocation) {
        if (!this._callerIsHelper(invocation)) {
            invocation.return_error_literal(Gio.IOErrorEnum, Gio.IOErrorEnum.PERMISSION_DENIED,
                'Screen capture is only permitted through keysharp-helper.');
            return;
        }

        const [x, y, width, height] = params;
        const empty = () => invocation.return_value(new GLib.Variant('(ay)', [new Uint8Array(0)]));

        if (width <= 0 || height <= 0) {
            empty();
            return;
        }

        try {
            const screenshot = new Shell.Screenshot();
            const stream = Gio.MemoryOutputStream.new_resizable();

            screenshot.screenshot_area(x, y, width, height, stream, (_obj, result) => {
                try {
                    screenshot.screenshot_area_finish(result);
                    stream.close(null);
                    invocation.return_value(new GLib.Variant('(ay)',
                        [new Uint8Array(stream.steal_as_bytes().get_data())]));
                } catch (e) {
                    logError(e, 'Keysharp: CaptureArea screenshot failed');
                    empty();
                }
            });
        } catch (e) {
            logError(e, 'Keysharp: CaptureArea failed');
            empty();
        }
    }

    CaptureWindowAsync(params, invocation) {
        if (!this._callerIsHelper(invocation)) {
            invocation.return_error_literal(Gio.IOErrorEnum, Gio.IOErrorEnum.PERMISSION_DENIED,
                'Screen capture is only permitted through keysharp-helper.');
            return;
        }

        const [handle] = params;
        invocation.return_value(new GLib.Variant('(ay)', [this._captureWindowBytes(handle)]));
    }

    // Capture a single window's contents and return PNG bytes. Unlike CaptureArea (which images
    // the composited screen and so loses anything behind other windows), this reads the window actor's
    // own backing buffer through meta_window_actor_get_image, so an occluded window still captures
    // correctly. A minimized window has no live texture and yields an empty result (the C# caller then
    // falls back to a rectangle grab).
    _captureWindowBytes(handle) {
        let file = null;
        try {
            const win = this._findWindow(handle);
            if (!win)
                return new Uint8Array(0);

            const actor = win.get_compositor_private();
            if (!actor || typeof actor.get_image !== 'function')
                return new Uint8Array(0);

            // Clip the actor image to the frame rect (decorations, no shadow): the C# side derives
            // the HiDPI buffer scale as capture-size / logical-frame-size, which only works when the
            // capture covers exactly the frame. null clip (whole actor incl. shadow) is the fallback.
            let clip = null;
            try {
                const frame = win.get_frame_rect();
                const [ax, ay] = actor.get_position();
                clip = new Mtk.Rectangle({
                    x: Math.round(frame.x - ax),
                    y: Math.round(frame.y - ay),
                    width: frame.width,
                    height: frame.height,
                });
            } catch (_e) {
                clip = null;
            }

            let surface = null;
            try {
                surface = actor.get_image(clip);
            } catch (e) {
                logError(e, 'Keysharp: meta_window_actor_get_image failed');
                return new Uint8Array(0);
            }

            if (!surface)
                return new Uint8Array(0);

            // GJS cairo can write a surface to a PNG *file* but not to a memory stream, so round-trip
            // through a private temp file that we create, read and delete entirely within the shell.
            const stream = Gio.File.new_tmp('keysharp-winshot-XXXXXX.png');
            file = stream[0];
            stream[1].close(null);
            const path = file.get_path();

            surface.flush();
            surface.writeToPNG(path);

            const [ok, bytes] = GLib.file_get_contents(path);
            if (!ok || !bytes || bytes.length === 0)
                return new Uint8Array(0);

            return bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
        } catch (e) {
            logError(e, 'Keysharp: CaptureWindow failed');
            return new Uint8Array(0);
        } finally {
            if (file !== null) {
                try { file.delete(null); } catch (_e) {}
            }
        }
    }

    // Caller gate for the capture methods: true only when the D-Bus caller is the installed, root-owned,
    // setuid keysharp-helper — the sole process permitted to request capture. keysharp-helper
    // drops to our uid but re-marks itself inspectable (PR_SET_DUMPABLE) before calling, so /proc/<pid>/exe
    // resolves to the on-disk binary; a user cannot forge a root-owned setuid file, so this can't be spoofed.
    _callerIsHelper(invocation) {
        try {
            const sender = invocation.get_sender();
            if (!sender)
                return false;

            const reply = Gio.DBus.session.call_sync(
                'org.freedesktop.DBus', '/org/freedesktop/DBus', 'org.freedesktop.DBus',
                'GetConnectionUnixProcessID', new GLib.Variant('(s)', [sender]),
                new GLib.VariantType('(u)'), Gio.DBusCallFlags.NONE, 1000, null);
            const [pid] = reply.deep_unpack();
            if (!pid || pid <= 0)
                return false;

            const exeLink = `/proc/${pid}/exe`;
            let target;
            try { target = GLib.file_read_link(exeLink); } catch (_e) { return false; }
            if (!target || GLib.path_get_basename(target) !== 'keysharp-helper')
                return false;

            const info = Gio.File.new_for_path(exeLink).query_info(
                'unix::uid,unix::mode', Gio.FileQueryInfoFlags.NONE, null);
            const uid = info.get_attribute_uint32('unix::uid');
            const mode = info.get_attribute_uint32('unix::mode');
            // Root-owned + setuid + not group/other-writable: only root could have installed it.
            return uid === 0 && (mode & 0o4000) !== 0 && (mode & 0o022) === 0;
        } catch (_e) {
            return false;
        }
    }

    // Register a client as an overlay owner. The client re-registers whenever its D-Bus connection is
    // (re)established; this re-stamps the current busName onto any overlays it already owns and cancels a
    // pending reconnect-grace deletion, so a client that merely reconnected keeps its overlays.
    RegisterHighlightOwner(ownerKey, busName) {
        const owner = this._parseOverlayOwner(ownerKey);
        const bus = String(busName || '');

        // Registration is the first extension call made by a new Keysharp process. Reap dead owners here as
        // well as from the periodic timer so stale actors/content cannot accumulate across process launches if
        // GNOME delayed or dropped that timer while the shell was busy (for example during screen capture).
        // Live owners are retained, so concurrently-running HUD scripts are unaffected.
        this._cleanupDeadOverlays();
        this._cancelOverlayReconnectTimer(owner.key);

        for (const entry of this._highlights.values()) {
            if (entry.ownerKey === owner.key) {
                entry.ownerPid = owner.pid;
                entry.ownerStartTime = owner.startTime;
                entry.busName = bus;
            }
        }

        for (const entry of this._imageOverlays.values()) {
            if (entry.ownerKey === owner.key) {
                entry.ownerPid = owner.pid;
                entry.ownerStartTime = owner.startTime;
                entry.busName = bus;
            }
        }

        for (const entry of this._tooltips.values()) {
            if (entry.ownerKey === owner.key) {
                entry.ownerPid = owner.pid;
                entry.ownerStartTime = owner.startTime;
                entry.busName = bus;
            }
        }

        return true;
    }

    // Draw or update a click-through rectangle-outline overlay. GNOME has no wlr-layer-shell, so
    // instead of a layer surface we add four edge actors to the shell's top chrome. Two things make
    // them click-through: reactive:false (they never grab events) and addTopChrome(..., {
    // affectsInputRegion: false }) (they are excluded from the shell's input region, so clicks fall
    // through to the window beneath — the compositor-side equivalent of the layer surface's empty
    // input region on KWin). Four edges (rather than one filled rect) keep the centre visually clear.
    // The overlay is tagged with its owner (see _overlayKey) so it can be reaped if the client dies.
    ShowHighlight(id, ownerKey, busName, x, y, width, height, color, thickness) {
        try {
            const owner = this._parseOverlayOwner(ownerKey);
            const key = this._overlayKey(id, owner.key);
            const bus = String(busName || '');

            this._cancelOverlayReconnectTimer(owner.key);
            this._removeHighlightByKey(key);

            if (width < 1 || height < 1) {
                if (!this._hasAnyOverlays())
                    this._stopOverlayCleanupTimer();
                return true;
            }

            const t   = Math.max(1, thickness);
            const css = `background-color: #${color};`;

            const rects = [
                [x,             y,              width,             t],                          // top
                [x,             y + height - t, width,             t],                          // bottom
                [x,             y + t,          t,                 Math.max(0, height - 2 * t)],// left
                [x + width - t, y + t,          t,                 Math.max(0, height - 2 * t)],// right
            ];

            const edges = [];

            for (const [ex, ey, ew, eh] of rects) {
                if (ew < 1 || eh < 1)
                    continue;

                const a = new St.Widget({ reactive: false, style: css });
                a.set_position(ex, ey);
                a.set_size(ew, eh);
                this._addTopChromeClickThrough(a);
                edges.push(a);
            }

            this._highlights.set(key, {
                edges: edges,
                ownerKey: owner.key,
                ownerPid: owner.pid,
                ownerStartTime: owner.startTime,
                busName: bus,
            });
            this._ensureOverlayCleanupTimer();
            return true;
        } catch (e) {
            logError(e, 'Keysharp: ShowHighlight failed');
            return false;
        }
    }

    HideHighlight(id, ownerKey, _busName) {
        const owner = this._parseOverlayOwner(ownerKey);
        this._removeHighlightByKey(this._overlayKey(id, owner.key));

        if (!this._hasOverlaysForOwner(owner.key))
            this._cancelOverlayReconnectTimer(owner.key);

        if (!this._hasAnyOverlays())
            this._stopOverlayCleanupTimer();

        return true;
    }

    ShowImageOverlay(id, ownerKey, busName, x, y, width, height, pngData) {
        try {
            const owner = this._parseOverlayOwner(ownerKey);
            const key = this._overlayKey(id, owner.key);

            this._cancelOverlayReconnectTimer(owner.key);

            // Empty pixels = a clear request.
            if (!pngData || pngData.length === 0 || width < 1 || height < 1)
                return this._clearImageOverlay(key);

            return this._presentImageOverlay(key, owner, busName, x, y, width, height,
                () => this._decodeImageFrame(pngData));
        } catch (e) {
            logError(e, 'Keysharp: ShowImageOverlay failed');
            return false;
        }
    }

    // The same overlay, with the frame handed over as shared memory instead of an encoded PNG. Only the
    // pixels arrive differently; everything downstream is the ShowImageOverlay path.
    ShowImageOverlayShm(id, ownerKey, busName, x, y, width, height, shmPath, pixelWidth, pixelHeight, stride) {
        try {
            const owner = this._parseOverlayOwner(ownerKey);
            const key = this._overlayKey(id, owner.key);

            this._cancelOverlayReconnectTimer(owner.key);

            if (!shmPath || pixelWidth < 1 || pixelHeight < 1 || width < 1 || height < 1)
                return this._clearImageOverlay(key);

            return this._presentImageOverlay(key, owner, busName, x, y, width, height,
                entry => this._mapImageFrame(entry, shmPath, pixelWidth, pixelHeight, stride));
        } catch (e) {
            logError(e, 'Keysharp: ShowImageOverlayShm failed');
            return false;
        }
    }

    // An empty frame is how a client asks for the overlay to go away.
    _clearImageOverlay(key) {
        this._removeImageOverlayByKey(key);

        if (!this._hasAnyOverlays())
            this._stopOverlayCleanupTimer();

        return true;
    }

    // The shared tail of both Show paths: makeFrame() produces the pixels -- decoded, or mapped out of the
    // client's shared buffer -- and everything from there on is one actor, created once and updated after.
    _presentImageOverlay(key, owner, busName, x, y, width, height, makeFrame) {
        const bus = String(busName || '');
        const existing = this._imageOverlays.get(key);
        const frame = makeFrame(existing);

        if (!frame)
            return false;

        if (existing && existing.actor) {
            try {
                this._updateImageActor(existing.actor, frame, x, y, width, height);
                existing.ownerPid = owner.pid;
                existing.ownerStartTime = owner.startTime;
                existing.busName = bus;
                this._ensureOverlayCleanupTimer();
                return true;
            } catch (_e) {
                this._removeImageOverlayByKey(key);
            }
        }

        const actor = new Clutter.Actor({ reactive: false });
        this._updateImageActor(actor, frame, x, y, width, height);
        this._addTopChromeClickThrough(actor);
        this._imageOverlays.set(key, {
            actor: actor,
            ownerKey: owner.key,
            ownerPid: owner.pid,
            ownerStartTime: owner.startTime,
            busName: bus,
            shm: frame.shm || null,
        });
        this._ensureOverlayCleanupTimer();
        return true;
    }

    MoveImageOverlay(id, ownerKey, _busName, x, y, width, height) {
        try {
            const owner = this._parseOverlayOwner(ownerKey);
            const entry = this._imageOverlays.get(this._overlayKey(id, owner.key));

            // No live actor for this id: report failure so the caller re-sends the pixels via ShowImageOverlay.
            if (!entry || !entry.actor)
                return false;

            entry.actor.set_position(x, y);
            entry.actor.set_size(width, height);
            return true;
        } catch (e) {
            logError(e, 'Keysharp: MoveImageOverlay failed');
            return false;
        }
    }

    HideImageOverlay(id, ownerKey, _busName) {
        const owner = this._parseOverlayOwner(ownerKey);
        this._removeImageOverlayByKey(this._overlayKey(id, owner.key));

        if (!this._hasOverlaysForOwner(owner.key))
            this._cancelOverlayReconnectTimer(owner.key);

        if (!this._hasAnyOverlays())
            this._stopOverlayCleanupTimer();

        return true;
    }

    SupportsTooltip() {
        return true;
    }

    // Draw or update a click-through tooltip label. slot identifies it per owner; empty text clears it.
    // Click-through and owner-tagged like the other overlays (see ShowHighlight for the mechanism).
    ShowTooltip(slot, ownerKey, busName, text, x, y) {
        try {
            const owner = this._parseOverlayOwner(ownerKey);
            const key = this._overlayKey(slot, owner.key);
            const bus = String(busName || '');
            const labelText = String(text || '');

            this._cancelOverlayReconnectTimer(owner.key);
            this._removeTooltipByKey(key);

            if (!labelText) {
                if (!this._hasAnyOverlays())
                    this._stopOverlayCleanupTimer();
                return true;
            }

            const actor = new St.Label({
                reactive: false,
                text: labelText,
                style: 'background-color: #ffffe1; color: #000000; border: 1px solid #000000; padding: 6px; font-size: 10pt; font-family: sans-serif;',
            });
            actor.set_position(x, y);
            this._addTopChromeClickThrough(actor);

            this._tooltips.set(key, {
                actor: actor,
                ownerKey: owner.key,
                ownerPid: owner.pid,
                ownerStartTime: owner.startTime,
                busName: bus,
            });
            this._ensureOverlayCleanupTimer();
            return true;
        } catch (e) {
            logError(e, 'Keysharp: ShowTooltip failed');
            return false;
        }
    }

    HideTooltip(slot, ownerKey, _busName) {
        const owner = this._parseOverlayOwner(ownerKey);
        this._removeTooltipByKey(this._overlayKey(slot, owner.key));

        if (!this._hasOverlaysForOwner(owner.key))
            this._cancelOverlayReconnectTimer(owner.key);

        if (!this._hasAnyOverlays())
            this._stopOverlayCleanupTimer();

        return true;
    }

    // ----------------------------------------------------------------
    // Private helpers
    // ----------------------------------------------------------------

    _removeHighlightByKey(key) {
        const entry = this._highlights.get(key);
        if (!entry)
            return;

        for (const a of (entry.edges || [])) {
            try { Main.layoutManager.removeChrome(a); } catch (_e) {}
            try { a.destroy(); } catch (_e) {}
        }

        this._highlights.delete(key);
    }

    _removeImageOverlayByKey(key) {
        const entry = this._imageOverlays.get(key);
        if (!entry)
            return;

        try { Main.layoutManager.removeChrome(entry.actor); } catch (_e) {}
        try { entry.actor.destroy(); } catch (_e) {}
        this._imageOverlays.delete(key);
    }

    _removeTooltipByKey(key) {
        const entry = this._tooltips.get(key);
        if (!entry)
            return;

        try { Main.layoutManager.removeChrome(entry.actor); } catch (_e) {}
        try { entry.actor.destroy(); } catch (_e) {}
        this._tooltips.delete(key);
    }

    // ---- overlay ownership: keying + owner parsing -------------------------------

    // Namespaces an overlay id under its owner so two clients using the same id never collide.
    _overlayKey(id, ownerKey) {
        return `${ownerKey}:${id}`;
    }

    // ownerKey is "<pid>:<starttime>" (see WaylandOverlayOwner on the C# side).
    _parseOverlayOwner(ownerKey) {
        const key = String(ownerKey || '');
        const parts = key.split(':');
        const pid = parts.length > 0 ? Number(parts[0]) : 0;
        const startTime = parts.length > 1 ? parts[1] : '';
        return {
            key: key,
            pid: Number.isFinite(pid) ? Math.round(pid) : 0,
            startTime: startTime,
        };
    }

    // True unless the owning process is provably gone. Reads field 22 (starttime) of /proc/<pid>/stat
    // and requires it to match the value captured when the overlay was created, so a reused pid (a
    // different process now holding <pid>) is treated as dead. Fails open on any read/parse error.
    _overlayOwnerAlive(pid, startTime) {
        if (!pid || pid <= 0)
            return true;

        const statPath = `/proc/${pid}/stat`;

        if (!GLib.file_test(statPath, GLib.FileTest.EXISTS))
            return false;

        if (!startTime)
            return true;

        try {
            const [ok, bytes] = GLib.file_get_contents(statPath);
            if (!ok)
                return true;

            const stat = new TextDecoder().decode(bytes);
            const end = stat.lastIndexOf(')');

            if (end < 0 || end + 2 >= stat.length)
                return true;

            const fields = stat.substring(end + 2).trim().split(/\s+/);
            return fields.length > 19 && fields[19] === startTime;
        } catch (_e) {
            return true;
        }
    }

    // ---- overlay ownership: reconnect grace timers -------------------------------

    // Called when a client's D-Bus name vanishes. If the owning process is already dead, reap now;
    // otherwise wait OVERLAY_RECONNECT_GRACE_MS and reap only if it stayed dead or never came back under
    // a new bus name (a reconnecting client re-registers, replacing busName -> _ownerHasReplacementBus).
    _handleOverlayConnectionLost(ownerKey, lostBusName, sampleEntry) {
        if (!this._overlayOwnerAlive(sampleEntry.ownerPid, sampleEntry.ownerStartTime)) {
            this._removeOwnerOverlays(ownerKey);
            return;
        }

        this._cancelOverlayReconnectTimer(ownerKey);
        const timerId = GLib.timeout_add(GLib.PRIORITY_DEFAULT, OVERLAY_RECONNECT_GRACE_MS, () => {
            this._overlayReconnectTimers.delete(ownerKey);

            const entry = this._firstOverlayForOwner(ownerKey);

            if (!entry)
                return GLib.SOURCE_REMOVE;

            if (!this._overlayOwnerAlive(entry.ownerPid, entry.ownerStartTime)
                || !this._ownerHasReplacementBus(ownerKey, lostBusName))
                this._removeOwnerOverlays(ownerKey);

            return GLib.SOURCE_REMOVE;
        });
        this._overlayReconnectTimers.set(ownerKey, timerId);
    }

    _cancelOverlayReconnectTimer(ownerKey) {
        const timerId = this._overlayReconnectTimers.get(ownerKey);

        if (!timerId)
            return;

        try { GLib.source_remove(timerId); } catch (_e) {}
        this._overlayReconnectTimers.delete(ownerKey);
    }

    _cancelAllOverlayReconnectTimers() {
        for (const timerId of this._overlayReconnectTimers.values()) {
            try { GLib.source_remove(timerId); } catch (_e) {}
        }

        this._overlayReconnectTimers.clear();
    }

    _ownerHasReplacementBus(ownerKey, lostBusName) {
        for (const entry of this._highlights.values()) {
            if (entry.ownerKey === ownerKey && entry.busName && entry.busName !== lostBusName)
                return true;
        }

        for (const entry of this._imageOverlays.values()) {
            if (entry.ownerKey === ownerKey && entry.busName && entry.busName !== lostBusName)
                return true;
        }

        for (const entry of this._tooltips.values()) {
            if (entry.ownerKey === ownerKey && entry.busName && entry.busName !== lostBusName)
                return true;
        }

        return false;
    }

    // ---- overlay ownership: periodic /proc liveness sweep ------------------------

    // Safety net for a client that dies without its D-Bus name-drop reaching us: a 2s sweep reaps any
    // overlay whose owning process is gone. Self-terminating once no overlays remain.
    _ensureOverlayCleanupTimer() {
        if (this._overlayCleanupId !== 0)
            return;

        this._overlayCleanupId = GLib.timeout_add_seconds(GLib.PRIORITY_DEFAULT, 2, () => {
            this._cleanupDeadOverlays();

            if (!this._hasAnyOverlays()) {
                this._overlayCleanupId = 0;
                return GLib.SOURCE_REMOVE;
            }

            return GLib.SOURCE_CONTINUE;
        });
    }

    _stopOverlayCleanupTimer() {
        if (this._overlayCleanupId === 0)
            return;

        try { GLib.source_remove(this._overlayCleanupId); } catch (_e) {}
        this._overlayCleanupId = 0;
    }

    _cleanupDeadOverlays() {
        const owners = new Map();

        for (const entry of this._highlights.values()) {
            if (!this._overlayOwnerAlive(entry.ownerPid, entry.ownerStartTime))
                owners.set(entry.ownerKey, true);
        }

        for (const entry of this._imageOverlays.values()) {
            if (!this._overlayOwnerAlive(entry.ownerPid, entry.ownerStartTime))
                owners.set(entry.ownerKey, true);
        }

        for (const entry of this._tooltips.values()) {
            if (!this._overlayOwnerAlive(entry.ownerPid, entry.ownerStartTime))
                owners.set(entry.ownerKey, true);
        }

        for (const ownerKey of owners.keys())
            this._removeOwnerOverlays(ownerKey);
    }

    // ---- overlay ownership: NameOwnerChanged watch -------------------------------

    _handleNameOwnerChanged(parameters) {
        let name, oldOwner, newOwner;

        try {
            [name, oldOwner, newOwner] = parameters.deep_unpack();
        } catch (_e) {
            return;
        }

        // Only react to a name being released (had an owner, now has none).
        if (!name || !oldOwner || newOwner)
            return;

        const owners = new Map();

        for (const entry of this._highlights.values()) {
            if (entry.busName === name && !owners.has(entry.ownerKey))
                owners.set(entry.ownerKey, entry);
        }

        for (const entry of this._imageOverlays.values()) {
            if (entry.busName === name && !owners.has(entry.ownerKey))
                owners.set(entry.ownerKey, entry);
        }

        for (const entry of this._tooltips.values()) {
            if (entry.busName === name && !owners.has(entry.ownerKey))
                owners.set(entry.ownerKey, entry);
        }

        for (const [ownerKey, entry] of owners.entries())
            this._handleOverlayConnectionLost(ownerKey, name, entry);
    }

    // ---- overlay ownership: per-owner queries + removal --------------------------

    _firstOverlayForOwner(ownerKey) {
        for (const entry of this._highlights.values())
            if (entry.ownerKey === ownerKey)
                return entry;

        for (const entry of this._imageOverlays.values())
            if (entry.ownerKey === ownerKey)
                return entry;

        for (const entry of this._tooltips.values())
            if (entry.ownerKey === ownerKey)
                return entry;

        return null;
    }

    _hasOverlaysForOwner(ownerKey) {
        return this._firstOverlayForOwner(ownerKey) !== null;
    }

    _hasAnyOverlays() {
        return this._highlights.size !== 0 || this._imageOverlays.size !== 0 || this._tooltips.size !== 0;
    }

    _removeOwnerOverlays(ownerKey) {
        for (const [key, entry] of [...this._highlights.entries()]) {
            if (entry.ownerKey === ownerKey)
                this._removeHighlightByKey(key);
        }

        for (const [key, entry] of [...this._imageOverlays.entries()]) {
            if (entry.ownerKey === ownerKey)
                this._removeImageOverlayByKey(key);
        }

        for (const [key, entry] of [...this._tooltips.entries()]) {
            if (entry.ownerKey === ownerKey)
                this._removeTooltipByKey(key);
        }

        this._cancelOverlayReconnectTimer(ownerKey);

        if (!this._hasAnyOverlays())
            this._stopOverlayCleanupTimer();
    }

    _decodeImageFrame(pngData) {
        const loader = GdkPixbuf.PixbufLoader.new();
        loader.write(pngData);
        loader.close();
        const pixbuf = loader.get_pixbuf();

        if (!pixbuf)
            throw new Error('Could not decode PNG overlay image.');

        return {
            pixbuf, // keeps the borrowed pixel array alive until the texture copy completes
            pixels: pixbuf.get_pixels(),
            format: pixbuf.get_has_alpha() ? Cogl.PixelFormat.RGBA_8888 : Cogl.PixelFormat.RGB_888,
            width: pixbuf.get_width(),
            height: pixbuf.get_height(),
            rowStride: pixbuf.get_rowstride(),
        };
    }

    _createImageContent(frame) {
        // Shell 48 removed Clutter.Image; St.ImageContent replaces it, with set_bytes()
        // taking a leading Cogl.Context parameter. Give St.ImageContent a real preferred size (its default
        // -1x-1 produces one shell warning per HUD frame on GNOME 50) and retain the pixels as GLib.Bytes so
        // their lifetime is owned by the content rather than by GJS's temporary Uint8Array wrapper.
        let content;
        if (SHELL_MAJOR >= 48) {
            content = new St.ImageContent({
                preferredWidth: frame.width,
                preferredHeight: frame.height,
            });
            const coglContext = global.stage.context.get_backend().get_cogl_context();
            content.set_bytes(coglContext, frame.pixbuf.read_pixel_bytes(), frame.format,
                frame.width, frame.height, frame.rowStride);
        } else {
            content = new Clutter.Image();
            content.set_data(frame.pixels, frame.format, frame.width, frame.height, frame.rowStride);
        }

        return content;
    }

    // Maps the client's frame buffer. Both processes map the same file, so the pixels the client wrote are
    // already here and the only per-frame cost is the texture upload in _updateImageActor. The mapping is
    // kept on the overlay entry and redone only when the client names a different file, which is how it
    // reports a size change -- a resized overlay always gets a fresh path.
    _mapImageFrame(entry, shmPath, width, height, stride) {
        const path = String(shmPath);

        // Only ever map a buffer one of our own clients wrote.
        if (!GLib.path_get_basename(path).startsWith('keysharp-overlay-'))
            return null;

        let shm = entry ? entry.shm : null;

        if (!shm || shm.path !== path || shm.width !== width || shm.height !== height || shm.stride !== stride) {
            let mapped = null;

            try {
                mapped = GLib.MappedFile.new(path, false);
            } catch (_e) {
                return null;
            }

            // A short file would have the texture upload read past the mapping.
            if (mapped.get_length() < stride * height)
                return null;

            shm = {path, mapped, width, height, stride};

            if (entry)
                entry.shm = shm;
        }

        return {
            // Shares the mapping rather than copying it, so this stays cheap at video rates.
            pixels: shm.mapped.get_bytes().toArray(),
            // Cairo's ARGB32, which is what the client draws into: premultiplied BGRA on little-endian.
            format: Cogl.PixelFormat.BGRA_8888_PRE,
            width,
            height,
            rowStride: stride,
            shm,
        };
    }

    _updateImageActor(actor, frame, x, y, width, height) {
        let content = actor.get_content();

        // Both content setters replace the Cogl texture; update that texture in place while the size is stable.
        if (content && actor._keysharpImageWidth === frame.width &&
            actor._keysharpImageHeight === frame.height) {
            try {
                let updated = false;

                if (SHELL_MAJOR >= 48 && typeof content.get_texture === 'function') {
                    const texture = content.get_texture();

                    if (texture && typeof texture.set_region === 'function') {
                        updated = texture.set_region(0, 0, 0, 0,
                            frame.width, frame.height, frame.width, frame.height,
                            frame.format, frame.rowStride, frame.pixels);

                        if (updated)
                            content.invalidate();
                    }
                } else if (typeof content.set_area === 'function') {
                    const area = new CairoGI.RectangleInt();
                    area.x = 0;
                    area.y = 0;
                    area.width = frame.width;
                    area.height = frame.height;
                    updated = content.set_area(frame.pixels, frame.format, area, frame.rowStride);
                }

                if (!updated)
                    content = null;
            } catch (_e) {
                content = null;
            }
        } else {
            content = null;
        }

        if (!content) {
            content = this._createImageContent(frame);
            actor.set_content(content);
            actor._keysharpImageWidth = frame.width;
            actor._keysharpImageHeight = frame.height;
        }

        actor.set_position(x, y);
        actor.set_size(width, height);
    }

    // Add a reactive:false actor to the top chrome as a CLICK-THROUGH overlay (clicks fall through to the
    // window beneath). GNOME Shell 50 removed the `affectsInputRegion` chrome parameter: the shell input
    // region is now built only from REACTIVE actors, so a reactive:false actor is click-through with no
    // flag. Shells 45–49 still need `affectsInputRegion: false`, or the non-reactive chrome keeps clipping
    // the input region and eats clicks. Passing the param on 50 throws "unrecognized parameter
    // affectsInputRegion" from _trackActor — and because addTopChrome adds the actor to uiGroup BEFORE it
    // validates params, the throw leaves a visible, untracked, un-reapable orphan actor on stage while the
    // caller reports failure (which duplicated the overlay via the Eto fallback and persisted till logout).
    _addTopChromeClickThrough(actor) {
        if (SHELL_MAJOR >= 50)
            Main.layoutManager.addTopChrome(actor);
        else
            Main.layoutManager.addTopChrome(actor, { affectsInputRegion: false });
    }

    _emitActiveWindowChanged() {
        if (!this._dbusImpl) return;
        try {
            const json = this.GetActiveWindow();
            this._dbusImpl.emit_signal(
                'ActiveWindowChanged',
                new GLib.Variant('(s)', [json]));
        } catch (_e) {
            // best-effort; never throw inside a signal handler
        }
    }

    // Emit a generic WindowEvent for the given window, resolving its full info JSON
    // (falling back to a bare { id } if the window is mid-teardown).
    _emitWindowEvent(type, win) {
        if (!win) return;
        let json;
        try {
            json = JSON.stringify(this._windowInfo(win));
        } catch (_e) {
            try { json = JSON.stringify({id: String(win.get_stable_sequence())}); }
            catch (_e2) { return; }
        }
        this._emitWindowEventRaw(type, json);
    }

    _emitWindowEventRaw(type, json) {
        if (!this._dbusImpl) return;
        try {
            this._dbusImpl.emit_signal(
                'WindowEvent',
                new GLib.Variant('(ss)', [type, json]));
        } catch (_e) {
            // best-effort; never throw inside a signal handler
        }
    }

    // Connect per-window signals once. Handler ids are stashed on the window so they
    // can be disconnected in _unhookWindow / on 'unmanaged'.
    _hookWindow(win) {
        if (!win || win._keysharpHooked)
            return;
        win._keysharpHooked = true;

        // Read the pid now, while the window is certainly alive; see _clientPid.
        this._clientPid(win, true);

        const ids = [];
        ids.push(win.connect('notify::title', () => this._emitWindowEvent('title', win)));
        ids.push(win.connect('notify::minimized',
            () => this._emitWindowEvent(win.minimized ? 'minimize' : 'restore', win)));
        ids.push(win.connect('position-changed', () => this._emitWindowEvent('move', win)));
        ids.push(win.connect('size-changed',     () => this._emitWindowEvent('move', win)));
        ids.push(win.connect('unmanaged', () => {
            try {
                this._emitWindowEventRaw('close',
                    JSON.stringify({id: String(win.get_stable_sequence())}));
            } catch (_e) {}
            this._unhookWindow(win);
        }));
        win._keysharpHandlerIds = ids;
    }

    _unhookWindow(win) {
        if (!win || !win._keysharpHooked)
            return;
        for (const id of (win._keysharpHandlerIds || [])) {
            try { win.disconnect(id); } catch (_e) {}
        }
        win._keysharpHandlerIds = null;
        win._keysharpHooked = false;
    }

    // An unmanaged window lingers in global.get_window_actors() for as long as the shell's close animation
    // runs. It is not a window any more -- reporting it as one is wrong, and asking it about its Wayland
    // client is fatal: Muffin's meta_window_wayland_get_client_pid() hands the already-freed wl_resource
    // straight to wl_resource_get_client(), which dereferences it without a NULL check and takes the whole
    // shell down with it. Everything that walks the actor list therefore goes through here first.
    _isLiveWindow(win) {
        try {
            const actor = win ? win.get_compositor_private() : null;

            if (!actor)
                return false;

            return typeof actor.is_destroyed !== 'function' || !actor.is_destroyed();
        } catch (_e) {
            return false;
        }
    }

    _liveMetaWindow(actor) {
        const win = actor ? actor.get_meta_window() : null;
        return (win && this._isLiveWindow(win)) ? win : null;
    }

    // The window's client pid, read once while the window is unquestionably alive and cached on it after
    // that, so a later query -- which may well arrive mid-teardown -- never has to ask the compositor again.
    // A window that is already gone has no pid to give: -1, the same answer as the platforms that cannot say.
    //
    // `alive` is for the callers that hold that guarantee themselves -- window-created, where the window has
    // no compositor actor yet and _isLiveWindow cannot tell "not composited" from "destroyed" -- and it is
    // exactly those callers that seed the cache for everyone else.
    _clientPid(win, alive) {
        if (typeof win.__ksClientPid === 'number')
            return win.__ksClientPid;

        if (!alive && !this._isLiveWindow(win))
            return -1;

        let pid = -1;

        try {
            // Muffin answers the client pid only via get_client_pid() (get_pid() is -1 for Wayland
            // clients); modern Mutter removed get_client_pid() and made get_pid() answer it instead.
            pid = win.get_pid() > 0 ? win.get_pid() : (win.get_client_pid ? win.get_client_pid() : -1);
        } catch (_e) {
            pid = -1;
        }

        if (pid > 0)
            win.__ksClientPid = pid;

        return pid;
    }

    _isTrackedWindow(win) {
        switch (win.window_type) {
        case Meta.WindowType.NORMAL:
        case Meta.WindowType.DIALOG:
        case Meta.WindowType.MODAL_DIALOG:
        case Meta.WindowType.UTILITY:
            return true;
        default:
            return false;
        }
    }

    _windowInfo(win) {
        const frame = win.get_frame_rect();

        // The surface as the client drew it, shadow included. That is precisely why it is wrong as a client
        // origin (see below) and right as the surface origin: a Wayland client is never told where its surface
        // sits, so this is the only thing its own coordinates can be resolved against. Absent on a compositor
        // that cannot answer, which leaves the consumer uncorrected rather than wrong.
        let buffer = null;
        try {
            const b = win.get_buffer_rect();
            if (b) buffer = {x: b.x, y: b.y, width: b.width, height: b.height};
        } catch (_e) {
        }

        // get_maximized() was removed in Mutter 50; use the individual boolean
        // GObject properties instead, which are stable across all versions.
        const maximized = !!(win.maximized_horizontally && win.maximized_vertically);

        // Use frame_rect for both frame and client geometry. get_buffer_rect() extends
        // outside the visible window to include compositor shadow/glow effects, so using
        // it as the client origin produces a ~20 px negative offset in coordinate
        // transforms. On GNOME Wayland, apps use client-side decorations and their
        // entire surface is the frame rect, so frame == client for coordinate purposes.
        // Windows on other workspaces still "exist" (enumeration/matching), but must not win
        // at-point hit-tests — global.get_window_actors() spans ALL workspaces.
        let onCurrentWorkspace = true;
        try {
            const ws = win.get_workspace();
            onCurrentWorkspace = win.is_on_all_workspaces()
                || (ws !== null && ws === global.workspace_manager.get_active_workspace());
        } catch (_e) {
        }

        return {
            id:        String(win.get_stable_sequence()),
            title:     win.get_title()            ?? '',
            appId:     win.get_wm_class()         ?? win.get_wm_class_instance() ?? '',
            pid:       this._clientPid(win),
            frame:     {x: frame.x, y: frame.y, width: frame.width, height: frame.height},
            client:    {x: frame.x, y: frame.y, width: frame.width, height: frame.height},
            buffer:    buffer,
            active:    !!win.appears_focused,
            minimized: !!win.minimized,
            maximized,
            visible:   !win.minimized,
            alwaysOnTop: !!win.is_above(),
            decorated:   !!win.decorated,
            transparency: this._windowOpacity(win),
            onCurrentWorkspace,
        };
    }

    // Current whole-window opacity, 0 (transparent)..255 (opaque), read back from the compositor actor.
    // Mirrors SetWindowOpacity's actor access so WinGetTransparent round-trips; defaults to opaque when
    // the window has no live actor. Matches the Cinnamon extension's opacity() helper.
    _windowOpacity(win) {
        try {
            const actor = (typeof win.get_compositor_private === 'function')
                ? win.get_compositor_private()
                : null;
            if (!actor)
                return 255;
            const value = (typeof actor.get_opacity === 'function') ? actor.get_opacity() : actor.opacity;
            return Math.max(0, Math.min(255, Math.round(Number(value))));
        } catch (_e) {
            return 255;
        }
    }

    _findWindow(handle) {
        const seq = Number(handle);
        for (const actor of global.get_window_actors()) {
            const win = this._liveMetaWindow(actor);
            if (win && win.get_stable_sequence() === seq)
                return win;
        }
        return null;
    }

    _findWindowByXid(xid) {
        const target = Number(xid);
        for (const actor of global.get_window_actors()) {
            const win = this._liveMetaWindow(actor);
            if (win && typeof win.get_xwindow === 'function' && Number(win.get_xwindow()) === target)
                return win;
        }
        return null;
    }
}
