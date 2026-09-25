using GymQ.Desktop.Presentation;

namespace GymQ.Desktop.ViewModels;

/// <summary>
/// Login page. Credentials are checked by GymSession.Login (UserService);
/// on success the shell routes members and staff to their own dashboards.
/// </summary>
public sealed class LoginViewModel : PageViewModel
{
    private string _userName = "";
    public string UserName { get => _userName; set { if (Set(ref _userName, value)) Message = ""; } }

    private string _password = "";
    public string Password { get => _password; set { if (Set(ref _password, value)) Message = ""; } }

    private string _message = "";
    public string Message { get => _message; private set { if (Set(ref _message, value)) Notify(nameof(HasMessage)); } }
    public bool HasMessage => Message.Length > 0;

    public ActionCommand LogIn { get; }

    // Commands for demo logins, to save us from typing credentials repeatedly.
    public ActionCommand LogInAsStaff {  get; }
    public ActionCommand LogInAsUserJayden { get; }
    public ActionCommand LogInAsUserChris { get; }

    public LoginViewModel(ShellViewModel shell) : base(shell)
    {
        LogIn = new(() =>
        {
            if (string.IsNullOrWhiteSpace(UserName) || string.IsNullOrEmpty(Password))
            {
                Message = "Enter your username and password.";
                return;
            }

            var account = shell.Gym.Login(UserName, Password);
            if (account == null)
            {
                // Clear the password first: its setter clears Message.
                Password = "";
                // One generic message, so the screen never reveals which usernames exist.
                Message = "Incorrect username or password.";
                return;
            }

            shell.SignIn(account);
        });


        // Demo account quick logins
        LogInAsStaff = new(() =>
        {
            UserName = "Gym_Staff";
            Password = "GS123";
            var account = shell.Gym.Login(UserName, Password);
            shell.SignIn(account);
        });

        LogInAsUserJayden = new(() =>
        {
            UserName = "Jayden_Marsh";
            Password = "JM123";
            var account = shell.Gym.Login(UserName, Password);
            shell.SignIn(account);
        });

        LogInAsUserChris = new(() =>
        {
            UserName = "Christian_Cantos";
            Password = "CC123";
            var account = shell.Gym.Login(UserName, Password);
            shell.SignIn(account);
        });
    }
}
