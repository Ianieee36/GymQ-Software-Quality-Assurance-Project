using Microsoft.VisualStudio.TestTools.UnitTesting;
using GymQ.Models;
using GymQ.QueueModule;

namespace GymQ.Tests
{
    [TestClass]
    public class QueueServiceTests
    {
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
    }
}