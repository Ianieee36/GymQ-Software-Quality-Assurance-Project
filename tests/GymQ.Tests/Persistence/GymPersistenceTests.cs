using GymQ.Models;
using GymQ.Persistence;
using GymQ.Services;

namespace GymQ.Tests;

[TestClass]
public sealed class GymPersistenceTests
{
    [TestMethod]
    public void Restart_PreservesEquipmentSessionsAndAllReportStates_WithSharedReportObjects()
    {
        using var fixture = new PersistentFixture();
        var gym = fixture.Open();
        gym.Start("E1", gym.FindMember("M001"));
        gym.Start("E2", gym.FindMember("M002"));
        fixture.Clock.Advance(TimeSpan.FromMinutes(3));
        gym.Finish("E2", gym.FindMember("M002"));
        gym.Start("E4", gym.FindMember("M003"));
        var confirmedActive = Report(gym, "E1", "Broken handle");
        gym.Review(confirmedActive, gym.FindMember("S001"), true);
        var confirmedIdle = Report(gym, "E3", "Resistance fault");
        gym.Review(confirmedIdle, gym.FindMember("S001"), true);
        var rejected = Report(gym, "E2", "Noise checked by staff");
        gym.Review(rejected, gym.FindMember("S001"), false);
        var pending = Report(gym, "E4", "Loose pedal");
        var expectedSessions = SessionValues(gym);
        var expectedReports = ReportValues(gym);

        var restored = fixture.Open();

        Assert.AreEqual(EquipmentStatus.Unavailable, restored.Equipment["E1"].Status);
        Assert.AreEqual(EquipmentStatus.Available, restored.Equipment["E2"].Status);
        Assert.AreEqual(EquipmentStatus.Unavailable, restored.Equipment["E3"].Status);
        Assert.AreEqual(EquipmentStatus.InUse, restored.Equipment["E4"].Status);
        Assert.AreEqual("M001", restored.Sessions.ReadActiveSession("E1")!.MemberId);
        Assert.AreEqual("M003", restored.Sessions.ReadActiveSession("E4")!.MemberId);
        CollectionAssert.AreEquivalent(expectedSessions, SessionValues(restored));
        CollectionAssert.AreEquivalent(expectedReports, ReportValues(restored));
        Assert.AreSame(restored.Equipment["E1"], restored.Sessions.GetAllEquipmentStatus().Single(e => e.EquipmentId == "E1"));
        var pendingFromList = restored.Reports.Single(r => r.ReportId == pending);
        Assert.AreSame(pendingFromList, restored.Faults.GetPendingReports().Single());
        restored.Review(pending, restored.FindMember("S001"), false);
        Assert.AreEqual(FaultReportStatus.Rejected, pendingFromList.Status);
        Assert.HasCount(0, restored.Faults.GetPendingReports());
    }

    [TestMethod]
    public void Restart_NewReportsContinueUniqueNumbering_WithoutOverwritingHistory()
    {
        using var fixture = new PersistentFixture();
        var gym = fixture.Open();
        Assert.AreEqual("R-1", Report(gym, "E1", "First report"));
        Assert.AreEqual("R-2", Report(gym, "E2", "Second report"));

        var restored = fixture.Open();
        Assert.AreEqual("R-3", Report(restored, "E3", "Third report"));
        var restartedAgain = fixture.Open();
        Assert.AreEqual("R-4", Report(restartedAgain, "E4", "Fourth report"));
        CollectionAssert.AreEquivalent(new[] { "R-1", "R-2", "R-3", "R-4" }, restartedAgain.Reports.Select(r => r.ReportId).ToArray());
    }

    [TestMethod]
    public void Restart_UnexpiredClaim_PreservesFifoJoinedTimesAndOriginalNotification()
    {
        using var fixture = new PersistentFixture();
        var gym = fixture.Open();
        gym.Start("E1", gym.FindMember("M001"));
        gym.Join("E1", gym.FindMember("M002"));
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        gym.Join("E1", gym.FindMember("M003"));
        fixture.Clock.Advance(TimeSpan.FromSeconds(9));
        gym.Finish("E1", gym.FindMember("M001"));
        var expected = gym.Queue.ReadQueue("E1").Select(q => (q.MemberId, q.JoinedAt, q.NotifiedAt)).ToArray();
        fixture.Close();
        fixture.Clock.Advance(TimeSpan.FromSeconds(45));

        var restored = fixture.Open();

        CollectionAssert.AreEqual(expected, restored.Queue.ReadQueue("E1").Select(q => (q.MemberId, q.JoinedAt, q.NotifiedAt)).ToArray());
        restored.Claim("E1", restored.FindMember("M002"));
        Assert.AreEqual("M002", restored.Sessions.ReadActiveSession("E1")!.MemberId);
        var remaining = restored.Queue.ReadQueue("E1").Single();
        Assert.AreEqual("M003", remaining.MemberId);
        Assert.IsNull(remaining.NotifiedAt);
    }

    [TestMethod]
    public void Restart_PreservesPendingNudgeAndEquipmentCooldown_AcrossFrontMemberChange()
    {
        using var fixture = new PersistentFixture();
        var gym = fixture.Open();
        gym.Start("E1", gym.FindMember("M001"));
        gym.Join("E1", gym.FindMember("M002"));
        gym.Join("E1", gym.FindMember("M003"));
        gym.SendNudge("E1", gym.FindMember("M002"));
        var pending = gym.Nudges.Single();
        fixture.Close();
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        var restored = fixture.Open();

        Assert.AreEqual(pending, restored.Nudges.Single());
        restored.Respond("E1", restored.FindMember("M001"), true);
        restored.Leave("E1", restored.FindMember("M002"));
        Assert.ThrowsExactly<InvalidOperationException>(() => restored.SendNudge("E1", restored.FindMember("M003")));
        fixture.Clock.Advance(TimeSpan.FromMinutes(4) - TimeSpan.FromTicks(1));
        Assert.ThrowsExactly<InvalidOperationException>(() => restored.SendNudge("E1", restored.FindMember("M003")));
        fixture.Clock.Advance(TimeSpan.FromTicks(1));
        restored.SendNudge("E1", restored.FindMember("M003"));
        Assert.AreEqual(restored.UtcNow.AddMinutes(2), restored.Nudges.Single().ExpiresAt);
    }

    [TestMethod]
    public void Restart_CancelledQueueStaysEmpty_AndAcknowledgementsRemainCleared()
    {
        using var fixture = new PersistentFixture();
        var gym = fixture.Open();
        gym.Start("E1", gym.FindMember("M001"));
        gym.Join("E1", gym.FindMember("M002"));
        gym.Join("E1", gym.FindMember("M003"));
        var reportId = Report(gym, "E1", "Belt fault");
        gym.Review(reportId, gym.FindMember("S001"), true);
        var notice = gym.ReadQueueCancellation("M002");
        Assert.IsNotNull(notice);

        var restored = fixture.Open();
        Assert.AreEqual(notice, restored.ReadQueueCancellation("M002"));
        Assert.IsNotNull(restored.ReadQueueCancellation("M003"));
        Assert.HasCount(0, restored.Queue.ReadQueue("E1"));
        restored.AcknowledgeQueueCancellation(restored.ReadQueueCancellation("M002")!);
        fixture.Close();
        fixture.Clock.Advance(TimeSpan.FromMinutes(10));

        var restartedAgain = fixture.Open();
        Assert.IsNull(restartedAgain.ReadQueueCancellation("M002"));
        Assert.IsNotNull(restartedAgain.ReadQueueCancellation("M003"));
        Assert.HasCount(0, restartedAgain.Queue.ReadQueue("E1"));
        Assert.HasCount(0, restartedAgain.Nudges);
        restartedAgain.AcknowledgeQueueCancellation(restartedAgain.ReadQueueCancellation("M003")!);
        var afterAllAcknowledged = fixture.Open();
        Assert.IsNull(afterAllAcknowledged.ReadQueueCancellation("M002"));
        Assert.IsNull(afterAllAcknowledged.ReadQueueCancellation("M003"));
        Assert.HasCount(0, afterAllAcknowledged.Queue.ReadQueue("E1"));
    }

    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(true, 1)]
    public void MissingFile_SeedsOnlyWhenRequested_AndWritesInitialState(bool seed, int expectedSeedCount)
    {
        using var fixture = new PersistentFixture();
        Assert.IsFalse(File.Exists(fixture.StatePath));

        var gym = fixture.Open(seed);

        Assert.HasCount(expectedSeedCount, gym.Sessions.ReadSessions());
        Assert.HasCount(expectedSeedCount, gym.Reports);
        Assert.IsTrue(File.Exists(fixture.StatePath));
    }

    [TestMethod]
    public void ExistingValidEmptyFile_IsRestoredWithoutReseeding()
    {
        using var fixture = new PersistentFixture();
        using (var store = new JsonGymStateStore(fixture.StatePath))
            store.Save(new GymStateSnapshot { SavedAtUtc = fixture.Clock.GetUtcNow().UtcDateTime });

        var gym = fixture.Open(seed: true);

        Assert.HasCount(0, gym.Equipment);
        Assert.HasCount(0, gym.Sessions.ReadSessions());
        Assert.HasCount(0, gym.Reports);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Restart_ExpiredSession_RecordsOriginalDeadline_AndPreservesMaintenanceStatus(bool unavailable)
    {
        using var fixture = new PersistentFixture();
        var gym = fixture.Open();
        gym.Start("E1", gym.FindMember("M001"));
        var deadline = gym.Sessions.ReadActiveSession("E1")!.StartTime.AddMinutes(30);
        if (unavailable)
            gym.Review(Report(gym, "E1", "Fault during active session"), gym.FindMember("S001"), true);
        fixture.Close();
        fixture.Clock.Advance(TimeSpan.FromMinutes(45));

        var restored = fixture.Open();

        Assert.IsNull(restored.Sessions.ReadActiveSession("E1"));
        var ended = restored.Sessions.ReadSessions().Single();
        Assert.AreEqual(deadline, ended.EndTime);
        Assert.AreEqual(SessionEndReason.MaxDurationReached, ended.EndReason);
        Assert.AreEqual(unavailable ? EquipmentStatus.Unavailable : EquipmentStatus.Available, restored.Equipment["E1"].Status);
        Assert.AreEqual(deadline, fixture.Open().Sessions.ReadSessions().Single().EndTime);
    }

    [TestMethod]
    public void Restart_ExpiredNudge_EndsAssociatedSessionAtDeadline_AndOffersFreshTurn()
    {
        using var fixture = new PersistentFixture();
        var gym = fixture.Open();
        gym.Start("E1", gym.FindMember("M001"));
        gym.Join("E1", gym.FindMember("M002"));
        gym.Join("E1", gym.FindMember("M003"));
        gym.SendNudge("E1", gym.FindMember("M002"));
        var deadline = gym.Nudges.Single().ExpiresAt;
        fixture.Close();
        fixture.Clock.Advance(TimeSpan.FromMinutes(15));

        var restored = fixture.Open();

        var ended = restored.Sessions.ReadSessions().Single();
        Assert.AreEqual("M001", ended.MemberId);
        Assert.AreEqual(deadline, ended.EndTime);
        Assert.AreEqual(SessionEndReason.NudgeTimeout, ended.EndReason);
        Assert.HasCount(0, restored.Nudges);
        var queue = restored.Queue.ReadQueue("E1");
        CollectionAssert.AreEqual(new[] { "M002", "M003" }, queue.Select(q => q.MemberId).ToArray());
        Assert.AreEqual(restored.UtcNow, queue[0].NotifiedAt);
        Assert.IsNull(queue[1].NotifiedAt);
    }

    [TestMethod]
    [DataRow(27, SessionEndReason.NudgeTimeout, 29)]
    [DataRow(29, SessionEndReason.MaxDurationReached, 30)]
    public void Restart_UsesEarliestSessionOrNudgeDeadline(int nudgeMinute, SessionEndReason expectedReason, int endMinute)
    {
        using var fixture = new PersistentFixture();
        var gym = fixture.Open();
        gym.Start("E1", gym.FindMember("M001"));
        var start = gym.Sessions.ReadActiveSession("E1")!.StartTime;
        gym.Join("E1", gym.FindMember("M002"));
        fixture.Clock.Advance(TimeSpan.FromMinutes(nudgeMinute));
        gym.SendNudge("E1", gym.FindMember("M002"));
        fixture.Close();
        fixture.Clock.Advance(TimeSpan.FromMinutes(10));

        var restored = fixture.Open();

        var ended = restored.Sessions.ReadSessions().Single();
        Assert.AreEqual(expectedReason, ended.EndReason);
        Assert.AreEqual(start.AddMinutes(endMinute), ended.EndTime);
        Assert.HasCount(0, restored.Nudges);
    }

    [TestMethod]
    public void Restart_ExpiredClaim_RemovesOnlyOfferedMember_AndGivesNextMemberFreshDeadline()
    {
        using var fixture = new PersistentFixture();
        var gym = fixture.Open();
        gym.Join("E1", gym.FindMember("M001"));
        gym.Join("E1", gym.FindMember("M002"));
        gym.Join("E1", gym.FindMember("M003"));
        fixture.Close();
        fixture.Clock.Advance(TimeSpan.FromMinutes(20));

        var restored = fixture.Open();

        Assert.IsNull(restored.Queue.GetQueuePosition("E1", "M001"));
        var queue = restored.Queue.ReadQueue("E1");
        CollectionAssert.AreEqual(new[] { "M002", "M003" }, queue.Select(q => q.MemberId).ToArray());
        var offeredAt = queue[0].NotifiedAt;
        Assert.AreEqual(restored.UtcNow, offeredAt);
        Assert.IsNull(queue[1].NotifiedAt);
        fixture.Close();
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.AreEqual(offeredAt, fixture.Open().Queue.ReadQueue("E1")[0].NotifiedAt);
    }

    [TestMethod]
    public void Restart_PreservesDemoOffsetAndAdvancedBy_WithoutExtendingClaimDeadline()
    {
        using var fixture = new PersistentFixture();
        var gym = fixture.Open();
        gym.Start("E1", gym.FindMember("M001"));
        gym.Join("E1", gym.FindMember("M002"));
        gym.Join("E1", gym.FindMember("M003"));
        gym.AdvanceDemoTime(2);
        gym.Finish("E1", gym.FindMember("M001"));
        var offeredAt = gym.Queue.ReadQueue("E1")[0].NotifiedAt;
        fixture.Close();
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));

        var restored = fixture.Open();

        Assert.AreEqual(TimeSpan.FromMinutes(2), restored.AdvancedBy);
        Assert.AreEqual(TimeSpan.FromMinutes(2), restored.CaptureState().ClockOffset);
        Assert.AreEqual(fixture.Clock.GetUtcNow().UtcDateTime.AddMinutes(2), restored.UtcNow);
        Assert.AreEqual(offeredAt, restored.Queue.ReadQueue("E1")[0].NotifiedAt);
        fixture.Clock.Advance(TimeSpan.FromSeconds(90));
        restored.Tick();
        Assert.IsNull(restored.Queue.GetQueuePosition("E1", "M002"));
        Assert.AreEqual("M003", restored.Queue.ReadQueue("E1")[0].MemberId);
        Assert.AreEqual(restored.UtcNow, restored.Queue.ReadQueue("E1")[0].NotifiedAt);
    }

    [TestMethod]
    public void SaveFailure_PreservesMemoryAction_AndSuccessfulRetryClearsError()
    {
        var store = new TestStateStore();
        var gym = GymSession.OpenPersistent(store, seedWhenMissing: false, clock: new TestClock());
        var errorChanges = 0;
        gym.PersistenceChanged += () => errorChanges++;
        store.FailSaves = true;

        gym.Start("E1", gym.FindMember("M001"));

        Assert.AreEqual("M001", gym.Sessions.ReadActiveSession("E1")!.MemberId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(gym.PersistenceError));
        Assert.IsFalse(gym.SaveState());
        store.FailSaves = false;
        gym.Join("E1", gym.FindMember("M002"));
        Assert.AreEqual("", gym.PersistenceError);
        Assert.IsTrue(gym.SaveState());
        Assert.IsGreaterThanOrEqualTo(2, errorChanges);
        Assert.IsNotNull(store.Latest);
        Assert.AreEqual("M001", store.Latest.Sessions.Single().MemberId);
        Assert.AreEqual("M002", store.Latest.Queue.Single().MemberId);
    }

    [TestMethod]
    public void AdvanceDemoTime_WithMultipleTimeouts_SavesOnlyFinalStateOnce()
    {
        var store = new TestStateStore();
        var gym = GymSession.OpenPersistent(store, seedWhenMissing: false, clock: new TestClock());
        gym.Start("E1", gym.FindMember("M001"));
        gym.Join("E1", gym.FindMember("M002"));
        gym.Join("E1", gym.FindMember("M003"));
        gym.SendNudge("E1", gym.FindMember("M002"));
        var savesBeforeAdvance = store.SaveCount;

        gym.AdvanceDemoTime(30);

        Assert.AreEqual(savesBeforeAdvance + 1, store.SaveCount);
        Assert.HasCount(0, gym.Queue.ReadQueue("E1"));
        Assert.AreEqual(SessionEndReason.NudgeTimeout, gym.Sessions.ReadSessions().Single().EndReason);
        Assert.IsNotNull(store.Latest);
        Assert.AreEqual(TimeSpan.FromMinutes(30), store.Latest.AdvancedBy);
        Assert.AreEqual(TimeSpan.FromMinutes(30), store.Latest.ClockOffset);
    }

    [TestMethod]
    public void DefaultConstructor_RemainsMemoryOnly_WithoutRequiringAStateStore()
    {
        var gym = new GymSession(seed: false, clock: new TestClock());

        gym.Start("E1", gym.FindMember("M001"));

        Assert.IsFalse(gym.SaveState());
        Assert.AreEqual("", gym.PersistenceError);
        Assert.AreEqual("M001", gym.CaptureState().Sessions.Single().MemberId);
    }

    private static string Report(GymSession gym, string equipmentId, string description)
    {
        gym.Report(equipmentId, gym.FindMember("M001"), description);
        return gym.Reports.Last().ReportId;
    }

    private static object[] SessionValues(GymSession gym) => gym.Sessions.ReadSessions()
        .Select(s => (object)(s.SessionId, s.EquipmentId, s.MemberId, s.StartTime, s.EndTime, s.EndReason)).ToArray();

    private static object[] ReportValues(GymSession gym) => gym.Reports
        .Select(r => (object)(r.ReportId, r.EquipmentId, r.SubmittedByMemberId, r.Description, r.Status, r.SubmittedAt, r.ReviewedAt, r.ReviewedByStaffId)).ToArray();

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }

    private sealed class PersistentFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "gymq-persistence-tests", Guid.NewGuid().ToString("N"));
        private JsonGymStateStore? _store;
        public string StatePath => Path.Combine(_directory, "state.json");
        public TestClock Clock { get; } = new();

        public PersistentFixture() => Directory.CreateDirectory(_directory);

        public GymSession Open(bool seed = false)
        {
            Close();
            _store = new JsonGymStateStore(StatePath);
            return GymSession.OpenPersistent(_store, seedWhenMissing: seed, clock: Clock);
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

    private sealed class TestStateStore : IGymStateStore
    {
        public bool FailSaves { get; set; }
        public int SaveCount { get; private set; }
        public GymStateSnapshot? Latest { get; private set; }
        public GymStateSnapshot? Load() => null;
        public void Save(GymStateSnapshot state)
        {
            if (FailSaves) throw new IOException("Simulated write failure.");
            SaveCount++;
            Latest = state;
        }
    }
}
