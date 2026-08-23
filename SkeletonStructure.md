# GymQ — Skeleton Structure

Logic-only skeleton, no GUI. Each module can be developed and unit tested independently, then wired together in integration tests.

## Folder structure

```
GymQ/
├── Models/
│   ├── Equipment.cs      # Shared: EquipmentStatus enum + Equipment class
│   └── Member.cs         # Shared: Member class + QueueEntry class
├── QueueModule/
│   └── QueueService.cs   # Person A — FR-001, FR-002, FR-003, FR-004
├── FaultModule/
│   └── FaultReportService.cs   # Person B — FR-005, FR-006, FR-007
└── SessionModule/
    └── SessionService.cs # Person C — FR-008, FR-009
```

## How to use this skeleton

- Every method currently `throw new NotImplementedException();` — replace the TODO comments with real logic.
- Method signatures, XML doc comments, and requirement IDs (FR-00X) are already mapped — do not change signatures without telling the other two people, since your modules call into each other (see "Cross-module dependencies" below).
- `Models/` is shared. If you need to change `EquipmentStatus`, `Equipment`, `Member`, or `QueueEntry`, flag it to the team first — all three modules depend on these.

## Cross-module dependencies (read before changing signatures)

- **Person A → Person C**: a successful queue claim should trigger `SessionService.StartSession()`.
- **Person C → Person A**: when a session ends (`SessionService.EndSession()`), it should trigger `QueueService.NotifyNextInQueue()` so the queue keeps moving.
- **Person A → Person C**: `QueueService.HandleNudgeResponse()` (nudge finished/timeout) should trigger `SessionService.EndSession()`.
- **Person B → shared Equipment**: `FaultReportService.UpdateEquipmentStatus()` writes to the same `Equipment.Status` field that Person C reads in `GetAllEquipmentStatus()`.

Because of these links, it's worth agreeing early on **how modules talk to each other** — direct method calls between service instances (simplest for a prototype), or a shared event/callback pattern. Simplest for a semester project: pass references to the other services' instances into the constructor where needed (basic dependency injection), rather than building a full event bus.

## Suggested next steps

1. Each person implements their own module's TODOs against the in-memory stores already scaffolded.
2. Write unit tests per method (MSTest) before wiring modules together — this is your Task 6 test case material.
3. Once individual modules pass their own tests, wire up the cross-module calls listed above and write integration tests.
4. GUI comes after the logic layer is verified working, per team decision.
