using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GymQ.Desktop.ViewModels;
using GymQ.Desktop.Views;
using GymQ.Services;
using GymQ_ENSE707_SQA_Project;

namespace GymQ.Tests;

[TestCategory("UI")]
[TestClass]
[DoNotParallelize]
public class SessionTitleUiTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(SessionEndReason.MaxDurationReached)]
    [DataRow(SessionEndReason.NudgeTimeout)]
    [DataRow(SessionEndReason.NudgeResponse)]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task SessionEnds_UpdatesRenderedTitle_WithoutReopeningPage(SessionEndReason reason)
    {
        await UiSession.Run(() =>
        {
            var gym = new GymSession(false, new FixedClock());
            gym.Start("E1", gym.FindMember("M001"));
            gym.Join("E1", gym.FindMember("M002"));
            var shell = new ShellViewModel(gym);
            shell.SignIn(gym.FindMember("M001"));
            shell.Navigate("session", "E1");
            var page = (SessionViewModel)shell.Page;
            var window = new MainWindow(shell);
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                var view = window.GetVisualDescendants().OfType<SessionView>().Single();
                var title = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Parent is Grid);
                Assert.AreEqual("Using Treadmill #3", title.Text);
                Assert.IsTrue(EndButton(view).IsEffectivelyVisible);

                if (reason == SessionEndReason.MaxDurationReached)
                    gym.AdvanceDemoTime(30);
                else
                {
                    gym.SendNudge("E1", gym.FindMember("M002"));
                    if (reason == SessionEndReason.NudgeTimeout)
                        gym.AdvanceDemoTime(2);
                    else
                        gym.Respond("E1", gym.FindMember("M001"), stillUsing: false);
                }

                // Keep the same page and bound TextBlock: inspecting a freshly opened page
                // would hide the missing Title change notification that caused GQ-07.
                Dispatcher.UIThread.RunJobs();
                Assert.AreSame(page, shell.Page);
                Assert.AreSame(view, window.GetVisualDescendants().OfType<SessionView>().Single());
                Assert.AreEqual(reason, gym.Sessions.ReadSessions().Single().EndReason);
                Assert.IsFalse(page.IsActive);
                Assert.AreEqual("Treadmill #3", title.Text);
                Assert.IsTrue(view.GetVisualDescendants().OfType<TextBlock>().Any(t =>
                    t.IsEffectivelyVisible && t.Text == "Session complete"));
                Assert.IsFalse(EndButton(view).IsEffectivelyVisible);
                SaveFrame(window, "gq07-session-title-" + reason);

                // The title remains completed when the next queued member claims the machine.
                gym.Claim("E1", gym.FindMember("M002"));
                shell.Tick();
                Dispatcher.UIThread.RunJobs();
                Assert.AreSame(page, shell.Page);
                Assert.AreEqual("Treadmill #3", title.Text);
                Assert.AreEqual("M002", gym.Sessions.ReadActiveSession("E1")!.MemberId);
                Assert.IsFalse(EndButton(view).IsEffectivelyVisible);
                Assert.IsTrue(view.GetVisualDescendants().OfType<TextBlock>().Any(t =>
                    t.IsEffectivelyVisible && t.Text != null &&
                    t.Text.Contains("Another member is now using this machine.", StringComparison.Ordinal)));
            }
            finally { window.Close(); }
        }, TestContext.CancellationToken);
    }

    private static Button EndButton(SessionView view) =>
        view.GetVisualDescendants().OfType<Button>().Single(b =>
            b.Content is string label && label == "End Session");

    private static void SaveFrame(MainWindow window, string name)
    {
        var output = Environment.GetEnvironmentVariable("GYMQ_SCREENSHOTS");
        if (string.IsNullOrEmpty(output)) return;
        Directory.CreateDirectory(output);
        Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var bitmap = window.CaptureRenderedFrame();
        Assert.IsNotNull(bitmap);
        bitmap.Save(Path.Combine(output, name + ".png"));
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);
    }
}
