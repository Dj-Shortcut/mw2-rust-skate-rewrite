# Pack the installed launcher for a friend. Takes what `zz launcher` put on this PC - the unpacked
# loader, the interop set for the installed Rust build, the changed runtime, the released plugin
# with its park models and the launcher's own scripts - and writes one zip in Downloads with an
# installer (install.ps1 of this branch) that a friend runs on his own PC without any of the
# development tools. Nothing is built or downloaded; the pack is exactly what this PC plays.
# $pserver = 0 leaves server.txt out of the pack (by default it goes in when it is there, so that
# the friend's launcher can show the address to join).
$dl = "$HOME\Downloads"; $probe = "$dl\claude-loader-probe"; $L = "$probe\launcher"
$rust = 'C:\Program Files (x86)\Steam\steamapps\common\Rust'
$acf = 'C:\Program Files (x86)\Steam\steamapps\appmanifest_252490.acf'
if (-not (Test-Path $acf)) { 'ABORT: Rust is not installed in the default Steam library'; return }
$pb = ((gc $acf | sls '"buildid"').Line -split '"')[3]
if (-not $pb) { 'ABORT: could not read the installed Rust build'; return }
$version = if (Test-Path "$L\version.txt") { (gc "$L\version.txt" -First 1).Trim() } else { '' }
if (-not $version) { 'ABORT: no installed launcher (no launcher\version.txt); run zz launcher first'; return }
if ($version -notmatch "for Rust build $pb$") { "ABORT: the launcher was prepared for another Rust build ($version; installed $pb); run zz launcher first"; return }
$rel = if ($version -match '^Skate mod ([0-9a-f]{7})') { $Matches[1] } else { 'unknown' }
$gen = "$probe\gen-$pb"
if (-not ((Test-Path "$gen\out\Assembly-CSharp.dll") -and (sls -Path "$gen\gen.log" -Pattern 'GEN DONE.* errors=0( |$)' -Quiet))) { "ABORT: no complete, error-free interop set for build $pb (zz interop)"; return }
$need = @("$probe\bep788\winhttp.dll", "$probe\bep788\BepInEx\core\BepInEx.Core.dll", "$probe\rt\Il2CppInterop.Runtime.dll", "$probe\play\ShortcutSkateClient.dll") + @('session.ps1', 'skate.ps1', 'watch.ps1', 'clean.ps1', 'sticks.ps1' | % { "$L\$_" })
$missing = @($need | ? { -not (Test-Path -LiteralPath $_) })
if ($missing) { 'ABORT: not on this PC: ' + (($missing | % { $_.Replace($dl, '~') }) -join ', '); return }
# The installer is taken from this branch, like every other script: it is not part of the launcher.
$enc = New-Object Text.UTF8Encoding($false)
$inst = "" + (zg 'install.ps1')
if ($inst.Length -lt 200) { 'ABORT: could not fetch install.ps1 from the branch'; return }
$name = "RustSkate-$rel-build$pb"
$stage = "$probe\pack-$name"; $zip = "$dl\$name.zip"
Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
$to = "$stage\claude-loader-probe"
New-Item -ItemType Directory -Force "$to\gen-$pb" | Out-Null
Copy-Item "$probe\bep788" "$to\bep788" -Recurse
Copy-Item "$gen\out" "$to\gen-$pb\out" -Recurse
Copy-Item "$gen\gen.log" "$to\gen-$pb\gen.log"
Copy-Item "$probe\rt" "$to\rt" -Recurse
# play\ is copied whole into the game's plugin folder by the launcher: only the plugin and the models.
New-Item -ItemType Directory -Force "$to\play", "$to\launcher" | Out-Null
Copy-Item "$probe\play\ShortcutSkateClient.dll" "$to\play"
ls "$probe\play" -File -Filter *.glb | % { Copy-Item $_.FullName "$to\play" }
foreach ($f in 'session.ps1', 'skate.ps1', 'watch.ps1', 'clean.ps1', 'sticks.ps1', 'version.txt') { Copy-Item "$L\$f" "$to\launcher" }
$server = ''
if (($null -eq $pserver -or $pserver) -and (Test-Path "$probe\server.txt")) { Copy-Item "$probe\server.txt" "$to\server.txt"; $server = (gc "$probe\server.txt" -First 1).Trim() }
[IO.File]::WriteAllText("$stage\install.ps1", $inst, $enc)
[IO.File]::WriteAllText("$stage\Install Rust Skate.cmd", "@echo off`r`ntitle Install Rust Skate`r`npowershell -NoProfile -ExecutionPolicy Bypass -File `"%~dp0install.ps1`"`r`necho.`r`npause`r`n", [Text.Encoding]::ASCII)
# The comma binds tighter than +, so each line is made before the list is.
$made = 'made ' + (Get-Date -Format 'yyyy-MM-dd HH:mm'); $srv = 'server ' + $(if ($server) { 'included' } else { 'not included' })
@("pack $name", $made, "release $rel", "build $pb", $version, $srv) | Out-File "$stage\pack.txt" -Encoding utf8
$readme = @"
RUST SKATE - the skate mod for Rust, for the owner's own server

What you need: Rust installed through Steam in Steam's own library on C:, Steam signed in, and
(for the real controls) an Xbox controller. The mod runs on the owner's server only, which has no
anti-cheat. Rust started by this launcher cannot join other servers, and Rust started the normal
way is unchanged: the mod is in the Rust folder only while the launcher's window is open.

1. Unpack this zip anywhere (right-click, Extract All) and open the unpacked folder.
2. In Steam: Games > Add a Non-Steam Game to My Library > Browse, pick
   C:\Program Files (x86)\Steam\steamapps\common\Rust\RustClient.exe and add it. Keep its name,
   RustClient. In that game's page: controller icon > controller layout > choose the template
   "Keyboard (WASD) and Mouse". Without the shortcut the launcher does not start; with another
   layout the controller does nothing in Rust.
3. Double-click "Install Rust Skate.cmd". It checks your PC, copies the mod to
   Downloads\claude-loader-probe and puts two files on your desktop.
4. Start "Rust Skate" from the desktop and keep its window open while you play. In Rust's main
   menu press F1 and type   connect <address>   (the window shows the address when it was packed
   in; otherwise ask the owner), then Enter.
5. On the server: Y on the controller (or K on the keyboard) gets you on the board. A pushes,
   the left stick steers, B brakes, the right stick held back and flicked forward is an ollie,
   LT/RT held in the air is a grab, Y gets you off. The first seconds on the board show the controls.
6. Quit Rust the normal way. The window says "Done" when the Rust folder is back to normal. If it
   does not, or the PC went off while playing, run "Rust Skate - remove mod files" from the desktop
   before starting Rust normally.

After a Rust update the launcher refuses to start and says so: ask the owner for a new pack.
To remove everything: delete the two desktop files and the folder Downloads\claude-loader-probe.
This pack was made from the owner's own installed game; keep it among the people it was made for.
"@
[IO.File]::WriteAllText("$stage\README.txt", $readme.Replace("`r`n", "`n").Replace("`n", "`r`n"), $enc)
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item -LiteralPath $stage -Recurse -Force
$mb = [int]((gi $zip).Length / 1MB)
"pack: $zip ($mb MB)"
"inside: " + $version + " | interop set gen-$pb | plugin " + (gi "$probe\play\ShortcutSkateClient.dll").Length + " bytes | models " + @(ls "$probe\play" -Filter *.glb).Count + " | server.txt " + $(if ($server) { 'included' } else { 'left out' })
'give the zip to the friend with README.txt inside it; see FRIEND.md in the client folder'
