using Avalonia.Controls;
namespace GymQ.Desktop.Views;
public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
        // Put the cursor in the username box as soon as the login screen appears.
        AttachedToVisualTree += (_, _) => UserNameBox.Focus();
    }
}
