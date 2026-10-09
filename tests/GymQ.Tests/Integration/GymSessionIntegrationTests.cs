using GymQ.Services;
using GymQ.Models;
using System.Reflection;
namespace GymQ.Tests;
[TestClass]
public class GymSessionIntegrationTests
{
    [TestMethod]
    public void Services_ShareEquipmentInstances_AndQueuedTurnsCannotBeBypassed()
    {
        var g = new GymSession(seed: false);
        Assert.AreSame(g.Equipment["E1"], g.Sessions.GetAllEquipmentStatus().Single(e => e.EquipmentId == "E1"));
        g.Join("E1", g.Members[0]);
        Assert.ThrowsExactly<InvalidOperationException>(() => g.Start("E1", g.Members[1]));
        g.Claim("E1", g.Members[0]);
        Assert.AreEqual(EquipmentStatus.InUse, g.Equipment["E1"].Status);
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => g.Finish("E1", g.Members[1]));
    }

    [TestMethod]
    public void SuccessfulClaim_StartsSessionImmediately_WithNoExtraReservationStep()
    {
        var g = new GymSession(); g.Join("E2", g.Members[0]); g.Finish("E2", g.Members[1]); g.Claim("E2", g.Members[0]);
        Assert.AreEqual("M001", g.Sessions.ReadActiveSession("E2")!.MemberId);
        Assert.IsNull(g.Queue.GetQueuePosition("E2", "M001"));
    }

    [TestMethod]
    public void SendNudge_NewFrontMember_HasItsOwnCooldown()
    {
        var g = new GymSession(); g.Join("E2", g.Members[0]); g.Join("E2", g.Members[2]);
        g.SendNudge("E2", g.Members[0]); g.Respond("E2", g.Members[1], true); g.Leave("E2", g.Members[0]);
        // The cooldown belongs to the previous nudger, so the replacement front member can nudge now.
        Assert.AreEqual(1, g.Queue.GetQueuePosition("E2", g.Members[2].MemberId));
        g.SendNudge("E2", g.Members[2]);
        Assert.HasCount(1, g.Nudges);
        Assert.AreEqual(g.Members[1].MemberId, g.Nudges.Single().MemberId);
        Assert.AreEqual(g.Members[2].MemberId, g.Nudges.Single().RequestedBy);
    }
    
    [TestMethod]
    public void ManualFinish_OffersTheNextTurn_AndLeaveAdvancesIt()
    {
        var g = new GymSession(); g.Join("E2", g.Members[0]); g.Join("E2", g.Members[2]); g.Finish("E2", g.Members[1]);
        Assert.IsNotNull(g.Queue.ReadQueue("E2")[0].NotifiedAt);
        g.Leave("E2", g.Members[0]); Assert.AreEqual("M003", g.Queue.ReadQueue("E2")[0].MemberId);
        Assert.IsNotNull(g.Queue.ReadQueue("E2")[0].NotifiedAt);
    }
    [TestMethod]
    public void FaultConfirmation_PreservesOriginalActiveSessionBehaviour()
    {
        var g = new GymSession(); g.Report("E2", g.Members[0], "Broken handle");
        var report = g.Faults.GetPendingReports().Single(); g.Review(report.ReportId, g.FindMember("S001"), true);
        Assert.IsNotNull(g.Sessions.ReadActiveSession("E2"));
        Assert.AreEqual(EquipmentStatus.Unavailable, g.Equipment["E2"].Status);
        g.Finish("E2", g.Members[1]); Assert.AreEqual(EquipmentStatus.Unavailable, g.Equipment["E2"].Status);
    }
    [TestMethod]
    public void NudgeScheduler_InvokesExistingSessionTimeoutReason()
    {
        var g = new GymSession(); g.Join("E2", g.Members[0]); g.SendNudge("E2", g.Members[0]);
        g.AdvanceDemoTime(2);
        Assert.AreEqual(SessionEndReason.NudgeTimeout, g.Sessions.ReadSessions().Last().EndReason);
        Assert.IsNotNull(g.Queue.ReadQueue("E2")[0].NotifiedAt);
    }
    [TestMethod]
    public void ClaimScheduler_CascadesExpiredQueueEntries()
    {
        var g = new GymSession(); g.Join("E2", g.Members[0]); g.Join("E2", g.Members[2]); g.Finish("E2", g.Members[1]);
        var field = typeof(QueueService).GetField("_queues", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var queues = (Dictionary<string,List<QueueEntry>>)field.GetValue(g.Queue)!;
        queues["E2"][0].NotifiedAt = DateTime.UtcNow.AddMinutes(-3);
        g.Tick(); Assert.IsNull(g.Queue.GetQueuePosition("E2", "M001"));
        Assert.AreEqual("M003", g.Queue.ReadQueue("E2")[0].MemberId);
        Assert.IsNotNull(g.Queue.ReadQueue("E2")[0].NotifiedAt);
    }
    [TestMethod]
    public void StaffReview_StillRejectsMemberIdentity()
    {
        var g = new GymSession(); g.Report("E1", g.Members[0], "Loose belt");
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => g.Review(g.Faults.GetPendingReports()[0].ReportId, g.Members[0], true));
    }
}
