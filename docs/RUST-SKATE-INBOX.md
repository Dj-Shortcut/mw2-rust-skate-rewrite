# Server-to-client echo diagnostic

Refs [#337](https://github.com/Dj-Shortcut/mw2-rust-skate-rewrite/issues/337#issuecomment-6142422510).
The client cannot currently read its held park item reliably. A client-readable
identity route must be demonstrated before implementing the agreed kind signal.
The client keeps the park preview cleared when identity is unknown.

## Bounded sender contract

The separate planned `ShortcutSkateProbe.cs` Oxide plugin supplies
`skate.probe <sequence>`. A connected in-game server administrator invokes it
from the Rust console. Server console/RCON without a player, disconnected
callers and ordinary players are refused. There is no target-player argument.

Accept exactly one positive uint token of at most ten ASCII decimal digits;
reject zero, signs, whitespace, extra arguments and overflow. Normalize the
number with invariant culture and call the owning player's native
`SendConsoleCommand("echo", "SKATE-PROBE " + sequence)`. This sends no item,
world, account or session data. There are no timers or automatic broadcasts.
Error replies contain no probe marker; only the intended echo contains it.

The diagnostic has no placement, payment, inventory, skin or save behavior.
It can be installed and removed separately from `ShortcutSkate`; it requires
no park-plugin upgrade, server-setting change or world-data migration.
Client receiver/build/native work remains Claude-owned, server source Codex-owned.

## Native verification to perform after source review

On the authorized Rust/Oxide test server, compile/load the separate source
with the matching genuine server references. Record the source hash and actual
Rust/Oxide versions. Start the current client with Unity log listening enabled
(`sv plisten 1; zz play`) as documented in the [client tools](../mods/rust/client/tools/README.md).
Confirm the inbox reports watching/reading, then issue `skate.probe 1` from
the connected administrator's in-game console. Record the corresponding new
`INBOX` line containing `SKATE-PROBE 1` and elapsed receipt time, or its absence.
Repeat with a different sequence; old/log-only markers are not new receipts.

Verify an ordinary player and malformed requests produce no new probe line;
with a second connected client, confirm only the invoking admin receives it.
Unload with `oxide.unload ShortcutSkateProbe`, remove that source from
`oxide/plugins/`, and verify the command is gone. Keep private logs outside GitHub.

## Evidence and next gate

This document records the agreed diagnostic design; the sender is not implemented
in this initial documentation commit. No native compilation, installation or
received-message result is claimed. Claude reports the main client inbox reading
in a live session, but no server probe has yet been sent or received.

An echo receipt proves only this route for that recorded build. The inbox acts
on nothing. It is not an authenticated item-identity receiver or release evidence.
Only after a genuine receipt should the separate version/session/sequence/UID/kind,
explicit-clear and expiry contract be implemented. Native placement/payment checks
remain authoritative; picker choice and arbitrary log text never establish identity.
