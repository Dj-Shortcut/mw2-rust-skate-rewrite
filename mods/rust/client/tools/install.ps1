# The friend's installer, started from "Install Rust Skate.cmd" in an unpacked pack (made by
# pack.ps1 on the owner's PC). Checks that this PC can play the pack, copies the pack's staging
# folder to Downloads\claude-loader-probe - the one place the launcher's scripts look - and writes
# the two desktop files. Nothing is downloaded, nothing is built and the Rust folder is not touched:
# the launcher puts the mod there while a session runs and takes it out again.
$here = $PSScriptRoot
$rust = 'C:\Program Files (x86)\Steam\steamapps\common\Rust'
$acf = 'C:\Program Files (x86)\Steam\steamapps\appmanifest_252490.acf'
$probe = "$HOME\Downloads\claude-loader-probe"
function line($m) { Write-Host $m }
function abort($m) { line ''; line "  $m"; line '  Nothing was installed.' }
line ''
line '  INSTALL RUST SKATE'
line '  ------------------'
$pack = @{}
if (Test-Path "$here\pack.txt") { gc "$here\pack.txt" | % { if ($_ -match '^(\w+) (.*)$') { $pack[$Matches[1]] = $Matches[2] } } }
if (-not $pack['build']) { abort 'This folder is not an unpacked Rust Skate pack (no pack.txt next to this installer).'; return }
$from = "$here\claude-loader-probe"
line ('  ' + $pack['pack'] + ', made ' + $pack['made'])
line ''
if (-not ((Test-Path $acf) -and (Test-Path "$rust\RustClient.exe"))) { abort 'Rust is not installed in Steam''s own library on C: (C:\Program Files (x86)\Steam\steamapps\common\Rust). The launcher only knows that place.'; return }
$pb = ((gc $acf | sls '"buildid"').Line -split '"')[3]
if ($pb -ne $pack['build']) { abort ("This pack was made for Rust build " + $pack['build'] + " and this PC has build $pb. Ask the owner for a pack made for your build."); return }
if (Get-Process RustClient -ErrorAction SilentlyContinue) { abort 'Rust is running. Quit Rust first, then start this again.'; return }
if (Test-Path "$probe\loader-in-rust-folder.txt") { abort 'An earlier session left the mod in the Rust folder. Run "Rust Skate - remove mod files" on the desktop first, then start this again.'; return }
$need = @("claude-loader-probe\bep788\winhttp.dll", "claude-loader-probe\gen-$pb\out\Assembly-CSharp.dll", "claude-loader-probe\gen-$pb\gen.log", 'claude-loader-probe\rt\Il2CppInterop.Runtime.dll', 'claude-loader-probe\play\ShortcutSkateClient.dll', 'claude-loader-probe\launcher\skate.ps1', 'claude-loader-probe\launcher\session.ps1', 'claude-loader-probe\launcher\clean.ps1', 'claude-loader-probe\launcher\watch.ps1', 'claude-loader-probe\launcher\sticks.ps1', 'claude-loader-probe\launcher\version.txt')
$missing = @($need | ? { -not (Test-Path -LiteralPath (Join-Path $here $_)) })
if ($missing) { abort ('The pack is incomplete (missing ' + ($missing -join ', ') + '). Unpack the whole zip again.'); return }
$free = [int]((Get-PSDrive C).Free / 1MB)
$mb = [int]((ls $from -Recurse -File | measure Length -Sum).Sum / 1MB)
if ($free -lt $mb + 1024) { abort "The pack needs about $mb MB on C: and only $free MB are free."; return }
# Everything the launcher uses is replaced; anything else already in the staging folder is left alone.
New-Item -ItemType Directory -Force $probe | Out-Null
foreach ($d in 'bep788', "gen-$pb", 'rt', 'play', 'launcher') {
  Remove-Item -LiteralPath "$probe\$d" -Recurse -Force -ErrorAction SilentlyContinue
  Copy-Item -LiteralPath "$from\$d" -Destination "$probe\$d" -Recurse
}
if (Test-Path "$from\server.txt") { Copy-Item "$from\server.txt" "$probe\server.txt" -Force }
$desk = [Environment]::GetFolderPath('Desktop')
$start = "@echo off`r`ntitle Rust Skate`r`npowershell -NoProfile -ExecutionPolicy Bypass -File `"%USERPROFILE%\Downloads\claude-loader-probe\launcher\skate.ps1`"`r`necho.`r`npause`r`n"
$clean = "@echo off`r`ntitle Rust Skate - remove mod files`r`npowershell -NoProfile -ExecutionPolicy Bypass -File `"%USERPROFILE%\Downloads\claude-loader-probe\launcher\clean.ps1`"`r`necho.`r`npause`r`n"
[IO.File]::WriteAllText("$desk\Rust Skate.cmd", $start, [Text.Encoding]::ASCII)
[IO.File]::WriteAllText("$desk\Rust Skate - remove mod files.cmd", $clean, [Text.Encoding]::ASCII)
line ('  Installed: ' + (gc "$probe\launcher\version.txt" -First 1) + " ($mb MB in Downloads\claude-loader-probe).")
line '  On the desktop: "Rust Skate" and "Rust Skate - remove mod files".'
# The launcher starts Rust through a Steam shortcut named RustClient, so that the controller
# layout made for it applies on foot as well. Steam keeps those shortcuts in a binary file; the
# installer only reads it, it does not make the shortcut.
function Find-Shortcut($name) {
  foreach ($f in (ls 'C:\Program Files (x86)\Steam\userdata\*\config\shortcuts.vdf' -ErrorAction SilentlyContinue)) {
    $t = [Text.Encoding]::GetEncoding(28591).GetString([IO.File]::ReadAllBytes($f.FullName))
    $nameKey = [string][char]1 + 'appname' + [char]0; $exeKey = [string][char]1 + 'exe' + [char]0
    $i = 0
    while (($i = $t.IndexOf($nameKey, $i, [StringComparison]::OrdinalIgnoreCase)) -ge 0) {
      $n = $i + $nameKey.Length; $i = $n
      $app = $t.Substring($n, $t.IndexOf([char]0, $n) - $n)
      if ($app -ne $name) { continue }
      $e = $t.IndexOf($exeKey, $n, [StringComparison]::OrdinalIgnoreCase); $exe = ''
      if ($e -ge 0) { $e += $exeKey.Length; $exe = $t.Substring($e, $t.IndexOf([char]0, $e) - $e) }
      return $exe
    }
  }
}
$sc = Find-Shortcut 'RustClient'
line ''
if ($null -eq $sc) {
  line '  STILL TO DO: Steam has no shortcut named RustClient yet. The launcher needs it.'
  line '  In Steam: Games > Add a Non-Steam Game to My Library > Browse, pick'
  line "  $rust\RustClient.exe and add it; keep the name RustClient."
  line '  Then on its page: controller settings > layout > template "Keyboard (WASD) and Mouse".'
} elseif ($sc -notlike '*\Rust\RustClient.exe*') {
  line "  CHECK: the Steam shortcut RustClient points at $sc, not at Rust's RustClient.exe. Fix it in Steam."
} else {
  line '  The Steam shortcut RustClient is there. Give it the controller layout "Keyboard (WASD) and Mouse" if it has none.'
}
if (Test-Path "$probe\server.txt") { line ('  Server to join (F1 in the main menu): connect ' + (gc "$probe\server.txt" -First 1).Trim()) }
else { line '  Ask the owner for the server address; in Rust''s main menu press F1 and type: connect <address>' }
line '  Start "Rust Skate" on the desktop to play; keep its window open while Rust runs.'
