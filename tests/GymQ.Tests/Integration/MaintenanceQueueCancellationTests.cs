using GymQ.Models;
using GymQ.Services;

namespace GymQ.Tests;

[TestClass]
public class MaintenanceQueueCancellationTests
{
    [TestMethod]
    public void ConfirmedFault_SingleOfferedMember_CancelsClaimAndNotifiesMember()
    {
        var gym = new GymSession(false);
        var member = gym.FindMember("M001");
        gym.Join("E1", member);
        Assert.IsNotNull(gym.Queue.ReadQueue("E1").Single().NotifiedAt);

        ConfirmFault(gym, "E1");

        Assert.AreEqual(EquipmentStatus.Unavailable, gym.Equipment["E1"].Status);
        Assert.HasCount(0, gym.Queue.ReadQueue("E1"));
        Assert.IsNull(gym.Queue.GetQueuePosition("E1", member.MemberId));
        Assert.IsFalse(gym.Queue.ClaimEquipment("E1", member.MemberId));
        Assert.IsNull(gym.Sessions.ReadActiveSession("E1"));

        var notice = gym.ReadQueueCancellation(member.MemberId);
        Assert.IsNotNull(notice);
        Assert.AreEqual(member.MemberId, notice.MemberId);
        Assert.AreEqual("E1", notice.EquipmentId);
        Assert.AreEqual(gym.Equipment["E1"].Name, notice.EquipmentName);
        Assert.AreEqual("The queue for Treadmill #3 has been cancelled because the equipment has been marked Out of Service.", notice.Message);
        Assert.IsNull(gym.ReadQueueCancellation("M002"));
        Assert.IsNull(gym.ReadQueueCancellation("S001"));
        gym.AcknowledgeQueueCancellation(notice);
        Assert.IsNull(gym.ReadQueueCancellation(member.MemberId));
    }

    [TestMethod]
    public void ConfirmedFault_MultipleMembers_CancelsEntireQueueAndPreservesOtherQueue()
    {
        var gym = new GymSession(false);
        var memberIds = new[] { "M001", "M002", "M003" };
        foreach (var memberId in memberIds) gym.Join("E1", gym.FindMember(memberId));
        gym.Join("E2", gym.FindMember("M001"));
        gym.Join("E2", gym.FindMember("M003"));
        var otherQueue = gym.Queue.ReadQueue("E2");
        Assert.IsNotNull(otherQueue[0].NotifiedAt);
        Assert.IsNull(gym.Queue.ReadQueue("E1")[1].NotifiedAt);

        ConfirmFault(gym, "E1");

        Assert.HasCount(0, gym.Queue.ReadQueue("E1"));
        foreach (var memberId in memberIds)
        {
            Assert.IsNull(gym.Queue.GetQueuePosition("E1", memberId));
            var notice = gym.ReadQueueCancellation(memberId);
            Assert.IsNotNull(notice);
            Assert.AreEqual("E1", notice.EquipmentId);
            gym.AcknowledgeQueueCancellation(notice);
            Assert.IsNull(gym.ReadQueueCancellation(memberId));
        }
        var remainingQueue = gym.Queue.ReadQueue("E2");
        CollectionAssert.AreEqual(otherQueue.Select(q => q.MemberId).ToArray(), remainingQueue.Select(q => q.MemberId).ToArray());
        CollectionAssert.AreEqual(otherQueue.Select(q => q.JoinedAt).ToArray(), remainingQueue.Select(q => q.JoinedAt).ToArray());
        CollectionAssert.AreEqual(otherQueue.Select(q => q.NotifiedAt).ToArray(), remainingQueue.Select(q => q.NotifiedAt).ToArray());
        Assert.AreEqual(EquipmentStatus.Available, gym.Equipment["E2"].Status);
    }

    [TestMethod]
    public void ConfirmedFault_PreservesActiveSessionAndExistingNudgeResponse_WithoutOfferingCancelledTurn()
    {
        var gym = new GymSession(false);
        var activeMember = gym.FindMember("M002");
        gym.Start("E1", activeMember);
        gym.Join("E1", gym.FindMember("M001"));
        gym.Join("E1", gym.FindMember("M003"));
        gym.SendNudge("E1", gym.FindMember("M001"));
        var activeSession = gym.Sessions.ReadActiveSession("E1");
        var pendingNudge = gym.Nudges.Single();

        ConfirmFault(gym, "E1");

        Assert.AreSame(activeSession, gym.Sessions.ReadActiveSession("E1"));
        Assert.IsNull(activeSession!.EndTime);
        Assert.AreEqual(pendingNudge, gym.Nudges.Single());
        Assert.HasCount(0, gym.Queue.ReadQueue("E1"));
        Assert.IsNotNull(gym.ReadQueueCancellation("M001"));
        Assert.IsNotNull(gym.ReadQueueCancellation("M003"));
        Assert.IsNull(gym.ReadQueueCancellation(activeMember.MemberId));

        gym.Respond("E1", activeMember, stillUsing: false);

        Assert.IsNull(gym.Sessions.ReadActiveSession("E1"));
        Assert.AreEqual(SessionEndReason.NudgeResponse, activeSession.EndReason);
        Assert.HasCount(0, gym.Nudges);
        Assert.AreEqual(EquipmentStatus.Unavailable, gym.Equipment["E1"].Status);
        Assert.HasCount(0, gym.Queue.ReadQueue("E1"));
        Assert.IsFalse(gym.Queue.ClaimEquipment("E1", "M001"));
    }

    [TestMethod]
    public void CancelledQueue_StaysEmptyAfterOldDeadlinesNotificationsAndRestoration()
    {
        var gym = new GymSession(false);
        var first = gym.FindMember("M001");
        gym.Join("E1", first);
        gym.Join("E1", gym.FindMember("M003"));
        ConfirmFault(gym, "E1");
        foreach (var memberId in new[] { "M001", "M003" })
        {
            var notice = gym.ReadQueueCancellation(memberId);
            Assert.IsNotNull(notice);
            gym.AcknowledgeQueueCancellation(notice);
        }

        // Advancing through both claim windows must not hand over a cancelled turn.
        gym.AdvanceDemoTime(2);
        gym.Queue.EnforceClaimTimeout("E1", "M001");
        gym.Queue.NotifyNextInQueue("E1");
        gym.AdvanceDemoTime(2);
        gym.Queue.EnforceClaimTimeout("E1", "M003");
        gym.Queue.NotifyNextInQueue("E1");
        gym.Tick();

        Assert.HasCount(0, gym.Queue.ReadQueue("E1"));
        Assert.IsNull(gym.ReadQueueCancellation("M001"));
        Assert.IsNull(gym.ReadQueueCancellation("M003"));
        Assert.ThrowsExactly<InvalidOperationException>(() => gym.Claim("E1", first));
        Assert.ThrowsExactly<InvalidOperationException>(() => gym.Join("E1", first));
        Assert.IsNull(gym.Sessions.ReadActiveSession("E1"));
        Assert.HasCount(0, gym.Queue.ReadQueue("E1"));

        gym.Faults.UpdateEquipmentStatus("E1", EquipmentStatus.Available);
        gym.Tick();
        gym.Queue.NotifyNextInQueue("E1");
        Assert.HasCount(0, gym.Queue.ReadQueue("E1"));
        Assert.IsFalse(gym.Queue.ClaimEquipment("E1", first.MemberId));
        Assert.IsNull(gym.ReadQueueCancellation("M001"));
        Assert.IsNull(gym.ReadQueueCancellation("M003"));
    }

    [TestMethod]
    public void RejectedUnauthorizedAndDuplicateReview_PreserveQueueAndDoNotNotifyCancellation()
    {
        var gym = new GymSession(false);
        var first = gym.FindMember("M001");
        gym.Join("E1", first);
        gym.Join("E1", gym.FindMember("M003"));
        var originalQueue = gym.Queue.ReadQueue("E1");
        gym.Report("E1", first, "Loose belt");
        var report = gym.Faults.GetPendingReports().Single();

        Assert.ThrowsExactly<UnauthorizedAccessException>(() => gym.Review(report.ReportId, first, true));
        gym.Review(report.ReportId, gym.FindMember("S001"), false);
        Assert.ThrowsExactly<InvalidOperationException>(() => gym.Review(report.ReportId, gym.FindMember("S001"), true));

        Assert.AreEqual(EquipmentStatus.Available, gym.Equipment["E1"].Status);
        var remainingQueue = gym.Queue.ReadQueue("E1");
        CollectionAssert.AreEqual(originalQueue.Select(q => q.MemberId).ToArray(), remainingQueue.Select(q => q.MemberId).ToArray());
        CollectionAssert.AreEqual(originalQueue.Select(q => q.NotifiedAt).ToArray(), remainingQueue.Select(q => q.NotifiedAt).ToArray());
        Assert.IsNull(gym.ReadQueueCancellation("M001"));
        Assert.IsNull(gym.ReadQueueCancellation("M003"));
    }

    [TestMethod]
    public void ConfirmedFault_EmptyQueueAndRepeatedReview_DoNotDuplicateCancellationNotices()
    {
        var gym = new GymSession(false);
        gym.Join("E1", gym.FindMember("M001"));
        var reportId = ConfirmFault(gym, "E1");
        var notice = gym.ReadQueueCancellation("M001");
        Assert.IsNotNull(notice);
        gym.AcknowledgeQueueCancellation(notice);

        Assert.ThrowsExactly<InvalidOperationException>(() => gym.Review(reportId, gym.FindMember("S001"), true));
        ConfirmFault(gym, "E1");
        ConfirmFault(gym, "E2");

        Assert.HasCount(0, gym.Queue.ReadQueue("E1"));
        Assert.HasCount(0, gym.Queue.ReadQueue("E2"));
        foreach (var memberId in new[] { "M001", "M002", "M003", "S001" })
            Assert.IsNull(gym.ReadQueueCancellation(memberId));
    }

    private static string ConfirmFault(GymSession gym, string equipmentId)
    {
        gym.Report(equipmentId, gym.FindMember("M003"), "Equipment needs inspection.");
        var report = gym.Faults.GetPendingReports().Single(r => r.EquipmentId == equipmentId);
        gym.Review(report.ReportId, gym.FindMember("S001"), true);
        return report.ReportId;
    }
}
