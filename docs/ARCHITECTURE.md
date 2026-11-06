# Architecture and next milestones

`Trainer.Core` holds versioned data profiles, a memory-target interface, patch
ownership/rollback, and signed update-package installation. Its tests use buffers
and simulated failures, not an external game.

`Trainer.Desktop` is a Windows Forms shell. Its browser dashboard is a read-only
loopback view of explicit app state and the most recent 200 messages. Data is
rendered as text, not HTML. It starts no public listener and sends no telemetry.

`Trainer.Cli` provides reproducible demo and file verification commands.

## Product direction

1. Windows trial of the packaged app, dashboard, diagnostics and update UI.
2. Choose an authenticated hosted status relay and distribution host. Add explicit
   start/stop sharing, short-lived session IDs, minimal status payloads and
   visible connection state. This is needed for assistant-visible live status.
3. Add a Windows memory adapter and process picker for a separately selected,
   supported offline game or a purpose-built local test game.
4. Add profile-generated feature controls, sliders with per-feature limits, and
   recovery reporting. Unknown versions stay read-only.
5. Add hosted release discovery, key rotation and release signing outside the
   client. No arbitrary scripts, plugins or remote command execution in updates.

Current limitations: no process synchronization or freeze-and-patch strategy,
no game adapter, no pointer scanner, no pattern scanning, no native hooks,
no save backup implementation, no hosted service, no remote assistant session.
The initial app must not be described as a finished universal game trainer.
