# Scripted check in a world, sound muted: ollies, flips, spins, a grab and two presses that come
# too late, seen from a fixed camera. Ends by itself after half a minute. Joins the private test
# server at start; press a key to wake the player.
$global:ptag='T'; $global:psteps='mute,trick'; if ($pyaw) { $global:psteps += ',yaw' + $pyaw }; $global:pdone=1500; $global:psteam=$false; $global:pconnect=$true; $global:pkeep=$true; $global:plisten=$false; $global:prt="$HOME\Downloads\claude-loader-probe\rt"; $global:pplug="$HOME\Downloads\claude-loader-probe\plugin"; $global:pwait=30; $global:ppreload=$false; $global:ptail=4
$global:pnext='session'; zz build
