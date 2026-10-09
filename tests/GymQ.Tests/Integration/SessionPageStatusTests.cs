using GymQ.Desktop.Presentation;
using GymQ.Models;
using GymQ.Services;

namespace GymQ.Tests;

/// <summary>
/// GQ-07 regression coverage: what a member's session page shows during and after their session.
/// Uses SessionStatus directly, so no Avalonia session is needed and the tests run in milliseconds.
/// </summary>
[TestClass]
public class SessionPageStatusTests
{
    // The session the page would pin when opened: the member's latest session on that machine.
    private static SessionStatus PageFor(GymSession gym, string equipmentId, string memberId) =>
        SessionStatus.For(gym, gym.Sessions.ReadLatestSession(equipmentId, memberId));

    [TestMethod]
    public void ActiveSession_ShowsLiveDurationAndTimeRemaining()
    {
        var gym = new GymSession(seed: false);
        gym.Start("E1", gym.FindMember("M001"));
        gym.AdvanceDemoTime(2);

        var page = PageFor(gym, "E1", "M001");

        Assert.IsTrue(page.IsActive);
        Assert.AreEqual("You're using", page.Headline);
        Assert.AreEqual("02:00", page.Duration);
        Assert.AreEqual("28:00 left of your 30-minute session", page.Detail);
    }

    // The original GQ-07 scenario.
    [TestMethod]
    public void AfterHandover_KeepsOwnDuration_AndSaysAnotherMemberIsUsingIt()
    {
        var gym = new GymSession(seed: false);
        var a = gym.FindMember("M001");
        var b = gym.FindMember("M002");

        gym.Start("E1", a);
        gym.Join("E1", b);
        gym.AdvanceDemoTime(2);
        gym.Finish("E1", a);
        gym.Claim("E1", b);
        gym.AdvanceDemoTime(2);         // B's session runs on; A's page must not follow it

        var page = PageFor(gym, "E1", a.MemberId);

        Assert.IsFalse(page.IsActive);
        Assert.AreEqual("Session complete", page.Headline);
        Assert.AreEqual("02:00", page.Duration, "A's page must keep A's real duration, not 00:00 or B's timer.");
        Assert.Contains("Another member is now using this machine.", page.Detail);
        Assert.DoesNotContain("ready", page.Detail, "Must not claim the machine is ready while B uses it.");
    }

    [TestMethod]
    public void EndedWithNextMemberOffered_SaysItWasOfferedToTheQueue()
    {
        var gym = new GymSession(seed: false);
        gym.Start("E1", gym.FindMember("M001"));
        gym.Join("E1", gym.FindMember("M002"));
        gym.AdvanceDemoTime(1);
        gym.Finish("E1", gym.FindMember("M001"));   // M002 is notified but has not claimed yet

        var page = PageFor(gym, "E1", "M001");

        Assert.AreEqual("01:00", page.Duration);
        Assert.Contains("offered to the next member", page.Detail);
    }

    [TestMethod]
    public void EndedWithNobodyWaiting_SaysTheMachineIsReady()
    {
        var gym = new GymSession(seed: false);
        gym.Start("E1", gym.FindMember("M001"));
        gym.AdvanceDemoTime(1);
        gym.Finish("E1", gym.FindMember("M001"));

        var page = PageFor(gym, "E1", "M001");

        Assert.AreEqual("01:00", page.Duration);
        Assert.AreEqual("You ended your session. This machine is ready for the next member.", page.Detail);
    }

    [TestMethod]
    public void ReachedTimeLimit_KeepsThirtyMinutesAndGivesTheReason()
    {
        var gym = new GymSession(seed: false);
        gym.Start("E1", gym.FindMember("M001"));
        gym.AdvanceDemoTime(30);

        var page = PageFor(gym, "E1", "M001");

        Assert.IsFalse(page.IsActive);
        Assert.AreEqual("30:00", page.Duration);
        Assert.StartsWith("Your session reached the 30-minute limit.", page.Detail);
    }

    [TestMethod]
    public void MachineMarkedOutOfService_SaysOutOfService()
    {
        var gym = new GymSession(seed: false);
        var member = gym.FindMember("M001");
        gym.Start("E1", member);
        var report = gym.Faults.SubmitFaultReport("E1", member, "Belt slipping");
        gym.Faults.ReviewFaultReport(report.ReportId, gym.FindMember("S001"), confirm: true);
        gym.Finish("E1", member);

        var page = PageFor(gym, "E1", member.MemberId);

        Assert.EndsWith("This machine is now out of service.", page.Detail);
    }

    [TestMethod]
    public void ReadLatestSession_ReturnsOwnEndedSession_NotTheNextMembers()
    {
        var gym = new GymSession(seed: false);
        var a = gym.FindMember("M001");
        var b = gym.FindMember("M002");
        gym.Start("E1", a);
        gym.Join("E1", b);
        gym.Finish("E1", a);
        gym.Claim("E1", b);

        var latestForA = gym.Sessions.ReadLatestSession("E1", a.MemberId)!;

        Assert.AreEqual(a.MemberId, latestForA.MemberId);
        Assert.IsNotNull(latestForA.EndTime);
        Assert.IsNull(gym.Sessions.ReadLatestSession("E1", "M003"));
    }
}