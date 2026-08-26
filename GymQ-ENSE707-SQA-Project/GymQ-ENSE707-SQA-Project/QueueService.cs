using System;
using System.Collections.Generic;
using System.Linq;
using GymQ.Models;

namespace GymQ.QueueModule
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
    public class QueueService
    {
        // In-memory store for the prototype. One list per equipment, keyed by EquipmentId.
        // TODO: replace with proper storage/repository if the project moves beyond prototype stage.
        private readonly Dictionary<string, List<QueueEntry>> _queues = new();
        private readonly Dictionary<string, DateTime> _lastNudgeAt = new();

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

            var entry = new QueueEntry(equipmentId, member.MemberId);

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
            // 2. Set NotifiedAt = DateTime.UtcNow on that entry (used by EnforceClaimTimeout)
            // 3. Send an in-app notification to that member (notification mechanism TBD — stub for now)
            // 4. If queue is empty, equipment simply stays Available with no notification
            
            if (!_queues.TryGetValue(equipmentId, out var queue))
                return;

            if (queue.Count == 0)
                return;

            var nextMember = queue[0];

            // Once the next member is notified, we record the time of notification.
            if (nextMember.NotifiedAt == null)
            {
                nextMember.NotifiedAt = DateTime.UtcNow;
            }
            // Notification mechanism will be integrated later.

        }

        /// <summary>
        /// FR-003: Called when the next-in-queue member sends a "nudge" to the current user.
        /// Enforces a cooldown of 1 nudge per equipment item every 5 minutes.
        /// </summary>
        /// <param name="equipmentId">The equipment in question.</param>
        /// <param name="fromMemberId">The member sending the nudge (must be next in queue).</param>
        /// <returns>True if the nudge was sent; false if blocked by cooldown.</returns>
        /// 
        
        public bool SendNudge(string equipmentId, string fromMemberId)
        {
            // TODO:
            // 1. Validate fromMemberId is actually next in queue for equipmentId
            // 2. Check cooldown (track last nudge time per equipmentId)
            // 3. If allowed, send notification to current user and start 1-minute response timer
            // 4. Return true/false based on whether the nudge was actually sent
            
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

            if (queue[0].MemberId != fromMemberId)
            {
                return false;
            }

            var now = DateTime.UtcNow;

            if (_lastNudgeAt.TryGetValue(equipmentId, out var lastNudgeAt) &&
                now - lastNudgeAt < TimeSpan.FromMinutes(5))
            {
                return false;
            }

            _lastNudgeAt[equipmentId] = now;

            // Notification and one-minute scheduling will be added later.
            return true;

        }

        /// <summary>
        /// FR-003: Called when the current user responds to a nudge, or when the
        /// 1-minute nudge response window times out.
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
            throw new NotImplementedException();
        }
    }
}
