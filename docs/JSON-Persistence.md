# Local JSON persistence

The desktop app now saves one local gym's state between runs. This is file persistence for the existing desktop prototype: one process owns the store, and its windows share one GymSession. It does not introduce a remote client, external database, or coordination between separate app processes.

## Location and demo options

The default file is `GymQ/gym-state.json` under the platform's `Environment.SpecialFolder.LocalApplicationData` directory. It is outside the repository and does not depend on the current working directory.

- Set `GYMQ_DATA_PATH` to a JSON file path to use a separate local store.
- Set `GYMQ_DISABLE_PERSISTENCE=1` to run the seeded demo entirely in memory. Closing that run discards its changes and demo-time offset.
- Set `GYMQ_TWO_DEVICES=1` to open the existing two-window demo. Both windows share the same GymSession and store; they are not independent remote devices.

Examples from the repository root:

```sh
GYMQ_DATA_PATH=/tmp/gymq-demo/gym-state.json dotnet run --project src/GymQ.Desktop/GymQ.Desktop.csproj
GYMQ_DISABLE_PERSISTENCE=1 dotnet run --project src/GymQ.Desktop/GymQ.Desktop.csproj
GYMQ_TWO_DEVICES=1 dotnet run --project src/GymQ.Desktop/GymQ.Desktop.csproj
```

The first persistent run uses the normal sample state when neither a primary file nor a backup exists. Existing saved state is restored instead of being seeded again. Every run starts at the login screen: the signed-in account, page, overlay instance, form contents, and credentials are not saved.

## Saved state and responsibilities

The versioned snapshot contains equipment and its status; complete session history with original IDs, start/end times and reasons; all fault reports with review details and the report-number counter; queue entries in FIFO order with their joined and claim-notification timestamps; pending nudges and their deadlines; nudge-cooldown timestamps keyed by session and requesting member; and unacknowledged queue-cancellation notices. `ClockOffset` and `AdvancedBy` retain the shared demo-time advance across persistent restarts. The computer clock is unchanged.

- `GymStateSnapshot` and `SessionState` define the saved data. `GymStateValidator` checks the schema, identities, references, timestamps, ownership and queue/session invariants before restoration or saving. Every saved field must be present, including nullable fields whose value is `null`; incomplete files are rejected rather than silently resetting part of the state.
- `IGymStateStore` separates storage from the coordinator. `JsonGymStateStore` reads and writes JSON, manages the backup, and holds the exclusive writer lease.
- `LegacyGymStateMigration` validates the original version 1 format and converts it to the current version 3 snapshot before restoration. The store preserves the original bytes before saving the upgrade.
- Service persistence partials export detached copies and restore authoritative service data without generating new IDs, restarting timers, or notifying queues during hydration. Restored report objects are shared with GymSession's report list so subsequent staff review stays consistent.
- `GymSession.OpenPersistent` loads, validates, restores and reconciles deadlines before screens use the state, then subscribes saving to business changes. Ordinary `new GymSession(...)` construction remains in memory, so existing service/UI tests do not open the user's file.
- `App` chooses the path and owns the store until shutdown. The shell uses the existing error banner to show persistence problems.

Business transitions and actual timeout changes trigger saves; countdown-only UI ticks do not. Demo-time advances still process intermediate one-second steps, but save once after the complete advance. Startup reconciliation is saved, and normal shutdown makes a final save attempt before releasing the store.

## Deadlines while the app is closed

Restoration uses real elapsed time plus the retained demo offset. For each active session, the earliest applicable nudge deadline or 30-minute limit ends the session if it has passed. The recorded end time is that original deadline, not the restart time, and the corresponding end reason is retained. Out of Service equipment remains unavailable.

An unexpired offered claim keeps its original notification time and remaining window. If an already offered claim expired while the app was closed, that member is removed. The next waiting member receives a fresh claim window at startup; members who were never offered a turn while offline are not silently expired in sequence. FIFO order otherwise remains unchanged. A waiting queue is offered a turn only when its equipment is available.

Current version 3 nudge cooldowns are restored even when there is no pending popup, including after a Still Using response or after the requesting member leaves the queue. Reopening does not restart or bypass the original five-minute cooldown. Pending version 3 nudges retain their original two-minute response deadline.

## Upgrading original version 1 saves

A complete, valid version 1 save is accepted automatically. Equipment, session history, fault reports and their numbering, FIFO queue entries and original claim timestamps, cancellation notices, and the demo clock offset are preserved. The normal deadline reconciliation above still applies to time spent offline. Loading alone does not rewrite any data; the first successful startup save writes version 3.

Version 1 nudge records contain only equipment, target member and expiration; its cooldown ledger contains only equipment and timestamp. Neither records the requesting member or session ID required by the current policy. Queue membership may have changed since the request. The upgrade therefore retires these legacy pending nudges and equipment cooldowns without inventing an owner or ending a session because of an old nudge. This exception applies only to migration from version 1; complete version 3 nudge records and cooldowns are restored normally. Malformed legacy nudge records still cause validation failure before migration.

Before replacing a version 1 source, the store archives its exact original bytes as `gym-state.json.v1.<id>.bak` (or the configured filename with that suffix). This archive also covers a version 1 backup used to recover a missing or corrupt primary. It remains intact when the normal `.bak` file rotates on later saves. An archive failure prevents replacement of the original source. Only the known released version 1 and current version 3 formats are accepted; version 2 and unknown future formats remain preserved and unsupported.

## File protection and failure behavior

Saves write and flush a temporary file in the same directory, then replace the primary file. The previous validated primary is retained as `gym-state.json.bak` (or the configured filename plus `.bak`). If the primary is corrupt, loading can recover a validated backup. Before replacing a corrupt primary after successful recovery, the store preserves its original bytes in a `.corrupt.<id>` copy. An unsupported schema version is preserved and is not overwritten or silently downgraded.

The store holds an exclusive writer handle on the filename plus `.lock` until shutdown. The sidecar file remains afterward; ownership depends on the open handle, not whether the file exists. A second app process cannot write the same store while that lease is held.

If initial loading fails because the data, backup, path or writer lease cannot be opened safely, App preserves the existing files and starts an unseeded, memory-only GymSession. Equipment and built-in accounts remain available. A persistent warning appears through the existing error banner: this fallback run never writes over the saved data, and dismissing an ordinary action error does not remove the startup warning.

If a later save fails, the completed business action stays in memory and the banner warns that the latest changes are not saved. The app retries on subsequent save triggers; a successful save clears that persistence error. Closing before a successful save can lose those unsaved changes.
