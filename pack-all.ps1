#requires -Version 7.0

<#
.SYNOPSIS
    Builds and packs every Dragonfire library to ./artifacts/ at one shared version.

.DESCRIPTION
    Discovers all packable .csproj files (anything under */src/ — tests and
    samples are skipped) and runs `dotnet pack` against each one in Release
    configuration, writing the resulting .nupkg / .snupkg files to ./artifacts/.

    The version comes from the <Version> property in the root Directory.Build.props
    unless overridden by -Version. To ship a new release of every package at the
    same version, either bump that file or pass -Version on the command line.

.PARAMETER Version
    Override the shared package version (e.g. "1.2.3" or "1.2.3-preview.1").
    If omitted, the version from Directory.Build.props is used.

.PARAMETER Configuration
    MSBuild configuration. Defaults to Release.

.PARAMETER OutputDirectory
    Where to write the .nupkg files. Defaults to ./artifacts.

.PARAMETER NoBuild
    Skip the initial restore/build step (assumes everything is already built).

.PARAMETER Push
    After packing, push every produced .nupkg to NuGet.org. Requires the NUGET_API_KEY
    environment variable (or pass -ApiKey).

.PARAMETER ApiKey
    NuGet API key used when -Push is set. Falls back to $env:NUGET_API_KEY.

.PARAMETER Source
    NuGet feed URL used when -Push is set. Defaults to https://api.nuget.org/v3/index.json.

.EXAMPLE
    pwsh ./pack-all.ps1
    Pack everything at the version baked into Directory.Build.props.

.EXAMPLE
    pwsh ./pack-all.ps1 -Version 1.4.0
    Pack everything at 1.4.0 without modifying any files.

.EXAMPLE
    pwsh ./pack-all.ps1 -Version 1.4.0-preview.2 -Push
    Pack at 1.4.0-preview.2 and push every resulting nupkg to NuGet.org.
#>

[CmdletBinding()]
param(
    [string]   $Version,
    [string]   $Configuration   = 'Release',
    [string]   $OutputDirectory = (Join-Path $PSScriptRoot 'artifacts'),
    [switch]   $NoBuild,
    [switch]   $Push,
    [string]   $ApiKey          = $env:NUGET_API_KEY,
    [string]   $Source          = 'https://api.nuget.org/v3/index.json'
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# ---------------------------------------------------------------------------
# Resolve the version that will be shipped.
# ---------------------------------------------------------------------------
function Get-RootVersion {
    $rootProps = Join-Path $PSScriptRoot 'Directory.Build.props'
    if (-not (Test-Path $rootProps)) {
        throw "Root Directory.Build.props not found at $rootProps"
    }
    $xml = [xml](Get-Content $rootProps -Raw)
    $node = $xml.SelectSingleNode('//PropertyGroup/Version')
    if (-not $node) {
        throw "<Version> element not found in $rootProps"
    }
    return $node.InnerText.Trim()
}

if (-not $Version) { $Version = Get-RootVersion }

Write-Host ''
Write-Host '╔══════════════════════════════════════════════════════════════╗' -ForegroundColor Cyan
Write-Host ('║  Dragonfire pack-all — version {0,-32}║' -f $Version) -ForegroundColor Cyan
Write-Host '╚══════════════════════════════════════════════════════════════╝' -ForegroundColor Cyan
Write-Host ''

# ---------------------------------------------------------------------------
# Discover packable projects.
#   • exclude tests/, samples/, bin/, obj/ paths
#   • exclude any *Sample*.csproj or *SampleApp.csproj (defensive — a few
#     samples sit outside a samples/ folder and don't carry IsPackable=false)
#   • exclude projects that explicitly set <IsPackable>false</IsPackable>
# ---------------------------------------------------------------------------
$projects = Get-ChildItem -Recurse -Filter *.csproj |
    Where-Object {
        $p = $_.FullName.Replace('\', '/')
        $p -notmatch '/tests/' -and
        $p -notmatch '/samples/' -and
        $p -notmatch '/bin/' -and
        $p -notmatch '/obj/' -and
        $_.Name -notmatch 'Sample(\.|App)'
    } |
    Where-Object {
        $content = Get-Content $_.FullName -Raw
        $content -notmatch '<IsPackable>\s*false\s*</IsPackable>'
    } |
    Sort-Object FullName

if (-not $projects) {
    Write-Warning 'No packable projects discovered.'
    return
}

Write-Host "Discovered $($projects.Count) packable project(s):" -ForegroundColor Green
$projects | ForEach-Object {
    $rel = [IO.Path]::GetRelativePath($PSScriptRoot, $_.FullName)
    Write-Host "  • $rel"
}
Write-Host ''

# ---------------------------------------------------------------------------
# Prepare output directory.
# ---------------------------------------------------------------------------
if (Test-Path $OutputDirectory) {
    Get-ChildItem $OutputDirectory -Filter '*.nupkg'  -ErrorAction SilentlyContinue | Remove-Item -Force
    Get-ChildItem $OutputDirectory -Filter '*.snupkg' -ErrorAction SilentlyContinue | Remove-Item -Force
} else {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

# ---------------------------------------------------------------------------
# Build (unless skipped).
# ---------------------------------------------------------------------------
if (-not $NoBuild) {
    Write-Host '── Build (Release) ──' -ForegroundColor Yellow
    foreach ($proj in $projects) {
        dotnet build $proj.FullName -c $Configuration --nologo --verbosity quiet `
            -p:Version=$Version
        if ($LASTEXITCODE -ne 0) { throw "Build failed for $($proj.Name)" }
    }
}

# ---------------------------------------------------------------------------
# Pack.
# ---------------------------------------------------------------------------
Write-Host '── Pack ──' -ForegroundColor Yellow
$packed = @()
foreach ($proj in $projects) {
    Write-Host ('  → ' + $proj.BaseName) -ForegroundColor White
    $packArgs = @(
        'pack', $proj.FullName,
        '-c', $Configuration,
        '-o', $OutputDirectory,
        '--nologo', '--verbosity', 'quiet',
        "-p:Version=$Version"
    )
    if ($NoBuild) { $packArgs += '--no-build' }
    & dotnet @packArgs
    if ($LASTEXITCODE -ne 0) { throw "Pack failed for $($proj.Name)" }
    $packed += $proj.BaseName
}

# ---------------------------------------------------------------------------
# Summary.
# ---------------------------------------------------------------------------
$nupkgs = Get-ChildItem $OutputDirectory -Filter '*.nupkg' | Sort-Object Name
Write-Host ''
Write-Host "── Produced $($nupkgs.Count) package(s) in $OutputDirectory ──" -ForegroundColor Green
$nupkgs | ForEach-Object {
    Write-Host ('  ✓ ' + $_.Name) -ForegroundColor Green
}
Write-Host ''

# ---------------------------------------------------------------------------
# Optional push to NuGet.
# ---------------------------------------------------------------------------
if ($Push) {
    if (-not $ApiKey) {
        throw 'Push requested but no API key supplied. Pass -ApiKey or set NUGET_API_KEY.'
    }
    Write-Host "── Pushing $($nupkgs.Count) package(s) to $Source ──" -ForegroundColor Yellow
    foreach ($pkg in $nupkgs) {
        Write-Host ('  ↗ ' + $pkg.Name) -ForegroundColor White
        dotnet nuget push $pkg.FullName --api-key $ApiKey --source $Source --skip-duplicate
        if ($LASTEXITCODE -ne 0) { throw "Push failed for $($pkg.Name)" }
    }
    Write-Host ''
    Write-Host "Pushed $($nupkgs.Count) package(s) at version $Version." -ForegroundColor Green
}

Write-Host 'Done.' -ForegroundColor Cyan
