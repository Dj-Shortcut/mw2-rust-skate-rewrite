# Build the head of the branch and play it with keyboard, mouse and controller, sound on. The
# client is started through the owner's Steam shortcut "RustClient", as the launcher does, so that
# the controller layout of that shortcut applies on foot as well as on the board; a client started
# directly gets no Steam Input, and the controller then only works on the board. Pick the server
# under QUICK JOIN in the main menu, or give the shortcut `+connect <address>` as its launch
# options in Steam once. Quitting Rust ends the session; it is ended after an hour at the latest,
# or $plimit seconds after its start. $pquiet turns the sound down. $pdirect=1 starts the client
# directly instead and joins the private test server at start (keyboard and mouse only). $plisten=1
# keeps UnityLogListening on, which the inbox probe needs.
$global:ptag='P'; $global:psteps='skate'; if ($pquiet) { $global:psteps='mute,skate' }; $global:pdone=3600; if ($plimit) { $global:pdone=[int]$plimit }; $global:pkeep=$false; $global:plisten=("$plisten" -in '1','true'); $global:prt="$HOME\Downloads\claude-loader-probe\rt"; $global:pplug="$HOME\Downloads\claude-loader-probe\plugin"; $global:pwait=30; $global:ppreload=$false; $global:ptail=4
if ($pdirect) { $global:psteam=$false; $global:pconnect=$true } else { $global:psteam=$true; $global:pconnect=$false }
$global:pnext='session'; zz build
