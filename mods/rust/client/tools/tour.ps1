# Scripted ride over the server's own park pieces, sound muted: joins the private test server, gets
# on the board and rides to each piece the client found near the spawn and across it, logging per
# piece how high the board rose, its air, grinds, bails and put-backs (`TOUR` lines). Ends by itself.
# $tourto = 'x/z' first rides there on the board, for a spawn out of the park's reach.
$global:ptag='T'; $global:psteps='mute,' + $(if ($global:tourto) { "to:$global:tourto," } else { '' }) + 'tour'; $global:pdone=600; $global:psteam=$false; $global:pconnect=$true; $global:pkeep=$true; $global:plisten=$false; $global:prt="$HOME\Downloads\claude-loader-probe\rt"; $global:pplug="$HOME\Downloads\claude-loader-probe\plugin"; $global:pwait=30; $global:ppreload=$false; $global:ptail=4
$global:pnext='session'; zz build
