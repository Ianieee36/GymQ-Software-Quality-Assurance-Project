using GymQ.Models;
using GymQ.Repository;
using GymQ.Services;

namespace GymQ.Tests;

[TestClass]
public sealed class EquipmentNudgeCooldownTests
{
    [TestMethod]
    public void SendNudge_SharedCooldown_BlocksBeforeFiveMinutes_AndAllowsAtOrAfterDeadline()
    {
        var clock = new TestClock();
        var queue = new QueueService(clock: clock);
        queue.JoinQueue("E1", Member("M001"));

        Assert.IsTrue(queue.SendNudge("E1", "M001"));
        clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1));
        Assert.IsFalse(queue.SendNudge("E1", "M001"));

        clock.Advance(TimeSpan.FromTicks(1));
        Assert.IsTrue(queue.SendNudge("E1", "M001"));
        Assert.IsFalse(queue.SendNudge("E1", "M001"));

        clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromTicks(1));
        Assert.IsTrue(queue.SendNudge("E1", "M001"));
    }

    [TestMethod]
    public void SendNudge_RejectedNonFrontMember_DoesNotStartEquipmentCooldown()
    {
        var clock = new TestClock();
        var queue = new QueueService(clock: clock);
        queue.JoinQueue("E1", Member("M001"));
        queue.JoinQueue("E1", Member("M002"));

        Assert.IsFalse(queue.SendNudge("E1", "M002"));
        Assert.IsTrue(queue.SendNudge("E1", "M001"));
        Assert.AreEqual(1, queue.GetQueuePosition("E1", "M001"));
        Assert.AreEqual(2, queue.GetQueuePosition("E1", "M002"));
    }

    [TestMethod]
    public void SendNudge_ReplacementFrontMembersRejectedAttempts_DoNotExtendSharedDeadline()
    {
        var clock = new TestClock();
        var queue = new QueueService(clock: clock);
        queue.JoinQueue("E1", Member("M001"));
        queue.JoinQueue("E1", Member("M002"));
        Assert.IsTrue(queue.SendNudge("E1", "M001"));
        queue.LeaveQueue("E1", "M001");

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.IsFalse(queue.SendNudge("E1", "M002"));
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.IsFalse(queue.SendNudge("E1", "M002"));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.IsTrue(queue.SendNudge("E1", "M002"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SendNudge_QueueEmptiedAndRecreated_DoesNotResetEquipmentCooldown(bool cancelQueue)
    {
        var clock = new TestClock();
        var queue = new QueueService(clock: clock);
        queue.JoinQueue("E1", Member("M001"));
        Assert.IsTrue(queue.SendNudge("E1", "M001"));
        clock.Advance(TimeSpan.FromMinutes(2));

        if (cancelQueue)
            CollectionAssert.AreEqual(new[] { "M001" }, queue.CancelQueue("E1").ToArray());
        else
            queue.LeaveQueue("E1", "M001");

        Assert.HasCount(0, queue.ReadQueue("E1"));
        queue.JoinQueue("E1", Member("M001"));
        Assert.IsFalse(queue.SendNudge("E1", "M001"));

        clock.Advance(TimeSpan.FromMinutes(3));
        Assert.IsTrue(queue.SendNudge("E1", "M001"));
    }

    [TestMethod]
    public void SendNudge_SessionHandedOverByClaim_NewFrontMemberSharesRemainingCooldown()
    {
        var clock = new TestClock();
        var equipment = new Equipment("E1", "Treadmill");
        var repository = new InMemoryEquipmentRepository(new() { ["E1"] = equipment });
        var sessions = new SessionService(repository, clock);
        var queue = new QueueService(sessions, clock);
        sessions.StartSession("E1", "M001");
        queue.JoinQueue("E1", Member("M002"));
        queue.JoinQueue("E1", Member("M003"));
        Assert.IsTrue(queue.SendNudge("E1", "M002"));

        clock.Advance(TimeSpan.FromMinutes(1));
        queue.HandleNudgeResponse("E1", stillUsing: false);
        Assert.IsTrue(queue.ClaimEquipment("E1", "M002"));
        Assert.AreEqual("M002", sessions.ReadActiveSession("E1")!.MemberId);
        Assert.AreEqual(1, queue.GetQueuePosition("E1", "M003"));
        Assert.IsFalse(queue.SendNudge("E1", "M003"));

        clock.Advance(TimeSpan.FromMinutes(4) - TimeSpan.FromTicks(1));
        Assert.IsFalse(queue.SendNudge("E1", "M003"));
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.IsTrue(queue.SendNudge("E1", "M003"));
        Assert.AreEqual("M002", sessions.ReadActiveSession("E1")!.MemberId);
        Assert.HasCount(1, sessions.ReadSessions().Where(s => s.EndTime == null).ToArray());
        Assert.IsNull(queue.ReadQueue("E1").Single().NotifiedAt);
    }

    private static Member Member(string id) => new(id, id, "test-password", id);

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
}
