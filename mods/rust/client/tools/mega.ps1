# Scripted ride in a MEGA halfpipe drawn around the rider, sound muted: pushed to speed on the flat,
# then left to roll; the log says how far up each transition it went. Nothing is placed on the
# server; the model is this client's only. Joins the private test server at start.
$global:ptag='M'; $global:psteps='mute,mega'; $global:pdone=900; $global:psteam=$false; $global:pconnect=$true; $global:pkeep=$true; $global:plisten=$false; $global:prt="$HOME\Downloads\claude-loader-probe\rt"; $global:pplug="$HOME\Downloads\claude-loader-probe\plugin"; $global:pwait=30; $global:ppreload=$false; $global:ptail=4
$global:pnext='session'; zz build
