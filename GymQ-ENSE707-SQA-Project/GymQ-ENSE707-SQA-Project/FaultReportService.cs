using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GymQ.Models;

namespace GymQ.FaultModule
{
    // Status of a fault report as it moves through staff review.
    // Pending -> Confirmed (becomes a formal maintenance report, FR-007)
    // Pending -> Rejected (not a valid fault, no equipment status change)
    public enum FaultReportStatus
    {
        Pending,
        Confirmed,
        Rejected
    }

    // Represents a single fault report submitted by a member and reviewed by staff.
    public class FaultReport
    {
        public string ReportId { get; set; }
        public string EquipmentId { get; set; }
        public string SubmittedByMemberId { get; set; }
        public string Description { get; set; }
        public FaultReportStatus Status { get; set; } = FaultReportStatus.Pending;
        public DateTime SubmittedAt { get; set; }

        public string ReviewedByStaffId { get; set; }
        public DateTime? ReviewedAt { get; set; }
    }

    // Testable interface for Equipment repository
    public interface IEquipmentRepository
    {
        Equipment GetById(string equipmentId);
    }


    // PERSON B — Fault Reporting & Maintenance Module
    // Covers FR-005, FR-006, FR-007.
    //
    // Responsible for:
    // - Letting members submit fault reports for equipment
    // - Letting staff confirm or reject a submitted report
    // - Updating equipment status to "Unavailable" when a report is confirmed
    //
    // Depends on: Models.Equipment (specifically EquipmentStatus enum)
    // Coordinate with: Person A and C on EquipmentStatus enum values — do not rename.
    public class FaultReportService
    {
        // Thread-safe in-memory store for the prototype.   ### AI suggested improvement
        private readonly ConcurrentDictionary<string, FaultReport> _reports = new();

        // Atomic counter for generating unique report IDs (instead of a simple integer)  ### AI suggested improvement
        private long _nextReportId = 0;


        private readonly IEquipmentRepository _equipmentRepository;

        public FaultReportService (IEquipmentRepository equipmentRepository)
        {
            // Validate that the equipment repository is not null
            _equipmentRepository = equipmentRepository ?? throw new ArgumentNullException(nameof(equipmentRepository));
        }

        // FR-005: A member submits a fault report for a piece of equipment.
        public FaultReport SubmitFaultReport(string equipmentId, Member member, string description)
        {

            // Validation checks
            if (string.IsNullOrWhiteSpace(equipmentId))
            {
                throw new ArgumentException("Equipment ID is required.", nameof(equipmentId));
            }

            if (member == null)
            {
                throw new ArgumentNullException(nameof(member), "Member cannot be null.");
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                throw new ArgumentException("Description cannot be empty");
            }

            // Create new fault report with atomic ID generation
            var report = new FaultReport
            {
                ReportId = $"R-{Interlocked.Increment(ref _nextReportId)}", //### AI suggested improvement
                EquipmentId = equipmentId,
                SubmittedByMemberId = member.MemberId,
                Description = description,
                Status = FaultReportStatus.Pending,
                SubmittedAt = DateTime.UtcNow
            };

            // Add report and return it
            if (!_reports.TryAdd(report.ReportId, report))
            {
                throw new InvalidOperationException("Failed to add fault report.");
            }

            return report;
        }

        // FR-006: Staff reviews a pending fault report and either confirms it as a formal maintenance report or rejects it.
        public void ReviewFaultReport(string reportId, Member staff, bool confirm)
        {
            //Validation checks
            if (staff == null)
            {
                throw new ArgumentNullException(nameof(staff));
            }

            if (!staff.IsStaff)
            {
                throw new UnauthorizedAccessException("Only staff members can review fault reports.");
            }

            // find the report by reportId and validate it exists
            if (!_reports.TryGetValue(reportId, out var report))
            {
                throw new ArgumentException($"No fault report found with ID '{reportId}'.", nameof(reportId));
            }

            // check that the report is actually pending
            if (report.Status != FaultReportStatus.Pending)
            {
                throw new InvalidOperationException($"Report '{reportId}' has already been reviewed and is not pending. Current report status: '{report.Status}'");
            }

            // Update the report status and review details
            report.Status = confirm ? FaultReportStatus.Confirmed : FaultReportStatus.Rejected;

            report.ReviewedByStaffId = staff.MemberId;
            report.ReviewedAt = DateTime.UtcNow;

            // FR-007: If the report is confirmed, update the equipment status to Unavailable
            if (confirm)
            {
                UpdateEquipmentStatus(report.EquipmentId, EquipmentStatus.Unavailable);
            }
        }

        // FR-007: Updates the given equipment's status. Called internally after a
        // report is confirmed (Unavailable), and should also be callable when
        // maintenance is completed to restore Available status (future FR, not in current list).
        public void UpdateEquipmentStatus(string equipmentId, EquipmentStatus newStatus)
        {
            // validation checks
            if (string.IsNullOrWhiteSpace(equipmentId))
            {
                throw new ArgumentException("Equipment ID is required.", nameof(equipmentId));
            }

            // Check if the equipment exists in the repository
            var equipment = _equipmentRepository.GetById(equipmentId);
            if (equipment == null)
            {
                throw new ArgumentException($"No equipment found with ID '{equipmentId}'.", nameof(equipmentId));
            }

            // Update the equipment status
            equipment.Status = newStatus;
        }

        // Helper for staff-facing review screens: returns all reports still awaiting review.
        public List<FaultReport> GetPendingReports()
        {
            // Return a list of all reports that have pending status
            return _reports.Values.Where(r => r.Status == FaultReportStatus.Pending).ToList();
        }
    }
}
