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

    [TestMethod]
    public void Restart_AfterStillUsingResponse_RetainsCooldownWithoutAnOpenNudge_AtOriginalBoundary()
    {
        using var fixture = new PersistentFixture();
        var gym = Nudged(fixture.Open());
        var sessionId = gym.Sessions.ReadActiveSession("E1")!.SessionId;
        var cooldown = gym.CaptureState().NudgeCooldowns.Single();
        gym.Respond("E1", gym.FindMember("M002"), stillUsing: true);
        Assert.IsEmpty(gym.Nudges);
        fixture.Close();
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        var restored = fixture.Open();

        Assert.IsEmpty(restored.Nudges);
        Assert.AreEqual(cooldown, restored.CaptureState().NudgeCooldowns.Single());
        Assert.AreEqual(sessionId, restored.Sessions.ReadActiveSession("E1")!.SessionId);
        Assert.AreEqual(TimeSpan.FromMinutes(4), restored.Nudging.CooldownRemaining(sessionId, "M001"));
        Assert.ThrowsExactly<InvalidOperationException>(() => restored.SendNudge("E1", restored.FindMember("M001")));
        fixture.Clock.Advance(TimeSpan.FromMinutes(4) - TimeSpan.FromTicks(1));
        Assert.AreEqual(TimeSpan.FromTicks(1), restored.Nudging.CooldownRemaining(sessionId, "M001"));
        Assert.ThrowsExactly<InvalidOperationException>(() => restored.SendNudge("E1", restored.FindMember("M001")));
        fixture.Clock.Advance(TimeSpan.FromTicks(1));
        Assert.AreEqual(TimeSpan.Zero, restored.Nudging.CooldownRemaining(sessionId, "M001"));
        restored.SendNudge("E1", restored.FindMember("M001"));
        Assert.AreEqual(restored.UtcNow.Add(NudgeService.ResponseWindow), restored.Nudges.Single().ExpiresAt);
    }

    [TestMethod]
    public void Restart_AfterRequesterLeaves_RetainsSameSessionCooldown_WhenRequesterRejoins()
    {
        using var fixture = new PersistentFixture();
        var gym = Nudged(fixture.Open());
        var sessionId = gym.Sessions.ReadActiveSession("E1")!.SessionId;
        var cooldown = gym.CaptureState().NudgeCooldowns.Single();
        gym.Leave("E1", gym.FindMember("M001"));
        Assert.IsEmpty(gym.Nudges);
        Assert.IsEmpty(gym.Queue.ReadQueue("E1"));
        fixture.Close();
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));

        var restored = fixture.Open();

        Assert.IsEmpty(restored.Nudges);
        Assert.IsEmpty(restored.Queue.ReadQueue("E1"));
        Assert.AreEqual(sessionId, restored.Sessions.ReadActiveSession("E1")!.SessionId);
        Assert.AreEqual(cooldown, restored.CaptureState().NudgeCooldowns.Single());
        restored.Join("E1", restored.FindMember("M001"));
        Assert.AreEqual(TimeSpan.FromMinutes(3), restored.Nudging.CooldownRemaining(sessionId, "M001"));
        Assert.ThrowsExactly<InvalidOperationException>(() => restored.SendNudge("E1", restored.FindMember("M001")));
        fixture.Clock.Advance(TimeSpan.FromMinutes(3));
        restored.SendNudge("E1", restored.FindMember("M001"));
        Assert.AreEqual(sessionId, restored.Nudges.Single().SessionId);
        Assert.AreEqual("M001", restored.Nudges.Single().RequestedBy);
    }

    [TestMethod]
    public void Restart_WithAnotherRequesterNudgeOpen_RestoresAllCooldownsAndOriginalResponseDeadline()
    {
        using var fixture = new PersistentFixture();
        var gym = Nudged(fixture.Open());
        var sessionId = gym.Sessions.ReadActiveSession("E1")!.SessionId;
        gym.Respond("E1", gym.FindMember("M002"), stillUsing: true);
        gym.Leave("E1", gym.FindMember("M001"));
        gym.Join("E1", gym.FindMember("M003"));
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        gym.SendNudge("E1", gym.FindMember("M003"));
        var cooldowns = gym.CaptureState().NudgeCooldowns;
        var pending = gym.Nudges.Single();
        Assert.HasCount(2, cooldowns);
        fixture.Close();
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));

        var restored = fixture.Open();

        CollectionAssert.AreEquivalent(cooldowns, restored.CaptureState().NudgeCooldowns);
        Assert.AreEqual(pending, restored.Nudges.Single());
        Assert.AreEqual(TimeSpan.FromMinutes(4), restored.Nudging.CooldownRemaining(sessionId, "M001"));
        Assert.AreEqual(TimeSpan.FromMinutes(4.5), restored.Nudging.CooldownRemaining(sessionId, "M003"));
        Assert.AreEqual(TimeSpan.FromMinutes(1.5), restored.Nudges.Single().ExpiresAt - restored.UtcNow);
        restored.Respond("E1", restored.FindMember("M002"), stillUsing: true);
        restored.Leave("E1", restored.FindMember("M003"));
        restored.Join("E1", restored.FindMember("M001"));
        Assert.ThrowsExactly<InvalidOperationException>(() => restored.SendNudge("E1", restored.FindMember("M001")));
    }

    [TestMethod]
    public void Restart_PendingNudge_ExpiresAtOriginalResponseDeadline_WithoutRestartingTimer()
    {
        using var fixture = new PersistentFixture();
        var gym = Nudged(fixture.Open());
        var pending = gym.Nudges.Single();
        fixture.Close();
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        var restored = fixture.Open();

        Assert.AreEqual(pending, restored.Nudges.Single());
        Assert.AreEqual(TimeSpan.FromMinutes(1), pending.ExpiresAt - restored.UtcNow);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1) - TimeSpan.FromTicks(1));
        restored.Tick();
        Assert.AreEqual(pending, restored.Nudges.Single());
        Assert.IsNotNull(restored.Sessions.ReadActiveSession("E1"));
        fixture.Clock.Advance(TimeSpan.FromTicks(1));
        restored.Tick();
        Assert.IsEmpty(restored.Nudges);
        Assert.IsNull(restored.Sessions.ReadActiveSession("E1"));
        var ended = restored.Sessions.ReadSessions().Single();
        Assert.AreEqual(pending.ExpiresAt, ended.EndTime);
        Assert.AreEqual(SessionEndReason.NudgeTimeout, ended.EndReason);
        Assert.AreEqual(pending.ExpiresAt, restored.Queue.ReadQueue("E1").Single().NotifiedAt);
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
        public void Advance(TimeSpan amount) => _now += amount;
    }

    private sealed class PersistentFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "gymq-nudge-persistence-tests", Guid.NewGuid().ToString("N"));
        private JsonGymStateStore? _store;
        public TestClock Clock { get; } = new();

        public PersistentFixture() => Directory.CreateDirectory(_directory);

        public GymSession Open()
        {
            Close();
            _store = new JsonGymStateStore(Path.Combine(_directory, "state.json"));
            return GymSession.OpenPersistent(_store, seedWhenMissing: false, clock: Clock);
        }

        public void Close()
        {
            _store?.Dispose();
            _store = null;
        }

        public void Dispose()
        {
            Close();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
