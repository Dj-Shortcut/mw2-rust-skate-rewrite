# Which types and members the interop set of the installed build offers (reflection only; nothing
# from the game is executed). The game's own code is obfuscated and changes names between builds,
# so this is how to find out what a new piece of client code can rely on. Inputs (globals):
#   $pt  comma separated entries Type or Type~memberRegex (exact simple or full type name),
#        or with $pfind a regex of type names
#   $pp  default regex on member names (default: everything)
#   $pfind  when set, only list matching type names across all interop assemblies
#   $psig  when set, a regex on method signatures "Type::Name(ParamTypes):Return" across the
#        game assemblies: finds a renamed type by what its methods look like
$dl = "$HOME\Downloads"; $probe = "$dl\claude-loader-probe"
$pb = ((gc 'C:\Program Files (x86)\Steam\steamapps\appmanifest_252490.acf' | sls '"buildid"').Line -split '"')[3]
$gen = "$probe\gen-$pb\out"
if (-not (Test-Path "$gen\Assembly-CSharp.dll")) { "no interop set for build $pb"; return }
$inner = "$probe\api-inner.ps1"
@'
param([string]$ArgsFile)
$q = Get-Content $ArgsFile -Raw | ConvertFrom-Json
$Gen = $q.Gen; $Core = $q.Core; $Types = $q.Types; $Pat = $q.Pat; $Find = $q.Find; $Sig = $q.Sig; $Out = $q.Out
$ErrorActionPreference = 'SilentlyContinue'
foreach ($n in 'Il2CppInterop.Common','Il2CppInterop.Runtime') { [void][Reflection.Assembly]::LoadFrom("$Core\$n.dll") }
[AppDomain]::CurrentDomain.add_AssemblyResolve([ResolveEventHandler]{ param($s, $e)
  $n = ($e.Name -split ',')[0]
  foreach ($d in $Gen, $Core) { $p = Join-Path $d "$n.dll"; if (Test-Path $p) { return [Reflection.Assembly]::LoadFrom($p) } }
  return $null })
function TypesOf($asm) { try { $asm.GetTypes() } catch { $_.Exception.InnerException.Types | ? { $_ } } }
function Short($t) { if ($null -eq $t) { return '?' }; $n = $t.Name; if ($n -eq 'Object') { $n = $t.FullName }; if ($t.IsGenericType) { $n = ($n -replace '`\d+','') + '<' + (($t.GetGenericArguments() | % { Short $_ }) -join ',') + '>' }; $n }
$lines = New-Object Collections.Generic.List[string]
if ($Find) {
  foreach ($f in (ls $Gen -Filter *.dll)) {
    $a = [Reflection.Assembly]::LoadFrom($f.FullName)
    foreach ($t in (TypesOf $a)) { if ($t.FullName -match $Find -and $t.FullName -notmatch '<|__c') { $lines.Add($f.BaseName + ': ' + $t.FullName) } }
  }
} elseif ($Sig) {
  $asms = 'Assembly-CSharp','Facepunch.Console','Facepunch.System','Facepunch.Unity','Facepunch.Network','Rust.Global','Rust.Data' | % { "$Gen\$_.dll" } | ? { Test-Path $_ }
  $flags = [Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly'
  foreach ($p in $asms) {
    $a = [Reflection.Assembly]::LoadFrom($p)
    foreach ($t in (TypesOf $a)) {
      if ($t.FullName -match '<|__c') { continue }
      foreach ($m in $t.GetMethods($flags)) {
        $line = $t.FullName + '::' + $(if ($m.IsStatic) { 'static ' } else { '' }) + $m.Name + '(' + (($m.GetParameters() | % { Short $_.ParameterType }) -join ',') + '):' + (Short $m.ReturnType)
        if ($line -match $Sig) { $lines.Add([IO.Path]::GetFileNameWithoutExtension($p) + ': ' + $line) }
      }
    }
  }
} else {
  $want = $Types -split ',' | % { $_.Trim() } | ? { $_ }
  $asms = 'Assembly-CSharp','Facepunch.Console','Facepunch.System','Facepunch.Unity','Facepunch.Input','Rust.Global','Rust.Data','Facepunch.Network','UnityEngine.CoreModule','UnityEngine.PhysicsModule','UnityEngine.InputLegacyModule','Unity.InputSystem','UnityEngine.AnimationModule','UnityEngine.AudioModule','UnityEngine.IMGUIModule','UnityEngine.UIModule','UnityEngine.UI','Unity.TextMeshPro','UnityEngine.TextRenderingModule' | % { "$Gen\$_.dll" } | ? { Test-Path $_ }
  $all = foreach ($p in $asms) { TypesOf ([Reflection.Assembly]::LoadFrom($p)) }
  $flags = [Reflection.BindingFlags]'Public,NonPublic,Instance,Static,DeclaredOnly'
  $defaultPat = $Pat
  foreach ($entry in $want) {
    $w, $own = $entry -split '~', 2
    $Pat = $defaultPat; if ($own) { $Pat = $own }
    $hits = @($all | ? { $_.Name -eq $w -or $_.FullName -eq $w })
    if (-not $hits) { $lines.Add("== $w : NOT FOUND"); continue }
    foreach ($t in $hits) {
      $props = @($t.GetProperties($flags) | ? { $_.Name -match $Pat })
      $meths = @($t.GetMethods($flags) | ? { -not $_.IsSpecialName -and $_.Name -match $Pat })
      $lines.Add('== ' + $t.FullName + ' : ' + (Short $t.BaseType) + ' [' + $t.Assembly.GetName().Name + '] props=' + $props.Count + ' methods=' + $meths.Count)
      $genRx = '^(field_|prop_|Method_|method_)'
      $pdesc = { param($x) $(if (($x.GetGetMethod($true), $x.GetSetMethod($true) | ? { $_ } | select -First 1).IsStatic) { 'static ' } else { '' }) }
      $real = $props | ? { $_.Name -notmatch $genRx } | sort Name | % { (& $pdesc $_) + $_.Name + ':' + (Short $_.PropertyType) }
      if ($real) { $lines.Add('P ' + ($real -join ' | ')) }
      $grp = $props | ? { $_.Name -match $genRx } | group { (& $pdesc $_) + (Short $_.PropertyType) } | sort Count -Descending | select -First 40 | % { $_.Name + ' x' + $_.Count }
      if ($grp) { $lines.Add('P(generated names, by type) ' + ($grp -join ' | ')) }
      $sig = { param($m) $(if ($m.IsStatic) { 'static ' } else { '' }) + '(' + (($m.GetParameters() | % { Short $_.ParameterType }) -join ',') + '):' + (Short $m.ReturnType) }
      $real = $meths | ? { $_.Name -notmatch $genRx } | sort Name | % { $(if ($_.IsStatic) { 'static ' } else { '' }) + $_.Name + '(' + (($_.GetParameters() | % { (Short $_.ParameterType) + ' ' + $_.Name }) -join ', ') + '):' + (Short $_.ReturnType) }
      if ($real) { $lines.Add('M ' + ($real -join ' | ')) }
      $grp = $meths | ? { $_.Name -match $genRx } | group { & $sig $_ } | sort Count -Descending | select -First 25 | % { $_.Name + ' x' + $_.Count }
      if ($grp) { $lines.Add('M(generated names, by signature) ' + ($grp -join ' | ')) }
    }
  }
}
[IO.File]::WriteAllLines($Out, $lines)
'@ | Out-File $inner -Encoding utf8
$out = "$probe\api-out.txt"
$find = ''; if ($pfind) { $find = $pt }
$pat = '.'; if ($pp) { $pat = $pp }
$argsFile = "$probe\api-args.json"
@{ Gen = $gen; Core = "$probe\bep788\BepInEx\core"; Types = "$pt"; Pat = $pat; Find = $find; Sig = "$psig"; Out = $out } | ConvertTo-Json | Out-File $argsFile -Encoding utf8
if (Test-Path $out) { Clear-Content $out }
& "$dl\codex-bep788-probe\pwsh-diag\pwsh.exe" -NoLogo -NoProfile -File $inner -ArgsFile $argsFile
$global:pfile = $out; $global:poff = 0
zz more
