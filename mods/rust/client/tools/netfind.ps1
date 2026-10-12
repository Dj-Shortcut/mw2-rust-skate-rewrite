# The types of the network library's listing (console-net.txt, written by the send check) that the
# console path is looked for in: every type with a method taking the packet enum (its parameter
# type is named Type) or a string and a bool, every struct, and every type with a static member,
# each with its members. Page with `zz more`.
$f = Get-ChildItem $plast -Recurse -Filter 'console-net.txt' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $f) { $f = Get-ChildItem "$HOME\Downloads\claude-loader-probe" -Recurse -Filter 'console-net.txt' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1 }
if (-not $f) { 'no console-net.txt; run zz send first'; return }
$blocks = @(); $cur = $null
foreach ($line in (Get-Content $f.FullName)) {
  if ($line -like 'TYPE *') { if ($cur) { $blocks += ,$cur }; $cur = @($line) } elseif ($cur) { $cur += $line }
}
if ($cur) { $blocks += ,$cur }
$keep = @()
foreach ($b in $blocks) {
  $head = $b[0]; $body = $b[1..($b.Count)] -join "`n"
  if ($head -like '* enum') { if ($body -match 'ConsoleCommand') { $keep += ($b -join "`n") }; continue }
  if ($body -match '\(Type[,)]' -or $body -match '\(String, Boolean\)' -or $head -like '* struct' -or $body -match 'static (field|property)') { $keep += ($b | Select-Object -First 60) -join "`n" }
}
$view = "$HOME\Downloads\claude-loader-probe\view.txt"
'file=' + $f.FullName + ' types=' + $blocks.Count + ' kept=' + $keep.Count | Out-File $view -Encoding utf8
$keep | Out-File $view -Encoding utf8 -Append
$global:pfile = $view; $global:poff = 0
zz more
