<#
.SYNOPSIS
  Decompile the Cities: Skylines II managed assemblies into refsrc/ for grepping.

.DESCRIPTION
  Scout's findings are only as current as this tree. Rerun after every game update.

  refsrc/ is gitignored: it is large, fully regenerable, and not ours to redistribute.
  Nothing in the build depends on it -- it exists so agents can grep the real API surface
  instead of guessing at method bodies. Type and member *names* do not need this tree;
  Colossal.Mono.Cecil.dll ships with the game and tools/api-query.ps1 reads metadata
  directly. Only method bodies require a decompiler.

.PARAMETER Force
  Re-decompile assemblies whose output directory already exists.

.PARAMETER Only
  Decompile just the named assemblies (without .dll), e.g. -Only Game,Colossal.UI.Binding

.EXAMPLE
  .\tools\decompile.ps1
  .\tools\decompile.ps1 -Force -Only Game
#>
[CmdletBinding()]
param(
  [switch]$Force,
  [string[]]$Only
)

$ErrorActionPreference = 'Stop'

# --- Locate the game -------------------------------------------------------------------
# The modding toolchain sets CSII_INSTALLATIONPATH; fall back to the default Steam path so
# this works before the toolchain is installed (same contract as Agora.Mod.csproj).
$gameRoot = if ($env:CSII_INSTALLATIONPATH) {
  $env:CSII_INSTALLATIONPATH
} else {
  'C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II'
}

$managed = Join-Path $gameRoot 'Cities2_Data\Managed'
if (-not (Test-Path $managed)) {
  throw "Managed assemblies not found at '$managed'. Set CSII_INSTALLATIONPATH or edit `$gameRoot in this script."
}

# --- Locate the decompiler -------------------------------------------------------------
$ilspy = Get-Command ilspycmd -ErrorAction SilentlyContinue
if (-not $ilspy) {
  throw "ilspycmd not found on PATH. Install with: dotnet tool install -g ilspycmd --version 9.1.0.7988`n" +
        "(The unpinned 'latest' package is currently broken -- its DotnetToolSettings.xml is missing.)"
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$outRoot  = Join-Path $repoRoot 'refsrc'
New-Item -ItemType Directory -Force -Path $outRoot | Out-Null

# --- What to decompile -----------------------------------------------------------------
# Game.dll is the payload (~4400 types). The Colossal assemblies are small and carry the
# modding, binding, logging and settings surfaces Agora actually calls into.
$targets = @(
  'Game'
  'Colossal.Core'
  'Colossal.UI'
  'Colossal.UI.Binding'
  'Colossal.IO.AssetDatabase'
  'Colossal.Localization'
  'Colossal.Logging'
  'Colossal.Collections'
  'Colossal.Mathematics'
  'Colossal.IO'
  'Colossal.PSI.Common'
)

if ($Only) { $targets = $targets | Where-Object { $Only -contains $_ } }
if (-not $targets) { throw "No matching assemblies. Valid names: Game, Colossal.*" }

# --- Decompile -------------------------------------------------------------------------
$done = 0; $skipped = 0; $failed = @()

foreach ($name in $targets) {
  $dll = Join-Path $managed "$name.dll"
  if (-not (Test-Path $dll)) {
    Write-Warning "missing, skipping: $name.dll"
    continue
  }

  $out = Join-Path $outRoot $name
  if ((Test-Path $out) -and -not $Force) {
    Write-Host "skip     $name (already decompiled; -Force to redo)" -ForegroundColor DarkGray
    $skipped++
    continue
  }

  if (Test-Path $out) { Remove-Item -Recurse -Force $out }
  New-Item -ItemType Directory -Force -Path $out | Out-Null

  Write-Host "decompile $name ..." -ForegroundColor Cyan
  $sw = [System.Diagnostics.Stopwatch]::StartNew()

  # -p emits a compilable project tree (one file per type, foldered by namespace), which is
  # what makes the result greppable. -r lets ILSpy resolve the other 170-odd assemblies.
  & ilspycmd -p -o $out -r $managed $dll
  $code = $LASTEXITCODE
  $sw.Stop()

  if ($code -ne 0) {
    Write-Warning "$name failed (exit $code)"
    $failed += $name
    continue
  }

  $files = (Get-ChildItem $out -Recurse -Filter *.cs).Count
  Write-Host ("  ok  {0} files in {1:n1}s" -f $files, $sw.Elapsed.TotalSeconds) -ForegroundColor Green
  $done++
}

# --- Summary ---------------------------------------------------------------------------
$total = (Get-ChildItem $outRoot -Recurse -Filter *.cs -ErrorAction SilentlyContinue).Count
Write-Host ""
Write-Host "refsrc/: $total .cs files  ($done decompiled, $skipped skipped)" -ForegroundColor Green
if ($failed) { Write-Warning "failed: $($failed -join ', ')" }
Write-Host "Grep it, never read it in full. See CLAUDE.md."
