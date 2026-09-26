using GymQ.Services;
using GymQ.Models;

namespace GymQ.Tests;

[TestClass]
public class SingleActiveSessionTests
{
    [TestMethod]
    public void SecondStart_IsRejectedWithoutChangingEquipment_AndCanStartAfterFinish()
    {
        var g = new GymSession(false);
        var member = g.Members[0];
        g.Start("E1", member);
        Assert.ThrowsExactly<InvalidOperationException>(() => g.Start("E2", member));
        Assert.AreEqual(EquipmentStatus.Available, g.Equipment["E2"].Status);
        Assert.HasCount(1, g.Sessions.ReadSessions());
        g.Finish("E1", member);
        g.Start("E2", member);
        Assert.AreEqual(member.MemberId, g.Sessions.ReadActiveSession("E2")!.MemberId);
    }

    [TestMethod]
    public void ClaimWhileActive_PreservesQueueAndNotification_ThenSucceedsAfterFinish()
    {
        var g = new GymSession(false);
        var member = g.Members[0];
        g.Start("E1", member);
        g.Join("E2", member);
        var notification = g.Queue.ReadQueue("E2")[0].NotifiedAt;
        Assert.ThrowsExactly<InvalidOperationException>(() => g.Claim("E2", member));
        Assert.AreEqual(1, g.Queue.GetQueuePosition("E2", member.MemberId));
        Assert.AreEqual(notification, g.Queue.ReadQueue("E2")[0].NotifiedAt);
        Assert.AreEqual(EquipmentStatus.Available, g.Equipment["E2"].Status);
        g.Finish("E1", member);
        g.Claim("E2", member);
        Assert.IsNull(g.Queue.GetQueuePosition("E2", member.MemberId));
        Assert.AreEqual(member.MemberId, g.Sessions.ReadActiveSession("E2")!.MemberId);
    }

    [TestMethod]
    public void DifferentMembers_CanUseDifferentEquipment()
    {
        var g = new GymSession(false);
        g.Start("E1", g.Members[0]);
        g.Start("E2", g.Members[1]);
        Assert.HasCount(2, g.Sessions.ReadSessions());
    }

    [TestMethod]
    public void SimultaneousStarts_ForSameMember_AllowOnlyOneSession()
    {
        var g = new GymSession(false);
        var succeeded = 0;
        var rejected = 0;
        Parallel.ForEach(new[] { "E1", "E2" }, id =>
        {
            try { g.Sessions.StartSession(id, "M001"); Interlocked.Increment(ref succeeded); }
            catch (InvalidOperationException) { Interlocked.Increment(ref rejected); }
        });
        Assert.AreEqual(1, succeeded);
        Assert.AreEqual(1, rejected);
        Assert.HasCount(1, g.Sessions.ReadSessions());
        Assert.AreEqual(1, g.Equipment.Values.Count(e => e.Status == EquipmentStatus.InUse));
    }
}
