using GymQ.Models;
using GymQ.Services;

namespace GymQ.Desktop.Presentation;

/// <summary>
/// What the session page shows for one member's session (GQ-07).
/// Contains no Avalonia types, so it can be unit tested without starting the UI.
/// </summary>
public sealed record SessionStatus(bool IsActive, string Headline, string Duration, string Detail)
{
    public static SessionStatus For(GymSession gym, UsageSession? session)
    {
        if (session == null)
            return new(false, "No session", "00:00", "You don't have a session on this machine.");

        // Still running: live timer and time left before the 30-minute cap.
        if (session.EndTime == null)
        {
            var elapsed = gym.UtcNow - session.StartTime;
            return new(true, "You're using", Format(elapsed),
                FormatRemaining(TimeSpan.FromMinutes(30) - elapsed) + " left of your 30-minute session");
        }

        // Ended: keep this member's real duration and describe the machine as it is now.
        return new(false, "Session complete", Format(session.Duration!.Value),
            EndedBecause(session.EndReason) + " " + MachineNow(gym, session));
    }

    private static string EndedBecause(SessionEndReason? reason) => reason switch
    {
        SessionEndReason.ManualFinish => "You ended your session.",
        SessionEndReason.NudgeResponse => "You confirmed you were finished.",
        SessionEndReason.NudgeTimeout => "Your session ended because the nudge wasn't answered.",
        SessionEndReason.MaxDurationReached => "Your session reached the 30-minute limit.",
        _ => "Your session has ended."
    };

    private static string MachineNow(GymSession gym, UsageSession mine)
    {
        if (gym.Equipment[mine.EquipmentId].Status == EquipmentStatus.Unavailable)
            return "This machine is now out of service.";

        var current = gym.Sessions.ReadActiveSession(mine.EquipmentId);
        if (current != null && current.SessionId != mine.SessionId)
            return "Another member is now using this machine.";

        if (gym.Queue.ReadQueue(mine.EquipmentId).FirstOrDefault()?.NotifiedAt != null)
            return "It has been offered to the next member in the queue.";

        return "This machine is ready for the next member.";
    }

    /// <summary>Elapsed time, rounded down to whole seconds.</summary>
    public static string Format(TimeSpan duration)
    {
        var total = Math.Max(0, (int)duration.TotalSeconds);
        return $"{total / 60:00}:{total % 60:00}";
    }

    /// <summary>Time left, rounded up so a countdown never shows less time than remains.</summary>
    public static string FormatRemaining(TimeSpan remaining) =>
        Format(TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, remaining.TotalSeconds))));
}