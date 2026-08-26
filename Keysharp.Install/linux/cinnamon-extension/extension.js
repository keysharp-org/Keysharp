// Cinnamon extension for Keysharp integration.
// Exposes window management, window events and mouse simulation via D-Bus
// so Keysharp can use compositor-owned state on Cinnamon Wayland.
//
// D-Bus service name : io.github.keysharp.CinnamonShell
// Object path        : /io/github/keysharp/CinnamonShell
// Interface          : io.github.keysharp.CinnamonShell1

const Cinnamon = imports.gi.Cinnamon;
const Clutter = imports.gi.Clutter;
const Cogl = imports.gi.Cogl;
const Gio = imports.gi.Gio;
const GdkPixbuf = imports.gi.GdkPixbuf;
const GLib = imports.gi.GLib;
const Meta = imports.gi.Meta;
const St = imports.gi.St;
const CairoGI = imports.gi.cairo;
const Main = imports.ui.main;
const ByteArray = imports.byteArray;
const Cairo = imports.cairo;

const SERVICE_NAME = 'io.github.keysharp.CinnamonShell';

// How many compositor frames to wait for a reserved window to become placeable.
// Retries are timer-driven, so this is a real time budget: 40 x 16ms.
const PLACEMENT_MAX_TRIES = 40;
const PLACEMENT_RETRY_MS = 16;
// How long the actor watchdog stays armed after placement; the observed yank fight lasts ~150ms.
const PLACEMENT_WATCH_MS = 600;

// How long a reservation stays live if the window it was meant for never arrives.
const PLACEMENT_TTL_MS = 2000;

const OBJECT_PATH = '/io/github/keysharp/CinnamonShell';

const DBUS_IFACE_XML =
`<node>
  <interface name="io.github.keysharp.CinnamonShell1">
    <method name="GetWindowList">
      <arg type="b" direction="in" name="includeHidden"/>
      <arg type="s" direction="out" name="json"/>
    </method>

    <method name="GetActiveWindow">
      <arg type="s" direction="out" name="json"/>
    </method>

    <method name="GetCursorPosition">
      <arg type="i" direction="out" name="x"/>
      <arg type="i" direction="out" name="y"/>
    </method>

    <method name="GetWorkArea">
      <arg type="i" direction="out" name="x"/>
      <arg type="i" direction="out" name="y"/>
      <arg type="i" direction="out" name="width"/>
      <arg type="i" direction="out" name="height"/>
    </method>

    <!-- Capture a screen region and return raw PNG bytes. Permission-gated: only callable by the
         root-owned setuid keysharp-helper (which enforces the user's capture consent). -->
    <method name="CaptureArea">
      <arg type="i" direction="in" name="x"/>
      <arg type="i" direction="in" name="y"/>
      <arg type="i" direction="in" name="width"/>
      <arg type="i" direction="in" name="height"/>
      <arg type="ay" direction="out" name="pngData"/>
    </method>

    <!-- Capture one window's own buffer (occlusion-independent, frame-clipped) as PNG bytes.
         Same keysharp-helper permission gate as CaptureArea. -->
    <method name="CaptureWindow">
      <arg type="t" direction="in" name="handle"/>
      <arg type="ay" direction="out" name="pngData"/>
    </method>

    <method name="FocusWindow">
      <arg type="t" direction="in" name="handle"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <method name="RaiseWindow">
      <arg type="t" direction="in" name="handle"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

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

    <method name="MoveResizeWindow">
      <arg type="t" direction="in" name="handle"/>
      <arg type="i" direction="in" name="x"/>
      <arg type="i" direction="in" name="y"/>
      <arg type="i" direction="in" name="width"/>
      <arg type="i" direction="in" name="height"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <method name="MoveResizeWindowByXid">
      <arg type="t" direction="in" name="xid"/>
      <arg type="i" direction="in" name="x"/>
      <arg type="i" direction="in" name="y"/>
      <arg type="i" direction="in" name="width"/>
      <arg type="i" direction="in" name="height"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <method name="SetWindowState">
      <arg type="t" direction="in" name="handle"/>
      <arg type="i" direction="in" name="state"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <method name="SetWindowAbove">
      <arg type="t" direction="in" name="handle"/>
      <arg type="b" direction="in" name="above"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <method name="SetWindowDecorated">
      <arg type="t" direction="in" name="handle"/>
      <arg type="b" direction="in" name="decorated"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <method name="SetWindowOpacity">
      <arg type="t" direction="in" name="handle"/>
      <arg type="i" direction="in" name="opacity"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <method name="CloseWindow">
      <arg type="t" direction="in" name="handle"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <method name="KillWindow">
      <arg type="t" direction="in" name="handle"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

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

    <method name="SendMouseButton">
      <arg type="u" direction="in" name="button"/>
      <arg type="b" direction="in" name="pressed"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <method name="SendMouseScroll">
      <arg type="i" direction="in" name="delta"/>
      <arg type="b" direction="in" name="vertical"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

    <method name="RegisterHighlightOwner">
      <arg type="s" direction="in" name="ownerKey"/>
      <arg type="s" direction="in" name="busName"/>
      <arg type="b" direction="out" name="ok"/>
    </method>

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

    <!-- Clipboard bridge: Wayland has no focus-independent clipboard access for a background app, and
         Muffin exposes no data-control protocol, so the shell (which owns the selection) reads/writes it
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

    <signal name="ActiveWindowChanged">
      <arg type="s" name="json"/>
    </signal>

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
const STATE_MINIMIZED = 1;
const STATE_MAXIMIZED = 2;
const INT32_MIN = -2147483648;
const OVERLAY_RECONNECT_GRACE_MS = 2000;

let extension = null;

function init(_metadata) {
}

function enable() {
    extension = new KeysharpExtension();
    extension.enable();
}

function disable() {
    if (extension !== null) {
        extension.disable();
        extension = null;
    }
}

class KeysharpExtension {
    constructor() {
        this._dbusImpl = null;
        this._busNameId = 0;
        this._vPointer = null;
        this._focusId = null;
        this._winCreatedId = null;
        this._mapId = 0;
        this._placements = [];
        this._placed = {};
        this._placementSources = new Set();
        this._windowSignalIds = new Map();
        this._highlights = new Map();
        this._imageOverlays = new Map();
        this._overlayCleanupId = 0;
        this._overlayNameWatchId = 0;
        this._overlayReconnectTimers = new Map();
        this._clipboard = null;
        this._clipboardSelectionId = null;
    }

    enable() {
        try {
            const seat = Clutter.get_default_backend().get_default_seat();
            this._vPointer = seat.create_virtual_device(Clutter.InputDeviceType.POINTER_DEVICE);
        } catch (e) {
            global.logError(e, 'Keysharp: could not create Cinnamon virtual pointer device');
        }

        this._dbusImpl = Gio.DBusExportedObject.wrapJSObject(DBUS_IFACE_XML, this);
        this._dbusImpl.export(Gio.DBus.session, OBJECT_PATH);
        this._overlayNameWatchId = Gio.DBus.session.signal_subscribe(
            'org.freedesktop.DBus',
            'org.freedesktop.DBus',
            'NameOwnerChanged',
            '/org/freedesktop/DBus',
            null,
            Gio.DBusSignalFlags.NONE,
            (_connection, _sender, _path, _iface, _signal, parameters) => this._handleNameOwnerChanged(parameters));

        try {
            this._busNameId = Gio.bus_own_name(
                Gio.BusType.SESSION,
                SERVICE_NAME,
                Gio.BusNameOwnerFlags.NONE,
                null,
                null,
                null);
        } catch (e) {
            global.logError(e, 'Keysharp: could not own Cinnamon D-Bus name');
        }

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

        this._winCreatedId = global.display.connect('window-created', (_display, win) => {
            if (!win || !this._isTrackedWindow(win))
                return;

            this._placeOnMap(win);
            this._hookWindow(win);
            this._emitWindowEvent('create', win);
        });

        for (const actor of global.get_window_actors()) {
            const win = actor.get_meta_window();
            if (win && this._isTrackedWindow(win))
                this._hookWindow(win);
        }

        // Clipboard change notification: the shell owns the selection, so MetaSelection's owner-changed fires
        // for every clipboard change regardless of which client (or none) is focused — the focus-independent
        // path a background app otherwise can't get on Muffin (no data-control protocol).
        try {
            this._clipboard = St.Clipboard.get_default();
            this._clipboardSelectionId = global.display.get_selection().connect('owner-changed',
                (_selection, selectionType, _source) => {
                    if (selectionType === Meta.SelectionType.SELECTION_CLIPBOARD)
                        this._emitClipboardChanged();
                });
        } catch (e) {
            global.logError(e, 'Keysharp: could not hook clipboard selection');
        }
    }

    disable() {
        if (this._clipboardSelectionId !== null) {
            try { global.display.get_selection().disconnect(this._clipboardSelectionId); } catch (_e) {}
            this._clipboardSelectionId = null;
        }
        this._clipboard = null;

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

        if (this._focusId !== null) {
            global.display.disconnect(this._focusId);
            this._focusId = null;
        }

        for (const actor of global.get_window_actors()) {
            const win = actor.get_meta_window();
            if (win)
                this._unhookWindow(win);
        }

        for (const id of Array.from(this._highlights.keys()))
            this._removeHighlightByKey(id);

        for (const id of Array.from(this._imageOverlays.keys()))
            this._removeImageOverlayByKey(id);

        this._stopOverlayCleanupTimer();
        this._cancelAllOverlayReconnectTimers();

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
    }

    GetWindowList(includeHidden) {
        try {
            const windows = [];

            for (const actor of global.get_window_actors()) {
                const win = actor.get_meta_window();
                if (!win || !this._isTrackedWindow(win))
                    continue;
                if (!includeHidden && win.minimized)
                    continue;

                windows.push(this._windowInfo(win));
            }

            return JSON.stringify({ok: true, windows: windows});
        } catch (e) {
            return JSON.stringify({ok: false, error: String(e), windows: []});
        }
    }

    GetActiveWindow() {
        try {
            const win = global.display.get_focus_window();
            return JSON.stringify({
                ok: true,
                window: (win && this._isTrackedWindow(win)) ? this._windowInfo(win) : null
            });
        } catch (e) {
            return JSON.stringify({ok: false, window: null});
        }
    }

    GetCursorPosition() {
        const point = global.get_pointer();
        return [Math.round(point[0]), Math.round(point[1])];
    }

    GetWorkArea() {
        try {
            const ws = global.workspace_manager.get_active_workspace();
            const monitorIndex = Main.layoutManager.primaryIndex;
            const area = ws.get_work_area_for_monitor(monitorIndex);
            return [
                Math.round(area.x),
                Math.round(area.y),
                Math.round(area.width),
                Math.round(area.height)
            ];
        } catch (e) {
            const monitor = Main.layoutManager.primaryMonitor;
            if (!monitor)
                return [0, 0, 0, 0];
            return [
                Math.round(monitor.x),
                Math.round(monitor.y),
                Math.round(monitor.width),
                Math.round(monitor.height)
            ];
        }
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
            global.logError(e, 'Keysharp: SetClipboardContent failed');
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
        return this.SetClipboardContent('text/plain;charset=utf-8', ByteArray.fromString(String(text || '')));
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

    // Screen capture is the one genuinely privileged operation here — Wayland normally blocks foreign
    // screenshotting — so it is the ONLY method that is permission-gated. It may be invoked exclusively by
    // the root-owned setuid keysharp-helper, which enforces the user's screen-capture consent (via
    // the trust store) before it ever calls us. Everything else on this interface stays open to any same-user
    // caller. The async D-Bus form lets us read the caller's identity off the invocation and complete once
    // the compositor delivers the frame.
    CaptureAreaAsync(params, invocation) {
        if (!this._callerIsHelper(invocation)) {
            invocation.return_error_literal(Gio.IOErrorEnum, Gio.IOErrorEnum.PERMISSION_DENIED,
                'Screen capture is only permitted through keysharp-helper.');
            return;
        }

        const [x, y, width, height] = params;

        if (width <= 0 || height <= 0) {
            invocation.return_value(new GLib.Variant('(ay)', [new Uint8Array(0)]));
            return;
        }

        let file = null;
        try {
            // Cinnamon.Screenshot writes a PNG to a file (no in-memory stream API), so round-trip through a
            // private temp file we create, read and delete inside the shell. Coordinates arrive logical; the
            // Screenshot API wants device pixels, so scale by global.ui_scale (matching Cinnamon's own
            // screenshot service) — the returned PNG is therefore device pixels, as the C# caller expects.
            const tmp = Gio.File.new_tmp('keysharp-cinnamon-capture-XXXXXX.png');
            file = tmp[0];
            tmp[1].close(null);
            const path = file.get_path();
            const scale = global.ui_scale || 1;

            const screenshot = new Cinnamon.Screenshot();
            screenshot.screenshot_area(
                false,
                Math.round(x * scale),
                Math.round(y * scale),
                Math.round(width * scale),
                Math.round(height * scale),
                path,
                (_obj, success) => {
                    let bytes = new Uint8Array(0);

                    try {
                        if (success) {
                            const [ok, data] = GLib.file_get_contents(path);
                            if (ok && data && data.length > 0)
                                bytes = data instanceof Uint8Array ? data : new Uint8Array(data);
                        }
                    } catch (e) {
                        global.logError(e, 'Keysharp: CaptureArea read failed');
                    } finally {
                        try { file.delete(null); } catch (_e) {}
                    }

                    invocation.return_value(new GLib.Variant('(ay)', [bytes]));
                });
        } catch (e) {
            global.logError(e, 'Keysharp: CaptureArea failed');
            try { if (file) file.delete(null); } catch (_e) {}
            invocation.return_value(new GLib.Variant('(ay)', [new Uint8Array(0)]));
        }
    }

    // Captures a single window's contents from its actor's backing buffer (Meta.WindowActor.get_image —
    // present in Muffin 6.x), so an occluded window still captures correctly; the result is cropped to
    // the frame rect so the C# side can derive the device-pixel scale as capture-size / frame-size.
    // A minimized window has no live texture and yields empty (the C# caller falls back to a rect grab).
    CaptureWindowAsync(params, invocation) {
        if (!this._callerIsHelper(invocation)) {
            invocation.return_error_literal(Gio.IOErrorEnum, Gio.IOErrorEnum.PERMISSION_DENIED,
                'Screen capture is only permitted through keysharp-helper.');
            return;
        }

        const [handle] = params;
        invocation.return_value(new GLib.Variant('(ay)', [this._captureWindowBytes(handle)]));
    }

    _captureWindowBytes(handle) {
        let file = null;
        try {
            const win = this._findWindow(handle);
            if (!win)
                return new Uint8Array(0);

            const actor = win.get_compositor_private();
            if (!actor || typeof actor.get_image !== 'function')
                return new Uint8Array(0);

            let surface = actor.get_image(null);   // the whole actor, device pixels
            if (!surface)
                return new Uint8Array(0);

            // Crop to the frame rect (drops the shadow margin when the actor has one). All geometry is
            // logical; the buffer is logical * ui_scale device pixels (Muffin uses integer UI scaling).
            const scale = global.ui_scale || 1;
            const frame = win.get_frame_rect();
            const [ax, ay] = actor.get_position();
            const cropX = Math.max(0, Math.round((frame.x - ax) * scale));
            const cropY = Math.max(0, Math.round((frame.y - ay) * scale));
            const cropW = Math.round(frame.width * scale);
            const cropH = Math.round(frame.height * scale);

            if ((cropX !== 0 || cropY !== 0 || cropW !== surface.getWidth() || cropH !== surface.getHeight())
                && cropW > 0 && cropH > 0) {
                const cropped = new Cairo.ImageSurface(Cairo.Format.ARGB32, cropW, cropH);
                const cr = new Cairo.Context(cropped);
                cr.setSourceSurface(surface, -cropX, -cropY);
                cr.paint();
                cr.$dispose();
                surface = cropped;
            }

            const tmp = Gio.File.new_tmp('keysharp-cinnamon-winshot-XXXXXX.png');
            file = tmp[0];
            tmp[1].close(null);
            const path = file.get_path();

            surface.flush();
            surface.writeToPNG(path);

            const [ok, bytes] = GLib.file_get_contents(path);
            if (!ok || !bytes || bytes.length === 0)
                return new Uint8Array(0);

            return bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
        } catch (e) {
            global.logError(e, 'Keysharp: CaptureWindow failed');
            return new Uint8Array(0);
        } finally {
            if (file !== null) {
                try { file.delete(null); } catch (_e) {}
            }
        }
    }

    // Caller gate for the capture method: true only when the D-Bus caller is the installed, root-owned,
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

    FocusWindow(handle) {
        const win = this._findWindow(handle);
        if (!win)
            return false;

        if (win.minimized)
            win.unminimize();

        try {
            Main.activateWindow(win);
            return true;
        } catch (_e) {
            try {
                win.activate(global.get_current_time());
                return true;
            } catch (_e2) {
                return false;
            }
        }
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
        if (!win)
            return false;

        // Moving a window that has no monitor yet recurses to death inside
        // move_resize_internal -> update_monitor -> wayland_update_main_monitor. Reachable here because a
        // reserved window is correlated as soon as it has geometry, which can precede its monitor.
        if (win.get_monitor() < 0)
            return false;

        // A maximized (or tiled) window ignores move/resize until it is restored — mirror how dragging its
        // titlebar first unmaximizes it. Unmaximize BEFORE reading the frame rect so an unchanged-size move
        // keeps the restored size, not the maximized one.
        if (win.maximized_horizontally || win.maximized_vertically)
            win.unmaximize(Meta.MaximizeFlags.BOTH);

        const frame = win.get_frame_rect();
        const nx = (x !== INT32_MIN) ? x : frame.x;
        const ny = (y !== INT32_MIN) ? y : frame.y;
        const nw = (width > 0) ? width : frame.width;
        const nh = (height > 0) ? height : frame.height;

        try {
            // user_op = true marks this a user action, so Muffin applies the same "keep-minimal
            // on screen" constraint as an interactive titlebar drag (allowing partially/fully
            // off-screen placement) rather than the strict fully-on-screen clamp it forces on app
            // ConfigureRequests. It does not steal focus. This is why moving through the extension
            // can reach off-screen where a raw XMoveWindow / _NET_MOVERESIZE_WINDOW cannot.
            win.move_resize_frame(true, nx, ny, nw, nh);
            return true;
        } catch (_e) {
            return false;
        }
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
                pid: pid,
                cookie: cookie,
                x: x,
                y: y,
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
            // Muffin answers the client pid only via get_client_pid() (get_pid() is -1 for Wayland
            // clients); modern Mutter removed get_client_pid() and made get_pid() answer it instead.
            const pid = win.get_pid() > 0 ? win.get_pid() : (win.get_client_pid ? win.get_client_pid() : -1);

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
    // (XQueryTree / _NET_CLIENT_LIST), not the Muffin stable_sequence, so route the move through
    // the compositor this way to reach off-screen placement a raw XMoveWindow can't (Muffin clamps
    // that on screen).
    MoveResizeWindowByXid(xid, x, y, width, height) {
        return this._moveResizeWin(this._findWindowByXid(xid), x, y, width, height);
    }

    SetWindowState(handle, state) {
        const win = this._findWindow(handle);
        if (!win)
            return false;

        try {
            if (state === STATE_MINIMIZED) {
                win.minimize();
            } else if (state === STATE_MAXIMIZED) {
                if (win.minimized)
                    win.unminimize();
                win.maximize(Meta.MaximizeFlags.BOTH);
            } else {
                if (win.minimized)
                    win.unminimize();
                win.unmaximize(Meta.MaximizeFlags.BOTH);
            }
            return true;
        } catch (_e) {
            return false;
        }
    }

    SetWindowAbove(handle, above) {
        const win = this._findWindow(handle);
        if (!win)
            return false;

        try {
            if (above) {
                if (!win.is_above())
                    win.make_above();
            } else if (win.is_above()) {
                win.unmake_above();
            }
            return true;
        } catch (_e) {
            return false;
        }
    }

    SetWindowDecorated(handle, decorated) {
        const win = this._findWindow(handle);
        if (!win)
            return false;

        try {
            win.decorated = decorated;
            return true;
        } catch (_e) {
            return false;
        }
    }

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

    CloseWindow(handle) {
        const win = this._findWindow(handle);
        if (!win)
            return false;

        try {
            win.delete(global.get_current_time());
            return true;
        } catch (_e) {
            return false;
        }
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

        try {
            win.delete(global.get_current_time());
            return true;
        } catch (_e) {
            return false;
        }
    }

    SendMouseMoveAbsolute(x, y) {
        if (!this._vPointer)
            return false;

        try {
            this._vPointer.notify_absolute_motion(GLib.get_monotonic_time(), x, y);
            return true;
        } catch (_e) {
            return false;
        }
    }

    SendMouseMoveRelative(dx, dy) {
        if (!this._vPointer)
            return false;

        try {
            const point = global.get_pointer();
            this._vPointer.notify_absolute_motion(
                GLib.get_monotonic_time(),
                point[0] + dx,
                point[1] + dy);
            return true;
        } catch (_e) {
            return false;
        }
    }

    SendMouseButton(button, pressed) {
        if (!this._vPointer)
            return false;

        try {
            this._vPointer.notify_button(
                GLib.get_monotonic_time(),
                button,
                pressed ? Clutter.ButtonState.PRESSED : Clutter.ButtonState.RELEASED);
            return true;
        } catch (_e) {
            return false;
        }
    }

    SendMouseScroll(delta, vertical) {
        if (!this._vPointer)
            return false;

        try {
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
        } catch (_e) {
            return false;
        }
    }

    RegisterHighlightOwner(ownerKey, busName) {
        const owner = this._parseOverlayOwner(ownerKey);
        const bus = String(busName || '');

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

        return true;
    }

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

            const t = Math.max(1, Math.round(Number(thickness)));
            const css = `background-color: #${color};`;
            const rects = [
                [x,             y,              width,             t],
                [x,             y + height - t, width,             t],
                [x,             y + t,          t,                 Math.max(0, height - 2 * t)],
                [x + width - t, y + t,          t,                 Math.max(0, height - 2 * t)],
            ];
            const edges = [];

            for (const [ex, ey, ew, eh] of rects) {
                if (ew < 1 || eh < 1)
                    continue;

                const actor = new St.Widget({ reactive: false, style: css });
                actor.set_position(ex, ey);
                actor.set_size(ew, eh);
                Main.layoutManager.addChrome(actor, {
                    visibleInFullscreen: true,
                    affectsStruts: false,
                    affectsInputRegion: false
                });
                edges.push(actor);
            }

            this._highlights.set(key, {
                edges: edges,
                ownerKey: owner.key,
                ownerPid: owner.pid,
                ownerStartTime: owner.startTime,
                busName: bus
            });
            this._ensureOverlayCleanupTimer();
            return true;
        } catch (e) {
            global.logError(e, 'Keysharp: ShowHighlight failed');
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
            const bus = String(busName || '');

            this._cancelOverlayReconnectTimer(owner.key);

            if (!pngData || pngData.length === 0 || width < 1 || height < 1) {
                this._removeImageOverlayByKey(key);

                if (!this._hasAnyOverlays())
                    this._stopOverlayCleanupTimer();

                return true;
            }

            const entry = this._imageOverlays.get(key);

            if (entry && entry.actor) {
                try {
                    this._updateImageActor(entry.actor, pngData, x, y, width, height);
                    entry.ownerPid = owner.pid;
                    entry.ownerStartTime = owner.startTime;
                    entry.busName = bus;
                    this._ensureOverlayCleanupTimer();
                    return true;
                } catch (_e) {
                    this._removeImageOverlayByKey(key);
                }
            }

            const actor = this._createImageActor(pngData, x, y, width, height);
            Main.layoutManager.addChrome(actor, {
                visibleInFullscreen: true,
                affectsStruts: false,
                affectsInputRegion: false
            });

            this._imageOverlays.set(key, {
                actor: actor,
                ownerKey: owner.key,
                ownerPid: owner.pid,
                ownerStartTime: owner.startTime,
                busName: bus
            });
            this._ensureOverlayCleanupTimer();
            return true;
        } catch (e) {
            global.logError(e, 'Keysharp: ShowImageOverlay failed');
            return false;
        }
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
            global.logError(e, 'Keysharp: MoveImageOverlay failed');
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

    _overlayKey(id, ownerKey) {
        return `${ownerKey}:${id}`;
    }

    _parseOverlayOwner(ownerKey) {
        const key = String(ownerKey || '');
        const parts = key.split(':');
        const pid = parts.length > 0 ? Number(parts[0]) : 0;
        const startTime = parts.length > 1 ? parts[1] : '';
        return {
            key: key,
            pid: Number.isFinite(pid) ? Math.round(pid) : 0,
            startTime: startTime
        };
    }

    _removeHighlightByKey(key) {
        const entry = this._highlights.get(key);
        if (!entry)
            return;

        for (const actor of entry.edges) {
            try { Main.layoutManager.removeChrome(actor); } catch (_e) {}
            try { actor.destroy(); } catch (_e) {}
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

    _removeOwnerOverlays(ownerKey) {
        for (const [key, entry] of Array.from(this._highlights.entries())) {
            if (entry.ownerKey === ownerKey)
                this._removeHighlightByKey(key);
        }

        for (const [key, entry] of Array.from(this._imageOverlays.entries())) {
            if (entry.ownerKey === ownerKey)
                this._removeImageOverlayByKey(key);
        }

        this._cancelOverlayReconnectTimer(ownerKey);

        if (!this._hasAnyOverlays())
            this._stopOverlayCleanupTimer();
    }

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

        for (const ownerKey of owners.keys())
            this._removeOwnerOverlays(ownerKey);
    }

    _handleNameOwnerChanged(parameters) {
        let name, oldOwner, newOwner;

        try {
            [name, oldOwner, newOwner] = parameters.deep_unpack();
        } catch (_e) {
            return;
        }

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

        for (const [ownerKey, entry] of owners.entries())
            this._handleOverlayConnectionLost(ownerKey, name, entry);
    }

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

    _firstOverlayForOwner(ownerKey) {
        for (const entry of this._highlights.values())
            if (entry.ownerKey === ownerKey)
                return entry;

        for (const entry of this._imageOverlays.values())
            if (entry.ownerKey === ownerKey)
                return entry;

        return null;
    }

    _hasOverlaysForOwner(ownerKey) {
        return this._firstOverlayForOwner(ownerKey) !== null;
    }

    _hasAnyOverlays() {
        return this._highlights.size !== 0 || this._imageOverlays.size !== 0;
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

        return false;
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

    _updateImageActor(actor, pngData, x, y, width, height) {
        const frame = this._decodeImageFrame(pngData);
        let content = actor.get_content();

        // set_data replaces the Cogl texture; set_area keeps the compositor's existing texture allocation.
        if (content && actor._keysharpImageWidth === frame.width &&
            actor._keysharpImageHeight === frame.height && typeof content.set_area === 'function') {
            try {
                const area = new CairoGI.RectangleInt();
                area.x = 0;
                area.y = 0;
                area.width = frame.width;
                area.height = frame.height;

                if (!content.set_area(frame.pixels, frame.format, area, frame.rowStride))
                    content = null;
            } catch (_e) {
                content = null;
            }
        } else {
            content = null;
        }

        if (!content) {
            content = new Clutter.Image();
            content.set_data(frame.pixels, frame.format, frame.width, frame.height, frame.rowStride);
            actor.set_content(content);
            actor._keysharpImageWidth = frame.width;
            actor._keysharpImageHeight = frame.height;
        }

        actor.set_position(x, y);
        actor.set_size(width, height);
    }

    _createImageActor(pngData, x, y, width, height) {
        const actor = new Clutter.Actor({ reactive: false });
        this._updateImageActor(actor, pngData, x, y, width, height);
        return actor;
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

    _overlayOwnerAlive(pid, startTime) {
        if (!pid || pid <= 0)
            return true;

        const statPath = `/proc/${pid}/stat`;

        if (!GLib.file_test(statPath, GLib.FileTest.EXISTS))
            return false;

        if (!startTime)
            return true;

        try {
            const [, bytes] = GLib.file_get_contents(statPath);
            const stat = ByteArray.toString(bytes);
            const end = stat.lastIndexOf(')');

            if (end < 0 || end + 2 >= stat.length)
                return true;

            const fields = stat.substring(end + 2).trim().split(/\s+/);
            return fields.length > 19 && fields[19] === startTime;
        } catch (_e) {
            return true;
        }
    }

    _emitActiveWindowChanged() {
        if (!this._dbusImpl)
            return;

        try {
            this._dbusImpl.emit_signal(
                'ActiveWindowChanged',
                new GLib.Variant('(s)', [this.GetActiveWindow()]));
        } catch (_e) {
        }
    }

    _emitWindowEvent(type, win) {
        if (!win)
            return;

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
        if (!this._dbusImpl)
            return;

        try {
            this._dbusImpl.emit_signal('WindowEvent', new GLib.Variant('(ss)', [type, json]));
        } catch (_e) {
        }
    }

    _hookWindow(win) {
        if (!win)
            return;

        const seq = win.get_stable_sequence();
        if (this._windowSignalIds.has(seq))
            return;

        const ids = [];
        ids.push(win.connect('notify::title', () => this._emitWindowEvent('title', win)));
        ids.push(win.connect('notify::minimized', () => this._emitWindowEvent(win.minimized ? 'minimize' : 'restore', win)));
        ids.push(win.connect('position-changed', () => this._emitWindowEvent('move', win)));
        ids.push(win.connect('size-changed', () => this._emitWindowEvent('move', win)));
        ids.push(win.connect('unmanaged', () => {
            this._emitWindowEventRaw('close', JSON.stringify({id: String(seq)}));
            this._unhookWindow(win);
        }));

        this._windowSignalIds.set(seq, ids);
    }

    _unhookWindow(win) {
        if (!win)
            return;

        const seq = win.get_stable_sequence();
        const ids = this._windowSignalIds.get(seq);
        if (!ids)
            return;

        for (const id of ids) {
            try { win.disconnect(id); } catch (_e) {}
        }

        this._windowSignalIds.delete(seq);
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
        let buffer = null;
        try { const b = win.get_buffer_rect(); if (b) buffer = {x: b.x, y: b.y, width: b.width, height: b.height}; } catch (e) { }
        let workspace = -1;
        let monitor = -1;
        let opacity = 255;

        try {
            const ws = win.get_workspace();
            if (ws)
                workspace = ws.index();
        } catch (_e) {
        }

        try {
            if (typeof win.get_monitor === 'function')
                monitor = win.get_monitor();
        } catch (_e) {
        }

        try {
            const actor = (typeof win.get_compositor_private === 'function')
                ? win.get_compositor_private()
                : null;
            if (actor)
                opacity = (typeof actor.get_opacity === 'function') ? actor.get_opacity() : actor.opacity;
        } catch (_e) {
            opacity = 255;
        }

        // Windows on other workspaces still "exist" (enumeration/matching), but must not win
        // at-point hit-tests — global.get_window_actors() spans ALL workspaces.
        let onCurrentWorkspace = true;
        try {
            const active = global.workspace_manager
                ? global.workspace_manager.get_active_workspace()
                : global.screen.get_active_workspace();
            onCurrentWorkspace = win.is_on_all_workspaces()
                || (win.get_workspace() !== null && win.get_workspace() === active);
        } catch (_e) {
        }

        return {
            id: String(win.get_stable_sequence()),
            title: win.get_title() || '',
            appId: win.get_wm_class() || win.get_wm_class_instance() || '',
            // get_pid() is -1 for Wayland clients on Muffin; get_client_pid() is the real one there.
            pid: win.get_pid() > 0 ? win.get_pid() : (win.get_client_pid ? win.get_client_pid() : -1),
            workspace: workspace,
            monitor: monitor,
            onCurrentWorkspace: onCurrentWorkspace,
            frame: {x: frame.x, y: frame.y, width: frame.width, height: frame.height},
            client: {x: frame.x, y: frame.y, width: frame.width, height: frame.height},
            // The surface as the client drew it, shadow included. A Wayland client is never told where its
            // surface is, so this is the only origin its own coordinates can be resolved against. Omitted on a
            // compositor that cannot answer, which leaves the consumer uncorrected rather than wrong.
            buffer: buffer,
            active: !!win.appears_focused,
            minimized: !!win.minimized,
            maximized: !!(win.maximized_horizontally && win.maximized_vertically),
            visible: !win.minimized,
            alwaysOnTop: (typeof win.is_above === 'function') ? !!win.is_above() : !!win.above,
            decorated: win.decorated !== false,
            transparency: Math.max(0, Math.min(255, Math.round(Number(opacity))))
        };
    }

    _findWindow(handle) {
        const seq = Number(handle);

        for (const actor of global.get_window_actors()) {
            const win = actor.get_meta_window();
            if (win && win.get_stable_sequence() === seq)
                return win;
        }

        return null;
    }

    _findWindowByXid(xid) {
        const target = Number(xid);

        for (const actor of global.get_window_actors()) {
            const win = actor.get_meta_window();
            if (win && typeof win.get_xwindow === 'function' && Number(win.get_xwindow()) === target)
                return win;
        }

        return null;
    }
}
