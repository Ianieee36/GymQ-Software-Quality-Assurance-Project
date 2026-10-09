# Studio UI integration foundation

GymSession coordinates queue, session, and fault-report actions through the existing services. SessionService and FaultReportService receive the same InMemoryEquipmentRepository instance. The desktop app now adds [local JSON persistence](JSON-Persistence.md) around this shared state; no external database is introduced.

SessionService now enforces one active session per member inside its existing lock. Both direct starts and queue claims use this check; rejected claims retain their queue entry and original timeout. Other existing service method bodies are preserved. QueueService and SessionService are now partial classes so presentation helpers can live in separate files. Queue helpers provide a copied queue snapshot and a leave action. Session helpers expose active sessions and session history for the UI.

GymSession supports start, join, leave, claim, finish, nudge/response, report/review, and timeout processing. It preserves immediate session start on claim and the existing equipment-level nudge cooldown. Maintenance completion is outside this phase’s scope.

## Nudge cooldown policy (GQ-04)

On 8 October 2026, Our team have confirmed that the five-minute nudge cooldown is intentionally shared per equipment. GQ-04 is closed as an accepted business rule. This decision supersedes the per-member/per-equipment cooldown wording in the revised FR-003 in Part 1 (pages 39–40) for the current prototype; the original report remains a historical baseline.

The goal is to protect the current equipment user from repeated nudges by successive queued members. Only the front member may nudge, and all members use the same last-successful-nudge timestamp for that equipment.

- A successful nudge starts the equipment's five-minute cooldown. Another nudge is allowed at or after five minutes from that successful request.
- Invalid requests and rejected attempts do not start, restart or extend the cooldown.
- Leaving, rejoining, replacing the front member, session handover and queue cancellation retain the equipment timestamp. These actions cannot bypass the cooldown.
- Each equipment item has its own independent cooldown. A nudge on one machine does not block another machine.
- All pages and signed-in accounts share one GymSession and QueueService, so account switching retains the cooldown. The desktop app now persists that state, including cooldowns, across restarts by default. Closing an explicitly disabled-persistence demo still resets its in-memory state; see [local JSON persistence](JSON-Persistence.md).

The response deadline is a separate timer managed by GymSession; it does not control the cooldown. The current desktop footer says "One nudge per machine every 5 minutes", and rejected attempts report the same equipment-wide restriction. The separate GQ-08 issue concerns button eligibility and stale feedback; the service still enforces this policy.

This policy is suitable for the current single-instance desktop prototype: it limits interruptions, preserves FIFO eligibility, and avoids a new front member immediately repeating a recent nudge. The shared cooldown is an intentional trade-off even when that member has not personally nudged before. [Local JSON persistence](JSON-Persistence.md) now retains the cooldown across restarts. Coordination across separate app instances remains outside this prototype's guarantees; one process holds the writer lease for each file.

`SendNudge_NewFrontMember_SharesEquipmentCooldown` verifies the coordinator workflow, and EquipmentNudgeCooldownTests verifies exact clock boundaries, rejected attempts and queue/session transitions. Existing original queue tests cover independent equipment cooldowns and front-member eligibility.

## Integration setup

Sample equipment and member data are included for development and testing. Use new GymSession(seed: false) to start without seeded sessions or reports; equipment and member data remain available.

MainWindow now creates one ShellViewModel and GymSession shared by all screens. Its one-second UI timer calls Tick and stops when the window closes. The Equipment screen links to details, sessions, queues, and reports; Profile provides demo account selection and staff review access. Coordinator actions run on the UI thread. This layer does not fix concurrent access to the original QueueService.

- Create one GymSession instance shared by all screens.
- Call Tick() regularly from the UI timer.
- Refresh ViewModels when the Changed event fires.
- Run all coordinator actions on the UI thread.

This layer does not add support for concurrent access to QueueService.

## Validation

Run from the repository root:

`dotnet build GymQ.slnx -c Release`
`dotnet test GymQ.slnx -c Release`

GymSessionIntegrationTests covers shared equipment data, reserved turns, handover, claim, nudge cooldown and timeout, claim expiry, and staff review.



EquipmentUiTests verifies rendered screens, start/end, queue entry, member switching, nudge response, claim, reporting the selected machine, and staff confirmation. Screenshots are written to the system temporary directory under gymq-equipment-ui-screenshots.


## Demo time controls

Profile includes +1, +2, and +30 minute controls. A shared TimeProvider advances sessions, queue deadlines, nudge cooldowns, report timestamps, and UI countdowns without changing the computer clock. Time continues normally between clicks. Advances process one-second steps so earlier handovers and subsequent claim expiries occur in order. All accounts share the offset; the desktop app now saves it with the gym state and restores it on restart. Closing an explicitly disabled-persistence demo still resets the offset and in-memory data. See [local JSON persistence](JSON-Persistence.md) for these modes and offline deadline handling. QueueService and FaultReportService accept an optional clock; existing callers default to system time.
