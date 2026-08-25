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
            var queueService = new QueueService();
            var member = new Member("M001", "Enzo");

            int position = queueService.JoinQueue("SquatRack2", member);

            Assert.AreEqual(1, position);
        }
    }
}