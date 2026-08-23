using System;
using System.Collections.Generic;
using GymQ.Models;

namespace GymQ.FaultModule
{
    /// <summary>
    /// Status of a fault report as it moves through staff review.
    /// Pending -> Confirmed (becomes a formal maintenance report, FR-007)
    /// Pending -> Rejected (not a valid fault, no equipment status change)
    /// </summary>
    public enum FaultReportStatus
    {
        Pending,
        Confirmed,
        Rejected
    }

    /// <summary>
    /// Represents a single fault report submitted by a member and reviewed by staff.
    /// </summary>
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

    /// <summary>
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
    /// </summary>
    public class FaultReportService
    {
        // In-memory store for the prototype.
        // TODO: replace with proper storage/repository if the project moves beyond prototype stage.
        private readonly List<FaultReport> _reports = new();

        /// <summary>
        /// FR-005: A member submits a fault report for a piece of equipment.
        /// </summary>
        /// <param name="equipmentId">The equipment being reported.</param>
        /// <param name="member">The member submitting the report.</param>
        /// <param name="description">Free-text description of the issue.</param>
        /// <returns>The newly created FaultReport (Status = Pending).</returns>
        public FaultReport SubmitFaultReport(string equipmentId, Member member, string description)
        {
            // TODO:
            // 1. Validate description is not empty (consider a minimum length, e.g. 10 characters)
            // 2. Create a new FaultReport with Status = Pending, SubmittedAt = now
            // 3. Add to _reports
            // 4. Return the created report
            throw new NotImplementedException();
        }

        /// <summary>
        /// FR-006: Staff reviews a pending fault report and either confirms it as a
        /// formal maintenance report or rejects it.
        /// </summary>
        /// <param name="reportId">The report being reviewed.</param>
        /// <param name="staff">The staff member performing the review. Caller must ensure staff.IsStaff == true.</param>
        /// <param name="confirm">True to confirm (becomes maintenance report), false to reject.</param>
        public void ReviewFaultReport(string reportId, Member staff, bool confirm)
        {
            // TODO:
            // 1. Validate staff.IsStaff == true
            // 2. Find the FaultReport by reportId; validate it is still Pending
            // 3. Set Status = Confirmed or Rejected, ReviewedByStaffId, ReviewedAt
            // 4. If confirmed, call UpdateEquipmentStatus(report.EquipmentId, EquipmentStatus.Unavailable) (FR-007)
            //    -- this likely means calling into a shared EquipmentRepository/service, not duplicating state here
            throw new NotImplementedException();
        }

        /// <summary>
        /// FR-007: Updates the given equipment's status. Called internally after a
        /// report is confirmed (Unavailable), and should also be callable when
        /// maintenance is completed to restore Available status (future FR, not in current list).
        /// </summary>
        /// <param name="equipmentId">The equipment to update.</param>
        /// <param name="newStatus">The new status to set.</param>
        public void UpdateEquipmentStatus(string equipmentId, EquipmentStatus newStatus)
        {
            // TODO:
            // 1. Look up the shared Equipment record (via shared repository, TBD with team)
            // 2. Set its Status = newStatus
            // 3. Consider: should this also notify members currently queued for this equipment?
            //    (worth discussing with Person A — queue entries may need to be cleared/paused)
            throw new NotImplementedException();
        }

        /// <summary>
        /// Helper for staff-facing review screens: returns all reports still awaiting review.
        /// </summary>
        public List<FaultReport> GetPendingReports()
        {
            // TODO: filter _reports where Status == Pending
            throw new NotImplementedException();
        }
    }
}
