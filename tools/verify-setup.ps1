<#
.SYNOPSIS
  Check every precondition Agora needs, and say exactly what to do about each failure.

.DESCRIPTION
  Run this after installing the in-game modding toolchain. It answers "did that work?" in one
  command instead of six manual checks.

  Deliberately reads the CSII_* variables from the registry as well as the current process, because
  a shell started before the toolchain installed will not see them. That mismatch is the single most
  confusing failure mode here: everything is installed correctly and the build still cannot find it.

.PARAMETER Build
  Also run the C# build and the Core test suite. Slower, but proves the toolchain end to end.

.EXAMPLE
  .\tools\verify-setup.ps1
  .\tools\verify-setup.ps1 -Build
#>
[CmdletBinding()]
param([switch]$Build)

$repoRoot = Split-Path -Parent $PSScriptRoot
$results = New-Object System.Collections.Generic.List[object]

function Add-Check {
  param([string]$Name, [ValidateSet('PASS','FAIL','WARN','INFO')][string]$State, [string]$Detail, [string]$Fix = '')
  $results.Add([pscustomobject]@{ Check = $Name; State = $State; Detail = $Detail; Fix = $Fix })
}

# --- 1. Toolchain environment variables -------------------------------------------------
# HKCU:\Environment is the durable copy. $env: is this process's snapshot, taken at launch.
# The eight Mod.props / Mod.targets actually depend on. See docs/scout/0002-modding-toolchain.md.
$expected = @(
  'CSII_INSTALLATIONPATH','CSII_MANAGEDPATH','CSII_MSCORLIBPATH','CSII_USERDATAPATH',
  'CSII_TOOLPATH','CSII_LOCALMODSPATH','CSII_UNITYMODPROJECTPATH','CSII_MODPOSTPROCESSORPATH'
)

$reg = @{}
try {
  $props = Get-ItemProperty -Path 'HKCU:\Environment' -ErrorAction Stop
  foreach ($p in $props.PSObject.Properties) {
    if ($p.Name -like 'CSII_*') { $reg[$p.Name] = $p.Value }
  }
} catch { }

$missing = $expected | Where-Object { -not $reg.ContainsKey($_) -or -not $reg[$_] }

if ($missing.Count -eq $expected.Count) {
  Add-Check 'Modding toolchain' 'FAIL' 'No CSII_* variables set' `
    'Launch CS2 -> Options -> Modding -> install the toolchain, then reboot if needed.'
} elseif ($missing.Count -gt 0) {
  Add-Check 'Modding toolchain' 'WARN' "Partially set; missing: $($missing -join ', ')" `
    'The install may not have finished. Re-run Options -> Modding -> install.'
} else {
  Add-Check 'Modding toolchain' 'PASS' "$($reg.Count) CSII_* variables set; all $($expected.Count) required ones present"
}

# Every path-valued variable must actually resolve. CSII_LOCALMODSPATH is exempt: the toolchain sets
# it before creating the directory, and the first deploy is what brings it into existence.
$badPaths = @()
foreach ($n in $expected) {
  if ($n -eq 'CSII_LOCALMODSPATH') { continue }
  $v = $reg[$n]
  if ($v -and -not (Test-Path $v)) { $badPaths += $n }
}
if ($badPaths.Count -gt 0) {
  Add-Check 'Toolchain paths' 'FAIL' "Set but not on disk: $($badPaths -join ', ')" `
    'Re-run Options -> Modding -> install to reset these values.'
} elseif ($reg.Count -gt 0) {
  Add-Check 'Toolchain paths' 'PASS' 'Every required path resolves on disk'
}

# The Unity.Entities source generators. Without these, SystemAPI and IJobEntity do not compile --
# which makes this a hard prerequisite for M1 sensor work, not a nice-to-have.
if ($reg['CSII_UNITYMODPROJECTPATH'] -and $reg['CSII_ENTITIESVERSION']) {
  $gen = Join-Path $reg['CSII_UNITYMODPROJECTPATH'] `
    ("Library\PackageCache\com.unity.entities@{0}\Unity.Entities\SourceGenerators" -f $reg['CSII_ENTITIESVERSION'])
  if (Test-Path $gen) {
    Add-Check 'Entities generators' 'PASS' ("{0} analyzers (entities {1})" -f (Get-ChildItem $gen -Filter *.dll).Count, $reg['CSII_ENTITIESVERSION'])
  } else {
    Add-Check 'Entities generators' 'FAIL' 'Source generators not found' `
      'SystemAPI / IJobEntity will not compile. Re-run the in-game toolchain install.'
  }
}

# The stale-shell trap.
$staleShell = $false
foreach ($n in $reg.Keys) {
  $procVal = [Environment]::GetEnvironmentVariable($n, 'Process')
  if ($reg[$n] -and -not $procVal) { $staleShell = $true }
}
if ($staleShell) {
  Add-Check 'Current shell' 'WARN' 'CSII_* exist in the registry but not in this process' `
    'This shell (and any editor or agent launched from it) predates the install. Close and reopen it.'
} elseif ($reg.Count -gt 0) {
  Add-Check 'Current shell' 'PASS' 'Process environment matches the registry'
}

# --- 2. Game assemblies ------------------------------------------------------------------
$gameRoot = if ($env:CSII_INSTALLATIONPATH) { $env:CSII_INSTALLATIONPATH }
            elseif ($reg['CSII_INSTALLATIONPATH']) { $reg['CSII_INSTALLATIONPATH'] }
            else { 'C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II' }
$managed = Join-Path $gameRoot 'Cities2_Data\Managed'

if (Test-Path $managed) {
  $n = (Get-ChildItem $managed -Filter *.dll).Count
  Add-Check 'Game assemblies' 'PASS' "$n DLLs at $managed"
} else {
  Add-Check 'Game assemblies' 'FAIL' "Not found at $managed" `
    'Set CSII_INSTALLATIONPATH, or edit GameRoot in src/Agora.Mod/Agora.Mod.csproj.'
}

# --- 3. Local Mods folder (the deploy target) -------------------------------------------
$modsPath = if ($env:CSII_LOCALMODSPATH) { $env:CSII_LOCALMODSPATH }
            elseif ($reg['CSII_LOCALMODSPATH']) { $reg['CSII_LOCALMODSPATH'] }
            else { Join-Path $env:LOCALAPPDATA '..\LocalLow\Colossal Order\Cities Skylines II\Mods' }

# Deploy folder is $(TargetName), so Agora.Mod -- see docs/scout/0002-modding-toolchain.md.
if (Test-Path $modsPath) {
  Add-Check 'Local Mods folder' 'PASS' $modsPath
  $deployed = Join-Path $modsPath 'Agora.Mod\Agora.Mod.dll'
  if (Test-Path $deployed) {
    $t = (Get-Item $deployed).LastWriteTime
    Add-Check 'Agora deployed' 'PASS' "Agora.Mod.dll present (built $t)"
  } else {
    Add-Check 'Agora deployed' 'FAIL' 'Agora.Mod.dll not in the Mods folder' `
      'Run: dotnet build Agora.sln   (look for the "Copy output to deploy directory" line)'
  }
} else {
  Add-Check 'Local Mods folder' 'WARN' "Not created yet at $modsPath" `
    'The toolchain sets the variable but does not create the folder. The first build creates it.'
}

# --- 4. Steam launch options -------------------------------------------------------------
# Stored per-user in Steam's localconfig.vdf. Best-effort: Steam may not expose it if never set.
$steamRoot = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
$foundFlags = $false
if ($steamRoot) {
  $cfgs = Get-ChildItem (Join-Path $steamRoot 'userdata') -Filter 'localconfig.vdf' -Recurse -ErrorAction SilentlyContinue
  foreach ($c in $cfgs) {
    $txt = Get-Content $c.FullName -Raw -ErrorAction SilentlyContinue
    if ($txt -and $txt -match 'developerMode') { $foundFlags = $true }
  }
}
if ($foundFlags) {
  Add-Check 'Steam launch options' 'PASS' 'developerMode flag found in Steam config'
} else {
  Add-Check 'Steam launch options' 'WARN' 'Could not confirm --developerMode' `
    'Steam library -> right-click CS2 -> Properties -> General -> Launch Options: --developerMode --uiDeveloperMode'
}

# --- 5. Toolchains -----------------------------------------------------------------------
$sdk = (dotnet --version 2>$null)
if ($sdk) { Add-Check '.NET SDK' 'PASS' $sdk } else { Add-Check '.NET SDK' 'FAIL' 'dotnet not on PATH' 'Install the .NET SDK.' }

$node = (node --version 2>$null)
if ($node) {
  $major = [int]($node -replace '^v(\d+)\..*$', '$1')
  if ($major -gt 20) {
    Add-Check 'Node' 'WARN' "$node (CS2 UI templates target 20.11)" `
      'If webpack misbehaves, pin Node 20 with nvm rather than debugging the config.'
  } else {
    Add-Check 'Node' 'PASS' $node
  }
} else {
  Add-Check 'Node' 'FAIL' 'node not on PATH' 'Install Node 20 LTS.'
}

if (Get-Command ilspycmd -ErrorAction SilentlyContinue) {
  Add-Check 'ilspycmd' 'PASS' 'Available for refsrc/ regeneration'
} else {
  Add-Check 'ilspycmd' 'WARN' 'Not installed (only needed to regenerate refsrc/)' `
    'dotnet tool install -g ilspycmd --version 9.1.0.7988'
}

# --- 6. Repo state -----------------------------------------------------------------------
$refsrc = Join-Path $repoRoot 'refsrc'
if (Test-Path $refsrc) {
  $n = (Get-ChildItem $refsrc -Recurse -Filter *.cs -ErrorAction SilentlyContinue).Count
  if ($n -gt 1000) { Add-Check 'refsrc/ reference tree' 'PASS' "$n .cs files" }
  else { Add-Check 'refsrc/ reference tree' 'WARN' "$n .cs files (looks incomplete)" '.\tools\decompile.ps1 -Force' }
} else {
  Add-Check 'refsrc/ reference tree' 'WARN' 'Not generated' '.\tools\decompile.ps1'
}

$uiPkg = Join-Path $repoRoot 'ui\package.json'
if (Test-Path $uiPkg) {
  Add-Check 'ui/ build config' 'PASS' 'package.json present'
  if (Test-Path (Join-Path $repoRoot 'ui\node_modules')) {
    Add-Check 'ui/ dependencies' 'PASS' 'node_modules present'
  } else {
    Add-Check 'ui/ dependencies' 'FAIL' 'node_modules missing' 'cd ui; npm install'
  }
} else {
  Add-Check 'ui/ build config' 'FAIL' 'package.json missing' `
    'cd ui; npx create-csii-ui-mod  -- then copy package.json, tsconfig.json and the webpack config in. See ui/README.md.'
}

# --- 7. Optional build + test ------------------------------------------------------------
if ($Build) {
  Push-Location $repoRoot
  try {
    $out = dotnet build Agora.sln 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0) {
      # Toolchain mode logs "Copy output to deploy directory"; fallback mode logs "Agora deployed to".
      $deployLine = ($out -split "`n" |
        Where-Object { $_ -match 'Copy output to deploy directory|Agora deployed to' } |
        Select-Object -First 1)
      if ($deployLine) { Add-Check 'dotnet build' 'PASS' ($deployLine.Trim()) }
      else {
        Add-Check 'dotnet build' 'WARN' 'Built, but nothing was deployed' `
          'Neither deploy path ran. Check UseCsiiToolchain and that CSII_LOCALMODSPATH is set.'
      }
    } else {
      Add-Check 'dotnet build' 'FAIL' 'Build failed' 'Run dotnet build Agora.sln to see the errors.'
    }

    $tout = dotnet test 'tests\Agora.Core.Tests\Agora.Core.Tests.csproj' 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0) {
      $m = [regex]::Match($tout, 'Passed!\s*-\s*Failed:\s*(\d+),\s*Passed:\s*(\d+)')
      if ($m.Success) { Add-Check 'Core tests' 'PASS' "$($m.Groups[2].Value) passed, no game assemblies loaded" }
      else { Add-Check 'Core tests' 'PASS' 'Suite green' }
    } else {
      Add-Check 'Core tests' 'FAIL' 'Suite failed' 'dotnet test tests\Agora.Core.Tests\Agora.Core.Tests.csproj'
    }
  } finally { Pop-Location }
} else {
  Add-Check 'Build + tests' 'INFO' 'Skipped' 'Pass -Build to run them.'
}

# --- Report ------------------------------------------------------------------------------
Write-Host ""
Write-Host "AGORA setup check" -ForegroundColor Cyan
Write-Host ("=" * 78) -ForegroundColor DarkGray

foreach ($r in $results) {
  $color = switch ($r.State) { 'PASS' { 'Green' } 'FAIL' { 'Red' } 'WARN' { 'Yellow' } default { 'DarkGray' } }
  Write-Host ("  {0,-5} " -f $r.State) -ForegroundColor $color -NoNewline
  Write-Host ("{0,-24} " -f $r.Check) -NoNewline
  Write-Host $r.Detail -ForegroundColor DarkGray
  if ($r.Fix -and $r.State -in @('FAIL','WARN')) {
    Write-Host ("        -> " + $r.Fix) -ForegroundColor DarkCyan
  }
}

Write-Host ("=" * 78) -ForegroundColor DarkGray
$fails = @($results | Where-Object { $_.State -eq 'FAIL' }).Count
$warns = @($results | Where-Object { $_.State -eq 'WARN' }).Count

if ($fails -eq 0 -and $warns -eq 0) {
  Write-Host "All checks passed. Launch the game and walk the M0 gate in docs/status.md." -ForegroundColor Green
} elseif ($fails -eq 0) {
  Write-Host "$warns warning(s), nothing blocking. See docs/status.md for the M0 gate." -ForegroundColor Yellow
} else {
  Write-Host "$fails blocking issue(s), $warns warning(s). Work the '->' hints top to bottom." -ForegroundColor Red
}
Write-Host ""
