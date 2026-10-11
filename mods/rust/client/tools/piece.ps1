# Scripted check of the console typist: the plugin asks the server for a bank piece by typing
# skate.piece bank into the F1 console, and leaves the console open so the answer can be seen.
# Joins the private test server at start; press a key to wake the player.
$global:ptag='C'; $global:psteps='mute,piece'; $global:pdone=1500; $global:psteam=$false; $global:pconnect=$true; $global:pkeep=$true; $global:plisten=$false; $global:prt="$HOME\Downloads\claude-loader-probe\rt"; $global:pplug="$HOME\Downloads\claude-loader-probe\plugin"; $global:pwait=30; $global:ppreload=$false; $global:ptail=4
$global:pnext='session'; zz build
