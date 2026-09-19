using GymQ.Models;
namespace GymQ.Repository;

// Testable interface for Equipment repository
    public interface IEquipmentRepository
    {
        Equipment? GetById(string equipmentId);
        List<Equipment> GetAll();
    }