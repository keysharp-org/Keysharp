param(
    # Defaults to this machine's architecture. Pass -RuntimeIdentifier explicitly to cross-target,
    # e.g. -RuntimeIdentifier win-arm64 from an x64 box.
    [string] $Configuration = "Release",
    [ValidateSet("", "win-x64", "win-arm64")]
    [string] $RuntimeIdentifier = "",
    [string] $Version = "",
    [switch] $SkipPublish,
    [switch] $SkipMsi,

    # MSIX (Microsoft Store / sideload) is opt-in and EXCLUSIVE: -Msix produces the .msix and nothing
    # else. It needs a self-contained publish (the Store cannot assume a machine-wide .NET) that the zip
    # and MSI must not be built from, and a Store submission has no use for either of them. A plain run
    # produces the .zip and .msi; run the script twice to get all three.
    [switch] $Msix,

    # Package identity. No publisher is stored in this repository: a signed build reads it from the
    # signing certificate's own subject, which is the only value that can sideload, and an explicit
    # -Publisher that disagrees with the certificate is rejected. A Store upload needs the exact
    # values Partner Center shows under Product identity (Publisher is a CN=<GUID>) with -SkipSign,
    # since the Store re-signs the package itself.
    [string] $IdentityName = "Keysharp",
    [string] $Publisher = "",
    [string] $PublisherDisplayName = "Keysharp",

    # The bundled local test certificate is used when no path is supplied. Use -SkipSign for an
    # unsigned Store upload; the Store replaces any existing signature in a signed upload anyway.
    [switch] $SkipSign,
    [string] $SignCertPath = "",
    [string] $SignCertPassword = "",
    [string] $SignCertPasswordPath = "",

    # Countersigns the signature so it keeps validating past the certificate's expiry. Pass "" to sign
    # without one, which is what an offline build needs.
    [string] $TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

$msixOnlyParams = @("IdentityName", "Publisher", "PublisherDisplayName", "SkipSign", "SignCertPath",
                    "SignCertPassword", "SignCertPasswordPath", "TimestampUrl")

if ($Msix) {
    # -Msix never builds the MSI, so -SkipMsi alongside it is a misunderstanding of what the run produces
    # rather than a harmless no-op. Same for asking to sign and to skip signing at once.
    if ($PSBoundParameters.ContainsKey("SkipMsi")) {
        throw "-SkipMsi is meaningless with -Msix: the MSIX run builds neither the MSI nor the zip."
    }
    if ($SkipSign -and ($SignCertPath -or $SignCertPassword -or $SignCertPasswordPath)) {
        throw "-SkipSign conflicts with the certificate argument(s) also given. Drop one."
    }
}
else {
    # Fail rather than ignore: a caller passing signing or identity arguments plainly means to build an
    # MSIX, and silently producing only the zip and MSI would look like those arguments had been honoured.
    $msixOnly = $msixOnlyParams | Where-Object { $PSBoundParameters.ContainsKey($_) }
    if ($msixOnly) {
        throw "-$($msixOnly -join ', -'): MSIX-only argument(s), and the MSIX package is not built by default. Add -Msix."
    }
}

if (-not $RuntimeIdentifier) {
    $RuntimeIdentifier = switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
        "Arm64" { "win-arm64" }
        default { "win-x64" }
    }
    Write-Host "No -RuntimeIdentifier given; defaulting to this machine's architecture: $RuntimeIdentifier."
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = (Resolve-Path (Join-Path $scriptDir "..")).Path
$distDir = Join-Path $root "dist"
$publishRoot = Join-Path $distDir "publish\$RuntimeIdentifier"
$stagingDir = Join-Path $distDir "staging\$RuntimeIdentifier"
$packageName = "Keysharp-$RuntimeIdentifier"
$packageDir = Join-Path $stagingDir $packageName
$appDir = Join-Path $packageDir "app"
$zipPath = Join-Path $distDir "$packageName.zip"
$msiPath = Join-Path $distDir "$packageName.msi"
$publishProjects = @(
    (Join-Path $root "Keysharp\Keysharp.csproj"),
    (Join-Path $root "Keyview\Keyview.csproj")
)
$installerProject = Join-Path $root "Keysharp.Install\windows\wix\Keysharp.Installer.wixproj"
$payloadProject = Join-Path $root "Keysharp.Install\payload\Keysharp.Payload.proj"
$etoDir = Join-Path (Split-Path -Parent $root) "Eto"
$pathMap = "$root=/_/keysharp"
if (Test-Path $etoDir) {
    $etoDir = (Resolve-Path $etoDir).Path
    $pathMap = "$pathMap%2c$etoDir=/_/Eto"
}

# The MSI platform name WiX expects, which is not spelled the same as the RID.
$msiPlatform = if ($RuntimeIdentifier -eq "win-arm64") { "arm64" } else { "x64" }
$installerIntermediateDir = Join-Path $root "obj\Keysharp.Installer\$msiPlatform"

# MSIX outputs live in DELIBERATELY separate trees from the zip/MSI ones above: that publish is
# self-contained (the Store cannot assume a machine-wide .NET), and mixing it into dist\publish would
# silently change what the MSI harvest and the zip ship.
$msixPublishRoot = Join-Path $distDir "publish-msix\$RuntimeIdentifier"
$msixPkgDir = Join-Path $distDir "staging-msix\$RuntimeIdentifier\pkg"
$msixPath = Join-Path $distDir "$packageName.msix"
$msixDir = Join-Path $scriptDir "windows\msix"
$msixArch = $msiPlatform

$localPathPatterns = @($root)
if ($env:USERPROFILE) {
    $localPathPatterns += $env:USERPROFILE
}
if ($etoDir -and (Test-Path $etoDir)) {
    $localPathPatterns += $etoDir
}

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

    # The MSI ProductVersion is major.minor.build, and Windows Installer caps those fields at
    # 255 / 255 / 65535. A three-part version - the scheme from 0.0.1 on - is already that shape; a
    # legacy four-part 0.0.0.N folds patch and revision together as patch*1000 + revision, which is what
    # every vdproj-era release did. Both mappings are mirrored in Keysharp.Installer.wixproj and in
    # Convert-ToMsixVersion, so all three must change together.
    #
    # Checked here because the .wixproj cannot: MSBuild evaluates its properties before any target runs,
    # so a malformed version fails while computing the version itself, and an over-range one only
    # surfaces later as ICE24 against the already-folded number.
    if ($Version -notmatch '^\d+\.\d+(\.\d+){0,2}$') {
        throw "Version must have two to four numeric parts, for example 0.0.1 or 0.0.0.16. Got '$Version'."
    }

    $parts = $Version.Split('.')
    $build = if ($parts.Count -eq 4) { ([int] $parts[2]) * 1000 + [int] $parts[3] }
             elseif ($parts.Count -eq 3) { [int] $parts[2] }
             else { 0 }

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

function New-StagedAppTree {
    param(
        [string] $PublishRoot,
        [string] $AppRoot
    )

    # Both packages are the same merged tree: the MSI harvests it wholesale, the zip is compressed from
    # it, and makeappx packs it. Keysharp is copied last so its files win any collision with Keyview's.
    Write-Host "Staging package at $AppRoot..."
    Remove-Item -Path $AppRoot -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $AppRoot -Force | Out-Null

    Copy-DirectoryContents (Join-Path $PublishRoot "Keyview") $AppRoot
    Copy-DirectoryContents (Join-Path $PublishRoot "Keysharp") $AppRoot
    Normalize-NativeAssets $AppRoot

    Write-Host "Checking staged files for local absolute paths..."
    Assert-NoLocalPaths $AppRoot $localPathPatterns
    Assert-PayloadIsShippable $AppRoot
}

function Assert-PayloadIsShippable {
    param([string] $AppRoot)

    # The MSI harvests this tree wholesale (Keysharp.Install/windows/wix/Payload.wxs), the zip is compressed
    # from it, and makeappx packs the MSIX staging tree the same way, so the tree IS the package contents
    # and nothing filters it afterwards. These are the classes of file that used to leak in and are now
    # suppressed at their source; catch a regression here rather than in a release.
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

    # A bare launch runs Keysharp.cks - the Dash - through the ordinary <exe-name> probe. Neither it nor
    # the Keysharp.ks fallback present means the Start-menu shortcut opens an error dialog instead.
    if (-not (Test-Path (Join-Path $AppRoot "Keysharp.cks"))) {
        if (Test-Path (Join-Path $AppRoot "Keysharp.ks")) {
            Write-Warning "Keysharp.cks was not produced; the Dash ships as Keysharp.ks and is compiled in memory on every launch. Expected on a cross-architecture publish; otherwise the precompile step failed - check the publish output for 'Could not precompile'."
        }
        else {
            throw "The staged payload has neither Keysharp.cks nor Keysharp.ks. A launch with no script would open an error dialog instead of the Dash."
        }
    }

    # WindowSpy.cks is produced by the same target, and the same cross-architecture caveat applies. That is
    # tolerated - the Dash falls back to the .ks - but it is worth saying out loud, because it silently
    # costs startup time for everyone on that architecture. A same-architecture publish reaching this point
    # means the step itself failed: the publish log carries both the host's error output and
    # PrecompileBundledScripts' own warning naming the script.
    if (-not (Test-Path (Join-Path $AppRoot "Scripts\WindowSpy.cks"))) {
        Write-Warning "Scripts\WindowSpy.cks was not produced; the Dash's Window Spy card will run WindowSpy.ks and compile it on every launch. Expected on a cross-architecture publish; otherwise the precompile step failed - check the publish output for 'Could not precompile'."
    }

    foreach ($required in @("Keysharp.exe", "Keyview.exe")) {
        if (-not (Test-Path (Join-Path $AppRoot $required))) {
            throw "The staged payload is missing $required."
        }
    }
}

function Invoke-Publish {
    param(
        [string] $PublishRoot,
        [switch] $SelfContained
    )

    Write-Host "Publishing Keysharp and Keyview ($Configuration, $RuntimeIdentifier$(if ($SelfContained) { ', self-contained' }))..."
    # Publish does not prune, so a stale tree would keep shipping files a newer build no longer emits.
    Remove-Item -Path (Join-Path $PublishRoot "Keysharp"), (Join-Path $PublishRoot "Keyview") -Recurse -Force -ErrorAction SilentlyContinue

    # Keyview has a non-assembly project reference to Keysharp. Publishing the solution lets MSBuild
    # reach Keysharp both directly and through that reference, and the two invocations can write the
    # same publish directory concurrently. Publish the two deliverables in dependency order instead.
    foreach ($publishProject in $publishProjects) {
        $projectName = [System.IO.Path]::GetFileNameWithoutExtension($publishProject)
        $selfContainedArgs = if ($SelfContained) { @("--self-contained", "true", "-o", (Join-Path $PublishRoot $projectName)) } else { @() }
        dotnet publish $publishProject -c $Configuration -r $RuntimeIdentifier `
            @selfContainedArgs `
            -p:KeysharpVersion=$Version `
            -p:Deterministic=true `
            -p:ContinuousIntegrationBuild=true `
            -p:PathMap=$pathMap
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed for $publishProject with exit code $LASTEXITCODE."
        }
    }

    # The Dash, its template, the demos and every .cks: install payload, so Keysharp.csproj does not carry
    # it. Runs against the just-published host, which is what makes each .cks match this build.
    Write-Host "Staging install payload..."
    # Quoted as one token: an unquoted (Join-Path ...) becomes a separate argument, which MSBuild then
    # reads as a second project, and a space in the path would split it.
    $payloadDir = Join-Path $PublishRoot "Keysharp"
    dotnet msbuild $payloadProject "-p:PayloadDir=$payloadDir" "-p:KpmRid=$RuntimeIdentifier" --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Staging the install payload failed with exit code $LASTEXITCODE."
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

# ==================================== MSIX ====================================
# Everything below runs only with -Msix.

function Convert-ToMsixVersion {
    param([string] $Version)

    # MSIX identity versions are Major.Minor.Build.Revision with Revision RESERVED by the Store: a
    # submission must have .0 there. Two- and three-part versions - the scheme from 0.0.1 on, and the
    # only one the Store ever sees - just gain the missing fields. A legacy four-part 0.0.0.N folds the
    # same way the MSI ProductVersion does (build = patch*1000 + revision), UNCONDITIONALLY: passing
    # 0.0.1.0 straight through because its revision is already 0 would place it below 0.0.0.16's
    # 0.0.16.0 and invert the very ordering the fold exists to preserve.
    #
    # The scheme change itself is a deliberate step DOWN: 0.0.0.16 maps to 0.0.16.0, above the first
    # Store version 0.0.1 (0.0.1.0). That costs nothing, because no four-part version was ever
    # published as an MSIX - but a machine that SIDELOADED one must uninstall it before installing
    # 0.0.1, since Windows will not replace a package with a lower version.
    $parts = $Version.Split('.')
    foreach ($part in $parts) {
        if ($part -notmatch '^\d+$') {
            throw "Version '$Version' is not numeric."
        }
    }

    $msix = switch ($parts.Count) {
        2 { "$Version.0.0" }
        3 { "$Version.0" }
        4 { "$($parts[0]).$($parts[1]).$(([int] $parts[2]) * 1000 + [int] $parts[3]).0" }
        default { throw "Version '$Version' must have 2-4 numeric parts." }
    }

    foreach ($part in $msix.Split('.')) {
        if ([int] $part -gt 65535) {
            throw "MSIX version '$msix' (from '$Version') exceeds the 65535 per-field limit."
        }
    }

    return $msix
}

function Find-SdkTool {
    param([string] $Name)

    # makeappx/makepri/signtool live in the Windows SDK, versioned bin dirs, per host architecture.
    $bin = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    $hostArch = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq "Arm64") { "arm64" } else { "x64" }
    if (Test-Path $bin) {
        $tool = Get-ChildItem -Path $bin -Directory -Filter "10.*" |
            Sort-Object { [version] $_.Name } -Descending |
            ForEach-Object { Join-Path $_.FullName "$hostArch\$Name" } |
            Where-Object { Test-Path $_ } |
            Select-Object -First 1
        if ($tool) {
            return $tool
        }
    }

    throw "$Name was not found under '$bin'. Install a Windows 10/11 SDK to build MSIX packages."
}

function Get-LogoSource {
    param([int] $DrawSize)

    # Keysharp.ico carries hand-tuned 16/32/48/64/128 frames; a bicubic squeeze of the 256 px PNG down to
    # 16 px is visibly mushier than the frame drawn for that size. Take the frame only on an EXACT match -
    # Icon(path, w, h) silently returns the nearest one otherwise, and e.g. 32 -> 24 loses more than
    # 256 -> 24 does. Caller disposes.
    if ($script:LogoIcoPath -and (Test-Path -LiteralPath $script:LogoIcoPath)) {
        $icon = New-Object System.Drawing.Icon($script:LogoIcoPath, $DrawSize, $DrawSize)
        try {
            if ($icon.Width -eq $DrawSize -and $icon.Height -eq $DrawSize) {
                return $icon.ToBitmap()
            }
        }
        finally { $icon.Dispose() }
    }

    return [System.Drawing.Image]::FromFile($script:LogoPngPath)
}

function New-ScaledPng {
    param(
        [string] $Destination,
        [int] $Size,
        # Fraction of the canvas the artwork occupies, centred, the rest transparent. Tiles want padding
        # (see New-MsixVisualAssets); icons are drawn edge to edge.
        [double] $Fill = 1.0
    )

    $drawSize = [int][math]::Round($Size * $Fill)
    $offset = [int][math]::Round(($Size - $drawSize) / 2.0)
    $img = Get-LogoSource -DrawSize $drawSize
    try {
        if ($drawSize -gt $img.Width) {
            # Not fatal - the asset is still produced - but it is a real quality loss on exactly the tile
            # sizes a Store listing shows largest, and it is invisible unless said out loud.
            $script:UpscaledAssets += "$([System.IO.Path]::GetFileName($Destination)) ($($img.Width)px -> ${drawSize}px)"
        }

        $bmp = New-Object System.Drawing.Bitmap($Size, $Size)
        try {
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            try {
                $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $g.Clear([System.Drawing.Color]::Transparent)
                $g.DrawImage($img, $offset, $offset, $drawSize, $drawSize)
            }
            finally { $g.Dispose() }
            $bmp.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $bmp.Dispose() }
    }
    finally { $img.Dispose() }
}

function Resolve-SigningCertificate {
    # Returns the certificate path, password and SUBJECT to sign with, or an empty path for an unsigned
    # package. The subject is what the manifest's Publisher has to be; see Resolve-Publisher.
    if ($SkipSign) {
        return @{ Path = ""; Password = ""; Subject = "" }
    }

    $certPath = $SignCertPath
    if (-not $certPath) {
        $localSignCertPath = Join-Path $msixDir "assets\SignCert.pfx"
        if (Test-Path -LiteralPath $localSignCertPath) {
            $certPath = $localSignCertPath
        }
    }

    if (-not $certPath) {
        return @{ Path = ""; Password = ""; Subject = "" }
    }

    if (-not (Test-Path -LiteralPath $certPath -PathType Leaf)) {
        throw "Signing certificate does not exist: $certPath"
    }

    $certPath = (Resolve-Path -LiteralPath $certPath).Path
    $password = $SignCertPassword
    $passwordPath = $SignCertPasswordPath
    if (-not $password -and -not $passwordPath) {
        $localPasswordPath = Join-Path (Split-Path -Parent $certPath) "SignCertPasswd.txt"
        if (Test-Path -LiteralPath $localPasswordPath -PathType Leaf) {
            $passwordPath = $localPasswordPath
        }
    }
    if ($passwordPath) {
        if (-not (Test-Path -LiteralPath $passwordPath -PathType Leaf)) {
            throw "Signing certificate password file does not exist: $passwordPath"
        }
        $password = (Get-Content -Raw -Encoding UTF8 -LiteralPath $passwordPath).Trim()
    }

    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certPath, $password)
    try {
        $subject = $cert.Subject
    }
    finally { $cert.Dispose() }

    return @{ Path = $certPath; Password = $password; Subject = $subject }
}

function Resolve-Publisher {
    param([hashtable] $Signing)

    # The manifest's Publisher and the signing certificate's subject must be identical or Windows
    # refuses to install the package, so the certificate is the source of truth and nothing about the
    # publisher is stored in this repository.
    if (-not $Publisher) {
        if ($Signing.Subject) {
            return $Signing.Subject
        }

        throw "-Publisher is required when the package is not signed. Pass the exact value Partner Center shows under Product identity (a CN=<GUID>), or drop -SkipSign so the signing certificate's subject supplies it."
    }

    if ($Signing.Subject -and $Publisher -ne $Signing.Subject) {
        throw "-Publisher '$Publisher' does not match the signing certificate's subject '$($Signing.Subject)'. Sideloading needs them identical; for a Store upload carrying a Partner Center identity, add -SkipSign and let the Store sign."
    }

    return $Publisher
}

function New-MsixVisualAssets {
    param([string] $PkgDir)

    Write-Host "Generating tile/icon assets from assets\Keysharp.ico + Keysharp.png..."
    try {
        Add-Type -AssemblyName System.Drawing -ErrorAction Stop
    }
    catch {
        throw "System.Drawing is unavailable in this PowerShell host ($($PSVersionTable.PSEdition) $($PSVersionTable.PSVersion)), so the tile assets cannot be generated. Run the MSIX build from Windows PowerShell 5.1."
    }

    $assetsDir = Join-Path $PkgDir "Assets"
    New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null
    $script:LogoPngPath = Join-Path $root "assets\Keysharp.png"
    $script:LogoIcoPath = Join-Path $root "assets\Keysharp.ico"
    if (-not (Test-Path -LiteralPath $script:LogoPngPath)) {
        throw "The logo master is missing: $script:LogoPngPath"
    }
    $script:UpscaledAssets = @()

    # Fill is the fraction of the tile the artwork covers. Windows' tile guidance puts the icon at roughly
    # two thirds of a Square150x150 tile with transparent padding around it; drawing it edge to edge both
    # looks oversized next to every other tile AND forces a 600 px draw at scale-400, which the 256 px
    # master cannot supply. At 0.66 only scale-400 upscales at all, and by 1.6x instead of 2.3x.
    $logoSpecs = @(
        @{ Base = "StoreLogo";         Size = 50;  Fill = 1.0 },
        @{ Base = "Square150x150Logo"; Size = 150; Fill = 0.66 },
        @{ Base = "Square44x44Logo";   Size = 44;  Fill = 1.0 },
        @{ Base = "FileLogo";          Size = 64;  Fill = 1.0 }
    )
    foreach ($spec in $logoSpecs) {
        foreach ($scale in @(100, 125, 150, 200, 400)) {
            $px = [int][math]::Round($spec.Size * $scale / 100.0)
            New-ScaledPng -Destination (Join-Path $assetsDir "$($spec.Base).scale-$scale.png") -Size $px -Fill $spec.Fill
        }
    }
    # Taskbar/Start pick targetsize variants of the 44x44 logo when present; unplated avoids the
    # accent-colored backplate behind the icon.
    foreach ($ts in @(16, 24, 32, 48, 256)) {
        New-ScaledPng -Destination (Join-Path $assetsDir "Square44x44Logo.targetsize-$ts.png") -Size $ts
        New-ScaledPng -Destination (Join-Path $assetsDir "Square44x44Logo.targetsize-${ts}_altform-unplated.png") -Size $ts
    }

    if ($script:UpscaledAssets) {
        Write-Warning "Drawn above the 256px master, so these are soft: $($script:UpscaledAssets -join ', '). Render assets\Keysharp.svg to a >=600px PNG over assets\Keysharp.png to fix."
    }
}

function New-MsixPackage {
    param(
        [string] $MakeAppx,
        [string] $MakePri,
        [hashtable] $Signing,
        [string] $MsixVersion,
        [string] $Publisher
    )

    Write-Host "Packaging Keysharp $Version as MSIX $MsixVersion ($RuntimeIdentifier, identity '$IdentityName', publisher '$Publisher')."

    if (-not $SkipPublish) {
        Invoke-Publish -PublishRoot $msixPublishRoot -SelfContained
    }

    New-StagedAppTree -PublishRoot $msixPublishRoot -AppRoot $msixPkgDir
    New-MsixVisualAssets $msixPkgDir

    # --- manifest -------------------------------------------------------------------------------
    # -Encoding UTF8 explicitly: the template is ASCII today, but PS 5.1 would read a BOM-less
    # UTF-8 file as ANSI and silently mangle any non-ASCII character into the shipped manifest.
    $manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $msixDir "AppxManifest.xml")
    $manifest = $manifest.Replace("__IDENTITY_NAME__", $IdentityName)
    $manifest = $manifest.Replace("__PUBLISHER__", $Publisher)
    $manifest = $manifest.Replace("__PUBLISHER_DISPLAY__", $PublisherDisplayName)
    $manifest = $manifest.Replace("__VERSION__", $MsixVersion)
    $manifest = $manifest.Replace("__ARCH__", $msixArch)
    if ($manifest -match '__[A-Z_]+__') {
        throw "AppxManifest.xml still contains an unreplaced placeholder after token substitution."
    }
    $manifestPath = Join-Path $msixPkgDir "AppxManifest.xml"
    [System.IO.File]::WriteAllText($manifestPath, $manifest, (New-Object System.Text.UTF8Encoding($false)))

    # --- resources.pri, pack, sign --------------------------------------------------------------
    # The manifest's Assets\*.png references resolve through the resource index, which is also what
    # serves the right scale variant per monitor DPI.
    Write-Host "Indexing resources (makepri)..."
    $priConfig = Join-Path (Split-Path -Parent $msixPkgDir) "priconfig.xml"   # outside pkg so it does not ship
    Remove-Item -Path $priConfig, (Join-Path $msixPkgDir "resources.pri") -Force -ErrorAction SilentlyContinue
    # One default value per qualifier type only ("Invalid qualifier: Scale" otherwise); the
    # scale-NNN variants are indexed from their file names regardless of the default here.
    & $MakePri createconfig /cf $priConfig /dq lang-en-US_scale-100 /o
    if ($LASTEXITCODE -ne 0) {
        throw "makepri createconfig failed with exit code $LASTEXITCODE."
    }

    # createconfig's default template carries <packaging><autoResourcePackage qualifier="Scale"/>, which
    # makes `makepri new` SPLIT the index: resources.pri keeps scale-100 and the 125/150/200/400
    # candidates go to sibling resources.scale-NNN.pri files. Those are resource PACKAGES - only a
    # .msixbundle loads them. In a single .msix they are dead payload and every tile renders from the
    # scale-100 asset, i.e. blurry on every HiDPI display. Drop the node so one resources.pri holds
    # every candidate.
    $priXml = [xml](Get-Content -Raw -Encoding UTF8 -LiteralPath $priConfig)
    $packagingNode = $priXml.resources.SelectSingleNode("packaging")
    if ($packagingNode) {
        $null = $priXml.resources.RemoveChild($packagingNode)
        $priXml.Save($priConfig)
    }

    & $MakePri new /pr $msixPkgDir /cf $priConfig /of (Join-Path $msixPkgDir "resources.pri") /mn $manifestPath /o
    if ($LASTEXITCODE -ne 0) {
        throw "makepri new failed with exit code $LASTEXITCODE."
    }

    # Belt and braces on the above: a split index would ship candidates the package cannot reach.
    $splitPri = @(Get-ChildItem -LiteralPath $msixPkgDir -Filter "resources.*.pri" -ErrorAction SilentlyContinue)
    if ($splitPri) {
        throw "makepri split the index into $($splitPri.Count) resource-package file(s) ($($splitPri.Name -join ', ')). A single .msix only loads resources.pri, so those candidates would be unreachable."
    }

    Write-Host "Packing $msixPath..."
    Remove-Item -Path $msixPath -Force -ErrorAction SilentlyContinue
    & $MakeAppx pack /d $msixPkgDir /p $msixPath /o
    if ($LASTEXITCODE -ne 0) {
        throw "makeappx pack failed with exit code $LASTEXITCODE."
    }

    if ($Signing.Path) {
        $signtool = Find-SdkTool "signtool.exe"
        Write-Host "Signing with $($Signing.Path)..."
        $signArgs = @('sign', '/fd', 'SHA256', '/f', $Signing.Path)
        if ($TimestampUrl) {
            # Without a countersigned timestamp the signature stops validating the day the certificate
            # expires, which for a sideloaded package means it stops installing. The Store re-signs, so
            # this only matters off-Store - but that is the case the bundled test certificate serves.
            $signArgs += @('/tr', $TimestampUrl, '/td', 'SHA256')
        }
        if ($Signing.Password) {
            # signtool takes the PFX password only as an argument, so it is visible to anything that can
            # read this process's command line for the duration of the call. Nothing better exists short
            # of importing the certificate into a store and signing by thumbprint, which leaves machine
            # state behind; prefer -SignCertPasswordPath so at least it is not in shell history.
            $signArgs += @('/p', $Signing.Password)
        }
        $signArgs += $msixPath
        & $signtool @signArgs
        if ($LASTEXITCODE -ne 0) {
            # An unsigned .msix sitting at the expected path looks exactly like a finished one.
            Remove-Item -LiteralPath $msixPath -Force -ErrorAction SilentlyContinue
            throw "signtool failed with exit code $LASTEXITCODE; the unsigned package was deleted. Does the certificate subject match -Publisher '$Publisher'$(if ($TimestampUrl) { ", and is $TimestampUrl reachable (pass -TimestampUrl '' to sign without a timestamp)" })?"
        }
    }

    $sizeMb = [math]::Round((Get-Item $msixPath).Length / 1MB, 1)
    Write-Host "MSIX ready at $msixPath ($sizeMb MB)."
    if (-not $Signing.Path) {
        Write-Host "Unsigned: fine for a Store upload (the Store signs it), but sideloading needs a certificate matching '$Publisher' that is trusted by the machine."
    }
}

Push-Location $root
try {
    $Version = Resolve-KeysharpVersion $Version
    Write-Host "Packaging Keysharp version $Version ($RuntimeIdentifier)."

    if ($Msix) {
        # -Msix is its own deliverable, not an extra one: it needs a self-contained publish the zip and
        # MSI must not get, and a Store submission has no use for either of them. Everything it depends
        # on - the version mapping, the SDK tools, the certificate - is resolved before the publish, so a
        # one-line mistake fails in seconds rather than after ten minutes of building.
        $msixVersion = Convert-ToMsixVersion $Version
        $signing = Resolve-SigningCertificate
        New-MsixPackage -MsixVersion $msixVersion `
            -MakeAppx (Find-SdkTool "makeappx.exe") `
            -MakePri (Find-SdkTool "makepri.exe") `
            -Signing $signing `
            -Publisher (Resolve-Publisher $signing)
        return
    }

    Assert-PackagableVersion $Version

    if (-not $SkipPublish) {
        Invoke-Publish -PublishRoot $publishRoot
    }

    New-StagedAppTree -PublishRoot $publishRoot -AppRoot $appDir

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
        # WiX does not consistently include its temporary cabinet in CoreClean's tracked file list. Remove
        # this architecture's exact intermediate tree so a stale or partially written #cab1.cab cannot be
        # reused by the linker.
        Remove-Item -LiteralPath $installerIntermediateDir -Recurse -Force -ErrorAction SilentlyContinue
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
