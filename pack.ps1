# pack.ps1 - build the release archive dist\MarkdownLister-<version>.zip.
#
# Archive layout (same as release v0.1.4):
#   MarkdownLister\          - plugin files (x86 .wlx + x64 .wlx64 merged),
#                              install.ps1 / uninstall.ps1 / pluginst.inf / LICENSE / README.md / README.en.md
#
# Version is read from Directory.Build.props (single source of truth).
# Run build.ps1 first: the archive is assembled from dist\x86 and dist\x64.
#
# Author: Starinsky Oleg Grigorievich; email: info@electroman.by; telegram: @ElectroManBY.
[CmdletBinding()]
param(
    # Version override (default: from Directory.Build.props)
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# --- 1. Version from Directory.Build.props ---
if (-not $Version) {
    [xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
    $Version = [string]$props.Project.PropertyGroup.Version
    $Version = $Version.Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Bad version '$Version' (expected MAJOR.MINOR.PATCH)"
}
Write-Host "[1/3] Version: $Version"

# --- 2. Stage: dist\package\MarkdownLister\ ---
foreach ($plat in 'x86', 'x64') {
    if (-not (Test-Path (Join-Path $root "dist\$plat"))) {
        throw "dist\$plat not found - run build.ps1 first"
    }
}

$stage = Join-Path $root 'dist\package'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$pkg = Join-Path $stage 'MarkdownLister'
New-Item -ItemType Directory -Force -Path $pkg | Out-Null

# x64 layout as the base (has .wlx64), then add the x86 entry point
Copy-Item (Join-Path $root 'dist\x64\*') $pkg -Recurse -Force
Copy-Item (Join-Path $root 'dist\x86\MarkdownLister.wlx') $pkg -Force

Copy-Item (Join-Path $root 'src\MarkdownLister.Setup\install.ps1') $pkg -Force
Copy-Item (Join-Path $root 'src\MarkdownLister.Setup\uninstall.ps1') $pkg -Force
Copy-Item (Join-Path $root 'src\MarkdownLister.Setup\pluginst.inf') $pkg -Force
Copy-Item (Join-Path $root 'LICENSE') $pkg -Force
Copy-Item (Join-Path $root 'README.md') $pkg -Force
Copy-Item (Join-Path $root 'README.en.md') $pkg -Force
Write-Host "[2/3] Staged: $pkg"

# --- 3. Zip ---
$zip = Join-Path $root ("dist\MarkdownLister-" + $Version + ".zip")
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
$size = [math]::Round((Get-Item $zip).Length / 1KB)
Write-Host "[3/3] Archive: $zip ($size KB)"
Write-Host 'Done.' -ForegroundColor Green
