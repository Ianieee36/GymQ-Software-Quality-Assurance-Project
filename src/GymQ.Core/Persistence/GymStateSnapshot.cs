using System.Text.Json.Serialization;
using GymQ.Models;
using GymQ.Services;

namespace GymQ.Persistence;

/// <summary>Data needed to resume one local gym instance, independent of its UI.</summary>
public sealed class GymStateSnapshot
{
    public const int CurrentSchemaVersion = 3;

    [JsonRequired]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    [JsonRequired]
    public DateTime SavedAtUtc { get; init; }
    [JsonRequired]
    public TimeSpan ClockOffset { get; init; }
    [JsonRequired]
    public TimeSpan AdvancedBy { get; init; }
    [JsonRequired]
    public long NextReportNumber { get; init; }
    [JsonRequired]
    public Equipment[] Equipment { get; init; } = Array.Empty<Equipment>();
    [JsonRequired]
    public SessionState[] Sessions { get; init; } = Array.Empty<SessionState>();
    [JsonRequired]
    public FaultReport[] Reports { get; init; } = Array.Empty<FaultReport>();
    [JsonRequired]
    public QueueEntry[] Queue { get; init; } = Array.Empty<QueueEntry>();
    [JsonRequired]
    public NudgeCooldown[] NudgeCooldowns { get; init; } = Array.Empty<NudgeCooldown>();
    [JsonRequired]
    public NudgeNotice[] Nudges { get; init; } = Array.Empty<NudgeNotice>();
    [JsonRequired]
    public QueueCancellationNotice[] Cancellations { get; init; } = Array.Empty<QueueCancellationNotice>();
}

public sealed record SessionState(
    string SessionId,
    string EquipmentId,
    string MemberId,
    DateTime StartTime,
    DateTime? EndTime,
    SessionEndReason? EndReason);
