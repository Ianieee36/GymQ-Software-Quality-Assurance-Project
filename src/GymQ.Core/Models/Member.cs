using System;

namespace GymQ.Models
{
    /// <summary>
    /// Represents a gym member or staff account.
    /// IsStaff distinguishes staff-only actions (e.g. reviewing fault reports, FR-006)
    /// from member actions (e.g. joining a queue, FR-001).
    /// </summary>
    public class Member
    {
        // Kept private so the password can never be read from outside this class.
        private readonly string _password;

        public string MemberId { get; }
        public string UserName { get; }
        public string Name { get; }
        public bool IsStaff { get; }

        public Member(string memberId, string userName, string password, string name, bool isStaff = false)
        {
            MemberId = memberId;
            UserName = userName;
            _password = password;
            Name = name;
            IsStaff = isStaff;
        }

        /// <summary>
        /// The only way to use the stored password. Plain-text comparison is a documented
        /// prototype limitation; adding hashing later only changes this method.
        /// </summary>
        public bool CheckPassword(string password) => _password == password;
    }

    /// <summary>
    /// Represents one member's position in a specific equipment's queue.
    /// Created by JoinQueue(), read when starting a session.
    /// </summary>
    public class QueueEntry
    {
        public string EquipmentId { get; set; }
        public string MemberId { get; set; }
        public DateTime JoinedAt { get; set; }

        // Set when the member is notified equipment is free (FR-002),
        // used to enforce the 2-minute claim timeout (FR-004).
        public DateTime? NotifiedAt { get; set; }

        public QueueEntry(string equipmentId, string memberId)
        {
            EquipmentId = equipmentId;
            MemberId = memberId;
            JoinedAt = DateTime.UtcNow;
        }
    }
}
