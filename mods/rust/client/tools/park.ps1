# Scripted check in a world, sound muted: the authored park models one at a time ahead of the
# rider, seen from a fixed camera (K steps on), then a ride up the bank. Nothing is placed on the
# server; the models are this client's only. Joins the private test server at start.
$global:ptag='K'; $global:psteps='mute,park'; $global:pdone=900; $global:psteam=$false; $global:pconnect=$true; $global:pkeep=$true; $global:plisten=$false; $global:prt="$HOME\Downloads\claude-loader-probe\rt"; $global:pplug="$HOME\Downloads\claude-loader-probe\plugin"; $global:pwait=30; $global:ppreload=$false; $global:ptail=4
$global:pnext='session'; zz build
