param(
	[string]$Source = "docs/capabilities.json",
	[string]$DocsOut = "docs/capabilities.md",
	# The CAPABILITIES_OVERVIEW markers live in docs/reference.md; README.md carries a
	# hand-written condensed table instead and is deliberately not injected into.
	[string]$OverviewPath = "docs/reference.md"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function StatusIcon([string]$status, [bool]$asterisk = $false) {
    $green = [char]::ConvertFromUtf32(0x1F7E2)
    $yellow = [char]::ConvertFromUtf32(0x1F7E1)
    $orange = [char]::ConvertFromUtf32(0x1F7E0)
    $red = [char]::ConvertFromUtf32(0x1F534)
    $white = [char]::ConvertFromUtf32(0x26AA)
    switch ($status) {
        "full" { return "$green Full" }
        "partial" { return "$yellow Partial$(if ($asterisk) { '*' } else { '' })" }
        "planned" { return "$orange Planned" }
        "unsupported" { return "$red Unsupported" }
        default { return "$white Unknown" }
    }
}

function EscapeMdCell([string]$value) {
	if ($null -eq $value) { return "" }
	$v = $value -replace "`r?`n", "<br>"
	# Escape pipe so operators like "|" and "||" do not break table columns.
	$v = $v -replace "\|", "\\|"
	return $v
}

function BuildTableLines($inputRows) {
	$header = @(
		"| Capability | Windows | Linux (X11) | Linux (Wayland) | macOS | Notes |",
		"|---|---|---|---|---|---|"
	)

	$out = New-Object System.Collections.Generic.List[string]
	foreach ($line in $header) { $out.Add($line) }
	foreach ($row in $inputRows) {
		$feature = EscapeMdCell([string]$row.feature)
		$notes = EscapeMdCell([string]$row.notes)
		$scriptOwnedControlOnly = [string]$row.feature -match '^Control.*\(\)$'
		$out.Add("| $feature | $(StatusIcon $row.windows) | $(StatusIcon $row.linux_x11 $scriptOwnedControlOnly) | $(StatusIcon $row.linux_wayland $scriptOwnedControlOnly) | $(StatusIcon $row.macos $scriptOwnedControlOnly) | $notes |")
	}
	return $out
}

if (-not (Test-Path $Source)) {
	throw "Source file not found: $Source"
}

# ReadAllText, not Get-Content -Raw: Windows PowerShell 5.1 decodes with the system ANSI
# codepage by default, which double-encodes every non-ASCII character on the round trip.
$root = [System.IO.File]::ReadAllText((Resolve-Path $Source)) | ConvertFrom-Json

$allRows = $root.rows | Sort-Object @{ Expression = { $_.feature.ToString().ToLowerInvariant() } }, @{ Expression = { $_.feature.ToString() } }
$tableAll = BuildTableLines $allRows

$legendLines = @(
	"Status legend:",
	("- " + (StatusIcon "full") + ": " + $root.legend.full),
	("- " + (StatusIcon "partial") + ": " + $root.legend.partial),
	("- " + (StatusIcon "planned") + ": " + $root.legend.planned),
	("- " + (StatusIcon "unsupported") + ": " + $root.legend.unsupported),
	("- " + (StatusIcon "unknown") + ": " + $root.legend.unknown),
	'- `Partial*` on non-Windows `Control*()` functions means script-owned Keysharp controls are supported, but controls in foreign applications are not.'
)

$docsLines = New-Object System.Collections.Generic.List[string]
$docsLines.Add("# Capability Matrix")
$docsLines.Add("")
$docsLines.Add("Generated from `docs/capabilities.json` via `scripts/generate-capabilities.ps1`.")
$docsLines.Add("")
foreach ($line in $legendLines) { $docsLines.Add($line) }
$docsLines.Add("")
foreach ($line in $tableAll) { $docsLines.Add($line) }

$docsSection = [string]::Join("`n", $docsLines)

#This expects to be run from the Keysharp folder, not the scripts folder this script resides in.
$cur = $pwd
$docsOutFullPath = [System.IO.Path]::Combine($cur, $DocsOut)
$docsDir = [System.IO.Path]::GetDirectoryName($docsOutFullPath)

if ($docsDir -and -not (Test-Path $docsDir)) { New-Item -ItemType Directory -Path $docsDir | Out-Null }

[System.IO.File]::WriteAllText($docsOutFullPath, $docsSection, [System.Text.UTF8Encoding]::new($false))

# Build concise overview matrix for injection between the CAPABILITIES_OVERVIEW markers.
$overviewFeatures = @(
	"Parser and runtime execution",
	"Directives and preprocessing",
	"File and directory operations",
	"Keyboard/Mouse send (synthetic input)",
	"Global keyboard hooks",
	"Global mouse hooks",
	"Hotkeys/Hotstrings",
	"Script-owned window management",
	"Foreign window management (non-Keysharp apps)",
	"Tray icon and menu",
	"Screen capture and pixel/image functions",
	"Clipboard",
	"Sound APIs",
	"Registry APIs",
	"COM APIs"
)

$overviewRows = foreach ($name in $overviewFeatures) {
	$root.rows | Where-Object { $_.feature -eq $name } | Select-Object -First 1
}

$missingOverview = @($overviewFeatures | Where-Object {
	$target = $_
	-Not ($overviewRows | Where-Object { $_ -and $_.feature -eq $target })
})
if ($missingOverview.Count -gt 0) {
	Write-Warning "Some overview features were not found in capabilities.json:"
	$missingOverview | ForEach-Object { Write-Warning "  $_" }
}

$overviewLines = New-Object System.Collections.Generic.List[string]
foreach ($line in $legendLines) { $overviewLines.Add($line) }
$overviewLines.Add("")
foreach ($line in (BuildTableLines ($overviewRows | Where-Object { $_ }))) { $overviewLines.Add($line) }
$overviewSection = [string]::Join("`n", $overviewLines)

$startMarker = "<!-- CAPABILITIES_OVERVIEW:START -->"
$endMarker = "<!-- CAPABILITIES_OVERVIEW:END -->"
$overviewInjected = $false
if (Test-Path $OverviewPath) {
	$overviewText = [System.IO.File]::ReadAllText((Resolve-Path $OverviewPath))
	$overviewNewline = if ($overviewText -match "`r`n") { "`r`n" } else { "`n" }
	$pattern = [regex]::Escape($startMarker) + ".*?" + [regex]::Escape($endMarker)
	$replacement = $startMarker + $overviewNewline + ($overviewSection -replace "`n", $overviewNewline) + $overviewNewline + $endMarker
	if ([regex]::IsMatch($overviewText, $pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)) {
		$newOverview = [regex]::Replace($overviewText, $pattern, $replacement, [System.Text.RegularExpressions.RegexOptions]::Singleline)
        $overviewOutFullPath = [System.IO.Path]::Combine($cur, $OverviewPath)
		[System.IO.File]::WriteAllText($overviewOutFullPath, $newOverview, [System.Text.UTF8Encoding]::new($false))
		$overviewInjected = $true
	}
	else {
		Write-Warning "CAPABILITIES_OVERVIEW markers not found in $OverviewPath; skipped overview injection."
	}
}
else {
	Write-Warning "Overview file not found at $OverviewPath; skipped overview injection."
}

Write-Host "Generated:"
Write-Host "  $DocsOut"
if ($overviewInjected) {
	Write-Host "Injected concise overview into:"
	Write-Host "  $OverviewPath"
}
