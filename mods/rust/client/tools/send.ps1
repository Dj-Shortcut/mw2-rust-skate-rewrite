# Scripted check in a world, sound muted: a chat line and skate.status sent to the server by the
# network the way the game's console sends them, not typed. The chat line shows on the screen when
# it arrives. Joins the private test server at start; ends by itself.
$global:ptag='S'; $global:psteps='mute,send'; $global:pdone=300; $global:psteam=$false; $global:pconnect=$true; $global:pkeep=$true; $global:plisten=$false; $global:prt="$HOME\Downloads\claude-loader-probe\rt"; $global:pplug="$HOME\Downloads\claude-loader-probe\plugin"; $global:pwait=30; $global:ppreload=$false; $global:ptail=4
$global:pnext='session'; zz build
