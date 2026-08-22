# Design: Microsoft Store packaging (MSIX)

**Status: implemented and locally verified 2026-08-11 — not yet sideload-installed or submitted.**
`dist/Keysharp-win-x64.msix` (54.9 MB) builds end-to-end and its contents check out (§5); the
Dash compiles clean, the default-script mechanism it rides on is covered by a live run, and the
package is signed with the local test certificate and passes `signtool verify`. What has *not*
happened is an install of the package and the Store submission itself. (The curated test suite,
listed here as pending, was run green on 2026-08-17 — see §5.) See §5/§7.

**Revised 2026-08-17 (decisions by Descolada).** Three things changed shape, and the first two moved
work *out* of this design:

* **There is one Windows packager again.** `package-msix.ps1` and the `packaging-common.ps1`
  helper it forced are gone; `package-windows.ps1` builds the `.zip` and `.msi` as before and the
  `.msix` only with **`-Msix`**, since the Store package is an occasional errand rather than part
  of a release. See D7.
* **The Dash and the demos ship in *every* edition**, not just MSIX — MSI, zip, deb/tarball and
  the macOS bundle all get them. That made them payload of the *install* rather than of a single
  packager: one project, which each packager runs after publishing, produces `Keysharp.cks` and
  `Demos\*.cks`. The Dash itself was ported off Windows-only APIs to pay for it. See D2 and D6.
* **Every platform offers one `Keysharp` entry, never a separate "Dash" one** — the shape macOS
  already had. The MSI gained a Start-menu shortcut and the Linux `.desktop` became visible; both
  just launch the host with no arguments. See D8.

**Revised 2026-08-21 (decision by Descolada): product and install payload were separated.** The Dash,
its template and the demos are not part of Keysharp, so `Keysharp.csproj` no longer carries them and
`PrecompileBundledScripts` is gone. They belong to
[Keysharp.Install/payload/](../Keysharp.Install/payload/), whose `Keysharp.Payload.proj` every
packager runs against the freshly published tree. `Keysharp\Scripts\` keeps only what the host itself
resolves by path — WindowSpy, AtSpi and Ax — and nothing is precompiled by `dotnet publish` any more.

Precedent: the AutoHotkey v2 Store Edition package
(`C:\Users\minip\source\repos\AutoHotkey-v2-Store-Edition`), which shipped and passed
certification. Its manifest is the template for every Store-facing decision here; its structure
is deliberately *not* copied — see D3.

---

## 1. Shape of the package

```
Keysharp_<version>_<arch>/            (WindowsApps install root, read-only)
├── AppxManifest.xml                  from Keysharp.Install/windows/msix/AppxManifest.xml (tokens filled)
├── resources.pri                     makepri output (serves the Assets\* references)
├── Keysharp.exe, Keyview.exe         SELF-CONTAINED publish — full .NET 10 runtime included
├── Keysharp.cks                      the precompiled Dash (see D2)
├── <the merged Keysharp+Keyview publish: runtime, Keysharp.Core.dll, Roslyn, deps.json, ...>
├── runtimes\win-<arch>\native\       Scintilla.dll, Lexilla.dll (same layout as MSI/zip)
├── Scripts\                          WindowSpy.ks + WindowSpy.cks, Dash.ks (the Dash's source),
│                                     Template.ks (the ShellNew / "New script" seed)
├── Lib\                              OCR.ks
├── Demos\                            Shell.ks + 4 paired demo .ks/.cks files (see D6)
└── Assets\                           generated tile/file icons
```

Everything above the `Assets\` line now comes straight from the publish tree, so the MSI, zip,
deb/tarball and macOS bundle contain the same files (minus `Assets\`, which is MSIX-only). Only
the manifest, the generated tile art and `resources.pri` are specific to this package.

Two `Application` entries: **Keysharp** (the Dash tile, `keysharp.exe` execution alias, `.ks`/`.ahk`
run + Compile verbs, `.cks` open, Explorer "New > Keysharp script" via ShellNew) and **Keyview**
(editor tile and `keyview.exe` alias only — see D5 for why it has no association). Both declare
`uap10:SupportsMultipleInstances="true"` — a script host must never single-instance. The only
capability is `runFullTrust`; `uiAccess` is not available to Store apps, so packaged scripts
cannot interact with elevated windows (same limitation the AutoHotkey Store Edition shipped with,
worth a line in the Store listing).

## 2. Decisions

**D1 — self-contained publish, no trimming, R2R kept, single-file stays off.** The Store cannot
assume a machine-wide .NET and modern .NET has no Store framework package, so the runtime ships in
the package. Trimming is off the table: the script-facing API surface is reached by reflection and
scripts may call arbitrary .NET. `PublishSingleFile` must stay off — `Assembly.Location` returns
`""` under it and `CompilerHelper.CreateCompilation` resolves Roslyn metadata references from
`typeof(object).Assembly.Location`, which under a self-contained publish points at the app
directory (readable, works). The publish goes to **separate trees** (`dist/publish-msix`,
`dist/staging-msix`): mixing a self-contained publish into `dist/publish` would silently change
what the MSI/zip harvest ships.

*Open risk:* every existing test run is framework-dependent. `#CSharp`, script compilation and
`--compile exe` all resolve references from the app directory for the first time in this layout;
they should be smoke-tested against the packaged tree (§7).

**D2 — the Dash is the default script, shipped precompiled as `Keysharp.cks`.** Keysharp already
runs `<exe-name>.ahk/.ks` from the current directory when started with no arguments;
`Runner.Parse` was extended to probe **the executable's directory** as well and to accept
**`.cks`** (a discovered `.cks` takes the load-assembly path, exactly as an explicit
`keysharp foo.cks` argument does). Probe order: current directory before exe directory (preserves
the old precedence), and within a directory `.ahk` → `.ks` → `.cks`, so a stale compile never
shadows edited source. The stale "documents folder" error text was corrected at the same time.

The package ships **only the `.cks`** at the root (decision by Descolada): the install directory
is read-only so an on-first-launch compile could never be cached anyway, and the tile launch skips
Roslyn entirely. Because the tile is just "run the exe", the manifest needs no `uap10:Parameters`
and no hard-coded paths — which is the main reason the AutoHotkey package needed a VFS tree and
this one does not.

*Revised 2026-08-17 — the Dash ships in every edition, built by the publish.* Its source moved from
the MSIX asset folder to [Keysharp.Install/payload/Dash.ks](../Keysharp.Install/payload/Dash.ks), and
the payload project compiles it as part of the batch, then moves the result to `Keysharp.cks` **at the
app root**. Three consequences worth naming:

* **Source name ≠ output name, deliberately.** The probe prefers `.ks` over `.cks` within a
  directory, so a root `Keysharp.ks` would silently make *every* launch pay for a Roslyn compile.
  Keeping the source as `Scripts\Dash.ks` lets it ship for inspection with no way to shadow the
  assembly, and removes the "delete the .ks after compiling" step the MSIX packager used to need.
* **The cross-architecture fallback is now the only case that writes a root `.ks`.** When the
  freshly published host cannot run on the build machine, the target copies `Scripts\Dash.ks` to
  `Keysharp.ks` — never both, guarded on the `.cks` being absent. The packagers assert one of the
  two is present, because a payload with neither turns a bare launch into an error dialog.
* **The Dash had to become cross-platform.** It was Windows-first (backslash paths, `Keyview.exe`,
  `explorer.exe`/`notepad.exe`, Segoe UI, and `user32!WindowFromPoint` + `GetAncestor`). Two of
  those had portable answers already in the codebase and are now used unconditionally: the
  cross-platform `WinFromPoint` builtin, which already returns the top-level window (so the
  `GetAncestor(GA_ROOT)` step was redundant even on Windows), and `"HBITMAP:"` **without** the
  star, which hands the handle's ownership to the loader — the starred form obliged the script to
  `DllCall("DeleteObject")` the previous handle after every redraw, which has no portable spelling
  since the handle is a Pixbuf/NSImage off Windows. The rest are `#if` blocks ordered
  **OSX → LINUX → #else**, so a Windows host can syntax-check the other two branches with
  `--define:OSX` / `--define:LINUX`; the platform symbol is otherwise baked into the parser when
  *it* is built, leaving only the `#else` reachable. Keep new conditionals in that order.

The Dash's UI is **not native controls** — a first version built on ListView rows was rejected
("looks like an old win32 app", Descolada), and the rebuild leans on what makes Keysharp unique:
the whole window is a single `Image`-rendered surface (the same drawing layer the demos'
`Shell.ks` cards use) inside a borderless dark Gui, with Win11 rounded corners via
`DwmSetWindowAttribute`. A `BuildModel()` pass lays out every card/chip/link once in authored
96-DPI units; `Render()` rasterizes that model at the monitor's scale into a full-window
Picture (`Image.ToBitmap()` → `"HBITMAP:*"`), and a **40 ms mouse poll** (`MouseGetPos` +
`WinGetClientPos` + `GetKeyState` edge detection, gated on `WindowFromPoint`→`GetAncestor` so a
covering window can't be clicked through) maps hover, clicks and dragging back through the
*same* model, so drawing and hit-testing share one source of truth. Window messages were tried
first and rejected: the classic `WM_NCLBUTTONDOWN`/HTCAPTION drag dies under the WinForms
Picture control's mouse capture, and `OnMessage` routing over a child control could not be
pinned down reliably — the poll needs no plumbing and its drag/click/hover behavior is verified
end-to-end with injected mouse input.
Layout: logo + wordmark header (drawn ✕ closes; any empty area drags the window), two accent
primary cards (New script / Run a script), a 2×2 tools grid (Window Spy /
Keyview / Documentation / GitHub — absent targets simply don't appear), and demo cards each with
a `</>` view-source chip and an accent **Run** pill — explicit affordances instead of a context
menu, replacing the native ContextMenu event that had proven unreliable. Glyphs are Segoe UI
Emoji characters, which GDI+ renders as monochrome outlines — used deliberately as a consistent
icon set, so no image assets ship beyond the existing `Keysharp.png`.

Runtime lessons baked into it, each found by an actual failure: `#import KS { A_KsVersion }` is
required because Keysharp-extension accessors are not auto-visible like AHK builtins (reading
one un-imported is an *unset variable* at runtime while `--validate` passes); AHK v2's `//`
**throws on float operands** and `MeasureText` returns floats, so centering math uses
`Round((w - tw) / 2)`; and the interim ListView version surfaced a real Core compat bug —
ListView `Icon N` item/column options were applied 0-based where AHK is 1-based — fixed in
`ListViewHelper.cs` to match the TreeView code (the fix outlives the redesign).

**D3 — no VFS tree; write virtualization stays ON; `installedLocationVirtualization` declared.**
Three distinct things that all get called "VFS":

1. *Package `VFS\` folder tree* — *not used.* The AutoHotkey package has one only because the
   MSIX Packaging Tool captured an installed program with hard-coded Program Files paths.
   This package is built natively; everything is exe-relative; files sit at the package root.
2. *Registry + AppData write virtualization* — *left at its default (on)*, matching the shipped
   AutoHotkey Store Edition. Consequences for scripts are documented in §5.
3. *`installedLocationVirtualization`* (uap10 extension) — *declared*, with the same
   `ModifiedItems/DeletedItems="reset"`, `AddedItems="keep"` update policy the AutoHotkey package
   used. It lets a user edit a demo in Keyview and save inside a read-only install directory — the
   writes land in a per-user redirection area merged over the package files. On update, package
   files reset to the new build's copies; user-added files survive.

   *Revised 2026-08-21:* nothing Keysharp ships **depends** on this any more. The demos used to
   `IniWrite` a `Demos\Settings.ini` beside themselves, which only worked in the MSIX — under the
   MSI (`Program Files`), the deb (`/usr/local/lib`) and the macOS bundle that write fails, and it
   runs from `OnExit`, so quitting a demo whose card state had changed raised an error dialog every
   time. `Shell.ks` now writes `A_AppData\Keysharp\demos.ini`, the same folder Keyview autosaves its
   scratch document to, and the flush is wrapped so a failed write can never veto an exit.

**D4 — version mapping: Store reserves the fourth field.** An MSIX identity version must be
`Major.Minor.Build.0` for Store submission. `Convert-ToMsixVersion` appends the missing fields to
the two- and three-part versions Keysharp uses from 0.0.1 on, and folds a legacy four-part
`0.0.0.N` exactly like the MSI ProductVersion mapping (`build = patch*1000 + revision`, so
`0.0.0.16` → `0.0.16.0`). The planned first Store release is **0.0.1** → `0.0.1.0`.

*Corrected 2026-08-21:* the fold was originally skipped when the fourth field was already `0`, on
the reasoning that such a version needed no folding. That inverted the ordering it existed to
preserve — `0.0.1.0` came out as `0.0.1.0`, **below** `0.0.0.16`'s `0.0.16.0`, and `0.0.3.0` below
`0.0.2.5`'s `0.0.2005.0`. The fold is now unconditional. The scheme change itself remains a step
down (`0.0.16.0` → `0.0.1.0`), which costs nothing because no four-part version was ever published
as an MSIX — but a machine that **sideloaded** one must uninstall it first, since Windows will not
replace a package with a lower version.

Three-part versions also had to be taught to the MSI side, which until now required exactly four
numeric parts: `Assert-PackagableVersion` and `Keysharp.Installer.wixproj` both accept 2–4 parts
and fold identically, so `0.0.1` produces MSI ProductVersion `0.0.1` (verified) instead of failing
evaluation with an empty `Package/@Version`.

**D5 — manifest surface details, each with a reason.**

| Choice | Why |
|---|---|
| Execution alias `Keysharp.exe` (and `Keyview.exe`) | The alias **must match the executable name** or `Run`/`RunWait` through the alias fails — a hard-won lesson recorded in the AutoHotkey Store manifest, which had to ship renamed exe copies to satisfy it. Keysharp's names already match. |
| `uap10:Subsystem="console"` on the keysharp alias | A console-declared alias makes an invoking shell wait and receive the exit code, which `--validate`/`--compile`/`--errorstdout` callers expect. The Keyview alias stays a GUI launch. |
| No "Edit" verb on `.ks`, and no Keyview association at all | Verbs can only launch the association's own executable and Keysharp has no `--edit` switch. The fallback of giving Keyview its own `.ks`/`.ahk` association is **impossible**: makeappx hard-fails the package because *each file type may be declared only once per package* (error 80080204, found the hard way). So the MSI's "Open with > Keyview" has no MSIX equivalent today; the route back is an `edit` verb on the Keysharp association relaying through a future `Keysharp.exe` edit switch. |
| `Compile` verb → `--compile "%1"` | Mirrors what `--install` registers under `SystemFileAssociations` for the MSI (which MSIX manifests cannot express). |
| ShellNew on `.ks` seeded from `Scripts\Template.ks` | Explorer's New menu — same trick as the AutoHotkey package's "Minimal for v2.ahk". *Revised 2026-08-21:* the template moved out of the packager's assets into the shipped app tree, so one file now serves all four consumers — this manifest, the MSI's `.ks\ShellNew` key, the CLI `--install` switch, and the Dash's "New script" (which read from its own inline copy before). The MSI and `--install` had no ShellNew registration at all until then, so Explorer's New menu was an MSIX-only feature. |
| `MinVersion 10.0.19041` | Floor for the uap10 features used (installedLocationVirtualization, SupportsMultipleInstances, alias Subsystem). |
| `PackageIntegrity Enforcement="on"` | Parity with the shipped AutoHotkey release manifest. |

**D6 — demos: 4 of the 5 ship as paired source/compiled files, plus `Shell.ks`.** `WindowTiler`,
`WindowGrab`, `ClipboardHistory`, and `InputHUD` each ship as both `.ks` (for the Dash's source
button) and `.cks` (for the run button). Their shared support layer remains `Shell.ks`; its code is
folded into each compiled demo by `#include`, so it is not a standalone `.cks` entry point. Settings
persistence goes to `A_AppData\Keysharp\demos.ini` (see D3(3)), so nothing writes into the install
tree. **`OCRSnip` is excluded**: it `#include`s
`../Keysharp/Scripts/OCR.ks` by repo-relative path (dead inside a package) and depends on an OCR
engine the package does not carry. No settings file is shipped — it is user state the demos
create on demand, and shipping one would seed every user with this machine's preferences. The Dash
discovers demos by scanning `Demos\*.ks` (excluding `Shell.ks`), then launches the matching `.cks`
when present while retaining the `.ks` path for viewing.

*Revised 2026-08-17 — the demos ship in every edition too* (decision by Descolada), for the same
reason and by the same route as the Dash: the payload project copies the five `.ks` files into
`Demos\` and compiles every one of them except `Shell.ks` in place.
Adding a demo is one entry in its `_Demo` list — the precompile picks it up from a
`Demos\*.ks` glob, and all five packagers ship it without changes. The exclusions above are
unchanged and are still enforced by *not listing* OCRSnip.

**D7 — pipeline: one Windows packager, MSIX behind `-Msix`** (revised 2026-08-17, decision by
Descolada). The first implementation was a second script, `package-msix.ps1`, plus a
`packaging-common.ps1` holding the six staging functions the two had in common. Both are deleted:
`package-windows.ps1` is the Windows packager, the shared helpers are back inside it, and the
`.msix` is an opt-in mode. A normal run produces exactly the `.zip` and the `.msi`, which is what
the release workflow wants and what it already invokes.

*Revised 2026-08-21 — `-Msix` is **exclusive**, not additive* (decision by Descolada). It produces
the `.msix` and nothing else. The zip and MSI are built from a framework-dependent publish and the
MSIX from a self-contained one, so an additive run meant two full publishes plus a zip nobody
asked for; and a Store submission has no use for either of the other two artifacts. Getting all
three means running the script twice, which is also the only honest way to spend that time. Because
`-Msix` never builds the MSI, `-SkipMsi` alongside it now throws rather than reading as a no-op,
as does `-SkipSign` together with a certificate argument.

The MSIX is a separate mode rather than the default for a concrete reason, not tidiness: it needs a
**self-contained publish** (the Store cannot assume a machine-wide .NET) into deliberately separate
`dist/publish-msix` and `dist/staging-msix` trees, plus the Windows SDK's `makeappx`/`makepri`/
`signtool` — several minutes and a toolchain, for an artifact built a few times a year. The
MSIX-only arguments (`-IdentityName`, `-Publisher`, `-PublisherDisplayName`, `-SkipSign`,
`-SignCert*`, `-TimestampUrl`) **throw** when passed without `-Msix`, so a run that was meant to
sign a package cannot quietly produce an unsigned zip and MSI instead.

Both packages come off one staging function, `New-StagedAppTree`, which publishes → merges Keyview
and Keysharp (Keysharp last, so it wins collisions) → `Normalize-NativeAssets` →
`Relocate-LibraryScripts` → `Assert-NoLocalPaths` + `Assert-PayloadIsShippable`. What differs after
that is only the packaging step. `Assert-PayloadIsShippable` gained the Dash invariant: a payload
with neither `Keysharp.cks` nor `Keysharp.ks` **throws** (a bare launch would open an error dialog
instead of the launcher), and the `.ks`-only case warns the way the WindowSpy one does.
`package-linux.sh` and `package-macos.sh` each gained the same check as `verify_dash_present` —
they cannot share the PowerShell one, and an assertion that exists in only one of the three
packagers is exactly the kind that stops being true.

With `-Msix`, `New-MsixPackage` then:

1. maps the version and resolves `makeappx`/`makepri` and the signing certificate **first**, so a
   bad version, a missing SDK or an unreadable certificate fails before the slow publish rather
   than after it;
2. publishes Keysharp + Keyview self-contained (`-r <rid> --self-contained true -o …`, same
   Deterministic/CI/PathMap properties as the MSI path) and stages it with `New-StagedAppTree`;
3. generates every visual asset (scale-100…400 for StoreLogo / Square150x150 / Square44x44 /
   FileLogo, plus targetsize 16–256 and `altform-unplated` variants of the 44px icon for
   taskbar/Start) — see the asset note below;
4. token-fills the manifest template (identity, publisher, folded version, architecture; a
   leftover `__TOKEN__` fails the build);
5. `makepri createconfig`+`new` (the pri is what serves `Assets\*` at the right DPI scale),
   `makeappx pack`, and `signtool` with the gitignored local test certificate by default. Pass
   `-SignCertPath`/`-SignCertPasswordPath` for another certificate or `-SkipSign` for an unsigned
   Store artifact.

**The `makepri` config must have its `<packaging>` node removed** (found 2026-08-21). The template
`makepri createconfig` writes contains `<autoResourcePackage qualifier="Scale"/>`, which makes
`makepri new` *split* the index: `resources.pri` keeps only the scale-100 candidates and 125/150/
200/400 go into sibling `resources.scale-NNN.pri` files. Those are **resource packages** — only a
`.msixbundle` loads them. In a single `.msix` they are unreachable payload, and every tile renders
from the scale-100 asset, i.e. soft on any HiDPI display; `makeappx` packs the result without a
word. Confirmed by `makepri dump`: zero `scale-200` candidates in the shipped `resources.pri`
before the fix, four after. The packager now strips the node and additionally **throws** if any
`resources.*.pri` appears beside the index.

**Asset sources.** Sizes that exactly match a frame in `assets/Keysharp.ico` (16/32/48/64/128) are
taken from it — a bicubic squeeze of the 256px PNG down to 16px is visibly mushier than the frame
drawn for that size — and everything else comes from `assets/Keysharp.png`. `Square150x150Logo` is
drawn at 66% of the tile with transparent padding, per Windows' medium-tile guidance; the
edge-to-edge version both looked oversized and forced a 600px draw at scale-400 that a 256px master
cannot supply. Only `Square150x150Logo.scale-400` still upscales (256 → 396px), and the packager
**warns by name** when it does, pointing at `assets/Keysharp.svg` as the fix. Nothing else exceeds
the master.

**Signing.** `signtool` gets `/tr` + `/td SHA256` by default (`-TimestampUrl ''` to skip, which an
offline build needs): without a countersigned timestamp a sideload package stops installing the day
the certificate expires. A failed `signtool` now deletes the `.msix` — an unsigned package sitting
at the expected path is indistinguishable from a finished one. The PFX password is still passed as
`/p`, which is visible in the process command line for the duration; signtool offers no alternative
short of importing to a certificate store and signing by thumbprint, which leaves machine state
behind. `-SignCertPasswordPath` at least keeps it out of shell history.

Steps 3 and 4 of the old list — copy in the Dash, precompile it, delete its `.ks`, then copy and
precompile the demos — are simply **gone**. Both are payload of the publish now (D2, D6), so the
self-contained host compiles them during step 2 exactly as the framework-dependent one does for the
MSI, and the "is this a cross-architecture build?" branching the packager carried in two places
lives in one MSBuild target instead.

**Publisher identity is never stored in the repository** (2026-08-21). The manifest's `Publisher` and
the signing certificate's subject must be byte-identical or Windows refuses to install the package,
so the certificate is the source of truth: `Resolve-SigningCertificate` reads the subject off the
`.pfx` and `Resolve-Publisher` uses it when `-Publisher` is not given. An explicit `-Publisher` that
disagrees with the certificate is rejected before the publish rather than by `signtool` after it. A
Store build passes the exact Partner Center *Product identity* value (a `CN=<GUID>`) together with
`-SkipSign`, since the Store signs; `-Publisher` is required in that case, because there is no
certificate to derive it from. `-IdentityName` and `-PublisherDisplayName` default to `Keysharp`.

*Revised 2026-08-21 — the MSI's Start-menu folder holds only Keysharp and Keyview* (decision by
Descolada). The third entry, **Window Spy**, existed on Windows and nowhere else — no `.desktop`, no
`.app`, no MSIX tile — and it is reached from the Dash like the demos and the documentation, so
removing it is what makes the editions agree rather than what breaks them. Keyview stays: it has a
first-class launcher on every other platform (`keyview.desktop`, `Keyview.app`, its own MSIX tile),
so dropping it from Windows alone would create the divergence. This retires the `WindowSpyScript`
property in the wixproj, which existed only to keep that shortcut from dangling on a
cross-architecture publish.

**The MSI offers to open the Dash from its last page**, ticked by default. `WixShellExec` runs in the
installer's UI process — the user's ordinary unelevated token, where a deferred action would launch
the launcher as SYSTEM — and `Return="ignore"` keeps a failed start from failing the install. The
target is set from the Finish button rather than as a `<Property>`, because a Property value is
authored rather than formatted (WIX1077 flags the literal `[INSTALLFOLDER]`) and because reading it
at click time picks up a folder the user chose in CustomizeDlg. The shared WixUI ExitDialog hides
its checkbox unless the text property is set *and* this is a fresh install, so repair, modify and
uninstall never offer it, and a silent install has no ExitDialog at all.

There is no equivalent on Linux or macOS, deliberately: `dpkg` maintainer scripts and `pkg`
postinstall scripts run as root with no desktop session, and starting an application from them is
both against Debian policy and unreliable. What is consistent is the outcome — one obvious entry per
platform — so `install.sh` now closes by naming it instead.

**D8 — one "Keysharp" entry per platform, never a separate "Dash" one** (decision by Descolada).
macOS already had the shape: `Keysharp.app` and `Keyview.app`, and opening the former with no
document runs the Dash. Every edition now matches — the MSIX tile is `Keysharp`, the MSI gained a
Start-menu `Keysharp` shortcut targeting `Keysharp.exe` with **no arguments**, and the Linux
`keysharp.desktop` dropped `NoDisplay=true` (it was a MIME handler only) and gained
`Categories=Utility;Development;` so it appears in application menus. None of them names a script
path: they all rely on the same no-argument default-script probe, so the launcher can be renamed or
recompiled without touching a single package. Only the Dash's *window title* still reads "Keysharp
Dash", which distinguishes it from the windows of scripts the user runs.

## 3. What a Store install behaves like (deltas vs MSI/zip)

These follow from MSIX itself, not from anything Keysharp does; they belong in user-facing docs
when the package ships.

* **`RegWrite` to HKCU/HKLM is virtualized**: visible to the writing script (and its children),
  invisible to the rest of the machine, deleted on uninstall. The sharpest edge: a script adding
  itself to `HKCU\...\Run` *appears* to succeed but will never launch at logon, because Explorer
  reads the real hive. Mitigations (either/or, follow-on): declare a `startupTask` for a
  Dash-managed autostart story, or request the `unvirtualizedResources` restricted capability.
* **Child processes launched by scripts inherit package identity**, so *their* registry/AppData
  writes are virtualized too.
* **The real install path changes on every update**
  (`WindowsApps\<identity>_<version>_<hash>`), so anything a user persists from `A_AhkPath`
  (scheduled tasks, shortcuts) goes stale; the stable path is the alias
  `%LOCALAPPDATA%\Microsoft\WindowsApps\Keysharp.exe`. Worth considering `A_AhkPath` returning
  the alias when packaged (follow-on).
* **Updates defer while any packaged process runs** — and the archetypal user runs a hotkey
  script 24/7. Updates land at logoff; the compile daemon answers `WM_CLOSE`/`WM_ENDSESSION`
  since D8 of the WiX work, so it is not an extra blocker.
* **`#Package` is runtime-only.** The bundled provider runs the NuGet restore engine in-process
  from `components/packages/nuget`; it does not require the .NET SDK, MSBuild, `dotnet` or `nuget.exe`.
  Keep that nested provider payload intact in MSIX staging and do not flatten its DLLs into the
  application root.
* **`--install`/`--uninstall` should be inert when packaged** — the manifest owns associations
  and the alias covers PATH. Not yet gated (§8); today they would write into the virtualized
  hive, harmless but confusing.

## 4. Store submission checklist

1. Reserve the name in Partner Center; take *Product identity* → pass as
   `-IdentityName`/`-Publisher`/`-PublisherDisplayName`.
2. Build with `-Version 0.0.1` (or bump `Directory.Build.props` first) → identity `0.0.1.0`.
3. Upload the unsigned `.msix` (x64 first; arm64 can join the same submission later).
4. Listing: description, screenshots (the Dash + demos make good ones), privacy policy (the
   AutoHotkey Store Edition's `privacy_policy.txt` is the precedent), declare `runFullTrust`
   with the usual justification (desktop automation tool).
5. **Licensing gate**: distributing binaries on the Store is a formal act of distribution while
   the hook-code GPL-provenance question (0.1 blocker) is unresolved and the package claims BSD.
   The Store permits GPL software — the risk is the *labeling*, and it should be settled before
   submission.

## 5. Verification status

**Verified (x64, 2026-08-21) — the review-fix pass:**

* `makepri` split-index bug and its fix, proven with the SDK tools against a staged package:
  `makepri dump` of the shipped `resources.pri` shows **0** `scale-200` candidates before and
  **4** after (125/150/400 likewise), and five `.pri` files collapse to one. `makeappx pack`
  succeeds either way, which is exactly why it went unnoticed.
* Version mapping, exercised directly: `0.0.0.16`→`0.0.16.0`, `0.0.1.0`→`0.0.1000.0`,
  `0.0.2.5`→`0.0.2005.0`, `0.0.3.0`→`0.0.3000.0`, `0.0.1`→`0.0.1.0` — monotonic, where the first
  two of those previously inverted.
* Three-part versions through the MSI: `dotnet msbuild -getProperty:KeysharpMsiVersion` gives
  `0.0.1`→`0.0.1`, `0.0.0.16`→`0.0.16`, `1.2.3.4`→`1.2.3004`, malformed→empty (so the existing
  WIX0006 diagnostic still fires), and a full `-p:KeysharpVersion=0.0.1` MSI build produces
  ProductVersion `0.0.1` with 0 warnings.
* `Scripts\Template.ks` reaches both packages: present in the MSI `File` table alongside the
  `.ks\ShellNew` → `[INSTALLFOLDER]Scripts\Template.ks` registry row, and packed into the `.msix`
  as `Scripts/Template.ks` with the manifest's `ShellNewFileName` pointing at it (`makeappx`
  validates the manifest, so that path is schema-checked).
* Asset generation: only `Square150x150Logo.scale-400` now exceeds the 256px master (256→396px in
  a 600px canvas, down from three assets upscaled by up to 2.34×), and the packager names it in a
  warning. `Icon(ico, n, n)` returns exact frames for 16/32/48/64/128 and falls back to the PNG for
  24 and 256 — confirmed, so the exact-match guard is doing real work.
* Argument guards: `-SkipSign` without `-Msix`, `-Msix -SkipMsi`, and `-SkipSign` with
  `-SignCertPath` all throw with their intended messages.
* `Dash.ks` `--validate`s clean bare and under `--define:OSX` / `--define:LINUX`; `WindowTiler.ks`
  and `InputHUD.ks` likewise after the settings-path change. Full solution build: 0 warnings,
  0 errors.
* **Not verified:** the relocated `demos.ini` is compile-checked only — no demo was run to watch it
  write there, and the read-only-install failure it fixes was reasoned from the install layouts
  rather than reproduced. The Dash's model/render refactor is `--validate`-only; it was not run and
  screenshotted the way the original design was (§ below), so the redrawn header, footer, folder
  link and demo-card geometry are unconfirmed pixel-wise.

**Verified (x64, 2026-08-11):**

* `Runner.Parse` change: Debug build clean; live no-argument run from a neutral working directory
  discovered and executed a `Keysharp.cks` placed beside the exe (marker-file script, exit 0) —
  the exact MSIX tile scenario. Current-directory precedence and source-before-`.cks` ordering
  preserved by construction.
* The Dash passes `--validate` after every edit round, and the final launcher design was
  verified **live**: launched from the Debug host with a `Demos\` folder staged beside the exe,
  window screenshot-captured (via a DPI-aware `CopyFromScreen` harness — a DPI-unaware capture
  process on this 200% display grabs the wrong screen region entirely), icons/rows/blurbs all
  correct, then the process killed. Note Debug-host startup is ~12 s (cold in-memory compile),
  so window-probing harnesses need generous timeouts.
* The ListView `Icon` off-by-one fix in Core is covered by that same screenshot (before the fix
  every row visibly wore the next row's icon; after it each row shows its own).
* The relocated WiX project (`windows\wix\`) still builds: `dotnet build` of
  `Keysharp.Installer.wixproj` against a real staged tree produced an MSI cleanly after the
  `..\..\` → `..\..\..\` output/RepoRoot path adjustments.
* The MSIX pipeline (then `package-msix.ps1 -SkipPublish`, now `-Msix`) end-to-end on this machine from the existing self-contained
  publish: staging asserts, Dash and demo precompile, asset generation, makepri (258 named
  resources; scale, targetsize and unplated variants all indexed), makeappx — producing
  **`dist/Keysharp-win-x64.msix`, 55.1 MB,
  293 entries**. The package was signed with the copied `SignCert.pfx`; `signtool verify /pa`
  succeeds, PowerShell reports the signature as `Valid`, the manifest publisher matches the
  signer, and no private signing material is in the package. Contents verified by opening the
  package as a zip: `Keysharp.cks` at the root
  and **no** `Keysharp.ks`, four paired demo `.ks`/`.cks` files + source-only `Shell.ks` and
  **no** OCRSnip,
  `Scripts/WindowSpy.cks`, `Lib/OCR.ks`, `PCRE.NET.Native.dll` at root and Scintilla/Lexilla
  under `runtimes\win-x64\native` — i.e. the MSI layout invariants held through the new pipeline.
* The **self-contained host compiled scripts successfully** (WindowSpy.cks during publish; the
  Dash and all four launchable demos during staging), which retires most of the D1 risk: Roslyn
  metadata-reference resolution from the app directory works. `--compile exe` remains untested in
  this layout.
* All three packaging scripts tokenize clean under PS 5.1's parser; the manifest template is
  well-formed XML and pure ASCII. Four tooling traps were hit and fixed during this work, all
  worth remembering: **XML comments may not contain `--`** (so no `--switch` spellings inside
  manifest comments); **PS 5.1 reads BOM-less UTF-8 as ANSI**, which would have shipped mojibake
  into the manifest — the template is ASCII-only *and* the read is `-Encoding UTF8`; **makepri
  accepts one default value per qualifier type** (`/dq` with five scales fails with "Invalid
  qualifier: Scale"); and **each file type may be declared only once per package** — makeappx
  fails with error 80080204 on a duplicate, which is what removed Keyview's association (D5).
* CLI quirk found while testing: switches placed *after* the script name are not parsed as
  Keysharp switches by the main option loop (`--dest` after the script is silently inert;
  `--errorstdout` has trailing-position handling). The payload project therefore spells every switch
  **before** the script paths: `--errorstdout --compile asm <script>...`.

**Verified for the 2026-08-17 revision (x64):**

* `Keysharp.Install/payload/Dash.ks` passes `--validate` **three times** — bare, `--define:LINUX` and
  `--define:OSX` — so all three `#if` branches are syntax-checked from this Windows host. That is
  what the OSX → LINUX → `#else` ordering buys; the parser's own platform symbol cannot be turned
  off, so a Windows-first ordering would leave the other two branches unchecked forever.
* `dotnet publish` of Keysharp reports
  `Precompiled bundled scripts for faster startup: WindowSpy.cks, ClipboardHistory.cks,
  InputHUD.cks, WindowGrab.cks, WindowTiler.cks, Keysharp.cks`, and the tree has `Keysharp.cks`
  at the root with **no** `Keysharp.ks` beside it, `Scripts\Dash.ks` as source, and four paired
  demo `.ks`/`.cks` plus source-only `Shell.ks` in `Demos\`.
* The ported Dash runs **live** from the published host: `Keysharp.exe` started with no arguments
  from a neutral working directory opened the "Keysharp Dash" window with no stderr output, and a
  `PrintWindow` capture shows the surface rendering correctly with the demo cards discovered from
  the published `Demos\` folder. Use `PrintWindow` (PW_RENDERFULLCONTENT), *not* `CopyFromScreen`,
  for this: the Dash does not take focus, so a screen-region capture photographs whatever window
  is covering it — which is exactly what happened on the first attempt.
* `package-windows.ps1` with no MSIX arguments produced the zip and MSI: `MSI payload matches the
  staged tree (64 files)`, sequencing invariants OK. The MSI's `Shortcut` table has exactly
  `Keysharp` → `Keysharp.exe` (no arguments), `Keyview`, and `Window Spy` → `WindowSpy.cks`.
* `package-windows.ps1 -Msix` produced **all three** artifacts in one run: the zip, the MSI, and
  `dist/Keysharp-win-x64.msix` (56.5 MB, 321 entries), signed with the local test certificate. The
  package contains `Keysharp.cks` at the root and **no** `Keysharp.ks`, `Scripts/Dash.ks` +
  `Scripts/WindowSpy.ks`/`.cks`, four paired demo `.ks`/`.cks` with source-only `Shell.ks`, and
  `Lib/OCR.ks` — i.e. the layout invariants held after the Dash and demo staging moved out of the
  packager and into the publish.
* The MSIX-only arguments throw without `-Msix`, and the script tokenizes clean under PS 5.1.
* **Curated test suite: 422/422 passed** (Release, Windows, `curated-tests.yml`'s
  `CURATED_TEST_FILTER`) — this also clears the gate this document had been carrying open since
  2026-08-11 for the `Runner.Parse` default-script change. Note `vstest.console` lingered for
  several minutes *after* `Test Run Successful`, with the test host already gone and an orphaned
  `Keysharp.exe --daemon` still up; the run itself took 4.8 minutes. Do not read a long-running
  vstest process as a hung test without checking for a `testhost` child first.
* Note: `Content` items flow to referencing projects, so **Keyview's** publish now also carries
  `Demos\*.ks` (it already carried `Scripts\*.ks`). Harmless on Windows and Linux, where the two
  publishes merge into one tree; on macOS it means `Keyview.app` gains ~98 KB of demo sources it
  does not use.

**Not verified:**

| Scenario | What it needs |
|---|---|
| Sideload install + live package | The package is signed and this machine trusts the test certificate; run `Add-AppxPackage dist\Keysharp-win-x64.msix`, then: tile opens the Dash, `keysharp` alias resolves in a new shell, `.ks` double-click runs, ShellNew appears, demos persist `demos.ini` to `%AppData%\Keysharp`, Keyview can save an edited demo |
| Script compilation in the self-contained layout | run the *packaged* `Keysharp.exe` against a `#CSharp` script and `--compile exe` (D1 risk) |
| **The Dash on Linux and macOS** | a real desktop on each. It compiles for all three platforms and runs on Windows; see the follow-on in §7 for the specific things most likely to break (borderless Gui + Image surface under GTK/Cocoa, physical `LButton` state, the poll-loop drag, font availability) |
| **The `Keysharp.ks` cross-architecture fallback** | a cross-RID publish, to confirm the target writes the root `.ks` and that a bare launch then compiles and opens the Dash in memory. Only the same-architecture `.cks` path has been exercised |
| arm64 package | build on/for arm64 (Dash `.cks` currently requires a matching host — see §7) |
| Store certification | the submission itself; run WACK on the package first |

## 6. Files

| File | Role |
|---|---|
| [Keysharp.Install/package-windows.ps1](../Keysharp.Install/package-windows.ps1) | **the** Windows packager: zip + MSI always, MSIX with `-Msix` |
| [Keysharp.Install/windows/msix/AppxManifest.xml](../Keysharp.Install/windows/msix/AppxManifest.xml) | manifest template, `__TOKEN__` placeholders |
| [Keysharp.Install/payload/Template.ks](../Keysharp.Install/payload/Template.ks) | ShellNew seed for Explorer's New menu |
| [Keysharp.Install/payload/Dash.ks](../Keysharp.Install/payload/Dash.ks) | the Dash source, published to `Scripts/` and compiled to `Keysharp.cks` at the app root |
| [Keysharp.Install/payload/Keysharp.Payload.proj](../Keysharp.Install/payload/Keysharp.Payload.proj) | the install payload target: what puts the Dash, its template and `Demos/` in every package |
| [Keysharp.Install/windows/wix/Package.wxs](../Keysharp.Install/windows/wix/Package.wxs) | Start-menu `Keysharp` shortcut (no arguments — D8) |
| [Keysharp.Install/linux/keysharp.desktop](../Keysharp.Install/linux/keysharp.desktop) | menu-visible launcher; `NoDisplay=true` dropped (D8) |
| [Keysharp.Core/Internals/Scripting/Runner.cs](../Keysharp.Core/Internals/Scripting/Runner.cs) | default-script discovery: exe dir + `.cks` |
| [Keysharp.Core/Builtins/Gui/ListViewHelper.cs](../Keysharp.Core/Builtins/Gui/ListViewHelper.cs) | AHK-compat fix: ListView `Icon N` is 1-based |

Deleted in the 2026-08-17 pass: `Keysharp.Install/package-msix.ps1`,
`Keysharp.Install/packaging-common.ps1` and `Keysharp.Install/windows/msix/Keysharp.ks` (the Dash
source, now `Keysharp.Install/payload/Dash.ks`).

The WiX authoring moved from `Keysharp.Install/windows/` into `Keysharp.Install/windows/wix/`
alongside `windows/msix/` in the same pass (decision by Descolada, so the two Windows package
formats sit as siblings); the wixproj's output/RepoRoot relative paths gained one level and the
relocated project was rebuilt to prove it.

## 7. Follow-ons

* **Gate `--install`/`--uninstall` (and the PATH writes) on package identity**
  (`GetCurrentPackageFullName`), replacing them with a pointer at the manifest-owned equivalents.
* **Autostart story**: `startupTask` extension + a Dash toggle, or `unvirtualizedResources`;
  today a script's `HKCU\...\Run` write silently does nothing (§3).
* **Run the Dash on a real Linux and macOS desktop.** Done 2026-08-17 as far as this host allows:
  the port compiles for all three platforms and runs on Windows (§5), but the Linux/macOS
  behaviour is untested. The specific risks, in rough order: the borderless `-Caption +Border`
  Gui and the `Image`→Picture surface under Eto/GTK and Cocoa; `GetKeyState("LButton","P")`,
  which on Linux depends on the input daemon or the X11 fallback and may not report a physical
  button on Wayland; the poll-loop `WinMove` drag; and whether the chosen UI/emoji fonts
  (`DejaVu Sans`/`Noto Color Emoji`, `Helvetica Neue`/`Apple Color Emoji`) are actually present.
* **arm64 without the source fallback for Dash/demos**: script assemblies are AnyCPU, so a
  host-architecture Keysharp of the same version could compile each `.cks` during a cross-build;
  alternatively CI builds each architecture on a matching runner, as the MSI path already does.
* `A_AhkPath` → the stable alias path when packaged.
* A `Keysharp.exe` edit/relay switch, so the `.ks` association can grow an "Edit in Keyview" verb
  (the only MSIX-expressible route back to the MSI's "Open with > Keyview" — see D5).
* WACK as a packaging step; `.msixbundle` for a joint x64+arm64 upload.
