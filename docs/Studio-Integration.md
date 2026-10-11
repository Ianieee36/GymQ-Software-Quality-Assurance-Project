# Studio UI integration foundation

GymSession coordinates queue, session, and fault-report actions through the existing services. SessionService and FaultReportService receive the same InMemoryEquipmentRepository instance. The desktop app now adds [local JSON persistence](JSON-Persistence.md) around this shared state; no external database is introduced.

SessionService now enforces one active session per member inside its existing lock. Both direct starts and queue claims use this check; rejected claims retain their queue entry and original timeout. Other existing service method bodies are preserved. QueueService and SessionService are now partial classes so presentation helpers can live in separate files. Queue helpers provide a copied queue snapshot and a leave action. Session helpers expose active sessions and session history for the UI.

GymSession supports start, join, leave, claim, finish, nudge/response, report/review, and timeout processing. Claims start a session immediately. NudgeService manages the agreed per-session/requester cooldown. Maintenance completion is outside this phase’s scope.

## Nudge cooldown policy (GQ-04)

The team subsequently accepted the session-based nudge refactor. The current rule supersedes the equipment-wide decision recorded on 8 October 2026: a five-minute cooldown belongs to the pair `(SessionId, RequestedBy)`. GQ-04 is an intentional business-policy choice; code, tests and this documentation describe the current rule.

- Only the front queued member may nudge an active session; a member cannot nudge their own session.
- A successful nudge starts that requester's five-minute cooldown for that session. Another nudge is allowed at or after five minutes from the accepted request.
- Invalid requests and rejected attempts do not start, restart or extend the cooldown.
- Leaving and rejoining does not reset the same requester's cooldown while that session remains active. A replacement front member has an independent cooldown.
- A new session has an independent cooldown, including when it uses the same equipment. Completed-session cooldown entries are removed.
- Only one response request may be open for a session at a time. The request is withdrawn if the target session ends or the requester leaves the queue.
- All screens and signed-in accounts share one GymSession and NudgeService. [Local JSON persistence](JSON-Persistence.md) retains current-schema cooldowns across restarts, including after a response or the requester leaving. Explicitly disabled persistence retains state only for that run.

The response deadline is separate from the cooldown: the current user has two minutes to answer. This agrees with the submitted revised FR-003 (Part 1, page 39); historical AC-03 (page 37) and the repository's report copy say one minute. The original reports remain historical baselines, and this record does not claim lecturer approval of a requirements change.

This policy limits repeated nudges from the same waiting member while preserving FIFO eligibility and allowing a newly waiting member to ask about the current session. It is appropriate for the single-instance prototype. Separate app instances do not synchronize their queues; one process holds the writer lease for each JSON file.

`NudgeCooldownTests` verifies exact clock boundaries, a new session, a replacement requester, rejected attempts, and leaving/rejoining. `NudgePersistenceTests` verifies restored cooldowns and pending response deadlines. `QueueService.SendNudge` checks front-member eligibility; NudgeService enforces the cooldown and response-request lifecycle.

## Staff avatar policy (GQ-09)

On 11 October 2026, the team confirmed that the staff avatar intentionally returns to the staff dashboard/About view. Staff navigation remains restricted to that dashboard; a separate staff profile screen is outside the current prototype scope. Member avatars still open the member Profile screen. `ProfileNavigationPolicyUiTests` records both role-specific behaviours. This is a product-policy disposition, not lecturer approval or a new administrator-role implementation.

## Integration setup

Sample equipment and member data are included for development and testing. Use new GymSession(seed: false) to start without seeded sessions or reports; equipment and member data remain available.

MainWindow now creates one ShellViewModel and GymSession shared by all screens. Its one-second UI timer calls Tick and stops when the window closes. The Equipment screen links to details, sessions, queues, and reports. Member Profile provides logout and demo-time controls; account switching uses logout/login, and staff review is on the staff dashboard. Coordinator actions run on the UI thread. This layer does not fix concurrent access to the original QueueService.

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
