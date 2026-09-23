param(
    [string]$Filter,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Filter)) {
    throw 'Specify a safe test filter with -Filter. See Keysharp.Tests/TESTING.md.'
}

if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') {
    throw 'This script requires Windows and subst.exe.'
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
$usedLetters = @([IO.Directory]::GetLogicalDrives() | ForEach-Object { $_.Substring(0, 1) })
$usedLetters += @(Get-PSDrive -PSProvider FileSystem | Where-Object { $_.Name.Length -eq 1 } | ForEach-Object Name)
$drive = $null
$mapped = $false
$locationPushed = $false
$testExitCode = 1

try {
    foreach ($code in 90..68) {
        $letter = [string][char]$code

        if ($usedLetters -contains $letter) {
            continue
        }

        $candidate = "${letter}:"
        & subst.exe $candidate $repoRoot

        if ($LASTEXITCODE -eq 0) {
            $drive = $candidate
            $mapped = $true
            break
        }

        if ([IO.Directory]::GetLogicalDrives() -notcontains "${candidate}\") {
            throw "Could not map $candidate to $repoRoot."
        }
    }

    if (-not $mapped) {
        throw 'No free drive letter is available for the sandbox test run.'
    }

    $driveRoot = "${drive}\"
    Push-Location -LiteralPath $driveRoot
    $locationPushed = $true

    $testProject = Join-Path $driveRoot 'Keysharp.Tests\Keysharp.Tests.csproj'
    & dotnet test $testProject -c $Configuration --nologo --filter $Filter -p:UseSharedCompilation=false
    $testExitCode = $LASTEXITCODE
}
finally {
    try {
        if ($locationPushed) {
            Pop-Location
        }
    }
    finally {
        if ($mapped) {
            & subst.exe $drive /D

            if ($LASTEXITCODE -ne 0) {
                throw "Could not remove temporary drive $drive. Remove it with 'subst $drive /D'."
            }
        }
    }
}

exit $testExitCode
