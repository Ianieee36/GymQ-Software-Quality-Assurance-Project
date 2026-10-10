using System.Text.Json;
using System.Text.Json.Serialization;
using GymQ.Models;
using GymQ.Services;

namespace GymQ.Persistence;

/// <summary>Reads the released equipment-owned nudge format without inventing session/requester ownership.</summary>
internal static class LegacyGymStateMigration
{
    public static GymStateSnapshot UpgradeVersion1(byte[] data, JsonSerializerOptions options)
    {
        var legacy = JsonSerializer.Deserialize<LegacySnapshot>(data, options)
            ?? throw new InvalidDataException("The legacy gym state file is empty.");
        var state = new GymStateSnapshot
        {
            SavedAtUtc = legacy.SavedAtUtc,
            ClockOffset = legacy.ClockOffset,
            AdvancedBy = legacy.AdvancedBy,
            NextReportNumber = legacy.NextReportNumber,
            Equipment = legacy.Equipment,
            Sessions = legacy.Sessions,
            Reports = legacy.Reports,
            Queue = legacy.Queue,
            Cancellations = legacy.Cancellations
        };
        GymStateValidator.Validate(state);
        ValidateLegacyNudges(legacy);
        // Version 1 stores neither the requesting member nor the session ID. Queue membership
        // may have changed since the request, so assigning these records to today's front
        // member could time out the wrong session. Retire only these legacy transient records.
        return state;
    }

    private static void ValidateLegacyNudges(LegacySnapshot legacy)
    {
        if (legacy.LastNudgeAt == null || legacy.Nudges == null)
            throw new InvalidDataException("Legacy nudge collections must not be null.");
        var equipmentIds = legacy.Equipment.Select(e => e.EquipmentId).ToHashSet(StringComparer.Ordinal);
        var activeSessions = legacy.Sessions.Where(s => s.EndTime == null)
            .ToDictionary(s => s.EquipmentId, StringComparer.Ordinal);
        void EquipmentId(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !equipmentIds.Contains(id))
                throw new InvalidDataException($"Unknown legacy nudge equipment '{id}'.");
        }
        foreach (var (id, time) in legacy.LastNudgeAt)
        {
            EquipmentId(id);
            Utc(time, "Legacy last nudge time");
        }
        var nudgedEquipment = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nudge in legacy.Nudges)
        {
            if (nudge == null) throw new InvalidDataException("Legacy nudges must not contain null entries.");
            EquipmentId(nudge.EquipmentId);
            Utc(nudge.ExpiresAt, "Legacy nudge expiration");
            if (!nudgedEquipment.Add(nudge.EquipmentId))
                throw new InvalidDataException("Only one legacy pending nudge per equipment is allowed.");
            if (!activeSessions.TryGetValue(nudge.EquipmentId, out var session) || session.MemberId != nudge.MemberId)
                throw new InvalidDataException("A legacy pending nudge must belong to the active equipment user.");
            if (nudge.ExpiresAt < session.StartTime)
                throw new InvalidDataException("A legacy nudge cannot expire before its session starts.");
        }
    }

    private static void Utc(DateTime value, string description)
    {
        if (value.Kind != DateTimeKind.Utc || value == DateTime.MinValue)
            throw new InvalidDataException($"{description} must be a nonempty UTC timestamp.");
    }

    private sealed class LegacySnapshot
    {
        public LegacySnapshot() { }
        [JsonRequired] public int SchemaVersion { get; init; }
        [JsonRequired] public DateTime SavedAtUtc { get; init; }
        [JsonRequired] public TimeSpan ClockOffset { get; init; }
        [JsonRequired] public TimeSpan AdvancedBy { get; init; }
        [JsonRequired] public long NextReportNumber { get; init; }
        [JsonRequired] public Equipment[] Equipment { get; init; } = Array.Empty<Equipment>();
        [JsonRequired] public SessionState[] Sessions { get; init; } = Array.Empty<SessionState>();
        [JsonRequired] public FaultReport[] Reports { get; init; } = Array.Empty<FaultReport>();
        [JsonRequired] public QueueEntry[] Queue { get; init; } = Array.Empty<QueueEntry>();
        [JsonRequired] public Dictionary<string, DateTime> LastNudgeAt { get; init; } = new();
        [JsonRequired] public LegacyNudge[] Nudges { get; init; } = Array.Empty<LegacyNudge>();
        [JsonRequired] public QueueCancellationNotice[] Cancellations { get; init; } = Array.Empty<QueueCancellationNotice>();
    }

    private sealed class LegacyNudge
    {
        public LegacyNudge() { }
        [JsonRequired] public string EquipmentId { get; init; } = "";
        [JsonRequired] public string MemberId { get; init; } = "";
        [JsonRequired] public DateTime ExpiresAt { get; init; }
    }
}
