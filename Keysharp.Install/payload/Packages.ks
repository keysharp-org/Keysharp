#Requires Keysharp v0.0.0.17
#SingleInstance Ignore
#NoTrayIcon
#import KS { Json, Task, Await, A_KsVersion, A_DirSeparator }

; Declarative rather than a runtime A_GuiTheme assignment: the theme must be applied before the
; script's own windows exist, which only the manifest route guarantees.
#App { GuiTheme: "System" }

/*
    Keysharp Package Manager - a GUI over the bundled package manager, launched from the Dash.

    It is a front end, not a second implementation. Every mutation (add, remove, update, install)
    runs the same Kpm.Cli.CommandRunner the `keysharp` kpm switch runs, with its output captured into
    the log pane. Search resolves through KPM.Core too - Installable() picks the releases and the
    engine filter matches `kpm search` - because a rule about which release is installable, restated
    in script, is exactly the thing that drifts.

    The one read path that is genuinely this window's own is the Installed tab: it unions kpm.json's
    dependencies with kpm.lock.json so a declared package that failed to resolve still appears, and a
    transitive one is labelled as such. `kpm list` reports the lockfile alone, so there is nothing to
    call here. It parses both files directly, which couples it to their schema.

    Layout: project picker across the top; a tree of the project folder on the left, so what an
    install writes (Lib\KPM\...) is visible the moment it lands; tabs on the right - Browse with a
    details pane under the results, Installed, and the Log every command writes to.

    KPM.Core.dll is reached by reflection, never referenced: this script must compile and ship in
    an install whose kpm download failed, and then say package management is unavailable - the same
    contract the host's kpm switch keeps. Do not "clean this up" into a typed reference.

    Setup steps are deliberately not run from here. The bridge feeds the CLI an empty stdin, so a
    package's post-install step is reported and skipped; running one takes the CLI's own prompt.
*/

#CSharp
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

static Assembly kpmAssembly;
static string kpmUnavailable;

static Assembly LoadKpm()
{
    if (kpmAssembly != null || kpmUnavailable != null)
        return kpmAssembly;

    var path = Path.Combine(AppContext.BaseDirectory, "KPM.Core.dll");

    if (!File.Exists(path))
    {
        kpmUnavailable = $"Keysharp Package Manager needs {path}, but that file is missing."
            + "\nReinstall Keysharp, or place KPM.Core.dll from a KPM release beside the Keysharp executable:"
            + "\nhttps://github.com/keysharp-org/KPM/releases";
        return null;
    }

    try
    {
        var assembly = Assembly.LoadFrom(path);

        // The engine version the registry resolves against - same handshake the host's kpm switch makes.
        // Read through A_KsVersion, the one accessor that answers "which Keysharp is this", so the
        // version resolved against and the version on the status bar cannot become two numbers.
        Environment.SetEnvironmentVariable("KPM_ENGINE_VERSION", Keysharp.Builtins.Ks.A_KsVersion);
        kpmAssembly = assembly;
    }
    catch (Exception ex)
    {
        kpmUnavailable = $"KPM.Core.dll could not be loaded: {ex.Message}";
    }

    return kpmAssembly;
}

// Availability, versions, or the reason there is neither. Called once at startup.
public static object KpmState()
{
    var assembly = LoadKpm();

    if (assembly == null)
        return new Keysharp.Builtins.Map("Ok", 0L, "Error", kpmUnavailable);

    var run = assembly.GetType("Kpm.Cli.CommandRunner")?.GetMethod("RunAsync",
        new[] { typeof(string[]), typeof(TextWriter), typeof(TextWriter), typeof(TextReader) });
    var findRoot = assembly.GetType("Kpm.Project")?.GetMethod("FindRoot", new[] { typeof(string) });
    var getIndex = assembly.GetType("Kpm.KpmService")?.GetMethod("GetIndexAsync",
        new[] { typeof(bool), typeof(CancellationToken) });

    if (run == null || findRoot == null || getIndex == null)
        return new Keysharp.Builtins.Map("Ok", 0L, "Error",
            "The installed KPM.Core.dll is too old for this window: it does not provide the required package APIs.");

    return new Keysharp.Builtins.Map("Ok", 1L,
        "Version", assembly.GetName().Version?.ToString(3) ?? "?");
}

// Resolve the selected folder exactly as every CLI mutation will resolve --project. Without this,
// selecting a subdirectory would mutate an ancestor project while the window kept displaying the child.
public static object KpmProjectRoot(object DirectoryPath)
{
    var assembly = LoadKpm();
    return assembly.GetType("Kpm.Project", throwOnError: true)
        .GetMethod("FindRoot", new[] { typeof(string) })
        .Invoke(null, new object[] { DirectoryPath?.ToString() ?? "" });
}

// One kpm command line, exactly as the CLI would run it. stdin is empty on purpose: a command that
// would prompt (setup) reads end-of-input and skips, never silently approves.
public static async Task<object> KpmRun(object Arguments)
{
    var assembly = LoadKpm();

    if (assembly == null)
        return new Keysharp.Builtins.Map("Code", 1L, "Text", kpmUnavailable);

    var argv = ((System.Collections.IEnumerable)Arguments).Cast<object>()
               .Select(a => a?.ToString() ?? "").ToArray();
    var run = assembly.GetType("Kpm.Cli.CommandRunner", throwOnError: true)
              .GetMethod("RunAsync", new[] { typeof(string[]), typeof(TextWriter), typeof(TextWriter), typeof(TextReader) });
    using var writer = new StringWriter();
    var code = await ((Task<int>)run.Invoke(null, new object[] { argv, writer, writer, TextReader.Null })).ConfigureAwait(false);
    return new Keysharp.Builtins.Map("Code", (long)code, "Text", writer.ToString().TrimEnd());
}

// The newest release a fresh resolve could install, chosen by KPM.Core rather than here: Installable()
// is where "not yanked, and only the highest revision of a version" is defined, and a second copy of
// that rule in script would be the one thing that drifts. Null when the package has no such release.
static object NewestInstallable(object match)
{
    var releases = ((System.Collections.IEnumerable)match.GetType().GetMethod("Installable")
                       .Invoke(match, null)).Cast<object>().ToList();

    if (releases.Count == 0)
        return null;

    // MaxBy over a type reached by reflection: compare through the version type's own CompareTo.
    var release = releases[0].GetType().GetProperty("Release");
    var compare = release.PropertyType.GetMethod("CompareTo", new[] { release.PropertyType });
    var best = releases[0];

    foreach (var candidate in releases.Skip(1))
        if ((int)compare.Invoke(release.GetValue(candidate), new[] { release.GetValue(best) }) > 0)
            best = candidate;

    return best;
}

// Registry search, resolved the way `kpm search` resolves it - newest installable release, and
// packages that declare no support for this engine dropped unless allEngines is on. The window would
// otherwise offer packages `kpm add` then refuses.
//
// Serialized rather than reflected member by member: the index records serialize under the same names
// the published registry schema uses, and the script decodes them case-insensitively, so this stays
// correct across cosmetic model changes.
public static async Task<object> KpmSearch(object Query, object Refresh, object EngineFilter, object PlatformFilter)
{
    var assembly = LoadKpm();

    if (assembly == null)
        return System.Text.Json.JsonSerializer.Serialize(new { Error = kpmUnavailable });

    try
    {
        var serviceType = assembly.GetType("Kpm.KpmService", throwOnError: true);
        var constructor = serviceType.GetConstructors().First();
        var service = constructor.Invoke(constructor.GetParameters()
                          .Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray());
        var getIndex = serviceType.GetMethod("GetIndexAsync", new[] { typeof(bool), typeof(CancellationToken) });
        object fetch;
        string bridgeWarning = null;

        try
        {
            using var fetchTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var pending = (Task)getIndex.Invoke(service,
                new object[] { !"0".Equals(Refresh?.ToString()), fetchTimeout.Token });
            await pending.ConfigureAwait(false);
            fetch = pending.GetType().GetProperty("Result").GetValue(pending);
        }
        catch (OperationCanceledException)
        {
            // KPM reuses the cancelled operation token when it falls back to its cached index.
            // Retry offline with a fresh, short token: cache reads complete promptly, while a
            // missing cache cannot start another pair of long network waits.
            using var cacheTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var pending = (Task)getIndex.Invoke(service, new object[] { false, cacheTimeout.Token });
            await pending.ConfigureAwait(false);
            fetch = pending.GetType().GetProperty("Result").GetValue(pending);

            if ((bool)fetch.GetType().GetProperty("FromCache").GetValue(fetch))
                bridgeWarning = "registry request timed out; using the cached index";
        }

        var warning = bridgeWarning ?? fetch.GetType().GetProperty("Warning")?.GetValue(fetch)?.ToString();
        var index = fetch.GetType().GetProperty("Index").GetValue(fetch);
        var matches = ((System.Collections.IEnumerable)index.GetType().GetMethod("Search")
                          .Invoke(index, new object[] { Query?.ToString() ?? "" })).Cast<object>().ToList();
        // The engine names come from KPM.Core's own constants so a renamed engine cannot silently
        // empty a filtered list. "Any" is the broad platform view; an OS choice also includes
        // portable packages because those are installable on that OS.
        var enginesType = assembly.GetType("Kpm.Model.Engines", throwOnError: false);
        var engine = (string)enginesType?.GetField("Keysharp")?.GetValue(null) ?? "keysharp";
        var autoHotkey = (string)enginesType?.GetField("AutoHotkey")?.GetValue(null) ?? "autohotkey";
        var requestedEngine = EngineFilter?.ToString()?.Trim().ToLowerInvariant() switch
        {
            "keysharp" => engine,
            "autohotkey" => autoHotkey,
            _ => null
        };
        var requestedPlatform = PlatformFilter?.ToString()?.Trim().ToLowerInvariant() switch
        {
            "windows" => "win",
            "linux" => "linux",
            "macos" => "osx",
            _ => null
        };
        var rows = new List<object>();

        foreach (var match in matches)
        {
            var newest = NewestInstallable(match);

            if (newest == null)
                continue;

            var engines = newest.GetType().GetProperty("Engines").GetValue(newest);
            var supported = (bool)engines.GetType().GetMethod("ContainsKey", new[] { typeof(string) })
                                  .Invoke(engines, new object[] { engine });
            var autoHotkeySupported = (bool)engines.GetType().GetMethod("ContainsKey", new[] { typeof(string) })
                                                .Invoke(engines, new object[] { autoHotkey });

            if (requestedEngine == engine && !supported
                || requestedEngine == autoHotkey && !autoHotkeySupported)
                continue;

            var platforms = ((System.Collections.IEnumerable)newest.GetType().GetProperty("Platforms")
                                .GetValue(newest)).Cast<object>().Select(p => p?.ToString() ?? "").ToList();

            if (requestedPlatform != null && !platforms.Any(p => p == "any" || p == requestedPlatform
                                                               || p.StartsWith(requestedPlatform + "-",
                                                                               StringComparison.Ordinal)))
                continue;

            rows.Add(new
            {
                Package = match.GetType().GetProperty("Package").GetValue(match),
                Release = newest,
                Supported = supported,
                Engine = supported ? engine : autoHotkeySupported ? autoHotkey : null
            });
        }

        return System.Text.Json.JsonSerializer.Serialize(new { Warning = warning, Rows = rows });
    }
    catch (Exception ex)
    {
        var reported = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
        return System.Text.Json.JsonSerializer.Serialize(new { Error = reported.Message });
    }
}
#EndCSharp

; ---------------------------------------------------------------------------
; project selection, remembered across runs
; ---------------------------------------------------------------------------
Sep := A_DirSeparator
SettingsIni := A_AppData Sep "Keysharp" Sep "Packages.ini"
; No default project. Anything else means a first launch aims the tool at a folder the user never
; chose - and `add` writes kpm.json and a Lib\KPM tree into whatever that is, one double-click from
; the search results. Empty until picked, and everything that touches a project stays disabled.
; A remembered folder that has since been deleted or moved counts as unset for the same reason.
ProjectDir := A_Args.Length >= 1 && DirExist(A_Args[1]) ? A_Args[1]
    : IniRead(SettingsIni, "state", "project", "")
if ProjectDir != "" && !DirExist(ProjectDir)
    ProjectDir := ""

State := KpmState()
KpmOk := State["Ok"] = 1
if ProjectDir != "" && KpmOk
    ProjectDir := KpmProjectRoot(ProjectDir)
; Operations consult this flag because ListView gestures remain live while buttons are disabled.
Running := false
PendingCommand := ""

; Search-result details keyed by package id, and looked up through the row's own first column - the
; user can re-sort the ListView by clicking a header, so a row number is never a stable key.
SearchRows := Map()
SearchRows.CaseSense := "Off"
CatalogRows := Map()
CatalogRows.CaseSense := "Off"
InstalledRows := Map()
InstalledRows.CaseSense := "Off"
ListSortStates := Map()

; The log is initialized in the auto-execute section because the function definitions below the
; window's return do not execute their surrounding assignments.
LogLines := []
MaxLogLines := 400

; ---------------------------------------------------------------------------
; selftest: exercises the bridge with no window, for headless verification.
; ---------------------------------------------------------------------------
if A_Args.Length >= 1 && A_Args[1] = "--selftest" {
    try {
        FileAppend("state ok=" State["Ok"] (KpmOk ? " version=" State["Version"] : " error=" State["Error"]) "`n", "*")
        if !KpmOk
            throw Error(State["Error"])
        versionResult := Await(KpmRun(["version"]), 30000)
        FileAppend("run code=" versionResult["Code"] " text=" versionResult["Text"] "`n", "*")
        if versionResult["Code"] != 0
            throw Error("kpm version failed: " versionResult["Text"])
        ; Prefer KPM's cached index so this bridge check does not depend on registry availability.
        ; With no cache, KPM fetches within the bridge's shorter per-mirror timeout.
        found := Json.Decode(Await(KpmSearch("findtext", "0", "Keysharp", "Any"), 30000), caseSense: false)
        if found.Has("Error")
            throw Error("search failed: " found["Error"])
        FileAppend("search matches=" found["Rows"].Length "`n", "*")
        ; The filter is the difference between this window and `kpm search`, so the headless check
        ; proves it ran rather than only that some rows came back.
        every := Json.Decode(Await(KpmSearch("findtext", "0", "All", "Any"), 30000), caseSense: false)
        if every.Has("Error")
            throw Error("all-engines search failed: " every["Error"])
        if every["Rows"].Length < found["Rows"].Length
            throw Error("all-engines search returned fewer rows than the filtered search")
        FileAppend("search all-engines=" every["Rows"].Length "`n", "*")
        for platform in ["Windows", "Linux", "macOS"] {
            filtered := Json.Decode(Await(KpmSearch("Cebolla", "0", "AutoHotkey", platform), 30000), caseSense: false)
            if filtered.Has("Error") || filtered["Rows"].Length = 0
                throw Error("AutoHotkey/" platform " filter omitted a portable package")
            FileAppend("search AutoHotkey/" platform "=" filtered["Rows"].Length "`n", "*")
        }
        FileAppend("selftest done`n", "*")
    } catch as e {
        FileAppend("selftest failed: " e.Message "`n", "*")
        ExitApp(1)
    }
    ExitApp(0)
}

; ---------------------------------------------------------------------------
; window: project row on top, project tree left, tabbed work area right.
; Coordinates inside a tab are relative to the tab page's display area, not the window - the tab
; control is those controls' parent window (AHK Tab remarks). Everything else is window-relative.
; ---------------------------------------------------------------------------
BaseW := 980, BaseH := 642

Pk := Gui(, "Keysharp Package Manager")
Pk.SetFont("s9")
Pk.OnEvent("Close", ClosePackages)
Pk.OnEvent("Escape", ClosePackages)

Pk.AddText("x12 y14", "Project:")
ProjEdit := Pk.AddEdit("x66 y10 w636 ReadOnly", ProjectPrompt())
BrowseBtn := Pk.AddButton("x708 y9 w110", "Browse...")
BrowseBtn.OnEvent("Click", PickProject)
OpenBtn := Pk.AddButton("x824 y9 w130", "Open folder")
OpenBtn.OnEvent("Click", (*) => ShowFolder(ProjectDir))

Pk.AddText("x12 y44", "Package files")
Tree := Pk.AddTreeView("x12 y64 w255 h541")

Tabs := Pk.AddTab3("x276 y40 w692 h566", ["Browse", "Installed", "Log"])

; --- Browse ---
Tabs.UseTab(1)
SearchEdit := Pk.AddEdit("x12 y12 w536")
SearchBtn := Pk.AddButton("x558 y11 w104 Default", "Search")
SearchBtn.OnEvent("Click", RunSearch)
Pk.AddText("x12 y46", "Engine:")
EngineBox := Pk.AddComboBox("x65 y44 w130", ["All", "Keysharp", "AutoHotkey"])
EngineBox.Choose(1)
EngineBox.OnEvent("Change", ApplyFilters)
Pk.AddText("x215 y46", "Platform:")
PlatformBox := Pk.AddComboBox("x278 y44 w130", ["Any", "Windows", "Linux", "macOS"])
PlatformBox.Choose(1)
PlatformBox.OnEvent("Change", ApplyFilters)
ResultsLV := Pk.AddListView("x12 y72 w650 h270", ["Package", "Version", "Engines", "Platforms", "Description"])
ResultsLV.ModifyCol(1, 180), ResultsLV.ModifyCol(2, 60), ResultsLV.ModifyCol(3, 90)
ResultsLV.ModifyCol(4, 90), ResultsLV.ModifyCol(5, 200)
ResultsLV.OnEvent("ItemSelect", OnResultSelect)
ResultsLV.OnEvent("DoubleClick", InstallDoubleClick)
ResultsLV.OnEvent("ContextMenu", ShowBrowseContextMenu)
ResultsLV.OnEvent("ColClick", SortListView)
; Explicit heights rather than r-counts throughout: OnResize moves these by a delta, which needs a
; height it can add to.
DetailsEdit := Pk.AddEdit("x12 y350 w650 h100 ReadOnly", "Select a package to see its details.")
AddBtn := Pk.AddButton("x12 y460 w170", "Add to project")
AddBtn.OnEvent("Click", AddSelected)

; --- Installed ---
Tabs.UseTab(2)
InstalledLV := Pk.AddListView("x12 y12 w650 h438", ["Package", "Wanted", "Installed", "Platform"])
InstalledLV.ModifyCol(1, 280), InstalledLV.ModifyCol(2, 110), InstalledLV.ModifyCol(3, 120), InstalledLV.ModifyCol(4, 100)
InstalledLV.OnEvent("ItemSelect", (*) => SyncButtons())
InstalledLV.OnEvent("DoubleClick", RemoveDoubleClick)
InstalledLV.OnEvent("ContextMenu", ShowInstalledContextMenu)
InstalledLV.OnEvent("ColClick", SortListView)
RemoveBtn := Pk.AddButton("x12 y460 w130", "Remove")
RemoveBtn.OnEvent("Click", RemoveSelected)
UpdateBtn := Pk.AddButton("x148 y460 w130", "Update all")
UpdateBtn.OnEvent("Click", (*) => Mutate(["update"], "updating..."))
InstallBtn := Pk.AddButton("x284 y460 w170", "Install from lockfile")
InstallBtn.OnEvent("Click", (*) => Mutate(["install"], "installing..."))

; --- Log ---
Tabs.UseTab(3)
LogEdit := Pk.AddEdit("x12 y12 w650 h522 ReadOnly")
Tabs.UseTab()
Tabs.Choose(1)   ; building leaves the last-filled tab selected; open on Browse

SB := Pk.AddStatusBar()

if KpmOk
    SB.SetText("kpm " State["Version"] "  |  Keysharp " A_KsVersion)
else {
    ; The window still opens so the message is somewhere a user will read it.
    Log(State["Error"])
    SB.SetText("package management unavailable - see the Log tab")
}

SyncButtons()
; Shown before the tree walk reads the disk, so choosing a large project folder does not hold the
; window invisible while it counts files.
Pk.Show("w" BaseW " h" BaseH)
SearchEdit.Focus()
RefreshProject()
StartSearch("0")
return

ClosePackages(*) {
    if Running {
        Log("wait for the current package operation to finish before closing")
        return 1
    }
    ExitApp()
}

; Enabled state is derived, never toggled: it depends on three things at once (is kpm here, is a
; project chosen, is a command running) and a per-site guard cannot see all three.
SyncButtons() {
    local usable := KpmOk && !Running
    local project := usable && ProjectDir != ""
    local selected := ResultsLV.GetNext(0)
    local addable := project && selected > 0
        && SearchRows.Has(ResultsLV.GetText(selected, 1))
        && SearchRows[ResultsLV.GetText(selected, 1)]["Engine"] != ""
    local removable := project && InstalledLV.GetNext(0) > 0
    BrowseBtn.Enabled := !Running
    OpenBtn.Enabled := ProjectDir != "" && !Running
    EngineBox.Enabled := !Running
    PlatformBox.Enabled := !Running
    SearchBtn.Enabled := usable
    AddBtn.Enabled := addable
    RemoveBtn.Enabled := removable
    UpdateBtn.Enabled := project
    InstallBtn.Enabled := project
}

ProjectPrompt() => ProjectDir != "" ? ProjectDir : "No project chosen - Browse... for the folder where the script and kpm.json live"

; ---------------------------------------------------------------------------
; platform: reveal a folder in the file manager (same #if ordering as the Dash,
; so the other branches stay syntax-checkable via --define).
; ---------------------------------------------------------------------------
ShowFolder(FolderPath) {
#if OSX
    Run('open "' FolderPath '"')
#elif LINUX
    Run('xdg-open "' FolderPath '"')
#else
    Run('explorer.exe "' FolderPath '"')
#endif
}

; ---------------------------------------------------------------------------
; helpers
; ---------------------------------------------------------------------------
Log(text) {
    global LogLines
    text := text ""
    normalized := StrReplace(StrReplace(text, "`r`n", "`n"), "`r", "`n")
    added := normalized = "" ? [""] : StrSplit(normalized, "`n")
    for line in added
        LogLines.Push(line)
    if LogLines.Length > MaxLogLines
        LogLines.RemoveAt(1, LogLines.Length - MaxLogLines)
    joined := ""
    for i, line in LogLines
        joined .= (i > 1 ? "`r`n" : "") line
    LogEdit.Value := joined
    ; the newest line is the interesting one: show it in the status bar and keep it in view
    last := added[added.Length]
    SB.SetText(StrLen(last) > 110 ? SubStr(last, 1, 110) "..." : last)
    try
        SendMessage(0x115, 7, 0, LogEdit.Hwnd)   ; WM_VSCROLL SB_BOTTOM; best-effort per platform
}

; Paired with Finish(), and every operation between them is wrapped so that the pair cannot be
; broken by a throw: the buttons are the only way back in, so leaving them disabled ends the session.
Begin(note) {
    global Running
    Running := true
    SyncButtons()
    SB.SetText(note)
}

Finish() {
    global Running, PendingCommand
    Running := false
    PendingCommand := ""
    SyncButtons()
}

; Both preconditions in one place, each with the reason a user can act on.
Ready(needsProject := true) {
    if !KpmOk {
        Log(State["Error"])
        return false
    }
    if Running {
        Log("a package command is already running")
        return false
    }
    if needsProject && ProjectDir = "" {
        Log("choose a project folder first")
        return false
    }
    return true
}

PickProject(*) {
    global ProjectDir
    picked := DirSelect("*" (ProjectDir != "" ? ProjectDir : A_MyDocuments), 3,
        "Select the project folder (where the script and kpm.json live)")
    if picked = ""
        return
    selected := picked
    ProjectDir := KpmOk ? KpmProjectRoot(picked) : picked
    ProjEdit.Value := ProjectPrompt()
    ; The folder is Keysharp's user-data directory but nothing guarantees it exists yet - IniWrite
    ; creates the file, never its parent - and without this the choice is silently forgotten on
    ; every clean machine, which reads as the picker not working.
    try {
        DirCreate(A_AppData Sep "Keysharp")
        IniWrite(ProjectDir, SettingsIni, "state", "project")
    }
    SyncButtons()
    RefreshProject()
    if ProjectDir != selected
        Log("using project root " ProjectDir)
}

RefreshProject() {
    RefreshTree()
    RefreshInstalled()
}

; The tree is rooted at the directory KPM owns rather than recursively scanning the user's project.
; It remains bounded because a package payload can be large and this walk runs on the UI thread.
RefreshTree() {
    global Tree, TreeNodes
    Tree.Delete()
    TreeNodes := 0
    if ProjectDir = "" {
        Tree.Add("(no project chosen)", 0)
        return
    }
    packageDir := ProjectDir Sep "Lib" Sep "KPM"
    root := Tree.Add("Lib" Sep "KPM", 0, "Expand Bold")
    if !DirExist(packageDir) {
        Tree.Add("(no packages installed)", root)
        return
    }
    AddTreeLevel(packageDir, root, 1)
}

AddTreeLevel(dir, parent, depth) {
    global TreeNodes
    if depth > 4 || TreeNodes > 400
        return
    dirs := [], files := []
    Loop Files, dir Sep "*", "DF" {
        if InStr(A_LoopFileAttrib, "H")
            continue
        (InStr(A_LoopFileAttrib, "D") ? dirs : files).Push(A_LoopFileName)
    }
    for name in dirs {
        if (TreeNodes += 1) > 400 {
            Tree.Add("...", parent)
            return
        }
        node := Tree.Add(name, parent)
        AddTreeLevel(dir Sep name, node, depth + 1)
    }
    for name in files {
        if (TreeNodes += 1) > 400 {
            Tree.Add("...", parent)
            return
        }
        Tree.Add(name, parent)
    }
}

; Installed = the union of what kpm.json asks for and what kpm.lock.json pinned, so a declared
; dependency that failed to resolve still shows, and a transitive one is visible as such.
RefreshInstalled() {
    global InstalledLV, InstalledRows
    InstalledLV.Delete()
    InstalledRows := Map()
    InstalledRows.CaseSense := "Off"
    if ProjectDir = ""
        return
    wanted := Map(), locked := Map()
    wanted.CaseSense := "Off", locked.CaseSense := "Off"
    manifestPath := ProjectDir Sep "kpm.json"
    if FileExist(manifestPath) {
        try {
            manifest := Json.Decode(FileRead(manifestPath, "UTF-8"), caseSense: false)
            if manifest.Has("dependencies")
                for id, range in manifest["dependencies"]
                    wanted[id] := range
        } catch as err {
            Log("could not read kpm.json: " err.Message)
            return
        }
    }
    lockPath := ProjectDir Sep "kpm.lock.json"
    if FileExist(lockPath) {
        try {
            lockFile := Json.Decode(FileRead(lockPath, "UTF-8"), caseSense: false)
            if lockFile.Has("packages")
                for entry in lockFile["packages"] {
                    if !entry.Has("id") || !entry.Has("version")
                        throw Error("a package entry is missing id or version")
                    locked[entry["id"]] := entry
                }
        } catch as err {
            locked.Clear()
            Log("could not read kpm.lock.json: " err.Message)
        }
    }
    for id, range in wanted {
        entry := locked.Has(id) ? locked[id] : ""
        InstalledRows[id] := Map("Wanted", range, "Installed", entry != "")
        InstalledLV.Add("", id, range,
            entry != "" ? entry["version"] "-r" (entry.Has("revision") ? entry["revision"] : 1) : "not installed",
            entry != "" && entry.Has("platform") ? entry["platform"] : "")
        if entry != ""
            locked.Delete(id)
    }
    for id, entry in locked {
        InstalledRows[id] := Map("Wanted", "(dependency)", "Installed", true)
        InstalledLV.Add("", id, "(dependency)",
            entry["version"] "-r" (entry.Has("revision") ? entry["revision"] : 1),
            entry.Has("platform") ? entry["platform"] : "")
    }
    ApplyListSort(InstalledLV, 1, false)
}

; The cached catalog is loaded at startup; an explicit search refreshes it from the registry.
RunSearch(*) {
    StartSearch("1")
}

ApplyFilters(*) {
    StartSearch("0")
}

StartSearch(refresh) {
    if !Ready(false)
        return
    Begin("searching...")
    KpmSearch(SearchEdit.Value, refresh, EngineBox.Text, PlatformBox.Text).Then(ShowResults, SearchFailed)
}

SearchFailed(err) {
    try
        Log("search failed: " err.Message)
    finally
        Finish()
}

; Wrapped whole: an unexpected shape reaching the loop would otherwise leave Running set and every
; button disabled, which cannot be recovered from without restarting the window.
ShowResults(payload) {
    global SearchRows, CatalogRows
    try {
        ResultsLV.Delete()
        SearchRows := Map()
        SearchRows.CaseSense := "Off"
        parsed := Json.Decode(payload, caseSense: false)
        if parsed.Has("Error") && parsed["Error"] != "" {
            Log("search failed: " parsed["Error"])
            return
        }
        if parsed.Has("Warning") && parsed["Warning"] != ""
            Log("warning: " parsed["Warning"])
        for m in parsed["Rows"] {
            pkg := m["Package"], release := m["Release"]
            row := Map("Id", pkg["Owner"] "/" pkg["Name"],
                "Version", release.Has("Version") ? release["Version"] : "",
                "Description", pkg.Has("Description") ? pkg["Description"] : "",
                "Engines", JoinKeys(release.Has("Engines") ? release["Engines"] : Map()),
                "Platforms", JoinList(release.Has("Platforms") ? release["Platforms"] : []),
                "Authors", JoinList(pkg.Has("Authors") ? pkg["Authors"] : []),
                "DerivedFrom", pkg.Has("DerivedFrom") ? pkg["DerivedFrom"] : "",
                "License", pkg.Has("License") ? pkg["License"] : "",
                "Homepage", pkg.Has("Homepage") ? pkg["Homepage"] : "",
                "Supported", m.Has("Supported") && m["Supported"],
                "Engine", m.Has("Engine") ? m["Engine"] : "")
            ; Column 1 stays the bare id: it is both this map's key and the argument handed to
            ; `kpm add`. Whether a row suits this engine is already visible in the Engines column.
            ResultsLV.Add("", row["Id"], row["Version"], row["Engines"], row["Platforms"], row["Description"])
            SearchRows[row["Id"]] := row
            CatalogRows[row["Id"]] := row
        }
        ApplyListSort(ResultsLV, 1, false)
        Log("search: " SearchRows.Count " package(s)")
    } catch as err {
        Log("search results failed: " err.Message)
    } finally {
        Finish()
    }
}

SortListView(ctrl, column) {
    global ListSortStates
    key := ctrl
    descending := ListSortStates.Has(key)
        && (ListSortStates[key]["Column"] = column)
        && !ListSortStates[key]["Descending"]
    ApplyListSort(ctrl, column, descending)
}

ApplyListSort(ctrl, column, descending) {
    global ListSortStates
    ListSortStates[ctrl] := Map("Column", column, "Descending", descending)
    ctrl.ModifyCol(column, descending ? "Logical SortDesc" : "Logical Sort")
}

JoinKeys(m) {
    joined := ""
    for k in m
        joined .= (joined != "" ? ", " : "") k
    return joined
}

JoinList(a) {
    joined := ""
    for v in a
        joined .= (joined != "" ? ", " : "") v
    return joined
}

OnResultSelect(ctrl, item, selected) {
    global DetailsEdit
    SyncButtons()
    if !selected || item < 1 || !SearchRows.Has(ResultsLV.GetText(item, 1))
        return
    row := SearchRows[ResultsLV.GetText(item, 1)]
    text := row["Id"] "  " row["Version"]
    if row["Authors"] != ""
        text .= "`r`nby " row["Authors"] (row["DerivedFrom"] != "" ? ";  derived from " row["DerivedFrom"] : "")
    text .= "`r`n`r`n" row["Description"]
    text .= "`r`n`r`nengines: " row["Engines"] "    platforms: " row["Platforms"]
    if row["License"] != ""
        text .= "    license: " row["License"]
    if row["Homepage"] != ""
        text .= "`r`n" row["Homepage"]
    if row["Engine"] = "autohotkey"
        text .= "`r`n`r`nThis release declares AutoHotkey compatibility only. KPM will install it in AutoHotkey 2.0 compatibility mode; Keysharp compatibility has not been verified."
    else if row["Engine"] = ""
        text .= "`r`n`r`nThis package does not declare support for a recognized engine and cannot be added."
    DetailsEdit.Value := text
}

AddSelected(ctrl := "", item := 0, *) {
    row := item > 0 ? item : ResultsLV.GetNext(0)
    if row = 0 {
        Log("select a search result first")
        return
    }
    id := ResultsLV.GetText(row, 1)
    if !SearchRows.Has(id) {
        Log("the selected package is no longer in the search results")
        return
    }
    AddPackageId(id)
}

AddPackageId(id, action := "adding", *) {
    if !CatalogRows.Has(id) {
        Log(id " is not available in the loaded catalog")
        return
    }
    engine := CatalogRows[id]["Engine"]
    if engine = "" {
        Log(id " declares no support for a recognized engine; kpm would refuse it")
        return
    }
    arguments := ["add", id]
    if engine = "autohotkey"
        arguments.Push("--engine", "autohotkey", "--engine-version", "2.0")
    Mutate(arguments, action " " id "...")
}

RemoveSelected(ctrl := "", item := 0, *) {
    row := item > 0 ? item : InstalledLV.GetNext(0)
    if row = 0 {
        Log("select an installed package first")
        return
    }
    RemovePackageId(InstalledLV.GetText(row, 1), InstalledLV.GetText(row, 2))
}

RemovePackageId(id, wanted, *) {
    if wanted = "(dependency)" {
        if ProjectHasNoDirectDependencies() {
            try {
                CleanupEmptyProject()
                RefreshProject()
            } catch as err {
                Log("could not remove stale package files: " err.Message)
                Tabs.Choose(3)
            }
            return
        }
        Log("remove the direct dependency that brought in " id)
        return
    }
    Mutate(["remove", id], "removing " id "...")
}

; WinForms reports DoubleClick for every mouse button but the GUI event does not carry the button.
; Checking the physical left button here prevents a double right-click from becoming an install.
InstallDoubleClick(ctrl, item, *) {
    if GetKeyState("LButton", "P") && !GetKeyState("RButton", "P")
        AddSelected(ctrl, item)
}

RemoveDoubleClick(ctrl, item, *) {
    if GetKeyState("LButton", "P") && !GetKeyState("RButton", "P")
        RemoveSelected(ctrl, item)
}

ShowBrowseContextMenu(ctrl, item, *) {
    popup := Menu()
    if item < 1 || !SearchRows.Has(ctrl.GetText(item, 1)) {
        popup.Add("Refresh catalog", RunSearch)
        if ProjectDir != ""
            popup.Add("Open project folder", (*) => ShowFolder(ProjectDir))
        popup.Show()
        return
    }

    id := ctrl.GetText(item, 1)
    installed := InstalledRows.Has(id) ? InstalledRows[id] : ""
    if installed = "" {
        popup.Add("Install", AddPackageId.Bind(id, "installing"))
        if ProjectDir = "" || Running
            popup.Disable("Install")
    } else if installed["Wanted"] = "(dependency)" {
        popup.Add("Make direct dependency", AddPackageId.Bind(id, "adding"))
        if ProjectDir = "" || Running
            popup.Disable("Make direct dependency")
    } else {
        popup.Add("Update to latest", AddPackageId.Bind(id, "updating"))
        popup.Add("Remove", RemovePackageId.Bind(id, installed["Wanted"]))
        if Running {
            popup.Disable("Update to latest")
            popup.Disable("Remove")
        }
    }
    AddProjectMenuItems(popup)
    AddPackageMenuItems(popup, id)
    popup.Show()
}

ShowInstalledContextMenu(ctrl, item, *) {
    popup := Menu()
    if item < 1 || !InstalledRows.Has(ctrl.GetText(item, 1)) {
        AddProjectMenuItems(popup, false)
        popup.Add()
        popup.Add("Refresh installed list", (*) => RefreshProject())
        popup.Show()
        return
    }

    id := ctrl.GetText(item, 1)
    installed := InstalledRows[id]
    actionLabel := ""
    if CatalogRows.Has(id) {
        actionLabel := installed["Wanted"] = "(dependency)" ? "Make direct dependency" : "Update to latest"
        popup.Add(actionLabel,
            AddPackageId.Bind(id, installed["Wanted"] = "(dependency)" ? "adding" : "updating"))
    }
    removable := installed["Wanted"] != "(dependency)" || ProjectHasNoDirectDependencies()
    if removable
        popup.Add("Remove", RemovePackageId.Bind(id, installed["Wanted"]))
    if Running {
        if actionLabel != ""
            popup.Disable(actionLabel)
        if removable
            popup.Disable("Remove")
    }
    AddProjectMenuItems(popup)
    AddPackageMenuItems(popup, id)
    popup.Show()
}

AddProjectMenuItems(popup, separator := true) {
    if separator
        popup.Add()
    popup.Add("Update project", (*) => Mutate(["update"], "updating..."))
    popup.Add("Reinstall from lockfile", (*) => Mutate(["install"], "installing..."))
    if ProjectDir = "" || Running {
        popup.Disable("Update project")
        popup.Disable("Reinstall from lockfile")
    } else if !FileExist(ProjectDir Sep "kpm.lock.json")
        popup.Disable("Reinstall from lockfile")
}

AddPackageMenuItems(popup, id) {
    popup.Add()
    popup.Add("Copy package ID", CopyPackageText.Bind(id, "copied package ID"))
    popup.Add("Copy #Include directive", CopyPackageText.Bind("#Include <KPM/" id ">", "copied include directive"))
    if CatalogRows.Has(id) && CatalogRows[id]["Homepage"] != ""
        popup.Add("Open homepage", OpenPackageHomepage.Bind(id))
    if InstalledRows.Has(id) && InstalledRows[id]["Installed"]
        popup.Add("Open package folder", OpenPackageFolder.Bind(id))
}

CopyPackageText(text, note, *) {
    A_Clipboard := text
    Log(note ": " text)
}

OpenPackageHomepage(id, *) {
    if CatalogRows.Has(id) && CatalogRows[id]["Homepage"] != ""
        Run(CatalogRows[id]["Homepage"])
}

OpenPackageFolder(id, *) {
    parts := StrSplit(id, "/",, 2)
    folder := ProjectDir Sep "Lib" Sep "KPM" Sep parts[1] Sep parts[2]
    if DirExist(folder)
        ShowFolder(folder)
    else
        Log("the package folder does not exist: " folder)
}

; Every mutation is one kpm command line against the chosen project; the log gets the CLI's own words.
Mutate(arguments, note) {
    global ProjectDir, PendingCommand
    if !Ready()
        return
    if !DirExist(ProjectDir) {
        Log("the project folder no longer exists: " ProjectDir)
        return
    }
    try
        currentRoot := KpmProjectRoot(ProjectDir)
    catch as err {
        Log("could not resolve the project root: " err.Message)
        return
    }
    if currentRoot != ProjectDir {
        ProjectDir := currentRoot
        ProjEdit.Value := ProjectPrompt()
        try {
            DirCreate(A_AppData Sep "Keysharp")
            IniWrite(ProjectDir, SettingsIni, "state", "project")
        }
        RefreshProject()
        SyncButtons()
        Log("the project root changed to " ProjectDir "; review it, then run the command again")
        return
    }
    if !HasArgument(arguments, "--engine") && ProjectMutationEngine() = "autohotkey"
        arguments.Push("--engine", "autohotkey", "--engine-version", "2.0")
    arguments.Push("--project", ProjectDir)
    PendingCommand := arguments[1]
    Begin(note)
    line := ""
    for a in arguments
        line .= (line != "" ? " " : "") a
    Log("> kpm " line)
    KpmRun(arguments).Then(MutateDone, MutateFailed)
}

; Both wrapped so Finish() runs whatever the body does: leaving Running set disables every button,
; and the buttons are the only way to start anything.
MutateDone(result) {
    try {
        Log(result["Text"] != "" ? result["Text"] : "(no output)")
        if result["Code"] != 0 {
            Log("exited " result["Code"])
            Tabs.Choose(3)
        } else if PendingCommand = "remove"
            CleanupEmptyProject()
        RefreshProject()
    } catch as err {
        Log("command result failed: " err.Message)
    } finally {
        Finish()
    }
}

HasArgument(arguments, wanted) {
    for argument in arguments
        if argument = wanted
            return true
    return false
}

; KPM resolves a whole project for one engine. Existing AutoHotkey-only dependencies therefore need
; the same explicit compatibility context on update and remove that they needed when first added.
ProjectMutationEngine() {
    if ProjectDir = ""
        return "keysharp"
    manifestPath := ProjectDir Sep "kpm.json"
    if !FileExist(manifestPath)
        return "keysharp"
    try {
        manifest := Json.Decode(FileRead(manifestPath, "UTF-8"), caseSense: false)
        if !manifest.Has("dependencies")
            return "keysharp"
        sawAutoHotkeyOnly := false
        for id in manifest["dependencies"] {
            if !CatalogRows.Has(id)
                continue
            package := CatalogRows[id]
            if package["Engine"] = "autohotkey"
                sawAutoHotkeyOnly := true
            else if package["Engine"] = "keysharp"
                return "keysharp"
        }
        return sawAutoHotkeyOnly ? "autohotkey" : "keysharp"
    }
    return "keysharp"
}

; KPM 0.2.0 returns early when the last dependency is removed, before its normal installer prune.
; Only the generated KPM tree and the now-stale lockfile are removed, and only after a successful
; command plus a valid manifest proving that no direct dependencies remain.
CleanupEmptyProject() {
    if !ProjectHasNoDirectDependencies()
        return
    packageRoot := ProjectDir Sep "Lib" Sep "KPM"
    lockPath := ProjectDir Sep "kpm.lock.json"
    if DirExist(packageRoot)
        DirDelete(packageRoot, true)
    if FileExist(lockPath)
        FileDelete(lockPath)
    Log("removed generated package files")
}

ProjectHasNoDirectDependencies() {
    manifestPath := ProjectDir Sep "kpm.json"
    if !FileExist(manifestPath)
        return false
    try {
        manifest := Json.Decode(FileRead(manifestPath, "UTF-8"), caseSense: false)
        return manifest.Has("dependencies") && manifest["dependencies"].Count = 0
    }
    return false
}

MutateFailed(err) {
    try
        Log("command failed: " err.Message)
    finally
        Finish()
}
