using System;

namespace GymQ.Models
{
    /// <summary>
    /// Represents a gym member or staff user.
    /// IsStaff distinguishes staff-only actions (e.g. reviewing fault reports, FR-006)
    /// from member actions (e.g. joining a queue, FR-001).
    /// </summary>
    public class Member
    {
        public string MemberId { get; set; }
        public string Name { get; set; }
        public bool IsStaff { get; set; } = false;

        public Member(string memberId, string name, bool isStaff = false)
        {
            MemberId = memberId;
            Name = name;
            IsStaff = isStaff;
        }
    }

    /// <summary>
    /// Represents one member's position in a specific equipment's queue.
    /// Created by Person A's JoinQueue(), read by Person C when starting a session.
    /// </summary>
    public class QueueEntry
    {
        public string EquipmentId { get; set; }
        public string MemberId { get; set; }
        public DateTime JoinedAt { get; set; }

        // TODO (Person A): set when the member is notified equipment is free (FR-002),
        // used to enforce the 2-minute claim timeout (FR-004)
        public DateTime? NotifiedAt { get; set; }

        public QueueEntry(string equipmentId, string memberId)
        {
            EquipmentId = equipmentId;
            MemberId = memberId;
            JoinedAt = DateTime.UtcNow;
        }
    }
}
