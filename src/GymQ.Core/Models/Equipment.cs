using System;

namespace GymQ.Models
{
    /// <summary>
    /// Shared status values for a piece of gym equipment.
    /// All three modules (Queue, Fault, Session) read/write this enum,
    /// so do not change these names without telling the whole team.
    /// </summary>
    public enum EquipmentStatus
    {
        Available,
        InUse,
        Unavailable // set when a fault report is confirmed as a maintenance report (FR-007)
    }

    /// <summary>
    /// Represents a single piece of gym equipment (e.g. Treadmill #3).
    /// This is the shared entity all modules operate on.
    /// </summary>
    public class Equipment
    {
        public string EquipmentId { get; set; }
        public string Name { get; set; }
        public EquipmentStatus Status { get; set; } = EquipmentStatus.Available;

        // TODO (Person C): consider adding LastStatusChangedAt for admin dashboard reporting (FR-009)

        public Equipment(string equipmentId, string name)
        {
            EquipmentId = equipmentId;
            Name = name;
        }
    }
}
