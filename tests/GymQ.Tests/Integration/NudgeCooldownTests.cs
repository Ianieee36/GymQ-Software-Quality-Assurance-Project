using GymQ.Services;

namespace GymQ.Tests;

/// <summary>
/// The 5-minute nudge cooldown belongs to one pair: the session being nudged and the member nudging it.
/// A new session starts without a cooldown, and nobody is blocked by another member's nudge.
/// </summary>
[TestClass]
public sealed class NudgeCooldownTests
{
    [TestMethod]
    public void SameNudgerSameSession_BlockedUntilFiveMinutes_ThenAllowed()
    {
        var gym = Gym(out var clock);
        gym.SendNudge("E1", M(gym, "M001"));
        gym.Respond("E1", M(gym, "M002"), stillUsing: true);

        clock.Advance(NudgeService.CooldownWindow - TimeSpan.FromTicks(1));
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => gym.SendNudge("E1", M(gym, "M001")));
        Assert.StartsWith("You can nudge this member again in", ex.Message);

        clock.Advance(TimeSpan.FromTicks(1));
        gym.SendNudge("E1", M(gym, "M001"));
        Assert.HasCount(1, gym.Nudges);
    }

    [TestMethod]
    public void NewSession_StartsWithoutCooldown()
    {
        // The two-window flow: M001 uses E1, M002 nudges, M001 finishes, M002 claims and finishes,
        // M002 starts again and M001 queues and nudges straight away.
        var gym = new GymSession(false, new TestClock());
        gym.Start("E1", M(gym, "M001"));
        gym.Join("E1", M(gym, "M002"));
        gym.SendNudge("E1", M(gym, "M002"));
        gym.Respond("E1", M(gym, "M001"), stillUsing: false);
        gym.Claim("E1", M(gym, "M002"));
        gym.Finish("E1", M(gym, "M002"));
        gym.Start("E1", M(gym, "M002"));
        gym.Join("E1", M(gym, "M001"));

        gym.SendNudge("E1", M(gym, "M001"));

        Assert.AreEqual("M002", gym.Nudges.Single().MemberId);
        Assert.AreEqual("M001", gym.Nudges.Single().RequestedBy);
    }

    [TestMethod]
    public void ReplacementFrontMember_IsNotBlockedByPreviousNudgersCooldown()
    {
        var gym = Gym(out _);
        gym.Join("E1", M(gym, "M003"));
        gym.SendNudge("E1", M(gym, "M001"));
        gym.Respond("E1", M(gym, "M002"), stillUsing: true);
        gym.Leave("E1", M(gym, "M001"));

        gym.SendNudge("E1", M(gym, "M003"));

        Assert.AreEqual("M003", gym.Nudges.Single().RequestedBy);
    }

    [TestMethod]
    public void LeavingAndRejoining_DoesNotResetOwnCooldown()
    {
        var gym = Gym(out _);
        gym.SendNudge("E1", M(gym, "M001"));
        gym.Respond("E1", M(gym, "M002"), stillUsing: true);
        gym.Leave("E1", M(gym, "M001"));
        gym.Join("E1", M(gym, "M001"));

        Assert.ThrowsExactly<InvalidOperationException>(() => gym.SendNudge("E1", M(gym, "M001")));
        Assert.HasCount(0, gym.Nudges);
    }

    [TestMethod]
    public void RejectedAttempts_DoNotExtendCooldown()
    {
        var gym = Gym(out var clock);
        gym.SendNudge("E1", M(gym, "M001"));
        gym.Respond("E1", M(gym, "M002"), stillUsing: true);

        clock.Advance(TimeSpan.FromMinutes(3));
        Assert.ThrowsExactly<InvalidOperationException>(() => gym.SendNudge("E1", M(gym, "M001")));
        clock.Advance(TimeSpan.FromMinutes(2));

        gym.SendNudge("E1", M(gym, "M001"));
        Assert.HasCount(1, gym.Nudges);
    }

    [TestMethod]
    public void NonFrontMember_IsRejected_AndDoesNotStartACooldown()
    {
        var gym = Gym(out _);
        gym.Join("E1", M(gym, "M003"));

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => gym.SendNudge("E1", M(gym, "M003")));

        Assert.StartsWith("Only the next member in the queue can nudge", ex.Message);
        var session = gym.Sessions.ReadActiveSession("E1")!.SessionId;
        Assert.AreEqual(TimeSpan.Zero, gym.Nudging.CooldownRemaining(session, "M003"));
        gym.SendNudge("E1", M(gym, "M001"));
    }

    [TestMethod]
    public void CooldownRemaining_CountsDownForTheNudgerOnly()
    {
        var gym = Gym(out var clock);
        gym.SendNudge("E1", M(gym, "M001"));
        var session = gym.Sessions.ReadActiveSession("E1")!.SessionId;

        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.AreEqual(TimeSpan.FromMinutes(3), gym.Nudging.CooldownRemaining(session, "M001"));
        Assert.AreEqual(TimeSpan.Zero, gym.Nudging.CooldownRemaining(session, "M003"));
    }

    // M002 is using E1; M001 is at the front of the E1 queue.
    private static GymSession Gym(out TestClock clock)
    {
        clock = new TestClock();
        var gym = new GymSession(false, clock);
        gym.Start("E1", M(gym, "M002"));
        gym.Join("E1", M(gym, "M001"));
        return gym;
    }

    private static Models.Member M(GymSession gym, string id) => gym.FindMember(id);

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
}