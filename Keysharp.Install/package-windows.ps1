param(
    # Defaults to this machine's architecture. Pass -RuntimeIdentifier explicitly to cross-target,
    # e.g. -RuntimeIdentifier win-arm64 from an x64 box.
    [string] $Configuration = "Release",
    [ValidateSet("", "win-x64", "win-arm64")]
    [string] $RuntimeIdentifier = "",
    [string] $Version = "",
    [switch] $SkipPublish,
    [switch] $SkipMsi
)

$ErrorActionPreference = "Stop"

if (-not $RuntimeIdentifier) {
    $RuntimeIdentifier = switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
        "Arm64" { "win-arm64" }
        default { "win-x64" }
    }
    Write-Host "No -RuntimeIdentifier given; defaulting to this machine's architecture: $RuntimeIdentifier."
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = (Resolve-Path (Join-Path $scriptDir "..")).Path
$solution = Join-Path $root "Keysharp.sln"
$distDir = Join-Path $root "dist"
$publishRoot = Join-Path $distDir "publish\$RuntimeIdentifier"
$stagingDir = Join-Path $distDir "staging\$RuntimeIdentifier"
$packageName = "Keysharp-$RuntimeIdentifier"
$packageDir = Join-Path $stagingDir $packageName
$appDir = Join-Path $packageDir "app"
$zipPath = Join-Path $distDir "$packageName.zip"
$msiPath = Join-Path $distDir "$packageName.msi"
$installerProject = Join-Path $root "Keysharp.Install\windows\wix\Keysharp.Installer.wixproj"
$etoDir = Join-Path (Split-Path -Parent $root) "Eto"
$pathMap = "$root=/_/keysharp"
if (Test-Path $etoDir) {
    $etoDir = (Resolve-Path $etoDir).Path
    $pathMap = "$pathMap%2c$etoDir=/_/Eto"
}

# The MSI platform name WiX expects, which is not spelled the same as the RID.
$msiPlatform = if ($RuntimeIdentifier -eq "win-arm64") { "arm64" } else { "x64" }

function Resolve-KeysharpVersion {
    param([string] $ExplicitVersion)

    if ($ExplicitVersion) {
        return $ExplicitVersion
    }

    $propsPath = Join-Path $root "Directory.Build.props"
    if (Test-Path $propsPath) {
        $props = Get-Content -LiteralPath $propsPath -Raw
        $match = [regex]::Match($props, '<KeysharpVersion[^>]*>([^<]+)</KeysharpVersion>')
        if ($match.Success) {
            return $match.Groups[1].Value.Trim()
        }
    }

    throw "Could not determine KeysharpVersion. Pass -Version explicitly."
}

function Assert-PackagableVersion {
    param([string] $Version)

    # The MSI ProductVersion is major.minor.build with build folded as patch*1000 + revision, and Windows
    # Installer caps those fields at 255 / 255 / 65535. Checked here because the .wixproj cannot: MSBuild
    # evaluates its properties before any target runs, so a malformed version fails while computing the
    # version itself, and an over-range one only surfaces later as ICE24 against the already-folded number.
    if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
        throw "Version must have four numeric parts, for example 0.0.0.16. Got '$Version'."
    }

    $parts = $Version.Split('.')
    $build = ([int] $parts[2]) * 1000 + [int] $parts[3]

    if ([int] $parts[0] -gt 255 -or [int] $parts[1] -gt 255) {
        throw "Version '$Version' exceeds the MSI limit of 255 for the major and minor fields."
    }

    if ($build -gt 65535) {
        throw "Version '$Version' folds to MSI version $($parts[0]).$($parts[1]).$build, and Windows Installer allows at most 65535 in the build field. The patch*1000+revision mapping runs out at patch 65."
    }
}

function Assert-NoLocalPaths {
    param(
        [string] $ScanRoot,
        [string[]] $Patterns
    )

    $files = Get-ChildItem -Path $ScanRoot -Recurse -File -ErrorAction SilentlyContinue
    if (-not $files) {
        return
    }

    $matches = $files | Select-String -SimpleMatch -Pattern $Patterns -List -ErrorAction SilentlyContinue
    if ($matches) {
        $matches | Select-Object -First 20 | ForEach-Object {
            Write-Error "Local absolute path found in $($_.Path): $($_.Pattern)"
        }

        throw "Package payload contains local absolute paths. Rebuild with path mapping before packaging."
    }
}

function Copy-DirectoryContents {
    param(
        [string] $Source,
        [string] $Destination
    )

    if (-not (Test-Path $Source)) {
        throw "Expected publish directory does not exist: $Source"
    }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item -Path (Join-Path $Source "*") -Destination $Destination -Recurse -Force
}

function Normalize-NativeAssets {
	param([string] $AppRoot)

	# Native assets have two different, loader-specific layout requirements:
	#
	#  * PCRE.NET.Native.dll is a NuGet "native" runtime asset listed in the
	#    .deps.json (runtimes/<rid>/native/...). For a RID-specific publish the host
	#    resolves that entry by probing the app ROOT (it strips the
	#    runtimes/<rid>/native prefix), so this DLL must live in the root. Putting it
	#    only under runtimes/<rid>/native fails with
	#    "Unable to load DLL 'PCRE.NET.Native'".
	#
	#  * Scintilla.dll / Lexilla.dll ship as MSBuild "Content" (NOT deps.json native
	#    assets) and Scintilla.NET locates them via a hard-coded relative path:
	#    <appbase>\runtimes\win-<arch>\native\. It never probes the root, so these
	#    must stay under runtimes/<rid>/native/.
	#
	# Merging the Keyview and Keysharp publishes can scatter copies between the root
	# and runtimes/<rid>/native/ (and the Scintilla.NET Content copy also drags in
	# the non-target RIDs). Stage each asset where its loader expects it, then rebuild
	# a clean runtimes tree containing only the target RID.
	$runtimesDir = Join-Path $AppRoot "runtimes"
	$nativeDir = Join-Path $runtimesDir "$RuntimeIdentifier\native"
	$ridAssets = @("Lexilla.dll", "Scintilla.dll")

	# Stash the Scintilla satellite libraries (prefer the target-RID copy).
	$tempNativeDir = Join-Path ([System.IO.Path]::GetTempPath()) "KeysharpNative_$([guid]::NewGuid().ToString('N'))"
	New-Item -ItemType Directory -Path $tempNativeDir -Force | Out-Null
	foreach ($name in $ridAssets) {
		$ridNative = Join-Path $nativeDir $name
		$rootNative = Join-Path $AppRoot $name
		if (Test-Path $ridNative) {
			Copy-Item -Path $ridNative -Destination (Join-Path $tempNativeDir $name) -Force
		}
		elseif (Test-Path $rootNative) {
			Copy-Item -Path $rootNative -Destination (Join-Path $tempNativeDir $name) -Force
		}
	}

	# Ensure PCRE.NET.Native.dll is in the app root.
	$pcreRoot = Join-Path $AppRoot "PCRE.NET.Native.dll"
	$pcreRid = Join-Path $nativeDir "PCRE.NET.Native.dll"
	if ((-not (Test-Path $pcreRoot)) -and (Test-Path $pcreRid)) {
		Copy-Item -Path $pcreRid -Destination $pcreRoot -Force
	}

	# Rebuild runtimes/<rid>/native containing only the target-RID Scintilla libraries.
	if (Test-Path $runtimesDir) {
		Remove-Item -Path $runtimesDir -Recurse -Force
	}
	New-Item -ItemType Directory -Path $nativeDir -Force | Out-Null
	foreach ($name in $ridAssets) {
		$staged = Join-Path $tempNativeDir $name
		if (Test-Path $staged) {
			Move-Item -Path $staged -Destination (Join-Path $nativeDir $name) -Force
		}
		# Drop any stray root copy of the Scintilla libraries (it never loads from root).
		Remove-Item -Path (Join-Path $AppRoot $name) -Force -ErrorAction SilentlyContinue
	}

	Remove-Item -Path $tempNativeDir -Recurse -Force -ErrorAction SilentlyContinue
}

function Relocate-LibraryScripts {
    param([string] $AppRoot)

    # OCR.ks is a pure "#include <OCR>" library (no entry point, no .cks), so it ships in Lib\ rather than
    # Scripts\ so the library-include resolver finds it. WindowSpy.ks/.cks stay in Scripts\ (an app, not a
    # library). This must run before the MSI build: the installer harvests this same staged tree, and the
    # zip is produced from it too.
    $scriptsOcr = Join-Path $AppRoot "Scripts\OCR.ks"
    if (Test-Path $scriptsOcr) {
        $libDir = Join-Path $AppRoot "Lib"
        New-Item -ItemType Directory -Path $libDir -Force | Out-Null
        Move-Item -Path $scriptsOcr -Destination (Join-Path $libDir "OCR.ks") -Force
    }
    # Defensive: OCR is never precompiled, so a stray OCR.cks must not ship.
    Remove-Item -Path (Join-Path $AppRoot "Scripts\OCR.cks") -Force -ErrorAction SilentlyContinue
}

function Assert-PayloadIsShippable {
    param([string] $AppRoot)

    # The MSI harvests this tree wholesale (Keysharp.Install/windows/Payload.wxs) and the zip is compressed
    # from it, so the tree IS the package contents and nothing filters it afterwards. These are the classes
    # of file that used to leak in and are now suppressed at their source; catch a regression here rather
    # than in a release.
    $strays = @()
    $strays += Get-ChildItem -Path $AppRoot -Recurse -Directory -Filter "refs" -ErrorAction SilentlyContinue
    $strays += Get-ChildItem -Path $AppRoot -Recurse -File -Include "*.pdb", "*.icns" -ErrorAction SilentlyContinue

    if ($strays) {
        # Write-Host, not Write-Error: $ErrorActionPreference is Stop, so Write-Error terminates on the first
        # item and the explanatory throw below would never be reached - leaving one filename and no guidance
        # for what is often a hundred-plus files.
        $strays | ForEach-Object { Write-Host "  unshippable artefact staged into the package: $($_.FullName)" }
        throw "Staged payload contains $($strays.Count) build artefact(s) that must not ship. See PreserveCompilationContext / DebugType in Directory.Build.props and the csproj files."
    }

    # WindowSpy.cks is produced by the PrecompileBundledScripts target, which cannot run on a
    # cross-architecture publish (an arm64 host will not execute on an x64 build machine). That is tolerated
    # - the installer falls back to the .ks - but it is worth saying out loud, because it silently costs
    # startup time for everyone on that architecture.
    if (-not (Test-Path (Join-Path $AppRoot "Scripts\WindowSpy.cks"))) {
        Write-Warning "Scripts\WindowSpy.cks was not produced (cross-architecture publish?); the Window Spy shortcut will run WindowSpy.ks and compile it on every launch."
    }
}

function Assert-MsiMatchesPayload {
    param(
        [string] $MsiPath,
        [string] $AppRoot
    )

    # The MSI harvests $AppRoot wholesale and the zip is compressed from it, so the two must contain the
    # same files. Verified explicitly rather than assumed, because the ways this can drift are all silent:
    # an incremental link that missed an added file, a harvest that stopped matching, or a payload written
    # after the build. README.md is the one installed file that is authored rather than harvested.
    $expected = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    Get-ChildItem -LiteralPath $AppRoot -Recurse -File | ForEach-Object { [void] $expected.Add($_.Name) }
    [void] $expected.Add("README.md")

    $actual = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db = $null
    $view = $null
    try {
        $db = $installer.OpenDatabase($MsiPath, 0)
        $view = $db.OpenView('SELECT `FileName` FROM `File`')
        $view.Execute()
        while ($null -ne ($record = $view.Fetch())) {
            # FileName is "SHORTNAME|Long Name" when a short name was generated; keep the long one.
            $name = [string] $record.StringData(1)
            if ($name -match '\|') { $name = $name.Split('|')[1] }
            [void] $actual.Add($name)
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
        }
        $view.Close()
    }
    finally {
        if ($null -ne $view) { [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) }
        if ($null -ne $db) { [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($db) }
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
        [GC]::Collect()
        [GC]::WaitForPendingFinalizers()
    }

    $missing = @($expected | Where-Object { -not $actual.Contains($_) } | Sort-Object)
    $extra = @($actual | Where-Object { -not $expected.Contains($_) } | Sort-Object)

    if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
        $missing | ForEach-Object { Write-Host "  staged but MISSING from the MSI: $_" }
        $extra | ForEach-Object { Write-Host "  in the MSI but not staged: $_" }
        throw "The MSI payload does not match the staged tree ($($missing.Count) missing, $($extra.Count) extra). The zip is built from that tree, so the two artefacts would ship different contents."
    }

    Write-Host ("  MSI payload matches the staged tree ({0} files)." -f $actual.Count)
}

function Assert-MsiSequencing {
    param([string] $MsiPath)

    # Cheap invariants on the built MSI, checked because none of these show up as a build error - WiX and
    # ICE validation pass happily, and the failure only appears when a user runs the thing.
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db = $null
    $view = $null
    try {
        $db = $installer.OpenDatabase($MsiPath, 0)

        $seq = @{}
        $view = $db.OpenView('SELECT `Action`,`Sequence` FROM `InstallExecuteSequence`')
        $view.Execute()
        # IntegerData, not [int]StringData: Sequence is an integer column and is nullable, and a null would
        # otherwise surface as an unhelpful "cannot convert value to System.Int32".
        while ($null -ne ($record = $view.Fetch())) {
            $seq[[string]$record.StringData(1)] = [int]$record.IntegerData(2)
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
        }
        $view.Close()

        foreach ($required in @('InstallInitialize', 'InstallValidate')) {
            if (-not $seq.ContainsKey($required)) {
                throw "InstallExecuteSequence has no $required action; the package is malformed."
            }
        }

        # The close-applications action is what makes an in-use upgrade or uninstall work at all, so its
        # absence is itself a failure - otherwise checks 1 and 2 below would pass vacuously on a package
        # that had silently lost util:CloseApplication or the WixToolset.Util reference.
        $closeActions = @($seq.Keys | Where-Object { $_ -like 'Wix*CloseApplications*' -and $_ -notlike '*Deferred*' })
        if ($closeActions.Count -eq 0) {
            throw "No WixCloseApplications action is scheduled. An upgrade or uninstall with Keysharp running would fail to replace or delete its files."
        }

        # 1. Custom actions that schedule deferred work must sit inside the transaction. A deferred action
        #    cannot be written to the execution script before InstallInitialize opens it, and the result is
        #    error 2762 ("cannot write script record, transaction not started") on every uninstall.
        $initialize = $seq['InstallInitialize']
        foreach ($action in $closeActions) {
            if ($seq[$action] -lt $initialize) {
                throw "$action is sequenced at $($seq[$action]), before InstallInitialize ($initialize). It schedules a deferred action, so this fails every uninstall with error 2762."
            }
        }

        # 2. Running processes must be closed before anything removes files. RemoveExistingProducts takes an
        #    older build apart on an upgrade, and RemoveFiles deletes on uninstall; both fail on a locked file.
        foreach ($action in $closeActions) {
            foreach ($after in @('RemoveExistingProducts', 'RemoveFiles')) {
                if ($seq.ContainsKey($after) -and $seq[$action] -gt $seq[$after]) {
                    throw "$action is sequenced at $($seq[$action]), after $after ($($seq[$after])). Files are still locked when $after runs."
                }
            }
        }

        # 3. Property-driven launch conditions need their AppSearch to have run first.
        if ($seq.ContainsKey('LaunchConditions') -and $seq.ContainsKey('AppSearch') -and $seq['AppSearch'] -gt $seq['LaunchConditions']) {
            throw "AppSearch ($($seq['AppSearch'])) runs after LaunchConditions ($($seq['LaunchConditions'])); any property-based launch condition would evaluate empty."
        }

        # 4. A per-machine package must not declare that it never needs elevation, or Windows Installer
        #    silently refuses to prompt and the install fails with no UAC dialog and no fallback.
        $summary = $db.SummaryInformation(0)
        if (([int]$summary.Property(15) -band 8) -ne 0) {
            throw "The summary WordCount has the 'elevated privileges not required' bit set, so Setup will never prompt for UAC and a per-machine install cannot write to Program Files."
        }
    }
    finally {
        # Every RCW has to go, and the finalizers have to run, before this function returns. A leaked view or
        # record keeps the database - and therefore the .msi file handle - open, so a second packaging run in
        # the same console cannot overwrite the file it just inspected.
        if ($null -ne $view) { [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) }
        if ($null -ne $db) { [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($db) }
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
        [GC]::Collect()
        [GC]::WaitForPendingFinalizers()
    }

    Write-Host "  MSI sequencing invariants OK."
}

function Compress-WindowsPackage {
    param(
        [string] $SourceRoot,
        [string] $DestinationPath
    )

    if (-not (Test-Path $SourceRoot)) {
        throw "Expected package app directory does not exist: $SourceRoot"
    }

    Remove-Item -LiteralPath $DestinationPath -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path (Join-Path $SourceRoot "*") -DestinationPath $DestinationPath -Force
}

Push-Location $root
try {
    $Version = Resolve-KeysharpVersion $Version
    Assert-PackagableVersion $Version
    Write-Host "Packaging Keysharp version $Version ($RuntimeIdentifier)."

    if (-not $SkipPublish) {
        Write-Host "Publishing $solution ($Configuration, $RuntimeIdentifier)..."
        # Publish does not prune, so a stale tree would keep shipping files a newer build no longer emits.
        $publishProjectDirs = @(
            (Join-Path $publishRoot "Keysharp"),
            (Join-Path $publishRoot "Keyview")
        )
        Remove-Item -Path $publishProjectDirs -Recurse -Force -ErrorAction SilentlyContinue

        dotnet publish $solution -c $Configuration -r $RuntimeIdentifier `
            -p:KeysharpVersion=$Version `
            -p:Deterministic=true `
            -p:ContinuousIntegrationBuild=true `
            -p:PathMap=$pathMap
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE."
        }
    }

    Write-Host "Staging package at $packageDir..."
    Remove-Item -Path $packageDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $appDir -Force | Out-Null

    Copy-DirectoryContents (Join-Path $publishRoot "Keyview") $appDir
    Copy-DirectoryContents (Join-Path $publishRoot "Keysharp") $appDir
    Normalize-NativeAssets $appDir
    Relocate-LibraryScripts $appDir

    $localPathPatterns = @($root)
    if ($env:USERPROFILE) {
        $localPathPatterns += $env:USERPROFILE
    }
    if ($etoDir -and (Test-Path $etoDir)) {
        $localPathPatterns += $etoDir
    }

    Write-Host "Checking staged files for local absolute paths..."
    Assert-NoLocalPaths $appDir $localPathPatterns
    Assert-PayloadIsShippable $appDir

    if (-not $SkipMsi) {
        # WiX v5 is restored from NuGet by the .wixproj, so this needs nothing on the machine beyond the
        # .NET SDK - no Visual Studio, no devenv.com, and no Visual Studio Installer Projects extension.
        # The MSI is built from the staged tree above, which it harvests wholesale.
        #
        # -t:Rebuild is not optional. The harvested file set is captured at link time, and incremental build
        # does not treat a file ADDED to or REMOVED from the payload directory as an input change: it skips
        # the link in under a second and leaves the previous MSI in place, byte for byte. The zip is
        # recompressed from the same tree unconditionally, so the two artefacts silently disagree - a newly
        # added dependency ships in the zip and is missing from the installer.
        Write-Host "Building MSI ($msiPlatform) from $appDir..."
        dotnet build $installerProject -t:Rebuild -c $Configuration -p:Platform=$msiPlatform -p:PayloadDir=$appDir -p:KeysharpVersion=$Version --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "Installer build failed with exit code $LASTEXITCODE."
        }

        Assert-MsiMatchesPayload $msiPath $appDir

        Assert-MsiSequencing $msiPath
    }

    Write-Host "Creating zip package at $zipPath..."
    Compress-WindowsPackage $appDir $zipPath

    if (-not $SkipMsi) {
        Write-Host "Windows package ready at $msiPath"
    }
    Write-Host "Windows zip ready at $zipPath"
}
finally {
    Pop-Location
}
