using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
public class ProfileNavigationPolicyUiTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    [Timeout(60_000, CooperativeCancellation = true)]
    public async Task HeaderAvatar_UsesAgreedRoleNavigation(bool isStaff)
    {
        await UiSession.Run(() =>
        {
            var gym = new GymSession(false);
            var shell = new ShellViewModel(gym);
            shell.SignIn(gym.FindMember(isStaff ? "S001" : "M001"));
            var window = new MainWindow(shell);
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                // Exercise the actual header avatar, rather than directly calling Profile.
                var avatar = window.GetVisualDescendants().OfType<Button>().Single(b =>
                    b.Classes.Contains("imageButton") && ReferenceEquals(b.Command, shell.Profile));
                Assert.IsTrue(avatar.IsEffectivelyVisible);
                Assert.IsTrue(avatar.IsEffectivelyEnabled);
                avatar.Focus();
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();

                Assert.AreEqual(isStaff, shell.IsStaff);
                Assert.AreEqual(!isStaff, shell.ShowMemberNav);
                Assert.IsFalse(shell.HasError);
                if (isStaff)
                {
                    Assert.IsInstanceOfType<StaffViewModel>(shell.Page);
                    Assert.IsTrue(window.GetVisualDescendants().OfType<StaffView>().Any(v => v.IsEffectivelyVisible));
                    Assert.IsFalse(window.GetVisualDescendants().OfType<ProfileView>().Any(v => v.IsEffectivelyVisible));
                }
                else
                {
                    Assert.IsInstanceOfType<ProfileViewModel>(shell.Page);
                    Assert.IsTrue(window.GetVisualDescendants().OfType<ProfileView>().Any(v => v.IsEffectivelyVisible));
                    Assert.IsFalse(window.GetVisualDescendants().OfType<StaffView>().Any(v => v.IsEffectivelyVisible));
                }
                SaveFrame(window, isStaff ? "gq09-staff-avatar-policy" : "gq09-member-avatar-profile");
            }
            finally { window.Close(); }
        }, TestContext.CancellationToken);
    }

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
}
