using GymQ.Models;
namespace GymQ.Services;
public partial class QueueService
{
    public IReadOnlyList<QueueEntry> ReadQueue(string equipmentId) =>
        _queues.TryGetValue(equipmentId, out var queue)
        ? queue.Select(e => new QueueEntry(e.EquipmentId, e.MemberId) { JoinedAt = e.JoinedAt, NotifiedAt = e.NotifiedAt }).ToArray()
        : Array.Empty<QueueEntry>();
    // Supporting action shown in the mockup; existing service methods are unchanged.
    public void LeaveQueue(string equipmentId, string memberId)
    {
        if (!_queues.TryGetValue(equipmentId, out var queue)) return;
        queue.RemoveAll(e => e.MemberId == memberId);
    }

    // Removing the entries also removes their claim deadlines and pending offers.
    public IReadOnlyList<string> CancelQueue(string equipmentId)
    {
        if (!_queues.Remove(equipmentId, out var queue)) return Array.Empty<string>();
        return queue.Select(e => e.MemberId).ToArray();
    }
}
