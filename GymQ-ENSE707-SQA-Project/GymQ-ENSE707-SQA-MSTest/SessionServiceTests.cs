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

        /// <summary>
        /// Test clock whose "now" can be moved forward, so duration / time-cap
        /// behaviour can be exercised without real waiting.
        /// </summary>
        private sealed class AdvanceableTimeProvider : TimeProvider
        {
            private DateTimeOffset _now;
            public AdvanceableTimeProvider(DateTimeOffset now) => _now = now;
            public override DateTimeOffset GetUtcNow() => _now;
            public void Advance(TimeSpan by) => _now += by;
        }

        private static readonly DateTimeOffset BaseTime =
            new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);

        private static (SessionService service, Equipment equipment) BuildService(
            TimeProvider clock,
            EquipmentStatus status = EquipmentStatus.Available,
            string equipmentId = "treadmill-1")
        {
            var equipment = new Equipment(equipmentId, "Treadmill #1") { Status = status };
            var store = new Dictionary<string, Equipment> { [equipment.EquipmentId] = equipment };
            return (new SessionService(store, clock), equipment);
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

        [TestMethod]
        public void StartSession_EmptyMemberId_ThrowsArgumentException()
        {
            var (service, _) = BuildService(new FixedTimeProvider(BaseTime));

            Assert.ThrowsExactly<ArgumentException>(
                () => service.StartSession("treadmill-1", ""));
        }

        [TestMethod]
        public void StartSession_EmptyEquipmentId_ThrowsArgumentException()
        {
            var (service, _) = BuildService(new FixedTimeProvider(BaseTime));

            Assert.ThrowsExactly<ArgumentException>(
                () => service.StartSession("", "member-42"));
        }

        [TestMethod]
        public void StartSession_UnknownEquipment_ThrowsKeyNotFoundException()
        {
            var (service, _) = BuildService(new FixedTimeProvider(BaseTime));

            Assert.ThrowsExactly<KeyNotFoundException>(
                () => service.StartSession("bench-9", "member-42"));
        }

        [TestMethod]
        public void StartSession_EquipmentAlreadyHasActiveSession_ThrowsInvalidOperationException()
        {
            var (service, _) = BuildService(new FixedTimeProvider(BaseTime));

            service.StartSession("treadmill-1", "member-1");

            Assert.ThrowsExactly<InvalidOperationException>(
                () => service.StartSession("treadmill-1", "member-2"));
        }

        [TestMethod]
        public void StartSession_UnavailableEquipment_ThrowsInvalidOperationException()
        {
            var (service, _) = BuildService(
                new FixedTimeProvider(BaseTime),
                EquipmentStatus.Unavailable);

            Assert.ThrowsExactly<InvalidOperationException>(
                () => service.StartSession("treadmill-1", "member-42"));
        }

        [TestMethod]
        public void EndSession_ActiveSession_RecordsEndTimeReasonAndFreesEquipment()
        {
            var clock = new AdvanceableTimeProvider(BaseTime);
            var (service, equipment) = BuildService(clock);

            var session = service.StartSession("treadmill-1", "member-42");
            clock.Advance(TimeSpan.FromMinutes(12));

            service.EndSession("treadmill-1", SessionEndReason.ManualFinish);

            Assert.AreEqual(BaseTime.UtcDateTime.AddMinutes(12), session.EndTime);
            Assert.AreEqual(SessionEndReason.ManualFinish, session.EndReason);
            Assert.AreEqual(EquipmentStatus.Available, equipment.Status);
        }

        [TestMethod]
        public void EndSession_NoActiveSession_ReturnsWithoutThrowing()
        {
            var (service, equipment) = BuildService(new FixedTimeProvider(BaseTime));

            service.EndSession("treadmill-1", SessionEndReason.ManualFinish);

            Assert.AreEqual(EquipmentStatus.Available, equipment.Status);
        }

        [TestMethod]
        public void EndSession_EmptyEquipmentId_ThrowsArgumentException()
        {
            var (service, _) = BuildService(new FixedTimeProvider(BaseTime));

            Assert.ThrowsExactly<ArgumentException>(
                () => service.EndSession("", SessionEndReason.ManualFinish));
        }

        [TestMethod]
        public void EndSession_EquipmentUnavailable_KeepsEquipmentUnavailable()
        {
            var clock = new AdvanceableTimeProvider(BaseTime);
            var (service, equipment) = BuildService(clock);

            service.StartSession("treadmill-1", "member-42");

            // Equipment taken out of service mid-session (e.g. confirmed fault report).
            equipment.Status = EquipmentStatus.Unavailable;

            service.EndSession("treadmill-1", SessionEndReason.ManualFinish);

            Assert.AreEqual(EquipmentStatus.Unavailable, equipment.Status);
        }

        [TestMethod]
        public void EndSession_AfterEnding_AllowsNewSessionOnSameEquipment()
        {
            var clock = new AdvanceableTimeProvider(BaseTime);
            var (service, _) = BuildService(clock);

            service.StartSession("treadmill-1", "member-1");
            service.EndSession("treadmill-1", SessionEndReason.ManualFinish);

            clock.Advance(TimeSpan.FromMinutes(1));
            var next = service.StartSession("treadmill-1", "member-2");

            Assert.AreEqual("member-2", next.MemberId);
            Assert.IsNull(next.EndTime);
        }

        [TestMethod]
        public void EnforceMaxSessionDuration_SessionReached30Minutes_EndsSessionWithMaxDurationReason()
        {
            var clock = new AdvanceableTimeProvider(BaseTime);
            var (service, equipment) = BuildService(clock);

            var session = service.StartSession("treadmill-1", "member-42");
            clock.Advance(TimeSpan.FromMinutes(30));

            service.EnforceMaxSessionDuration("treadmill-1");

            Assert.IsNotNull(session.EndTime);
            Assert.AreEqual(SessionEndReason.MaxDurationReached, session.EndReason);
            Assert.AreEqual(EquipmentStatus.Available, equipment.Status);
        }

        [TestMethod]
        public void EnforceMaxSessionDuration_SessionUnder30Minutes_LeavesSessionActive()
        {
            var clock = new AdvanceableTimeProvider(BaseTime);
            var (service, equipment) = BuildService(clock);

            var session = service.StartSession("treadmill-1", "member-42");
            clock.Advance(TimeSpan.FromMinutes(29));

            service.EnforceMaxSessionDuration("treadmill-1");

            Assert.IsNull(session.EndTime);
            Assert.AreEqual(EquipmentStatus.InUse, equipment.Status);
        }

        [TestMethod]
        public void EnforceMaxSessionDuration_NoActiveSession_ReturnsWithoutThrowing()
        {
            var (service, _) = BuildService(new FixedTimeProvider(BaseTime));

            service.EnforceMaxSessionDuration("treadmill-1");
        }

        [TestMethod]
        public void GetSessionDuration_EndedSession_ReturnsElapsedTime()
        {
            var clock = new AdvanceableTimeProvider(BaseTime);
            var (service, _) = BuildService(clock);

            var session = service.StartSession("treadmill-1", "member-42");
            clock.Advance(TimeSpan.FromMinutes(15));
            service.EndSession("treadmill-1", SessionEndReason.ManualFinish);

            Assert.AreEqual(TimeSpan.FromMinutes(15), service.GetSessionDuration(session.SessionId));
        }

        [TestMethod]
        public void GetSessionDuration_ActiveSession_ReturnsNull()
        {
            var (service, _) = BuildService(new FixedTimeProvider(BaseTime));

            var session = service.StartSession("treadmill-1", "member-42");

            Assert.IsNull(service.GetSessionDuration(session.SessionId));
        }

        [TestMethod]
        public void GetSessionDuration_UnknownSessionId_ReturnsNull()
        {
            var (service, _) = BuildService(new FixedTimeProvider(BaseTime));

            Assert.IsNull(service.GetSessionDuration("does-not-exist"));
        }

        [TestMethod]
        public void GetAllEquipmentStatus_ReturnsEveryEquipmentWithCurrentStatus()
        {
            var clock = new FixedTimeProvider(BaseTime);
            var treadmill = new Equipment("treadmill-1", "Treadmill #1");
            var bike = new Equipment("bike-2", "Bike #2") { Status = EquipmentStatus.Unavailable };
            var store = new Dictionary<string, Equipment>
            {
                [treadmill.EquipmentId] = treadmill,
                [bike.EquipmentId] = bike
            };
            var service = new SessionService(store, clock);

            service.StartSession("treadmill-1", "member-42");

            var statuses = service.GetAllEquipmentStatus();

            Assert.AreEqual(2, statuses.Count);
            Assert.AreEqual(EquipmentStatus.InUse, statuses.Single(e => e.EquipmentId == "treadmill-1").Status);
            Assert.AreEqual(EquipmentStatus.Unavailable, statuses.Single(e => e.EquipmentId == "bike-2").Status);
        }
    }
}
