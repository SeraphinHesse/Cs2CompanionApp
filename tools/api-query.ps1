<#
.SYNOPSIS
  Query Cities: Skylines II assembly metadata -- types, members, enum values -- with no decompiler.

.DESCRIPTION
  Colossal.Mono.Cecil.dll ships with the game, so type names, member signatures, enum values and
  constructor arity are all readable straight out of the assemblies. This is the tool that produced
  docs/scout/0001-api-index.md.

  Reach for this FIRST. Only drop to refsrc/ (tools/decompile.ps1) when you need a method *body*:
  what the code actually does, rather than what it is called.

  Note the namespace: Colossal repackaged Cecil, so the types are Colossal.Mono.Cecil.*, not
  Mono.Cecil.*. Using the upstream namespace fails with a type-not-found error.

.PARAMETER Type
  List types whose full name matches this pattern (case-insensitive substring, or a regex).

.PARAMETER Members
  Full name of one type; lists its fields, properties, methods, and nested types.

.PARAMETER Enum
  Full name of an enum type; lists its members with their numeric values, ordered by value.

.PARAMETER Implements
  List types that implement or inherit the named interface/base type. Matched EXACTLY by simple or
  full name -- 'IMod' will not also return IModifierType. Use -Type when you want fuzzy matching.

.PARAMETER Assembly
  Restrict the scan (without .dll). Defaults to Game plus every Colossal.* assembly.

.PARAMETER Public
  Only show public members (default shows everything, including internals worth patching).

.EXAMPLE
  .\tools\api-query.ps1 -Type TimeSystem
  .\tools\api-query.ps1 -Members Game.Simulation.TimeSystem -Public
  .\tools\api-query.ps1 -Enum Game.City.CityModifierType
  .\tools\api-query.ps1 -Implements IMod
  .\tools\api-query.ps1 -Type "Statistic" -Assembly Game
#>
[CmdletBinding(DefaultParameterSetName = 'Type')]
param(
  [Parameter(ParameterSetName = 'Type', Position = 0)][string]$Type,
  [Parameter(ParameterSetName = 'Members', Mandatory = $true)][string]$Members,
  [Parameter(ParameterSetName = 'Enum', Mandatory = $true)][string]$Enum,
  [Parameter(ParameterSetName = 'Implements', Mandatory = $true)][string]$Implements,
  [string[]]$Assembly,
  [switch]$Public
)

$ErrorActionPreference = 'Stop'

$gameRoot = if ($env:CSII_INSTALLATIONPATH) {
  $env:CSII_INSTALLATIONPATH
} else {
  'C:\Program Files (x86)\Steam\steamapps\common\Cities Skylines II'
}
$managed = Join-Path $gameRoot 'Cities2_Data\Managed'
if (-not (Test-Path $managed)) { throw "Managed assemblies not found at '$managed'." }

# Cecil, as repackaged by Colossal.
Add-Type -Path (Join-Path $managed 'Colossal.Mono.Cecil.dll')

# --- Which assemblies to scan ----------------------------------------------------------
if ($Assembly) {
  $dlls = $Assembly | ForEach-Object {
    $p = Join-Path $managed "$_.dll"
    if (-not (Test-Path $p)) { Write-Warning "missing: $_.dll" } else { $p }
  }
} else {
  $dlls = @(Join-Path $managed 'Game.dll') +
          (Get-ChildItem $managed -Filter 'Colossal*.dll' | ForEach-Object { $_.FullName })
}
$dlls = $dlls | Where-Object { $_ }

function Read-Asm([string]$path) {
  try { return [Colossal.Mono.Cecil.AssemblyDefinition]::ReadAssembly($path) }
  catch { Write-Warning ("unreadable: {0} ({1})" -f (Split-Path -Leaf $path), $_.Exception.Message); return $null }
}

function Get-AllTypes {
  foreach ($dll in $dlls) {
    $asm = Read-Asm $dll
    if (-not $asm) { continue }
    foreach ($m in $asm.Modules) {
      foreach ($t in $m.GetTypes()) {
        [pscustomobject]@{ Asm = $asm.Name.Name; T = $t }
      }
    }
  }
}

function Format-Sig($m) {
  $ps = ($m.Parameters | ForEach-Object {
    $n = $_.ParameterType.Name
    if ($_.IsOptional) { "$n $($_.Name) = ..." } else { "$n $($_.Name)" }
  }) -join ', '
  $ret = $m.ReturnType.Name
  $name = $m.Name
  if ($m.HasGenericParameters) { $name += "<" + (($m.GenericParameters | ForEach-Object { $_.Name }) -join ', ') + ">" }
  return "$ret $name($ps)"
}

# --- Modes ------------------------------------------------------------------------------
switch ($PSCmdlet.ParameterSetName) {

  'Type' {
    if (-not $Type) { throw "Pass -Type <pattern>, or use -Members / -Enum / -Implements." }
    $hits = Get-AllTypes | Where-Object { $_.T.FullName -match [regex]::Escape($Type) -or $_.T.FullName -match $Type }
    $hits | ForEach-Object {
      $t = $_.T
      $kind = if ($t.IsEnum) { 'enum' }
              elseif ($t.IsInterface) { 'interface' }
              elseif ($t.IsValueType) { 'struct' }
              else { 'class' }
      [pscustomobject]@{ Kind = $kind; FullName = $t.FullName; Assembly = $_.Asm }
    } | Sort-Object FullName | Format-Table -AutoSize
    Write-Host ("{0} type(s)." -f @($hits).Count) -ForegroundColor DarkGray
  }

  'Enum' {
    $hit = Get-AllTypes | Where-Object { $_.T.FullName -eq $Enum -or $_.T.Name -eq $Enum } | Select-Object -First 1
    if (-not $hit) { throw "Enum '$Enum' not found. Try -Type $Enum to locate it." }
    $t = $hit.T
    if (-not $t.IsEnum) { throw "'$($t.FullName)' is not an enum. Use -Members instead." }
    Write-Host "$($t.FullName)  [$($hit.Asm)]" -ForegroundColor Cyan
    $t.Fields |
      Where-Object { $_.Name -ne 'value__' } |
      ForEach-Object { [pscustomobject]@{ Value = [int64]$_.Constant; Name = $_.Name } } |
      Sort-Object Value | Format-Table -AutoSize
  }

  'Members' {
    $hit = Get-AllTypes | Where-Object { $_.T.FullName -eq $Members -or $_.T.Name -eq $Members } | Select-Object -First 1
    if (-not $hit) { throw "Type '$Members' not found. Try -Type $Members to locate it." }
    $t = $hit.T
    Write-Host "$($t.FullName)  [$($hit.Asm)]" -ForegroundColor Cyan
    if ($t.BaseType) { Write-Host "  base:       $($t.BaseType.FullName)" -ForegroundColor DarkGray }
    if ($t.Interfaces.Count) {
      Write-Host "  implements: $((($t.Interfaces | ForEach-Object { $_.InterfaceType.Name }) -join ', '))" -ForegroundColor DarkGray
    }

    $flt = { param($x) if ($Public) { return $x } else { return $true } }

    $fields = $t.Fields | Where-Object { $_.Name -ne 'value__' -and (-not $Public -or $_.IsPublic) }
    if ($fields) {
      Write-Host "`n-- fields --" -ForegroundColor Yellow
      $fields | ForEach-Object {
        [pscustomobject]@{ Static = $_.IsStatic; Type = $_.FieldType.Name; Name = $_.Name }
      } | Format-Table -AutoSize
    }

    $props = $t.Properties | Where-Object {
      -not $Public -or ($_.GetMethod -and $_.GetMethod.IsPublic) -or ($_.SetMethod -and $_.SetMethod.IsPublic)
    }
    if ($props) {
      Write-Host "-- properties --" -ForegroundColor Yellow
      $props | ForEach-Object {
        $acc = @()
        if ($_.GetMethod) { $acc += 'get' }
        # A public setter on an engine type is worth noticing: it can retire a Harmony patch.
        if ($_.SetMethod) { if ($_.SetMethod.IsPublic) { $acc += 'SET(public)' } else { $acc += 'set' } }
        [pscustomobject]@{ Type = $_.PropertyType.Name; Name = $_.Name; Access = ($acc -join '/') }
      } | Format-Table -AutoSize
    }

    $methods = $t.Methods | Where-Object {
      (-not $Public -or $_.IsPublic) -and -not $_.IsGetter -and -not $_.IsSetter
    }
    if ($methods) {
      Write-Host "-- methods --" -ForegroundColor Yellow
      $methods | ForEach-Object {
        [pscustomobject]@{ Static = $_.IsStatic; Virtual = $_.IsVirtual; Signature = (Format-Sig $_) }
      } | Format-Table -AutoSize
    }

    if ($t.NestedTypes.Count) {
      Write-Host "-- nested --" -ForegroundColor Yellow
      $t.NestedTypes | ForEach-Object { $_.Name } | Sort-Object
    }
  }

  'Implements' {
    # Exact name match, deliberately. Substring matching turns a search for 'IMod' into a list of
    # IModelPostProcessor / IModifierType hits, which is worse than no answer. Use -Type for fuzzy.
    $hits = Get-AllTypes | Where-Object {
      $t = $_.T
      ($t.Interfaces | Where-Object { $_.InterfaceType.Name -eq $Implements -or $_.InterfaceType.FullName -eq $Implements }) -or
      ($t.BaseType -and ($t.BaseType.Name -eq $Implements -or $t.BaseType.FullName -eq $Implements))
    }
    $hits | ForEach-Object {
      [pscustomobject]@{ FullName = $_.T.FullName; Base = $(if ($_.T.BaseType) { $_.T.BaseType.Name } else { '' }); Assembly = $_.Asm }
    } | Sort-Object FullName | Format-Table -AutoSize
    Write-Host ("{0} type(s)." -f @($hits).Count) -ForegroundColor DarkGray
  }
}
