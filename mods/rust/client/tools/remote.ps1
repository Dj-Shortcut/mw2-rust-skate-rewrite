# Helpers for an agent that works on this PC through typed keys, where a long command is slow and
# some characters cannot be typed at all: `zz name` runs name.ps1 from this folder and `zg file`
# returns a file from it. Both read the branch named in $pref (default main) through the GitHub
# contents API: the raw host caches a branch for minutes and would hand out a stale script.
if (-not $pref) { $global:pref = 'main' }
$global:prepo = 'Dj-Shortcut/mw2-rust-skate-rewrite'
# The address of a file at a commit, on the raw host: a commit cannot be stale there, and the API
# answers a caller without a key only sixty times an hour, which three builds would use up.
function global:zu($path, $commit) { "https://raw.githubusercontent.com/$prepo/$commit/$path" }
# When the API refuses (its hourly limit for a caller without a key), the raw host is asked
# instead: it may hand out a branch a few minutes old, which beats no script at all.
function global:zg($f) {
  try { irm -Headers @{ Accept = 'application/vnd.github.raw' } ("https://api.github.com/repos/$prepo/contents/mods/rust/client/tools/{0}?ref={1}" -f $f, $pref) -ErrorAction Stop }
  catch { irm ("https://raw.githubusercontent.com/$prepo/$pref/mods/rust/client/tools/{0}" -f $f) }
}
function global:zz($n) { iex (zg "$n.ps1") }
$global:ErrorActionPreference = 'Continue'
"zz ready, branch $pref"
