using System.Globalization;
using GymQ.Models;
using GymQ.Services;

namespace GymQ.Persistence;

/// <summary>Rejects inconsistent files before they can mutate application state.</summary>
public static class GymStateValidator
{
    public static void Validate(GymStateSnapshot state, IEnumerable<string>? validMemberIds = null)
    {
        if (state == null) throw Invalid("The gym snapshot is null.");
        if (state.SchemaVersion != GymStateSnapshot.CurrentSchemaVersion)
            throw Invalid($"Unsupported gym state schema version '{state.SchemaVersion}'.");
        Utc(state.SavedAtUtc, "Save timestamp");
        if (state.ClockOffset < TimeSpan.Zero || state.AdvancedBy < TimeSpan.Zero || state.ClockOffset != state.AdvancedBy)
            throw Invalid("Demo clock offset and advanced time must be equal and nonnegative.");
        if (state.NextReportNumber < 0 || state.NextReportNumber == long.MaxValue)
            throw Invalid("The next report counter is invalid or exhausted.");
        if (state.Equipment == null || state.Sessions == null || state.Reports == null || state.Queue == null ||
            state.NudgeCooldowns == null || state.Nudges == null || state.Cancellations == null)
            throw Invalid("Snapshot collections must not be null.");

        var members = validMemberIds?.ToHashSet(StringComparer.Ordinal);
        void MemberId(string id)
        {
            Id(id, "Member ID");
            if (members != null && !members.Contains(id)) throw Invalid($"Unknown member '{id}'.");
        }

        var equipment = new Dictionary<string, Equipment>(StringComparer.Ordinal);
        foreach (var item in state.Equipment)
        {
            if (item == null) throw Invalid("Equipment must not contain null entries.");
            Id(item.EquipmentId, "Equipment ID");
            Id(item.Name, "Equipment name");
            if (!Enum.IsDefined(item.Status)) throw Invalid("Invalid equipment status.");
            if (!equipment.TryAdd(item.EquipmentId, item)) throw Invalid($"Duplicate equipment '{item.EquipmentId}'.");
        }
        Equipment EquipmentById(string id)
        {
            Id(id, "Equipment reference");
            return equipment.TryGetValue(id, out var item) ? item : throw Invalid($"Unknown equipment '{id}'.");
        }

        var sessionIds = new HashSet<string>(StringComparer.Ordinal);
        var activeEquipment = new Dictionary<string, SessionState>(StringComparer.Ordinal);
        var activeMembers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var session in state.Sessions)
        {
            if (session == null) throw Invalid("Sessions must not contain null entries.");
            Id(session.SessionId, "Session ID");
            if (!sessionIds.Add(session.SessionId)) throw Invalid($"Duplicate session '{session.SessionId}'.");
            EquipmentById(session.EquipmentId);
            MemberId(session.MemberId);
            Utc(session.StartTime, "Session start time");
            if (session.StartTime > DateTime.MaxValue.AddMinutes(-30))
                throw Invalid("The session start time cannot represent its maximum-duration deadline.");
            if (session.EndTime.HasValue != session.EndReason.HasValue)
                throw Invalid("Ended sessions require both an end time and a reason.");
            if (session.EndTime is { } end)
            {
                Utc(end, "Session end time");
                if (end < session.StartTime) throw Invalid("A session cannot end before it starts.");
                if (!Enum.IsDefined(session.EndReason!.Value)) throw Invalid("Invalid session end reason.");
            }
            else if (!activeEquipment.TryAdd(session.EquipmentId, session) || !activeMembers.Add(session.MemberId))
                throw Invalid("Only one active session per equipment and member is allowed.");
        }
        foreach (var item in equipment.Values)
        {
            bool active = activeEquipment.ContainsKey(item.EquipmentId);
            if ((item.Status == EquipmentStatus.InUse && !active) || (item.Status == EquipmentStatus.Available && active))
                throw Invalid($"Equipment '{item.EquipmentId}' disagrees with its active session state.");
        }

        var reportIds = new HashSet<string>(StringComparer.Ordinal);
        long highestReportNumber = 0;
        foreach (var report in state.Reports)
        {
            if (report == null) throw Invalid("Reports must not contain null entries.");
            Id(report.ReportId, "Report ID");
            if (!report.ReportId.StartsWith("R-", StringComparison.Ordinal) ||
                !long.TryParse(report.ReportId.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0)
                throw Invalid($"Invalid report ID '{report.ReportId}'.");
            if (!reportIds.Add(report.ReportId)) throw Invalid($"Duplicate report '{report.ReportId}'.");
            highestReportNumber = Math.Max(highestReportNumber, number);
            EquipmentById(report.EquipmentId);
            MemberId(report.SubmittedByMemberId);
            Id(report.Description, "Report description");
            Utc(report.SubmittedAt, "Report submission time");
            if (!Enum.IsDefined(report.Status)) throw Invalid("Invalid fault report status.");
            if (report.Status == FaultReportStatus.Pending)
            {
                if (report.ReviewedAt != null || report.ReviewedByStaffId != null)
                    throw Invalid("A pending report cannot have review details.");
            }
            else
            {
                if (report.ReviewedAt is not { } reviewedAt) throw Invalid("A reviewed report requires a review timestamp.");
                MemberId(report.ReviewedByStaffId!);
                Utc(reviewedAt, "Report review time");
                if (reviewedAt < report.SubmittedAt) throw Invalid("A report cannot be reviewed before submission.");
            }
        }
        if (state.NextReportNumber < highestReportNumber)
            throw Invalid("The report counter must cover every stored report ID.");

        var queueMembers = new HashSet<(string EquipmentId, string MemberId)>();
        var queuedEquipment = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in state.Queue)
        {
            if (entry == null) throw Invalid("Queues must not contain null entries.");
            var queueEquipment = EquipmentById(entry.EquipmentId);
            if (queueEquipment.Status == EquipmentStatus.Unavailable)
                throw Invalid("Unavailable equipment cannot have a live queue.");
            MemberId(entry.MemberId);
            if (!queueMembers.Add((entry.EquipmentId, entry.MemberId))) throw Invalid("A member cannot join the same queue twice.");
            Utc(entry.JoinedAt, "Queue join time");
            bool isFront = queuedEquipment.Add(entry.EquipmentId);
            if (entry.NotifiedAt is { } notifiedAt)
            {
                Utc(notifiedAt, "Queue notification time");
                if (queueEquipment.Status != EquipmentStatus.Available)
                    throw Invalid("A claim notification requires available equipment.");
                if (!isFront) throw Invalid("Only the front member of a queue may have a claim notification.");
                if (notifiedAt < entry.JoinedAt) throw Invalid("A queue notification cannot precede joining.");
                if (notifiedAt > DateTime.MaxValue.AddMinutes(-2))
                    throw Invalid("The notification time cannot represent its claim deadline.");
            }
        }

        var cooldownPairs = new HashSet<(string, string)>();
        foreach (var cooldown in state.NudgeCooldowns)
        {
            if (cooldown == null) throw Invalid("Nudge cooldowns must not contain null entries.");
            Id(cooldown.SessionId, "Cooldown session ID");
            if (!sessionIds.Contains(cooldown.SessionId)) throw Invalid($"Cooldown for unknown session '{cooldown.SessionId}'.");
            MemberId(cooldown.RequestedBy);
            Utc(cooldown.LastNudgeAt, "Last nudge time");
            if (!cooldownPairs.Add((cooldown.SessionId, cooldown.RequestedBy)))
                throw Invalid("Only one cooldown per session and nudger is allowed.");
        }

        var nudgedSessions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nudge in state.Nudges)
        {
            if (nudge == null) throw Invalid("Nudges must not contain null entries.");
            Id(nudge.SessionId, "Nudge session ID");
            EquipmentById(nudge.EquipmentId);
            MemberId(nudge.MemberId);
            MemberId(nudge.RequestedBy);
            Utc(nudge.ExpiresAt, "Nudge expiration");
            if (!nudgedSessions.Add(nudge.EquipmentId)) 
                throw Invalid("Only one pending nudge per equipment is allowed.");
            // A nudge belongs to one active session; equipment and target must agree with it.
            if (!activeEquipment.TryGetValue(nudge.EquipmentId, out var session) || 
                session.SessionId != nudge.SessionId || session.MemberId != nudge.MemberId)
                throw Invalid("A pending nudge must belong to the active session on its equipment.");
            
            if (nudge.RequestedBy == nudge.MemberId) 
                throw Invalid("A member cannot nudge their own session.");
            if (nudge.ExpiresAt < session.StartTime) 
                throw Invalid("A nudge cannot expire before its session starts.");
            // Whether the requester is still queued is not checked here: restore withdraws such
            // nudges instead of rejecting the whole file.
        }
        foreach (var notice in state.Cancellations)
        {
            if (notice == null) throw Invalid("Cancellation notices must not contain null entries.");
            EquipmentById(notice.EquipmentId);
            MemberId(notice.MemberId);
            Id(notice.EquipmentName, "Cancellation equipment name");
        }
    }

    private static void Id(string? value, string description)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Invalid($"{description} is required.");
    }

    private static void Utc(DateTime value, string description)
    {
        if (value.Kind != DateTimeKind.Utc || value == DateTime.MinValue)
            throw Invalid($"{description} must be a nonempty UTC timestamp.");
    }

    private static InvalidDataException Invalid(string message) => new(message);
}
