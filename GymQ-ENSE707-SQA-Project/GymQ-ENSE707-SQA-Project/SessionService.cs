using System;
using System.Collections.Generic;
using GymQ.Models;

namespace GymQ.SessionModule
{
    /// <summary>
    /// Why a session ended — useful for reporting and for debugging test failures
    /// (e.g. "did this end because of a nudge, or the time cap?").
    /// </summary>
    public enum SessionEndReason
    {
        ManualFinish,
        NudgeResponse,
        NudgeTimeout,
        MaxDurationReached
    }

    /// <summary>
    /// Represents one member's usage session on a piece of equipment,
    /// from claiming their turn to the session ending.
    /// </summary>
    public class UsageSession
    {
        public string SessionId { get; set; }
        public string EquipmentId { get; set; }
        public string MemberId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public SessionEndReason? EndReason { get; set; }

        // TODO (Person C): computed property once EndTime is set
        public TimeSpan? Duration => EndTime.HasValue ? EndTime.Value - StartTime : null;
    }

    /// <summary>
    /// PERSON C — Session Tracking & Admin Dashboard Module
    /// Covers FR-008, FR-009.
    ///
    /// Responsible for:
    /// - Starting/ending usage sessions and recording their duration
    /// - Enforcing the 30-minute maximum session duration
    /// - Providing the admin-facing view of all equipment statuses
    ///
    /// Depends on: Models.Equipment (EquipmentStatus), Person A's QueueService
    ///             (StartSession is triggered by a successful queue claim)
    /// </summary>
    public class SessionService
    {
        // In-memory store for the prototype.
        // TODO: replace with proper storage/repository if the project moves beyond prototype stage.
        private readonly List<UsageSession> _sessions = new();

        /// <summary>
        /// FR-008: Starts a new usage session when a member successfully claims
        /// their turn from the queue. Called by Person A's queue-claim flow.
        /// </summary>
        /// <param name="equipmentId">The equipment being used.</param>
        /// <param name="memberId">The member starting their session.</param>
        /// <returns>The newly created UsageSession.</returns>
        public UsageSession StartSession(string equipmentId, string memberId)
        {
            // TODO:
            // 1. Create a new UsageSession with StartTime = DateTime.UtcNow
            // 2. Add to _sessions
            // 3. Consider: also update Equipment.Status to InUse here, or leave that to caller?
            //    (decide with team, avoid setting status in two places)
            // 4. Return the created session
            throw new NotImplementedException();
        }

        /// <summary>
        /// FR-008: Ends an active usage session. Called from three different triggers:
        /// manual "Finish" tap, nudge response/timeout (Person A), or the max duration timer.
        /// </summary>
        /// <param name="equipmentId">The equipment whose session is ending.</param>
        /// <param name="reason">Why the session is ending — for reporting/debugging.</param>
        public void EndSession(string equipmentId, SessionEndReason reason)
        {
            // TODO:
            // 1. Find the active (EndTime == null) session for equipmentId
            // 2. Set EndTime = DateTime.UtcNow, EndReason = reason
            // 3. Call Person A's QueueService.NotifyNextInQueue(equipmentId) so the queue moves on
            //    (or raise an event/callback — discuss integration approach with Person A)
            throw new NotImplementedException();
        }

        /// <summary>
        /// FR-008: Enforces the 30-minute maximum session duration. Intended to be
        /// called by a background timer/scheduler per active session.
        /// </summary>
        /// <param name="equipmentId">The equipment to check.</param>
        public void EnforceMaxSessionDuration(string equipmentId)
        {
            // TODO:
            // 1. Find the active session for equipmentId
            // 2. If DateTime.UtcNow - StartTime >= 30 minutes, call EndSession(equipmentId, SessionEndReason.MaxDurationReached)
            throw new NotImplementedException();
        }

        /// <summary>
        /// FR-008 (supporting): Returns the recorded duration of a completed session,
        /// for administrative reporting.
        /// </summary>
        public TimeSpan? GetSessionDuration(string sessionId)
        {
            // TODO: look up session by SessionId, return its Duration property
            throw new NotImplementedException();
        }

        /// <summary>
        /// FR-009: Returns the current status of every piece of gym equipment,
        /// for the admin-facing view.
        /// </summary>
        /// <returns>A list of equipment with their current status.</returns>
        public List<Equipment> GetAllEquipmentStatus()
        {
            // TODO:
            // 1. Pull the full equipment list (via shared repository, TBD with team)
            // 2. Return as-is — status values (Available/InUse/Unavailable) are already
            //    kept current by QueueService, FaultReportService, and this class
            throw new NotImplementedException();
        }
    }
}
