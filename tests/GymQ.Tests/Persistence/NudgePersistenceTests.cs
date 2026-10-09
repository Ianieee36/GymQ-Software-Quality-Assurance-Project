using GymQ.Persistence;
using GymQ.Services;

namespace GymQ.Tests;

/// <summary>
/// The validator rejects saved nudges that break the session-owned rules, and names the rule.
/// Round trips, restarts and schema checks are covered in GymPersistenceTests and JsonGymStateStoreTests.
/// </summary>
[TestClass]
public sealed class NudgePersistenceTests
{
    [TestMethod]
    public void Validator_RejectsNudgeForAnotherSession()
    {
        var state = Nudged(new GymSession(false, new TestClock())).CaptureState();
        var nudge = state.Nudges.Single() with { SessionId = "S-old" };

        var ex = Assert.ThrowsExactly<InvalidDataException>(() => GymStateValidator.Validate(WithNudges(state, nudge)));

        Assert.StartsWith("A pending nudge must belong to the active session", ex.Message);
    }

    [TestMethod]
    public void Validator_RejectsSelfNudge()
    {
        var state = Nudged(new GymSession(false, new TestClock())).CaptureState();
        var nudge = state.Nudges.Single() with { RequestedBy = "M002" };

        var ex = Assert.ThrowsExactly<InvalidDataException>(() => GymStateValidator.Validate(WithNudges(state, nudge)));

        Assert.StartsWith("A member cannot nudge their own session", ex.Message);
    }

    // M002 is using E1; M001, at the front of the queue, nudges them.
    private static GymSession Nudged(GymSession gym)
    {
        gym.Start("E1", gym.FindMember("M002"));
        gym.Join("E1", gym.FindMember("M001"));
        gym.SendNudge("E1", gym.FindMember("M001"));
        return gym;
    }

    private static GymStateSnapshot WithNudges(GymStateSnapshot s, params NudgeNotice[] nudges) => new()
    {
        SchemaVersion = s.SchemaVersion, SavedAtUtc = s.SavedAtUtc, ClockOffset = s.ClockOffset,
        AdvancedBy = s.AdvancedBy, NextReportNumber = s.NextReportNumber, Equipment = s.Equipment,
        Sessions = s.Sessions, Reports = s.Reports, Queue = s.Queue, NudgeCooldowns = s.NudgeCooldowns,
        Nudges = nudges, Cancellations = s.Cancellations
    };

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
    }
}