using System;
using System.Windows;

namespace Mep1.Erp.Desktop
{
    public partial class LoginWindow : Window
    {
        private readonly ErpApiClient _api;

        public LoginWindow(ErpApiClient api)
        {
            InitializeComponent();
            _api = api;
            UsernameTextBox.Focus();
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusText.Text = "";
                LoginButton.IsEnabled = false;

                var username = (UsernameTextBox.Text ?? "").Trim();
                var password = PasswordBox.Password ?? "";

                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                {
                    StatusText.Text = "Enter a username and password.";
                    return;
                }

                var dto = await _api.DesktopAdminLoginAsync(username, password);

                // store in memory only
                DesktopActorSession.Set(dto);

                // set header for all future calls from this client
                _api.SetActorToken(dto.ActorToken);

                if (dto.MustChangePassword)
                {
                    StatusText.Text = "Your password must be changed (MustChangePassword=true). " +
                                      "For go-live, change it via the portal change-password endpoint.";
                    // You can choose to block here, but for “tight scope” I’m not forcing it.
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
            }
            finally
            {
                LoginButton.IsEnabled = true;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
