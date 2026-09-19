using Microsoft.VisualStudio.TestTools.UnitTesting;
using GymQ.Models;
using GymQ.FaultModule;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GymQ.Tests
{
    [TestClass]
    public class FaultReportServiceTests
    {
        // Simple in-memory test double for FaultReportService's repository dependency.
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

        private static (FaultReportService service, Dictionary<string, Equipment> store) CreateService(
            string equipmentId = "SquatRack2")
        {
            var equipment = new Equipment(equipmentId, "Squat Rack #2");
            var store = new Dictionary<string, Equipment> { [equipmentId] = equipment };
            var repository = new TestEquipmentRepository(store);
            var service = new FaultReportService(repository);

            return (service, store);
        }

        // --- SubmitFaultReport (FR-005) ---

        [TestMethod]
        public void SubmitFaultReport_ValidInput_ReturnsPendingReport()
        {
            var (service, _) = CreateService();
            var member = new Member("M001", "Enzo");

            var report = service.SubmitFaultReport("SquatRack2", member, "Cable feels loose");

            Assert.AreEqual("SquatRack2", report.EquipmentId);
            Assert.AreEqual("M001", report.SubmittedByMemberId);
            Assert.AreEqual("Cable feels loose", report.Description);
            Assert.AreEqual(FaultReportStatus.Pending, report.Status);
        }

        [TestMethod]
        public void SubmitFaultReport_MultipleReports_AssignsUniqueReportIds()
        {
            var (service, _) = CreateService();
            var member = new Member("M001", "Enzo");

            var first = service.SubmitFaultReport("SquatRack2", member, "Loose cable");
            var second = service.SubmitFaultReport("SquatRack2", member, "Squeaky pulley");

            Assert.AreNotEqual(first.ReportId, second.ReportId);
        }

        [TestMethod]
        public void SubmitFaultReport_EmptyEquipmentId_ThrowsArgumentException()
        {
            var (service, _) = CreateService();
            var member = new Member("M001", "Enzo");

            Assert.ThrowsExactly<ArgumentException>(
                () => service.SubmitFaultReport(" ", member, "Loose cable"));
        }

        [TestMethod]
        public void SubmitFaultReport_NullMember_ThrowsArgumentNullException()
        {
            var (service, _) = CreateService();

            Assert.ThrowsExactly<ArgumentNullException>(
                () => service.SubmitFaultReport("SquatRack2", null!, "Loose cable"));
        }

        [TestMethod]
        public void SubmitFaultReport_EmptyDescription_ThrowsArgumentException()
        {
            var (service, _) = CreateService();
            var member = new Member("M001", "Enzo");

            Assert.ThrowsExactly<ArgumentException>(
                () => service.SubmitFaultReport("SquatRack2", member, " "));
        }

        // --- ReviewFaultReport (FR-006) ---

        [TestMethod]
        public void ReviewFaultReport_StaffConfirms_ReportStatusBecomesConfirmed()
        {
            var (service, _) = CreateService();
            var member = new Member("M001", "Enzo");
            var staff = new Member("S001", "Staff Steph", isStaff: true);

            var report = service.SubmitFaultReport("SquatRack2", member, "Loose cable");
            service.ReviewFaultReport(report.ReportId, staff, confirm: true);

            Assert.AreEqual(FaultReportStatus.Confirmed, report.Status);
            Assert.AreEqual("S001", report.ReviewedByStaffId);
            Assert.IsNotNull(report.ReviewedAt);
        }

        [TestMethod]
        public void ReviewFaultReport_StaffRejects_ReportStatusBecomesRejected()
        {
            var (service, _) = CreateService();
            var member = new Member("M001", "Enzo");
            var staff = new Member("S001", "Staff Steph", isStaff: true);

            var report = service.SubmitFaultReport("SquatRack2", member, "Loose cable");
            service.ReviewFaultReport(report.ReportId, staff, confirm: false);

            Assert.AreEqual(FaultReportStatus.Rejected, report.Status);
        }

        [TestMethod]
        public void ReviewFaultReport_NonStaffMember_ThrowsUnauthorizedAccessException()
        {
            var (service, _) = CreateService();
            var member = new Member("M001", "Enzo");
            var notStaff = new Member("M002", "Mia", isStaff: false);

            var report = service.SubmitFaultReport("SquatRack2", member, "Loose cable");

            Assert.ThrowsExactly<UnauthorizedAccessException>(
                () => service.ReviewFaultReport(report.ReportId, notStaff, confirm: true));
        }

        [TestMethod]
        public void ReviewFaultReport_UnknownReportId_ThrowsArgumentException()
        {
            var (service, _) = CreateService();
            var staff = new Member("S001", "Staff Steph", isStaff: true);

            Assert.ThrowsExactly<ArgumentException>(
                () => service.ReviewFaultReport("R-999", staff, confirm: true));
        }

        [TestMethod]
        public void ReviewFaultReport_AlreadyReviewedReport_ThrowsInvalidOperationException()
        {
            var (service, _) = CreateService();
            var member = new Member("M001", "Enzo");
            var staff = new Member("S001", "Staff Steph", isStaff: true);

            var report = service.SubmitFaultReport("SquatRack2", member, "Loose cable");
            service.ReviewFaultReport(report.ReportId, staff, confirm: true);

            Assert.ThrowsExactly<InvalidOperationException>(
                () => service.ReviewFaultReport(report.ReportId, staff, confirm: false));
        }

        // --- UpdateEquipmentStatus / FR-007 cascade ---

        [TestMethod]
        public void ReviewFaultReport_Confirmed_SetsEquipmentStatusUnavailable()
        {
            var (service, store) = CreateService();
            var member = new Member("M001", "Enzo");
            var staff = new Member("S001", "Staff Steph", isStaff: true);

            var report = service.SubmitFaultReport("SquatRack2", member, "Loose cable");
            service.ReviewFaultReport(report.ReportId, staff, confirm: true);

            Assert.AreEqual(EquipmentStatus.Unavailable, store["SquatRack2"].Status);
        }

        [TestMethod]
        public void ReviewFaultReport_Rejected_DoesNotChangeEquipmentStatus()
        {
            var (service, store) = CreateService();
            var member = new Member("M001", "Enzo");
            var staff = new Member("S001", "Staff Steph", isStaff: true);

            var report = service.SubmitFaultReport("SquatRack2", member, "Loose cable");
            service.ReviewFaultReport(report.ReportId, staff, confirm: false);

            Assert.AreEqual(EquipmentStatus.Available, store["SquatRack2"].Status);
        }

        [TestMethod]
        public void UpdateEquipmentStatus_UnknownEquipment_ThrowsArgumentException()
        {
            var (service, _) = CreateService();

            Assert.ThrowsExactly<ArgumentException>(
                () => service.UpdateEquipmentStatus("UnknownEquipment", EquipmentStatus.Unavailable));
        }

        // --- GetPendingReports ---

        [TestMethod]
        public void GetPendingReports_MixOfStatuses_ReturnsOnlyPendingReports()
        {
            var (service, _) = CreateService();
            var member = new Member("M001", "Enzo");
            var staff = new Member("S001", "Staff Steph", isStaff: true);

            var pending = service.SubmitFaultReport("SquatRack2", member, "Loose cable");
            var reviewed = service.SubmitFaultReport("SquatRack2", member, "Squeaky pulley");
            service.ReviewFaultReport(reviewed.ReportId, staff, confirm: true);

            var pendingReports = service.GetPendingReports();

            Assert.HasCount(1, pendingReports);
            Assert.AreEqual(pending.ReportId, pendingReports[0].ReportId);
        }

        [TestMethod]
        public void GetPendingReports_NoReports_ReturnsEmptyList()
        {
            var (service, _) = CreateService();

            var pendingReports = service.GetPendingReports();

            Assert.HasCount(0, pendingReports);
        }
    }
}