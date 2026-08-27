using GymQ.Models;
using GymQ.SessionModule;

namespace GymQ_ENSE707_SQA_MSTest
{
    [TestClass]
    public sealed class SessionServiceTests
    {
        private sealed class FixedTimeProvider : TimeProvider
        {
            private readonly DateTimeOffset _now;
            public FixedTimeProvider(DateTimeOffset now) => _now = now;
            public override DateTimeOffset GetUtcNow() => _now;
        }


        [TestMethod]
        public void StartSession_ValidEquipment_ReturnsActiveSessionAndSetsEquipmentInUse()
        {
            var fixedTime = new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);
            var clock = new FixedTimeProvider(fixedTime);

            var treadmill = new Equipment("treadmill-1", "Treadmill #1");
            var equipmentStore = new Dictionary<string, Equipment>
            {
                [treadmill.EquipmentId] = treadmill
            };

            var sessionService = new SessionService(equipmentStore, clock);

            var session = sessionService.StartSession("treadmill-1", "member-42");

            Assert.AreEqual("treadmill-1", session.EquipmentId);
            Assert.AreEqual("member-42", session.MemberId);
            Assert.AreEqual(fixedTime.UtcDateTime, session.StartTime);
            Assert.IsNull(session.EndTime);
            Assert.AreEqual(EquipmentStatus.InUse, treadmill.Status);
        }
    }
}
