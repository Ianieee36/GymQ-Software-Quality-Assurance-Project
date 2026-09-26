using Avalonia.Headless;
using GymQ.Desktop.ViewModels;
using GymQ.Services;

namespace GymQ.Tests;

[TestClass]
[DoNotParallelize]
public class LoginTests
{
    [TestMethod]
    [DataRow("S001")]
    [DataRow("M002")]
    [DataRow("M003")]
    public async Task DemoLogin_RoutesToCorrectDashboard_AndLogoutRestoresGuard(string memberId)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(UiTestApp));
        await session.Dispatch(() =>
        {
            var shell = new ShellViewModel(new GymSession(false));
            var login = (LoginViewModel)shell.Page;
            var command = memberId switch
            {
                "S001" => login.LogInAsStaff,
                "M002" => login.LogInAsUserChris,
                _ => login.LogInAsUserJayden
            };
            command.Execute(null);
            Assert.IsTrue(shell.IsLoggedIn);
            Assert.AreEqual(memberId, shell.Current.MemberId);
            if (memberId == "S001")
            {
                Assert.IsInstanceOfType<StaffViewModel>(shell.Page);
                shell.Navigate("equipment");
                Assert.IsInstanceOfType<StaffViewModel>(shell.Page);
            }
            else
            {
                Assert.IsInstanceOfType<EquipmentViewModel>(shell.Page);
                shell.Navigate("staff");
                Assert.IsInstanceOfType<EquipmentViewModel>(shell.Page);
            }
            shell.LogOut.Execute(null);
            Assert.IsFalse(shell.IsLoggedIn);
            shell.Navigate("equipment");
            Assert.IsInstanceOfType<LoginViewModel>(shell.Page);
        }, CancellationToken.None);
    }

    [TestMethod]
    [DataRow("Lorenz_Soriano", "wrong-password")]
    [DataRow("unknown-user", "LS123")]
    public async Task InvalidCredentials_StayOnLogin_ShowError_AndCanRetry(string username, string password)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(UiTestApp));
        await session.Dispatch(() =>
        {
            var shell = new ShellViewModel(new GymSession(false));
            var login = (LoginViewModel)shell.Page;
            login.UserName = username;
            login.Password = password;
            login.LogIn.Execute(null);
            Assert.IsFalse(shell.IsLoggedIn);
            Assert.AreSame(login, shell.Page);
            Assert.AreEqual("Incorrect username or password.", login.Message);
            Assert.AreEqual("", login.Password);

            login.UserName = "Lorenz_Soriano";
            login.Password = "LS123";
            login.LogIn.Execute(null);
            Assert.IsTrue(shell.IsLoggedIn);
            Assert.AreEqual("M001", shell.Current.MemberId);
            Assert.IsInstanceOfType<EquipmentViewModel>(shell.Page);
        }, CancellationToken.None);
    }
}
