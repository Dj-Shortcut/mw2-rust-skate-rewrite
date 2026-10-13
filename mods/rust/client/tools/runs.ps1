# The recent session folders, the owner's launcher sessions among them, with how much was ridden
# in each: right-stick movements, and of those how many did nothing on the board. `$prun = name`
# makes that folder the one `log`, `logall`, `file` and `flicks` read.
$probe = "$HOME\Downloads\claude-loader-probe"
if ($prun) {
  $pick = "$probe\$prun"
  if (-not (Test-Path $pick)) { "no folder $prun"; return }
  $global:plast = $pick; $global:prun = $null
  "now reading $prun"; return
}
$all = @(Get-ChildItem $probe -Directory -Filter 'run-*' -ErrorAction SilentlyContinue | Sort-Object CreationTime | Select-Object -Last 20)
if ($all.Count -eq 0) { 'no session folder yet'; return }
foreach ($r in $all) {
  $log = "$($r.FullName)\skate.log"
  $moves = 0; $none = 0; $board = 0
  if (Test-Path $log) {
    foreach ($l in (Get-Content $log)) {
      if ($l -notmatch '^PAD stick \d+ ms, (.+?) -> (.+?) \|') { continue }
      $moves++
      if ($Matches[1] -ne 'on foot') { $board++; if ($Matches[2] -eq 'nothing') { $none++ } }
    }
  }
  "{0}  {1}  stick {2}, on the board {3}, nothing {4}" -f $r.Name, $r.CreationTime.ToString('MM-dd HH:mm'), $moves, $board, $none
}
