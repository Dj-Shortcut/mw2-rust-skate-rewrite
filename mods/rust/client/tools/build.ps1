# Compile the skate client plugin with the staged SDK's compiler. The sources are those listed in
# mods/rust/client/sources.txt at the head of the branch in $pref, or at the commit in $pcommit.
$dl = "$HOME\Downloads"; $probe = "$dl\claude-loader-probe"
# $pnext is used once, also by a build that stops early: a later build on its own must not start
# the session that an earlier attempt asked for.
$next = $pnext; $global:pnext = $null
$sdk = "$probe\sdk"
if (-not (Test-Path "$sdk\dotnet.exe")) { Write-Host 'ABORT: no SDK in the staging folder; run zz setup first'; return }
# The interop set must have been generated for the installed Steam build. A set from another build
# resolves wrappers to the wrong native methods and crashes the client, so there is no fallback:
# generate first with `zz interop`.
$pb = ((gc 'C:\Program Files (x86)\Steam\steamapps\appmanifest_252490.acf' | sls '"buildid"').Line -split '"')[3]
$gen = "$dl\claude-loader-probe\gen-$pb\out"
if (-not ((Test-Path "$gen\Assembly-CSharp.dll") -and (sls -Path "$dl\claude-loader-probe\gen-$pb\gen.log" -Pattern 'GEN DONE.* errors=0( |$)' -Quiet))) { Write-Host "ABORT: no complete, error-free interop set for installed build $pb; run zz interop first"; return }
$core = "$probe\bep788\BepInEx\core"; $fx = "$probe\bep788\dotnet"
New-Item -ItemType Directory -Force "$probe\plugin","$probe\src" | Out-Null
# One API request for the head of the branch; every file then comes from the raw host at that
# exact commit, which cannot be stale.
$ref = 'main'; if ($pref) { $ref = $pref }
$head = "$pcommit"
if (-not $head) { try { $head = ("" + (irm -Headers @{ Accept = 'application/vnd.github.sha' } "https://api.github.com/repos/$prepo/commits/$ref" -ErrorAction Stop)).Trim() } catch { $head = '' } }
# Over the API's hourly limit: the branch head from git's own ref listing, which has no such limit.
if ($head -notmatch '^[0-9a-f]{40}$') {
  try {
    # RawContent: the listing is served as a binary type, whose .Content is bytes, not text.
    $refs = "" + (iwr -UseBasicParsing "https://github.com/$prepo.git/info/refs?service=git-upload-pack" -ErrorAction Stop).RawContent
    $m = [regex]::Match($refs, '([0-9a-f]{40}) refs/heads/' + [regex]::Escape($ref) + '(\s|$)')
    if ($m.Success) { $head = $m.Groups[1].Value }
  } catch { }
}
if ($head -notmatch '^[0-9a-f]{40}$') { Write-Host 'ABORT: could not read the commit to build'; return }
$global:pbuilt = $null
$list = ("" + (irm (zu 'mods/rust/client/sources.txt' $head))) -split "`n"
Remove-Item "$probe\src\*.cs" -ErrorAction SilentlyContinue
$files = @()
foreach ($line in $list) {
  $t = $line.Trim(); if (-not $t -or $t.StartsWith('#')) { continue }
  $parts = $t -split '\s+'
  $commit = $head; if ($parts.Count -ge 3) { $commit = $parts[1] }
  $dst = "$probe\src\" + (Split-Path $parts[0] -Leaf)
  Invoke-WebRequest -Uri (zu $parts[0] $commit) -OutFile $dst -UseBasicParsing
  if ($parts.Count -ge 3 -and (Get-FileHash $dst -Algorithm SHA256).Hash.ToLower() -ne $parts[2]) { Write-Host ("ABORT: " + $parts[0] + " does not match its pinned hash"); return }
  $files += $dst
}
$refs = @()
$refs += ls $fx -Filter *.dll | ? { ($_.Name -like 'System*' -or $_.Name -in 'mscorlib.dll','netstandard.dll','Microsoft.CSharp.dll') -and $_.Name -notlike '*.Native.dll' } | % FullName
$refs += 'BepInEx.Core.dll','BepInEx.Unity.IL2CPP.dll','BepInEx.Unity.Common.dll','Il2CppInterop.Runtime.dll','Il2CppInterop.Common.dll' | % { "$core\$_" }
# All generated interop assemblies, so game and engine types resolve without a hand-kept list.
$refs += ls $gen -Filter *.dll | % FullName
# Exactly one plugin may be installed: an older build under another name would run as well.
Remove-Item "$probe\plugin\*.dll" -ErrorAction SilentlyContinue
# The park models the plugin draws (mods/rust/client/models.txt), from the same commit, next to
# the plugin. A commit without them builds without models; the plugin then says so in its log.
Remove-Item "$probe\plugin\*.glb" -ErrorAction SilentlyContinue
$models = @()
try { $modelList = ("" + (irm (zu 'mods/rust/client/models.txt' $head))) -split "`n" } catch { $modelList = @() }
foreach ($line in $modelList) {
  $t = $line.Trim(); if (-not $t -or $t.StartsWith('#')) { continue }
  $dst = "$probe\plugin\" + (Split-Path $t -Leaf)
  try { Invoke-WebRequest -Uri (zu $t $head) -OutFile $dst -UseBasicParsing; $models += (Split-Path $t -Leaf) } catch { Write-Host ("no model " + $t + " at this commit") }
}
"models: " + $models.Count + " " + ($models -join ', ')
$rsp = "$probe\src\client.rsp"
(@('-nologo','-target:library','-nostdlib','-optimize+','-nowarn:CS1701,CS1702,CS8632',"-out:`"$probe\plugin\ShortcutSkateClient.dll`"") + ($refs | % { "-r:`"$_`"" }) + ($files | % { "`"$_`"" })) | Out-File $rsp -Encoding ascii
$csc = (ls "$sdk\sdk" -Directory | select -First 1).FullName + '\Roslyn\bincore\csc.dll'
$out = & "$sdk\dotnet.exe" $csc "@$rsp" 2>&1
"csc exit=$LASTEXITCODE sources=" + $files.Count + " refs=" + $refs.Count + " head=" + $head.Substring(0, 7)
$out | select -First 25 | % { $s = "$_"; if ($s.Length -gt 260) { $s.Substring(0,260) } else { $s } }
ls "$probe\plugin" | % { "{0} {1}" -f $_.Name, $_.Length }

if ($LASTEXITCODE -eq 0) { $global:pbuilt = $head }
if ($LASTEXITCODE -eq 0 -and $next) { zz $next }
