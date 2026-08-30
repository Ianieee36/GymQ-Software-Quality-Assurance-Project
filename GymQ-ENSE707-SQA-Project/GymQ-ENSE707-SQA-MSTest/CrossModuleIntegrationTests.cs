using Microsoft.VisualStudio.TestTools.UnitTesting;
using GymQ.Models;
using GymQ.QueueModule;
using GymQ.SessionModule;
using GymQ.FaultModule;
using System;
using System.Collections.Generic;

namespace GymQ.Tests
{
    [TestClass]
    public class CrossModuleIntegrationTests
    {
        // Simple test double for FaultReportService's repository dependency,
        // backed by the same Dictionary<string, Equipment> that SessionService
        // uses, so both modules read/write the exact same Equipment instance.
        private class TestEquipmentRepository : IEquipmentRepository
        {
            private readonly Dictionary<string, Equipment> _equipment;

            public TestEquipmentRepository(Dictionary<string, Equipment> equipment)
            {
                _equipment = equipment;
            }

            public Equipment GetById(string equipmentId)
            {
                return _equipment.TryGetValue(equipmentId, out var equipment)
                    ? equipment
                    : null!;
            }
        }

        // 1. Queue claim -> Session start
        [TestMethod]
        public void ClaimEquipment_NotifiedFrontMember_StartsSessionAndUpdatesQueueAndEquipment()
        {
            var equipment = new Equipment("SquatRack2", "Squat Rack #2");
            var equipmentStore = new Dictionary<string, Equipment>
            {
                [equipment.EquipmentId] = equipment
            };
            var sessionService = new SessionService(equipmentStore);
            var queueService = new QueueService(sessionService);

            queueService.JoinQueue("SquatRack2", new Member("M001", "Enzo"));
            queueService.NotifyNextInQueue("SquatRack2");

            var result = queueService.ClaimEquipment("SquatRack2", "M001");

            Assert.IsTrue(result);
            Assert.AreEqual(EquipmentStatus.InUse, equipment.Status);
            Assert.IsNull(queueService.GetQueuePosition("SquatRack2", "M001"));
        }

        // 2. Nudge response -> Session end -> Queue advances
        [TestMethod]
        public void HandleNudgeResponse_Finished_EndsSessionAndNotifiesNextQueuedMember()
        {
            var equipment = new Equipment("SquatRack2", "Squat Rack #2");
            var equipmentStore = new Dictionary<string, Equipment>
            {
                [equipment.EquipmentId] = equipment
            };
            var sessionService = new SessionService(equipmentStore);
            var queueService = new QueueService(sessionService);

            // M001 is actively using the equipment
            sessionService.StartSession("SquatRack2", "M001");

            // M002 is waiting behind them
            queueService.JoinQueue("SquatRack2", new Member("M002", "Mia"));

            queueService.HandleNudgeResponse("SquatRack2", stillUsing: false);

            Assert.AreEqual(EquipmentStatus.Available, equipment.Status);

            var entries = GetQueueEntries(queueService, "SquatRack2");
            Assert.IsTrue(entries[0].NotifiedAt.HasValue);
        }

        // 3. Session end -> Equipment status
        [TestMethod]
        public void EndSession_ActiveSession_EquipmentStatusChangesInUseToAvailable()
        {
            var equipment = new Equipment("SquatRack2", "Squat Rack #2");
            var equipmentStore = new Dictionary<string, Equipment>
            {
                [equipment.EquipmentId] = equipment
            };
            var sessionService = new SessionService(equipmentStore);

            sessionService.StartSession("SquatRack2", "M001");
            Assert.AreEqual(EquipmentStatus.InUse, equipment.Status);

            sessionService.EndSession("SquatRack2", SessionEndReason.ManualFinish);

            Assert.AreEqual(EquipmentStatus.Available, equipment.Status);
        }

        // 4. Fault report -> Equipment unavailable -> Session start refused
        [TestMethod]
        public void ConfirmedFaultReport_MarksEquipmentUnavailable_AndBlocksNewSession()
        {
            var equipment = new Equipment("SquatRack2", "Squat Rack #2");
            var equipmentStore = new Dictionary<string, Equipment>
            {
                [equipment.EquipmentId] = equipment
            };

            var repository = new TestEquipmentRepository(equipmentStore);
            var faultReportService = new FaultReportService(repository);
            var sessionService = new SessionService(equipmentStore);

            var member = new Member("M001", "Enzo");
            var staff = new Member("S001", "Staff Steph", isStaff: true);

            var report = faultReportService.SubmitFaultReport(
                "SquatRack2", member, "Cable feels loose");

            faultReportService.ReviewFaultReport(report.ReportId, staff, confirm: true);

            Assert.AreEqual(EquipmentStatus.Unavailable, equipment.Status);

            Assert.ThrowsExactly<InvalidOperationException>(
                () => sessionService.StartSession("SquatRack2", "M002"));
        }

        // Reuses the same reflection-based helper pattern as QueueServiceTests
        private static List<QueueEntry> GetQueueEntries(
            QueueService service,
            string equipmentId)
        {
            var queuesField = typeof(QueueService).GetField(
                "_queues",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            var queues = (Dictionary<string, List<QueueEntry>>)
                queuesField!.GetValue(service)!;

            return queues[equipmentId];
        }
    }
}