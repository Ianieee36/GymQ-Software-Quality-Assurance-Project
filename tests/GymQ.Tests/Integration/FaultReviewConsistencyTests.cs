using GymQ.Models;
using GymQ.Repository;
using GymQ.Services;

namespace GymQ.Tests;

[TestClass]
public class FaultReviewConsistencyTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Confirmation_UsesValidatedEquipment_WhenAnotherLookupWouldFail(bool throwOnFailure)
    {
        var repository = new ReviewLookupRepository(throwOnFailure);
        var service = new FaultReportService(repository);
        var member = new Member("M001", "member", "password", "Member");
        var staff = new Member("S001", "staff", "password", "Staff", isStaff: true);
        var report = service.SubmitFaultReport("E1", member, "Loose belt");
        repository.BeginReview(failFirstLookup: false);

        service.ReviewFaultReport(report.ReportId, staff, confirm: true);

        Assert.AreEqual(1, repository.ReviewLookupCount);
        Assert.AreEqual(EquipmentStatus.Unavailable, repository.Equipment.Status);
        Assert.AreEqual(FaultReportStatus.Confirmed, report.Status);
        Assert.AreEqual(staff.MemberId, report.ReviewedByStaffId);
        Assert.IsNotNull(report.ReviewedAt);
        Assert.IsEmpty(service.GetPendingReports());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Confirmation_ValidationFailure_LeavesReportAndEquipmentUnchanged(bool throwOnFailure)
    {
        var repository = new ReviewLookupRepository(throwOnFailure);
        var service = new FaultReportService(repository);
        var member = new Member("M001", "member", "password", "Member");
        var staff = new Member("S001", "staff", "password", "Staff", isStaff: true);
        var report = service.SubmitFaultReport("E1", member, "Loose belt");
        repository.BeginReview(failFirstLookup: true);

        if (throwOnFailure)
            Assert.ThrowsExactly<IOException>(() => service.ReviewFaultReport(report.ReportId, staff, confirm: true));
        else
            Assert.ThrowsExactly<InvalidOperationException>(() => service.ReviewFaultReport(report.ReportId, staff, confirm: true));

        Assert.AreEqual(1, repository.ReviewLookupCount);
        Assert.AreEqual(EquipmentStatus.Available, repository.Equipment.Status);
        Assert.AreEqual(FaultReportStatus.Pending, report.Status);
        Assert.IsNull(report.ReviewedByStaffId);
        Assert.IsNull(report.ReviewedAt);
        Assert.AreSame(report, service.GetPendingReports().Single());
    }

    private sealed class ReviewLookupRepository(bool throwOnFailure) : IEquipmentRepository
    {
        public Equipment Equipment { get; } = new("E1", "Treadmill");
        public int ReviewLookupCount { get; private set; }
        private bool _reviewing;
        private bool _failFirstLookup;

        public void BeginReview(bool failFirstLookup)
        {
            _reviewing = true;
            _failFirstLookup = failFirstLookup;
            ReviewLookupCount = 0;
        }

        public Equipment? GetById(string equipmentId)
        {
            if (equipmentId != Equipment.EquipmentId) return null;
            if (!_reviewing) return Equipment;
            ReviewLookupCount++;
            if (!_failFirstLookup && ReviewLookupCount == 1) return Equipment;
            if (throwOnFailure) throw new IOException("The repository lookup is unavailable.");
            return null;
        }

        public List<Equipment> GetAll() => new() { Equipment };
    }
}
