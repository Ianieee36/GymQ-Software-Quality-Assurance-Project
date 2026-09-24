using GymQ.Models;

namespace GymQ.Repository;

/// <summary>Shares the existing equipment instances between application services.</summary>
public sealed class InMemoryEquipmentRepository : IEquipmentRepository
{
    private readonly Dictionary<string, Equipment> _equipment;

    public InMemoryEquipmentRepository(Dictionary<string, Equipment> equipment)
    {
        _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
    }

    public Equipment? GetById(string equipmentId)
        => _equipment.TryGetValue(equipmentId, out var equipment) ? equipment : null;

    public List<Equipment> GetAll() => _equipment.Values.ToList();
}
