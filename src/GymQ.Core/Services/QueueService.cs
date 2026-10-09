using System;
using System.Collections.Generic;
using System.Linq;
using GymQ.Models;

namespace GymQ.Services
{
    /// <summary>
    /// PERSON A — Queue Management Module
    /// Covers FR-001, FR-002, FR-003, FR-004.
    ///
    /// Responsible for:
    /// - Letting members join/leave a virtual queue for a piece of equipment
    /// - Notifying the next member when equipment becomes available
    /// - Handling "nudge" requests toward the current user
    /// - Enforcing the 2-minute claim timeout
    ///
    /// Depends on: Models.Equipment, Models.Member, Models.QueueEntry
    /// Read by: Person C's SessionService (session starts when a queue claim succeeds)
    /// </summary>
    public partial class QueueService
    {
        // In-memory store for the prototype. One list per equipment, keyed by EquipmentId.
        // TODO: replace with proper storage/repository if the project moves beyond prototype stage.
        private readonly Dictionary<string, List<QueueEntry>> _queues = new();

        private readonly SessionService? _sessionService;


        private readonly TimeProvider _clock;

        public QueueService(SessionService? sessionService = null, TimeProvider? clock = null)
        {
            _sessionService = sessionService;
            _clock = clock ?? TimeProvider.System;
        }
        /// <summary>
        /// FR-001: Adds a logged-in member to the queue for the given equipment
        /// and returns their position (1 = front of queue).
        /// </summary>
        /// <param name="equipmentId">The equipment being queued for.</param>
        /// <param name="member">The member joining. Caller must ensure member is logged in.</param>
        /// <returns>1-based queue position.</returns>
        public int JoinQueue(string equipmentId, Member member)
        {
            // TODO:
            // 1. Validate member is logged in (login check happens before this call, or pass a token)
            // 2. Validate member is not already in this equipment's queue (avoid duplicate entries)
            // 3. Create a new QueueEntry and add it to _queues[equipmentId]
            // 4. Return the member's 1-based position in the queue

            if (string.IsNullOrWhiteSpace(equipmentId))
                throw new ArgumentException("Equipment ID is required.", nameof(equipmentId));

            if (member == null)
                throw new ArgumentNullException(nameof(member));

            if (!_queues.ContainsKey(equipmentId))
            {
                _queues[equipmentId] = new List<QueueEntry>();
            }

            var queue = _queues[equipmentId];

            bool alreadyQueued = queue.Any(entry => entry.MemberId == member.MemberId);

            if (alreadyQueued)
            {
                throw new InvalidOperationException("Member is already in this equipment queue.");
            }

            var entry = new QueueEntry(equipmentId, member.MemberId) { JoinedAt = _clock.GetUtcNow().UtcDateTime };

            queue.Add(entry);

            return queue.Count;
        }

        /// <summary>
        /// FR-001 (supporting): Returns the current 1-based position of a member
        /// in a given equipment's queue, or null if they are not queued.
        /// </summary>
        public int? GetQueuePosition(string equipmentId, string memberId)
        {
            // TODO: look up _queues[equipmentId], find the member's index, return index + 1
            
            if (!_queues.TryGetValue(equipmentId, out var queue))
                return null;

            int index = queue.FindIndex(entry => entry.MemberId == memberId);

            if (index == -1)
                return null;

            return index + 1;

        }

        /// <summary>
        /// FR-002: Called when equipment becomes available (e.g. current session ends).
        /// Notifies the next member in line and starts their 2-minute claim window.
        /// </summary>
        /// <param name="equipmentId">The equipment that just became available.</param>
        public void NotifyNextInQueue(string equipmentId)
        {
            // TODO:
            // 1. Get the front-of-queue entry for equipmentId (if any)
            // 2. Set NotifiedAt = _clock.GetUtcNow().UtcDateTime on that entry (used by EnforceClaimTimeout)
            // 3. Send an in-app notification to that member (notification mechanism TBD — stub for now)
            // 4. If queue is empty, equipment simply stays Available with no notification
            
            if (!_queues.TryGetValue(equipmentId, out var queue))
                return;

            if (queue.Count == 0)
                return;

            // Core callers can bypass maintenance queue cancellation; never offer an unusable machine.
            if (_sessionService?.GetAllEquipmentStatus().Any(e =>
                e.EquipmentId == equipmentId && e.Status == EquipmentStatus.Unavailable) == true)
                return;

            var nextMember = queue[0];

            // Once the next member is notified, we record the time of notification.
            if (nextMember.NotifiedAt == null)
            {
                nextMember.NotifiedAt = _clock.GetUtcNow().UtcDateTime;
            }
            // Notification mechanism will be integrated later.

        }

        /// <summary>
        /// FR-003: Called when the next-in-queue member sends a "nudge" to the current user.
        /// Agreed GQ-04 policy: at most one accepted nudge per equipment item every 5 minutes,
        /// shared by all queued members, including a replacement front member.
        /// </summary>
        /// <remarks>
        /// Only accepted nudges update the equipment timestamp. Rejected attempts do not extend it.
        /// Leaving, rejoining, queue cancellation and session handover do not reset it.
        /// GymSession creates the notice and schedules its separate response deadline.
        /// </remarks>
        /// <param name="equipmentId">The equipment in question.</param>
        /// <param name="fromMemberId">The member sending the nudge (must be next in queue).</param>
        /// <returns>True if accepted; false for invalid queue eligibility or an unexpired equipment cooldown.</returns>
        /// 
        
        /// <summary>
        /// FR-003: Queue eligibility for a nudge: only the member at the front of the queue may nudge.
        /// </summary>
        /// <remarks>
        /// The 5-minute cooldown is not a queue rule. NudgeService owns it, per session and nudger.
        /// </remarks>
        /// <param name="equipmentId">The equipment in question.</param>
        /// <param name="fromMemberId">The member sending the nudge (must be next in queue).</param>
        /// <returns>True if the member is at the front of the queue for this equipment.</returns>
        public bool SendNudge(string equipmentId, string fromMemberId)
        {
            if (string.IsNullOrWhiteSpace(equipmentId) ||
                string.IsNullOrWhiteSpace(fromMemberId))
            {
                return false;
            }

            if (!_queues.TryGetValue(equipmentId, out var queue) ||
                queue.Count == 0)
            {
                return false;
            }

            return queue[0].MemberId == fromMemberId;
        }

        /// <summary>
        /// FR-003: Called when the current user responds to a nudge.
        /// GymSession handles the separate response timeout.
        /// </summary>
        /// <param name="equipmentId">The equipment in question.</param>
        /// <param name="stillUsing">True if user responded "Still Using"; false if "Finished" or timed out.</param>
        public void HandleNudgeResponse(string equipmentId, bool stillUsing)
        {
            // TODO:
            // 1. If stillUsing == false (either explicit "Finished" or timeout), end the current session
            //    (this should call into Person C's SessionService.EndSession, reason = "Nudge")
            // 2. Then call NotifyNextInQueue(equipmentId)
            // 3. If stillUsing == true, do nothing further (session continues)
           if (stillUsing)
            {
                return;
            }

            _sessionService?.EndSession(
                equipmentId,
                SessionEndReason.NudgeResponse);

            NotifyNextInQueue(equipmentId);
        }

        /// <summary>
        /// FR-004: Removes a member from the queue if they do not claim the equipment
        /// within 2 minutes of being notified (NotifiedAt).
        /// Intended to be called by a background timer/scheduler per queue entry.
        /// </summary>
        /// <param name="equipmentId">The equipment in question.</param>
        /// <param name="memberId">The member who was notified and did not respond in time.</param>
        public void EnforceClaimTimeout(string equipmentId, string memberId)
        {
            // TODO:
            // 1. Check elapsed time since NotifiedAt >= 2 minutes
            // 2. If so, remove this member's QueueEntry from _queues[equipmentId]
            // 3. Call NotifyNextInQueue(equipmentId) to cascade to the next member
            
             if (!_queues.TryGetValue(equipmentId, out var queue))
                return;

            var entry = queue.FirstOrDefault(
                queueEntry => queueEntry.MemberId == memberId);

            if (entry == null || entry.NotifiedAt == null)
                return;

            var elapsed = _clock.GetUtcNow().UtcDateTime - entry.NotifiedAt.Value;

            if (elapsed < TimeSpan.FromMinutes(2))
                return;

            queue.Remove(entry);

            NotifyNextInQueue(equipmentId);
        }

        /// <summary>
        /// FR-004 (success path): Called when a notified member taps "Claim" within
        /// their 2-minute window. Starts their session and removes them from the queue.
        /// </summary>
        /// <param name="equipmentId">The equipment being claimed.</param>
        /// <param name="memberId">The member attempting to claim it.</param>
        /// <returns>
        /// True if the claim succeeded; false if invalid, not their turn,
        /// not notified, expired, or session integration is unavailable.
        /// </returns>
        public bool ClaimEquipment(string equipmentId, string memberId)
        {
            if (string.IsNullOrWhiteSpace(equipmentId) ||
                string.IsNullOrWhiteSpace(memberId))
                return false;

            if (!_queues.TryGetValue(equipmentId, out var queue) ||
                queue.Count == 0)
                return false;

            var entry = queue[0];

            // Must be the front-of-queue member
            if (entry.MemberId != memberId)
                return false;

            // Must have actually been notified
            if (entry.NotifiedAt == null)
                return false;

            // Must still be inside the 2-minute claim window
            var elapsed = _clock.GetUtcNow().UtcDateTime - entry.NotifiedAt.Value;

            if (elapsed >= TimeSpan.FromMinutes(2))
                return false;

            // Claim cannot succeed without session tracking
            if (_sessionService == null)
                return false;

            // Start session before mutating the queue.
            // If StartSession throws, the member remains queued.
            _sessionService.StartSession(equipmentId, memberId);

            queue.Remove(entry);

            return true;
        }

    }
}
