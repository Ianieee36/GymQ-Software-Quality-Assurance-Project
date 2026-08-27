using Microsoft.VisualStudio.TestTools.UnitTesting;
using GymQ.Models;
using GymQ.QueueModule;
using GymQ.SessionModule;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace GymQ.Tests
{
    [TestClass]
    public class QueueServiceTests
    {

        // Tests for JoinQueue and GetQueuePosition
        [TestMethod]
        public void JoinQueue_FirstMember_ReturnsPositionOne()
        {
            var service = new QueueService();
            var member = new Member("M001", "Enzo");

            var position = service.JoinQueue("SquatRack2", member);

            Assert.AreEqual(1, position);
        }

        [TestMethod]
        public void JoinQueue_SecondMember_ReturnsPositionTwo()
        {
            var service = new QueueService();

            service.JoinQueue("SquatRack2", new Member("M001", "Enzo"));
            var position = service.JoinQueue("SquatRack2", new Member("M002", "Mia"));

            Assert.AreEqual(2, position);
        }

        [TestMethod]
        public void GetQueuePosition_QueuedMember_ReturnsCorrectPosition()
        {
            var service = new QueueService();

            service.JoinQueue("SquatRack2", new Member("M001", "Enzo"));
            service.JoinQueue("SquatRack2", new Member("M002", "Mia"));

            Assert.AreEqual(
                2,
                service.GetQueuePosition("SquatRack2", "M002"));
        }

        [TestMethod]
        public void GetQueuePosition_MemberNotQueued_ReturnsNull()
        {
            var service = new QueueService();

            service.JoinQueue("SquatRack2", new Member("M001", "Enzo"));

            Assert.IsNull(service.GetQueuePosition("SquatRack2", "M999"));
        }

        [TestMethod]
        public void GetQueuePosition_UnknownEquipment_ReturnsNull()
        {
            var service = new QueueService();

            Assert.IsNull(
                service.GetQueuePosition("UnknownEquipment", "M001"));
        }

        [TestMethod]
        public void JoinQueue_DuplicateMember_ThrowsInvalidOperationException()
        {
            var service = new QueueService();
            var member = new Member("M001", "Enzo");

            service.JoinQueue("SquatRack2", member);

            Assert.ThrowsExactly<InvalidOperationException>(
                () => service.JoinQueue("SquatRack2", member));
        }

        [TestMethod]
        public void JoinQueue_DuplicateMember_DoesNotChangeQueuePosition()
        {
            var service = new QueueService();
            var member = new Member("M001", "Enzo");

            service.JoinQueue("SquatRack2", member);

            Assert.ThrowsExactly<InvalidOperationException>(
                () => service.JoinQueue("SquatRack2", member));

            Assert.AreEqual(
                1,
                service.GetQueuePosition("SquatRack2", "M001"));
        }

        [TestMethod]
        public void JoinQueue_SameMemberForDifferentEquipment_IsAllowed()
        {
            var service = new QueueService();
            var member = new Member("M001", "Enzo");

            var firstPosition = service.JoinQueue("SquatRack1", member);
            var secondPosition = service.JoinQueue("SquatRack2", member);

            Assert.AreEqual(1, firstPosition);
            Assert.AreEqual(1, secondPosition);
        }

        [TestMethod]
        public void JoinQueue_EmptyEquipmentId_ThrowsArgumentException()
        {
            var service = new QueueService();
            var member = new Member("M001", "Enzo");

            Assert.ThrowsExactly<ArgumentException>(
                () => service.JoinQueue(" ", member));
        }

        [TestMethod]
        public void JoinQueue_NullMember_ThrowsArgumentNullException()
        {
            var service = new QueueService();

            Assert.ThrowsExactly<ArgumentNullException>(
                () => service.JoinQueue("SquatRack2", null!));
        }
        // Additional tests for NotifyNextInQueue
        [TestMethod]
        public void NotifyNextInQueue_QueueHasMembers_SetsFrontMemberNotifiedAt()
        {
            var service = new QueueService();
            service.JoinQueue("SquatRack2", new Member("M001", "Enzo"));

            var before = DateTime.UtcNow;

            service.NotifyNextInQueue("SquatRack2");

            var after = DateTime.UtcNow;
            var entry = GetQueueEntries(service, "SquatRack2")[0];

            Assert.IsTrue(entry.NotifiedAt.HasValue);
            Assert.IsTrue(entry.NotifiedAt.Value >= before);
            Assert.IsTrue(entry.NotifiedAt.Value <= after);
        }   

        // Additional test to ensure that NotifyNextInQueue only notifies the front member in the queue
        [TestMethod]
        public void NotifyNextInQueue_QueueHasMultipleMembers_NotifiesOnlyFrontMember()
        {
            var service = new QueueService();
            service.JoinQueue("SquatRack2", new Member("M001", "Enzo"));
            service.JoinQueue("SquatRack2", new Member("M002", "Mia"));

            service.NotifyNextInQueue("SquatRack2");

            var entries = GetQueueEntries(service, "SquatRack2");

            Assert.IsTrue(entries[0].NotifiedAt.HasValue);
            Assert.IsNull(entries[1].NotifiedAt);
        }

        // Additional test to ensure that NotifyNextInQueue does not throw an exception when called on an empty queue
        [TestMethod]
        public void NotifyNextInQueue_UnknownEquipment_ReturnsSafely()
        {
            var service = new QueueService();

            service.NotifyNextInQueue("UnknownEquipment");

            Assert.IsNull(
                service.GetQueuePosition("UnknownEquipment", "M001"));
        }

        // Helper method to access the private _queues field of QueueService

        private static List<QueueEntry> GetQueueEntries(
            QueueService service,
            string equipmentId)
        {
            var queuesField = typeof(QueueService).GetField(
                "_queues",
                BindingFlags.Instance | BindingFlags.NonPublic);

            var queues = (Dictionary<string, List<QueueEntry>>)
                queuesField!.GetValue(service)!;

            return queues[equipmentId];
        }

        private static void AddEmptyQueue(
            QueueService service,
            string equipmentId)
        {
            var queuesField = typeof(QueueService).GetField(
                "_queues",
                BindingFlags.Instance | BindingFlags.NonPublic);

            var queues = (Dictionary<string, List<QueueEntry>>)
                queuesField!.GetValue(service)!;

            queues[equipmentId] = new List<QueueEntry>();
        }

        [TestMethod]
        public void SendNudge_NextMemberInQueue_ReturnsTrue()
        {
            var service = new QueueService();

            service.JoinQueue("SquatRack2", new Member("M001", "Enzo"));

            var result = service.SendNudge("SquatRack2", "M001");

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void SendNudge_MemberIsNotNextInQueue_ReturnsFalse()
        {
            var service = new QueueService();

            service.JoinQueue("SquatRack2", new Member("M001", "Enzo"));
            service.JoinQueue("SquatRack2", new Member("M002", "Mia"));

            var result = service.SendNudge("SquatRack2", "M002");

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void SendNudge_UnknownEquipment_ReturnsFalse()
        {
            var service = new QueueService();

            var result = service.SendNudge("UnknownEquipment", "M001");

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void SendNudge_EmptyQueue_ReturnsFalse()
        {
            var service = new QueueService();
            AddEmptyQueue(service, "SquatRack2");

            var result = service.SendNudge("SquatRack2", "M001");

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void SendNudge_CooldownHasNotExpired_ReturnsFalse()
        {
            var service = new QueueService();

            service.JoinQueue("SquatRack2", new Member("M001", "Enzo"));

            Assert.IsTrue(service.SendNudge("SquatRack2", "M001"));

            var result = service.SendNudge("SquatRack2", "M001");

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void SendNudge_DifferentEquipment_CooldownsAreIndependent()
        {
            var service = new QueueService();

            service.JoinQueue("SquatRack1", new Member("M001", "Enzo"));
            service.JoinQueue("SquatRack2", new Member("M001", "Enzo"));

            Assert.IsTrue(service.SendNudge("SquatRack1", "M001"));
            Assert.IsTrue(service.SendNudge("SquatRack2", "M001"));
        }

        [TestMethod]
        public void EnforceClaimTimeout_NotifiedMemberHasTimedOut_RemovesMember()
        {
            var service = new QueueService();
            service.JoinQueue("SquatRack2", new Member("M001", "Enzo"));
            service.NotifyNextInQueue("SquatRack2");

            var entry = GetQueueEntries(service, "SquatRack2")[0];
            entry.NotifiedAt = DateTime.UtcNow.AddMinutes(-2);

            service.EnforceClaimTimeout("SquatRack2", "M001");

            Assert.IsNull(service.GetQueuePosition("SquatRack2", "M001"));
        }

        [TestMethod]
        public void EnforceClaimTimeout_NotifiedMemberHasNotTimedOut_KeepsMemberInQueue()
        {
            var service = new QueueService();
            service.JoinQueue("SquatRack2", new Member("M001", "Enzo"));
            service.NotifyNextInQueue("SquatRack2");

            var entry = GetQueueEntries(service, "SquatRack2")[0];
            entry.NotifiedAt = DateTime.UtcNow.AddMinutes(-1);

            service.EnforceClaimTimeout("SquatRack2", "M001");

            Assert.AreEqual(1, service.GetQueuePosition("SquatRack2", "M001"));
        }

        [TestMethod]
        public void EnforceClaimTimeout_MemberWasNeverNotified_KeepsMemberInQueue()
        {
            var service = new QueueService();
            service.JoinQueue("SquatRack2", new Member("M001", "Enzo"));

            service.EnforceClaimTimeout("SquatRack2", "M001");

            Assert.AreEqual(1, service.GetQueuePosition("SquatRack2", "M001"));
        }

        [TestMethod]
        public void EnforceClaimTimeout_UnknownEquipment_DoesNothing()
        {
            var service = new QueueService();

            service.EnforceClaimTimeout("UnknownEquipment", "M001");

            Assert.IsNull(
                service.GetQueuePosition("UnknownEquipment", "M001"));
        }

        [TestMethod]
        public void EnforceClaimTimeout_MemberIsNotQueued_DoesNothing()
        {
            var service = new QueueService();
            service.JoinQueue("SquatRack2", new Member("M001", "Enzo"));

            service.EnforceClaimTimeout("SquatRack2", "M999");

            Assert.AreEqual(1, service.GetQueuePosition("SquatRack2", "M001"));
        }

        [TestMethod]
        public void EnforceClaimTimeout_TimedOutFrontMember_RemovesMemberAndNotifiesNext()
        {
            var service = new QueueService();

            service.JoinQueue("SquatRack2", new Member("M001", "Enzo"));
            service.JoinQueue("SquatRack2", new Member("M002", "Mia"));
            service.NotifyNextInQueue("SquatRack2");

            var entries = GetQueueEntries(service, "SquatRack2");
            entries[0].NotifiedAt = DateTime.UtcNow.AddMinutes(-2).AddSeconds(-1);

            service.EnforceClaimTimeout("SquatRack2", "M001");

            entries = GetQueueEntries(service, "SquatRack2");

            Assert.HasCount(1, entries);
            Assert.AreEqual("M002", entries[0].MemberId);
            Assert.IsTrue(entries[0].NotifiedAt.HasValue);
        }

        [TestMethod]
        public void HandleNudgeResponse_Finished_EndsSessionAndMakesEquipmentAvailable()
        {
            var equipment = new Equipment("SquatRack2", "Squat Rack #2");

            var equipmentStore = new Dictionary<string, Equipment>
            {
                [equipment.EquipmentId] = equipment
            };

            var sessionService = new SessionService(equipmentStore);

            sessionService.StartSession("SquatRack2", "M001");

            var queueService = new QueueService(sessionService);

            queueService.JoinQueue(
                "SquatRack2",
                new Member("M002", "Mia"));

            queueService.HandleNudgeResponse(
                "SquatRack2",
                false);

            Assert.AreEqual(
                EquipmentStatus.Available,
                equipment.Status);
        }

    }
}   