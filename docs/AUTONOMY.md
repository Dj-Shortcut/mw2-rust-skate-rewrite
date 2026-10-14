# Standing project authorization

The standalone Rust/Bevy rewrite and its automatic loop remain parked since
5 October 2026; do not resume paused issues without a new owner direction.
On 6 October the owner set the existing-Rust PC mod goal as MW2-inspired gunplay and
skateboarding ([RUST-MW2-SKATE.md](RUST-MW2-SKATE.md), issue #275). **On 14 October 2026
he dropped the gunplay**: the goal is the skate mod alone, as good as possible, for the
owner and his friends on his own server without anti-cheat (it will never run on EAC
servers and is not a product for third parties). Skate feel and controls lead, with the
new skate. as primary reference and Skate 3 secondary ([complete goal](RUST-MW2-SKATE.md),
[first demo](RUST-SKATE-DEMO.md), issue #408). No new work on gunplay, operators, loadouts
or damage (#266, RUST-SERVER-MOD); that source stays as it is. Implementation, debugging
and verification of the skate mod are authorized. Preserve historical work; use open-source
code and original authored content, retaining licences.
Keep game/server files outside GitHub, player text English and historical rewrite work.

Continue from `TODO.md` and open tasks without repeating content confirmations.
The authorization includes project GitHub issues and coordination comments,
isolated feature branches, source commits, pushes and focused PRs. Integrate
routine changes after appropriate verification and review, respecting each open
task's ownership and merge instructions. Development source may be published
before the full product is finished; describe its actual state.

## The owner decides task assignments

The human project owner decides which agent does each task and may reassign work.
Follow the owner's latest explicit assignment in the conversation or issue;
record it in the relevant issue/PR with the branch and owned files. Agent names,
old handoffs, backlog entries and issue creation do not assign current work.
Do not appoint an integrator, delegate to another agent or transfer ownership
without the owner's direction. A request to work solo means no delegation.

Before editing an assigned task, check existing issues and PRs to avoid duplicate
work. Use isolated worktrees or checkouts; never push to another agent's branch.
Keep unrelated work in separate PRs; a focused correction may use a clearly
identified stacked PR. Preserve existing work and shared public APIs when the
owner changes assignments, and update the relevant issue/PR to prevent overlap.
Historical attribution remains evidence of who reported or completed earlier
work; it does not reserve future tasks for that agent.

When the owner authorizes parallel work, use distinct files and isolated
worktrees for the assigned agents. The owner also chooses any integrator for
shared files. Preparation and review can run alongside coding within that scope.
Use one build owner per shared Cargo target directory and freeze the integrated
source during verification. Report actual results before committing or merging.

Verify changes yourself; the owner does not test intermediate builds. Select
checks that exercise the actual changed behavior, then record evidence and
remaining limits. Temporary probes and logs belong in ignored `context/` and need
no separate permission per check. The existing named-owner-approval policy for
new permanent tests in `crates/approved_tests` remains unchanged.

Keep `TODO.md` honest about code present, headless verification, graphical
verification and release readiness. A compiler check or software-GPU run proves
only its stated scope. Deliver a release only after the agreed product criteria
and complete user flows are verified, with required assets, packaging and start
instructions. An unfinished development build is not a finished product.

The owner's "Full access" setting authorizes work within this scope; host sandbox
rules, available tools and active-session limits still apply. If an actual
platform permission blocks an action, report the action and the stated reason,
and use the platform's permission mechanism when needed. This document does not
keep agents running after sessions stop or grant access to an unavailable app.
Resume using the latest issues, PRs, `TODO.md` and repository instructions.
