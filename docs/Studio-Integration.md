# Studio UI integration foundation

GymSession coordinates queue, session, and fault-report actions through the existing services. SessionService and FaultReportService receive the same InMemoryEquipmentRepository instance. No database or persistence is added.

SessionService now enforces one active session per member inside its existing lock. Both direct starts and queue claims use this check; rejected claims retain their queue entry and original timeout. Other existing service method bodies are preserved. QueueService and SessionService are now partial classes so presentation helpers can live in separate files. Queue helpers provide a copied queue snapshot and a leave action. Session helpers expose active sessions and session history for the UI.

GymSession supports start, join, leave, claim, finish, nudge/response, report/review, and timeout processing. It preserves immediate session start on claim and the existing equipment-level nudge cooldown. Maintenance completion is outside this phase’s scope.

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

Profile includes +1, +2, and +30 minute controls. A shared TimeProvider advances sessions, queue deadlines, nudge cooldowns, report timestamps, and UI countdowns without changing the computer clock. Time continues normally between clicks. Advances process one-second steps so earlier handovers and subsequent claim expiries occur in order. All accounts share the offset, and closing the app resets it along with the in-memory data. QueueService and FaultReportService accept an optional clock; existing callers default to system time.
