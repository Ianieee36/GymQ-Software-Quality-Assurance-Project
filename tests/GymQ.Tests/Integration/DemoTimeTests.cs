using GymQ.Services;
namespace GymQ.Tests;
[TestClass]
public class DemoTimeTests
{
    [TestMethod]
    public void OneMinute_EndsUnansweredNudge_AndOffersNextTurn()
    {
        var g = new GymSession(); g.Join("E2", g.Members[0]); g.SendNudge("E2", g.Members[0]);
        g.AdvanceDemoTime(1);
        Assert.AreEqual(SessionEndReason.NudgeTimeout, g.Sessions.ReadSessions().Last().EndReason);
        Assert.IsNotNull(g.Queue.ReadQueue("E2")[0].NotifiedAt);
        Assert.HasCount(0, g.Nudges.ToList());
    }
    [TestMethod]
    public void TwoMinutes_ExpiresClaim_AndOffersFollowingMember()
    {
        var g = new GymSession(false); g.Join("E1", g.Members[0]); g.Join("E1", g.Members[1]);
        g.AdvanceDemoTime(2);
        Assert.IsNull(g.Queue.GetQueuePosition("E1", g.Members[0].MemberId));
        Assert.AreEqual(g.Members[1].MemberId, g.Queue.ReadQueue("E1")[0].MemberId);
        Assert.IsLessThan(2d, (g.UtcNow - g.Queue.ReadQueue("E1")[0].NotifiedAt!.Value).TotalSeconds);
    }
    [TestMethod]
    public void ThirtyMinutes_EndsSession_AndReportUsesSameClock()
    {
        var g = new GymSession(false); g.Start("E1", g.Members[0]); g.AdvanceDemoTime(30);
        Assert.AreEqual(SessionEndReason.MaxDurationReached, g.Sessions.ReadSessions().Single().EndReason);
        g.Report("E1", g.Members[0], "Loose belt");
        Assert.IsLessThan(1d, Math.Abs((g.UtcNow - g.Reports.Single().SubmittedAt).TotalSeconds));
        Assert.AreEqual(TimeSpan.FromMinutes(30), g.AdvancedBy);
    }
    [TestMethod]
    public void LargeAdvance_ProcessesNudgeThenClaimExpiryInOrder()
    {
        var g = new GymSession(); g.Join("E2", g.Members[0]); g.SendNudge("E2", g.Members[0]);
        g.AdvanceDemoTime(30);
        Assert.HasCount(0, g.Queue.ReadQueue("E2"));
        Assert.AreEqual(SessionEndReason.NudgeTimeout, g.Sessions.ReadSessions().Single().EndReason);
    }
    [TestMethod]
    public void AdvancingTime_ExpiresNudgeCooldown()
    {
        var g = new GymSession(); g.Join("E2", g.Members[0]); g.SendNudge("E2", g.Members[0]);
        g.Respond("E2", g.Members[1], true);
        Assert.ThrowsExactly<InvalidOperationException>(() => g.SendNudge("E2", g.Members[0]));
        g.AdvanceDemoTime(2); g.AdvanceDemoTime(2); g.AdvanceDemoTime(1);
        g.SendNudge("E2", g.Members[0]); Assert.HasCount(1, g.Nudges.ToList());
    }
}
