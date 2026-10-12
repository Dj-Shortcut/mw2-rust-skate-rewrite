# Scripted check of the whole placement route, sound muted: `skate.place bank` sent to the server
# by the network where the player looks, then the server's blocks and the model drawn over them are
# waited for and reported in the log. Needs ShortcutSkate 0.3.6 or later on the server; the player
# must face open ground. Joins the private test server at start; ends by itself.
$global:ptag='L'; $global:psteps='mute,place'; $global:pdone=300; $global:psteam=$false; $global:pconnect=$true; $global:pkeep=$true; $global:plisten=$false; $global:prt="$HOME\Downloads\claude-loader-probe\rt"; $global:pplug="$HOME\Downloads\claude-loader-probe\plugin"; $global:pwait=30; $global:ppreload=$false; $global:ptail=4
$global:pnext='session'; zz build
