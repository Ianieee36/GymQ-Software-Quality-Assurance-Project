using GymQ.Models;

namespace GymQ.Services;

public partial class QueueService
{
    internal QueueEntry[] ExportQueueState() =>
        _queues.Values.SelectMany(queue => queue).Select(CloneEntry).ToArray();

    internal void RestoreState(IEnumerable<QueueEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var restored = entries.Select(CloneEntry).ToArray();
        _queues.Clear();
        foreach (var entry in restored)
        {
            if (!_queues.TryGetValue(entry.EquipmentId, out var queue))
                _queues[entry.EquipmentId] = queue = new List<QueueEntry>();
            queue.Add(entry);
        }
    }

    private static QueueEntry CloneEntry(QueueEntry entry) => new(entry.EquipmentId, entry.MemberId)
    {
        JoinedAt = entry.JoinedAt,
        NotifiedAt = entry.NotifiedAt
    };
}