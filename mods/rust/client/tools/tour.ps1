# Scripted ride over the server's own park pieces, sound muted: joins the private test server, gets
# on the board and rides to each piece the client found near the spawn and across it, logging per
# piece how high the board rose, its air, grinds, bails and put-backs (`TOUR` lines). Ends by itself.
$global:ptag='T'; $global:psteps='mute,tour'; $global:pdone=600; $global:psteam=$false; $global:pconnect=$true; $global:pkeep=$true; $global:plisten=$false; $global:prt="$HOME\Downloads\claude-loader-probe\rt"; $global:pplug="$HOME\Downloads\claude-loader-probe\plugin"; $global:pwait=30; $global:ppreload=$false; $global:ptail=4
$global:pnext='session'; zz build
