# One bounded session of the Rust client with the mod loader: copies the loader, the interop set
# and the plugin into the Rust folder, starts the client, waits, and always moves every added file
# out again, also when something fails. The Rust folder must never keep the loader: a normal
# start of Rust would load it too, also on a server with anti-cheat.
# Inputs (globals, optional):
#   $ptag     run label                      (default 'A')
#   $plisten  UnityLogListening true/false   (default $false)
#   $prt      directory with replacement core DLLs to overlay (default none)
#   $pwait    seconds to keep the client alive after chainloader start (default 45)
#   $pplug    directory with plugin DLLs to install (default none)
#   $psteps   comma separated plugin steps written to plugins\steps.txt
#   $ppreload PreloadIL2CPPInteropAssemblies (default false)
#   $pdone    seconds to keep the client running until the plugin writes plugins\skate.done
#             (a session in a world; replaces $pwait)
#   $psteam   start the client through the owner's non-Steam shortcut instead of the executable,
#             so that the Steam Input layout made for that shortcut applies (controller support)
#   $pshortcut  name of that shortcut (default 'RustClient')
#   $pconnect when the client is started directly: join the private test server at start. Its
#             address is read from server.txt in the staging folder, a local file that is never
#             committed.
#   $pkeep    keep the client's window in front for the whole session, not only bring it there
#             once: for a scripted check that nobody watches
#   $psticks  path of sticks.ps1, the reader that shares the controller with the plugin; without
#             it the reader is fetched from the branch when these tools were loaded with zz
$ErrorActionPreference = 'Stop'
$reader = $null
if (-not $ptag) { $ptag = 'A' }
if ($null -eq $plisten) { $plisten = $false }
if (-not $pwait) { $pwait = 45 }
$rust = 'C:\Program Files (x86)\Steam\steamapps\common\Rust'
$dl = "$HOME\Downloads"
$probe = "$dl\claude-loader-probe"
$zip = "$dl\BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788+5b766a3.zip"
# The interop set must have been generated for the installed Steam build. A set from another build
# resolves wrappers to the wrong native methods and crashes the client, so there is no fallback:
# generate first with `zz interop`.
$pb = ((gc 'C:\Program Files (x86)\Steam\steamapps\appmanifest_252490.acf' | sls '"buildid"').Line -split '"')[3]
$gen = "$dl\claude-loader-probe\gen-$pb\out"
if (-not ((Test-Path "$gen\Assembly-CSharp.dll") -and (sls -Path "$dl\claude-loader-probe\gen-$pb\gen.log" -Pattern 'GEN DONE.* errors=0( |$)' -Quiet))) { Write-Host "ABORT: no complete, error-free interop set for installed build $pb; run zz interop first"; return }
$run = "$probe\run-$ptag-" + (Get-Date -Format 'HHmmss')
$roots = 'BepInEx','dotnet','winhttp.dll','doorstop_config.ini','.doorstop_version','changelog.txt'
# While this file exists, a loader in the Rust folder is one that these tools put there. clean.ps1
# moves a loader out only then: one that somebody installed for another mod is left alone. The
# note names two files of this loader by their hash, so that another loader put in its place after
# a session was cut off before copying is told apart.
$mark = "$probe\loader-in-rust-folder.txt"
$prints = 'winhttp.dll', 'BepInEx\core\BepInEx.Core.dll'
function say($m) { Write-Host $m }
# A client that comes up behind the window these tools run in never gets the keyboard. Its window
# is brought to the front once; after that the player switches windows as he likes. Windows lets a
# program take the foreground only right after a key press, and a tap of Alt counts as one.
function InFront {
  if (-not ('ShortcutSkate.Front' -as [type])) {
    Add-Type -Namespace ShortcutSkate -Name Front -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h); [DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow(); [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, System.UIntPtr e);'
  }
  return [ShortcutSkate.Front]::GetForegroundWindow()
}
function ToFront($h) {
  if ((InFront) -eq $h) { return 'in front' }
  [ShortcutSkate.Front]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [ShortcutSkate.Front]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
  [ShortcutSkate.Front]::SetForegroundWindow($h) | Out-Null
  Start-Sleep -Milliseconds 400
  if ([ShortcutSkate.Front]::GetForegroundWindow() -eq $h) { return 'brought to the front' }
  return 'still behind another window'
}
function Front($proc) {
  try {
    $proc.Refresh(); $h = $proc.MainWindowHandle
    if ($h -eq [IntPtr]::Zero) { return $null }
    return ToFront $h
  } catch { return 'not brought to the front: ' + $_.Exception.Message }
}
# A check that keeps the game in front is typed into a window from a distance, and whoever typed it
# can only type on in that window: it is noted here and brought back to the front at the end.
$typed = [IntPtr]::Zero
if ($pkeep) { try { $typed = InFront } catch { } }
# Steam keeps non-Steam shortcuts in a binary file. Returns the 64-bit id that steam://rungameid
# expects for the shortcut with this name, plus its target, or nothing when there is none.
function Find-Shortcut($name) {
  foreach ($f in (ls 'C:\Program Files (x86)\Steam\userdata\*\config\shortcuts.vdf' -ErrorAction SilentlyContinue)) {
    $t = [Text.Encoding]::GetEncoding(28591).GetString([IO.File]::ReadAllBytes($f.FullName))
    $idKey = [string][char]2 + 'appid' + [char]0; $nameKey = [string][char]1 + 'appname' + [char]0; $exeKey = [string][char]1 + 'exe' + [char]0
    $i = 0
    while (($i = $t.IndexOf($idKey, $i, [StringComparison]::OrdinalIgnoreCase)) -ge 0) {
      $at = $i + $idKey.Length
      $id = [uint64][byte]$t[$at] + ([uint64][byte]$t[$at + 1] -shl 8) + ([uint64][byte]$t[$at + 2] -shl 16) + ([uint64][byte]$t[$at + 3] -shl 24)
      $n = $t.IndexOf($nameKey, $at, [StringComparison]::OrdinalIgnoreCase)
      $i = $at
      if ($n -lt 0) { continue }
      $n += $nameKey.Length
      $app = $t.Substring($n, $t.IndexOf([char]0, $n) - $n)
      if ($app -ne $name) { continue }
      $e = $t.IndexOf($exeKey, $n, [StringComparison]::OrdinalIgnoreCase); $exe = ''
      if ($e -ge 0) { $e += $exeKey.Length; $exe = $t.Substring($e, $t.IndexOf([char]0, $e) - $e) }
      return [pscustomobject]@{ GameId = (($id -shl 32) -bor [uint64]0x02000000); Exe = $exe }
    }
  }
}
if (Get-Process RustClient -ErrorAction SilentlyContinue) { say 'ABORT: RustClient is already running'; return }
$present = $roots | ? { Test-Path (Join-Path $rust $_) }
if ($present) { say ('ABORT: loader files already in the Rust folder: ' + ($present -join ',')); return }
if (-not (Get-Process steam -ErrorAction SilentlyContinue)) { say 'ABORT: Steam is not running'; return }
$before = (ls $rust -Force | % Name | sort) -join '|'
New-Item -ItemType Directory -Force $run | Out-Null
$stage = "$probe\bep788"
# The archive is unpacked beside the stage and renamed when it is complete: an unpacking that was
# cut off must not pass for a loader.
if (-not (Test-Path "$stage\winhttp.dll")) {
  $fresh = "$stage-unpacking"
  Remove-Item -LiteralPath $fresh -Recurse -Force -ErrorAction SilentlyContinue
  Expand-Archive -LiteralPath $zip -DestinationPath $fresh -Force
  Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
  Rename-Item -LiteralPath $fresh -NewName (Split-Path $stage -Leaf)
}
$stRoots = ls $stage -Force | % Name
say ('stage roots: ' + ($stRoots -join ','))
try {
  Set-Content -LiteralPath $mark -Value (@('put there ' + (Get-Date -Format 's') + ' by session ' + $run) + ($prints | % { 'file ' + (Get-FileHash -LiteralPath (Join-Path $stage $_) -Algorithm SHA256).Hash + ' ' + $_ }))
  foreach ($n in $stRoots) { Copy-Item -LiteralPath (Join-Path $stage $n) -Destination $rust -Recurse }
  $bx = Join-Path $rust 'BepInEx'
  New-Item -ItemType Directory -Force "$bx\interop","$bx\config","$bx\plugins" | Out-Null
  Copy-Item "$gen\*" "$bx\interop"
  say ('interop files: ' + (ls "$bx\interop").Count + ' from ' + $gen.Replace($dl,'~'))
  if ($prt) { Copy-Item "$prt\*" "$bx\core" -Force; say ('overlay core: ' + ((ls $prt | % Name) -join ',')) }
  if ($psteps) { Set-Content "$bx\plugins\steps.txt" $psteps }
  if ($pplug) { Copy-Item "$pplug\*" "$bx\plugins" -Force; say ('plugins: ' + ((ls $pplug | % Name) -join ',')) }
  $cfg = "[IL2CPP]`r`nUpdateInteropAssemblies = false`r`nPreloadIL2CPPInteropAssemblies = " + ([bool]$ppreload).ToString().ToLower() + "`r`n`r`n[Logging]`r`nUnityLogListening = " + $plisten.ToString().ToLower() + "`r`n`r`n[Logging.Disk]`r`nLogLevels = All`r`n`r`n[Logging.Console]`r`nEnabled = false`r`n"
  [IO.File]::WriteAllText("$bx\config\BepInEx.cfg", $cfg)
  # The controller's reader runs beside the client as a process of its own: Steam must not have
  # started it, or it would see no more of the controller than the game does.
  $sticks = "$psticks"
  if (-not $sticks -and (Get-Command zg -ErrorAction SilentlyContinue)) {
    $sticks = "$probe\sticks.ps1"
    [IO.File]::WriteAllText($sticks, ("" + (zg 'sticks.ps1')), (New-Object Text.UTF8Encoding($false)))
  }
  if ($sticks -and (Test-Path -LiteralPath $sticks)) {
    $env:SKATE_PAD_SERVE = 'RustClient'
    $reader = Start-Process powershell -WindowStyle Hidden -PassThru -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$sticks`""
    $env:SKATE_PAD_SERVE = $null
    say 'controller reader started'
  } else { say 'no controller reader: keyboard only' }
  if ($psteam) {
    $sc = Find-Shortcut $(if ($pshortcut) { $pshortcut } else { 'RustClient' })
    if (-not $sc) { throw 'no Steam shortcut with that name; start without $psteam instead' }
    if ($sc.Exe -notlike '*\Rust\RustClient.exe*') { throw ('the Steam shortcut does not point at the Rust client: ' + $sc.Exe) }
    say ('starting through the Steam shortcut: ' + $sc.Exe)
    Start-Process ('steam://rungameid/' + $sc.GameId)
    $p = $null; $w0 = Get-Date
    while (-not $p -and ((Get-Date) - $w0).TotalSeconds -lt 90) { Start-Sleep 2; $p = Get-Process RustClient -ErrorAction SilentlyContinue | select -First 1 }
    if (-not $p) { throw 'RustClient did not start through Steam within 90 s' }
  } else {
    $go = @{ FilePath = (Join-Path $rust 'RustClient.exe'); WorkingDirectory = $rust; PassThru = $true }
    if ($pconnect) {
      if (-not (Test-Path "$probe\server.txt")) { throw 'no server.txt in the staging folder; start without $pconnect instead' }
      $go.ArgumentList = '+connect', (Get-Content "$probe\server.txt" -First 1).Trim()
      say 'joining the server named in server.txt at start'
    }
    $p = Start-Process @go
  }
  $log = "$bx\LogOutput.log"
  $t0 = Get-Date; $seen = $null; $state = 'timeout-before-chainloader'; $front = $null; $fronts = 0
  $limit = 240; if ($pdone) { $limit = [int]$pdone }
  while (((Get-Date) - $t0).TotalSeconds -lt $limit) {
    Start-Sleep 3
    if ($p.HasExited) { $code = try { $p.ExitCode } catch { '?' }; $state = 'exited code=' + $code + ' at ' + [int]((Get-Date) - $t0).TotalSeconds + 's'; break }
    if (-not $seen -and (Test-Path $log)) {
      $txt = Get-Content $log -Raw -ErrorAction SilentlyContinue
      if ($txt -match 'Chainloader startup complete') { $seen = Get-Date }
    }
    if ($seen -and ($pkeep -or ($front -notin 'in front','brought to the front' -and $fronts -lt 20))) {
      $was = $front; $now = Front $p
      if ($now) { $front = $now; $fronts++; if ($front -ne $was) { say ('the game window: ' + $front) } }
    }
    if ($pdone -and (Test-Path "$bx\plugins\skate.done")) { $state = 'plugin reported done at ' + [int]((Get-Date) - $t0).TotalSeconds + 's; responding=' + $p.Responding; break }
    if (-not $pdone -and $seen -and ((Get-Date) - $seen).TotalSeconds -ge $pwait) { $state = 'alive ' + $pwait + 's after chainloader; responding=' + $p.Responding; break }
  }
  if ($seen -and $state -eq 'timeout-before-chainloader') { $state = 'ended at its time limit of ' + $limit + ' s' }
  say ("RESULT[$ptag]: " + $state + '; chainloaderSeen=' + [bool]$seen)
  if (-not $p.HasExited) { $p.CloseMainWindow() | Out-Null; Start-Sleep 5; if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }; Start-Sleep 3 }
  Get-Process UnityCrashHandler64 -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
} finally {
  Start-Sleep 2
  # The reader ends by itself when the client has closed; one that never saw a client is ended here.
  if ($reader -and -not $reader.HasExited) { Stop-Process -Id $reader.Id -Force -ErrorAction SilentlyContinue }
  $bx = Join-Path $rust 'BepInEx'
  # Copies of the logs first. A copy that fails must not keep the loader in the Rust folder.
  try {
    if (Test-Path "$bx\plugins\skate.log") { Copy-Item "$bx\plugins\skate.log" $run -ErrorAction SilentlyContinue }
    foreach ($f in 'LogOutput.log','ErrorLog.log') { if (Test-Path "$bx\$f") { Copy-Item "$bx\$f" $run -ErrorAction SilentlyContinue } }
    ls $rust -Filter 'preloader_*.log' -ErrorAction SilentlyContinue | % { Copy-Item $_.FullName $run -ErrorAction SilentlyContinue }
    $pl = "$HOME\AppData\LocalLow\Facepunch Studios LTD\Rust\Player.log"
    if (Test-Path $pl) { Copy-Item $pl $run -ErrorAction SilentlyContinue }
  } catch { say ('log copies: ' + $_.Exception.Message) }
  New-Item -ItemType Directory -Force "$run\removed" | Out-Null
  # The loader goes out whatever happened. A client that is still shutting down, or its crash
  # handler, can hold a file in the loader's folder for a while (ErrorLog.log was seen held after
  # a normal exit): a move that fails is tried again for a minute, those processes are ended in
  # between, and what is still there at the end is said out loud, not thrown past the rest.
  $left = @(); $why = ''; $t1 = Get-Date
  while ($true) {
    $left = @()
    foreach ($n in ($roots + (ls $rust -Filter 'preloader_*.log' -ErrorAction SilentlyContinue | % Name))) {
      $src = Join-Path $rust $n
      if (-not (Test-Path -LiteralPath $src)) { continue }
      try { Move-Item -LiteralPath $src -Destination "$run\removed" -ErrorAction Stop }
      catch { $left += $n; $why = $_.Exception.Message }
    }
    if ($left.Count -eq 0 -or ((Get-Date) - $t1).TotalSeconds -gt 60) { break }
    Get-Process RustClient, UnityCrashHandler64 -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep 3
  }
  $after = (ls $rust -Force | % Name | sort) -join '|'
  if (-not ($roots | ? { Test-Path -LiteralPath (Join-Path $rust $_) })) { Remove-Item -LiteralPath $mark -ErrorAction SilentlyContinue }
  say ('rollback clean=' + ($before -eq $after))
  if ($before -ne $after) { say "before=$before"; say "after=$after" }
  if ($left.Count -gt 0) {
    say ('WARNING: still in the Rust folder: ' + ($left -join ',') + ' (' + $why + ')')
    say 'Run zz clean, or "Rust Skate - remove mod files.cmd" on the desktop, before Rust is started normally.'
  }
  if ($typed -ne [IntPtr]::Zero) {
    $back = try { ToFront $typed } catch { 'not brought to the front: ' + $_.Exception.Message }
    if ($back -ne 'in front') { say ('the window this was typed into: ' + $back) }
  }
}
$global:plast = $run
say "run dir: $run"
if (Test-Path "$run\LogOutput.log") {
  $l = Get-Content "$run\LogOutput.log"
  say ('log lines=' + $l.Count)
  $l | ? { $_ -notmatch 'DobbyDetour|NativeDetour' } | select -Last $(if ($ptail) { $ptail } else { 26 }) | % { if ($_.Length -gt 1500) { $_.Substring(0,1500) } else { $_ } }
} else { say 'no LogOutput.log' }
if (Test-Path "$run\skate.log") {
  $pl = @(Get-Content "$run\skate.log" | % { if ($_.Length -gt 1900) { $_.Substring(0,1900) } else { $_ } })
  say ('--- skate.log lines=' + $pl.Count)
  if ($pl.Count -le 34) { $pl } else { $pl | select -First 8; '...'; $pl | select -Last 24 }
  $global:pfile = "$run\skate.log"; $global:poff = 0
}
if (Test-Path "$run\ErrorLog.log") { say '--- ErrorLog'; Get-Content "$run\ErrorLog.log" | select -First 12 }
