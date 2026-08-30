using System;
using System.Collections.Generic;
using GymQ.Models;

namespace GymQ.FaultModule
{
    /// Status of a fault report as it moves through staff review.
    /// Pending -> Confirmed (becomes a formal maintenance report, FR-007)
    /// Pending -> Rejected (not a valid fault, no equipment status change)
    public enum FaultReportStatus
    {
        Pending,
        Confirmed,
        Rejected
    }

    /// Represents a single fault report submitted by a member and reviewed by staff.
    public class FaultReport
    {
        public string ReportId { get; set; }
        public string EquipmentId { get; set; }
        public string SubmittedByMemberId { get; set; }
        public string Description { get; set; }
        public FaultReportStatus Status { get; set; } = FaultReportStatus.Pending;
        public DateTime SubmittedAt { get; set; }

        // TODO (Person B): set when staff review the report (FR-006)
        public string ReviewedByStaffId { get; set; }
        public DateTime? ReviewedAt { get; set; }
    }



    /// PERSON B — Fault Reporting & Maintenance Module
    /// Covers FR-005, FR-006, FR-007.
    ///
    /// Responsible for:
    /// - Letting members submit fault reports for equipment
    /// - Letting staff confirm or reject a submitted report
    /// - Updating equipment status to "Unavailable" when a report is confirmed
    ///
    /// Depends on: Models.Equipment (specifically EquipmentStatus enum)
    /// Coordinate with: Person A and C on EquipmentStatus enum values — do not rename.
    public class FaultReportService
    {
        // In-memory store for the prototype.
        // TODO: replace with proper storage/database if the project moves beyond prototype stage.
        private readonly List<FaultReport> _reports = new();

        private int _nextReportId = 1; // Counter for generating unique report IDs

        /// FR-005: A member submits a fault report for a piece of equipment.
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

            // Create new fault report
            var report = new FaultReport
            {
                ReportId = $"R-{_nextReportId++}",
                EquipmentId = equipmentId,
                SubmittedByMemberId = member.MemberId,
                Description = description,
                Status = FaultReportStatus.Pending,
                SubmittedAt = DateTime.UtcNow
            };

            // Add report and return it
            _reports.Add(report);
            return report;
        }

        /// FR-006: Staff reviews a pending fault report and either confirms it as a formal maintenance report or rejects it.
        public void ReviewFaultReport(string reportId, Member staff, bool confirm)
        {
            // TODO:
            // 1. Validate staff.IsStaff == true
            // 2. Find the FaultReport by reportId; validate it is still Pending
            // 3. Set Status = Confirmed or Rejected, ReviewedByStaffId, ReviewedAt
            // 4. If confirmed, call UpdateEquipmentStatus(report.EquipmentId, EquipmentStatus.Unavailable) (FR-007)
            //    -- this likely means calling into a shared EquipmentRepository/service, not duplicating state here

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
            var report = _reports.Find(r => r.ReportId == reportId);
            if (report == null)
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

        /// FR-007: Updates the given equipment's status. Called internally after a
        /// report is confirmed (Unavailable), and should also be callable when
        /// maintenance is completed to restore Available status (future FR, not in current list).
        public void UpdateEquipmentStatus(string equipmentId, EquipmentStatus newStatus)
        {
            // TODO:
            // 1. Look up the shared Equipment record (via shared repository, TBD with team)
            // 2. Set its Status = newStatus
            // 3. Consider: should this also notify members currently queued for this equipment?
            //    (worth discussing with Person A — queue entries may need to be cleared/paused)
            throw new NotImplementedException();
        }

        /// Helper for staff-facing review screens: returns all reports still awaiting review.
        public List<FaultReport> GetPendingReports()
        {
            // TODO: filter _reports where Status == Pending
            throw new NotImplementedException();
        }
    }
}
