using GymQ.Models;
using GymQ.Services;

namespace GymQ.Tests;

[TestClass]
public class MaintenanceNotificationGuardTests
{
    [TestMethod]
    public void DirectFaultConfirmation_NudgeFinished_DoesNotOfferUnavailableMachine()
    {
        var gym = new GymSession(false);
        gym.Start("E1", gym.FindMember("M001"));
        gym.Join("E1", gym.FindMember("M002"));
        gym.Join("E1", gym.FindMember("M003"));
        gym.SendNudge("E1", gym.FindMember("M002"));
        var active = gym.Sessions.ReadActiveSession("E1");
        ConfirmFaultThroughCoreService(gym);

        // Core maintenance is public and can bypass GymSession.Review's cancellation.
        Assert.HasCount(2, gym.Queue.ReadQueue("E1"));
        Assert.AreSame(active, gym.Sessions.ReadActiveSession("E1"));
        gym.Respond("E1", gym.FindMember("M001"), stillUsing: false);

        Assert.AreEqual(SessionEndReason.NudgeResponse, active!.EndReason);
        Assert.IsNull(gym.Sessions.ReadActiveSession("E1"));
        Assert.AreEqual(EquipmentStatus.Unavailable, gym.Equipment["E1"].Status);
        Assert.HasCount(0, gym.Nudges);
        CollectionAssert.AreEqual(new[] { "M002", "M003" }, gym.Queue.ReadQueue("E1").Select(q => q.MemberId).ToArray());
        Assert.IsTrue(gym.Queue.ReadQueue("E1").All(q => q.NotifiedAt == null));
        Assert.IsFalse(gym.Queue.ClaimEquipment("E1", "M002"));
    }

    [TestMethod]
    public void DirectFaultConfirmation_ExpiredClaim_DoesNotNotifyFollowingMember()
    {
        var gym = new GymSession(false);
        gym.Join("E1", gym.FindMember("M001"));
        gym.Join("E1", gym.FindMember("M002"));
        Assert.IsNotNull(gym.Queue.ReadQueue("E1")[0].NotifiedAt);
        ConfirmFaultThroughCoreService(gym);

        gym.AdvanceDemoTime(2);

        // Keep the existing low-level expiry rule; prevent its handover on an unusable machine.
        Assert.IsNull(gym.Queue.GetQueuePosition("E1", "M001"));
        Assert.AreEqual("M002", gym.Queue.ReadQueue("E1").Single().MemberId);
        Assert.IsNull(gym.Queue.ReadQueue("E1").Single().NotifiedAt);
        Assert.AreEqual(EquipmentStatus.Unavailable, gym.Equipment["E1"].Status);
        gym.Queue.NotifyNextInQueue("E1");
        gym.AdvanceDemoTime(2);
        Assert.AreEqual(1, gym.Queue.GetQueuePosition("E1", "M002"));
        Assert.IsNull(gym.Queue.ReadQueue("E1").Single().NotifiedAt);
        Assert.IsNull(gym.Sessions.ReadActiveSession("E1"));
    }

    [TestMethod]
    public void DirectStatusUpdate_NotifyBlocksUnavailableEquipment_WithoutAffectingOtherQueue()
    {
        var gym = new GymSession(false);
        gym.Queue.JoinQueue("E1", gym.FindMember("M001"));
        gym.Queue.JoinQueue("E1", gym.FindMember("M002"));
        gym.Join("E2", gym.FindMember("M003"));
        var otherOffer = gym.Queue.ReadQueue("E2").Single().NotifiedAt;
        gym.Faults.UpdateEquipmentStatus("E1", EquipmentStatus.Unavailable);

        gym.Queue.NotifyNextInQueue("E1");

        Assert.HasCount(2, gym.Queue.ReadQueue("E1"));
        Assert.IsTrue(gym.Queue.ReadQueue("E1").All(q => q.NotifiedAt == null));
        Assert.AreEqual(otherOffer, gym.Queue.ReadQueue("E2").Single().NotifiedAt);
        Assert.IsFalse(gym.Queue.ClaimEquipment("E1", "M001"));

        // An explicit restoration still allows the original FIFO offer, without resetting it.
        gym.Faults.UpdateEquipmentStatus("E1", EquipmentStatus.Available);
        gym.Queue.NotifyNextInQueue("E1");
        var firstOffer = gym.Queue.ReadQueue("E1")[0].NotifiedAt;
        Assert.IsNotNull(firstOffer);
        Assert.AreEqual("M001", gym.Queue.ReadQueue("E1")[0].MemberId);
        Assert.IsNull(gym.Queue.ReadQueue("E1")[1].NotifiedAt);
        gym.AdvanceDemoTime(1);
        gym.Queue.NotifyNextInQueue("E1");
        Assert.AreEqual(firstOffer, gym.Queue.ReadQueue("E1")[0].NotifiedAt);
    }

    [TestMethod]
    [DataRow("manual")]
    [DataRow("nudge timeout")]
    [DataRow("maximum duration")]
    [DataRow("leave")]
    public void ConfirmedFault_CancelledQueue_IsNotReofferedByOtherAdvancePaths(string path)
    {
        var gym = new GymSession(false);
        var activeMember = gym.FindMember("M001");
        gym.Start("E1", activeMember);
        gym.Join("E1", gym.FindMember("M002"));
        gym.Join("E1", gym.FindMember("M003"));
        if (path == "nudge timeout") gym.SendNudge("E1", gym.FindMember("M002"));
        gym.Report("E1", gym.FindMember("M003"), "Loose belt");
        gym.Review(gym.Faults.GetPendingReports().Single().ReportId, gym.FindMember("S001"), true);
        var cancelledNotices = new[] { gym.ReadQueueCancellation("M002"), gym.ReadQueueCancellation("M003") };

        switch (path)
        {
            case "manual": gym.Finish("E1", activeMember); break;
            case "nudge timeout": gym.AdvanceDemoTime(2); break;
            case "maximum duration": gym.AdvanceDemoTime(30); break;
            case "leave": gym.Leave("E1", gym.FindMember("M002")); break;
        }

        Assert.AreEqual(EquipmentStatus.Unavailable, gym.Equipment["E1"].Status);
        Assert.HasCount(0, gym.Queue.ReadQueue("E1"));
        gym.Queue.NotifyNextInQueue("E1");
        gym.Tick();
        Assert.HasCount(0, gym.Queue.ReadQueue("E1"));
        Assert.AreSame(cancelledNotices[0], gym.ReadQueueCancellation("M002"));
        Assert.AreSame(cancelledNotices[1], gym.ReadQueueCancellation("M003"));
        // The nudger lost their place when the queue was cancelled, so the nudge was withdrawn
        // and cannot time the session out.
        if (path is "leave" or "nudge timeout")
        {
            Assert.IsNotNull(gym.Sessions.ReadActiveSession("E1"));
            Assert.HasCount(0, gym.Nudges);
        }
        else
        {
            Assert.IsNull(gym.Sessions.ReadActiveSession("E1"));
            var expectedReason = path switch
            {
                "manual" => SessionEndReason.ManualFinish,
                _ => SessionEndReason.MaxDurationReached
            };
            Assert.AreEqual(expectedReason, gym.Sessions.ReadSessions().Single().EndReason);
        }
    }

    private static void ConfirmFaultThroughCoreService(GymSession gym)
    {
        var report = gym.Faults.SubmitFaultReport("E1", gym.FindMember("M003"), "Loose belt");
        gym.Faults.ReviewFaultReport(report.ReportId, gym.FindMember("S001"), true);
    }
}
