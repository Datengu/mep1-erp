using System;
using System.Windows;
using Mep1.Erp.Core;

namespace Mep1.Erp.Desktop
{
    public partial class LoginWindow : Window
    {
        private readonly ErpApiClient _api;

        public LoginWindow(ErpApiClient api)
        {
            InitializeComponent();
            _api = api;

            if (_api.IsStaging && (Title?.IndexOf("[STAGING]", StringComparison.OrdinalIgnoreCase) ?? -1) < 0)
                Title = (Title ?? "Login") + " [STAGING]";

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


                // Unified JWT login
                var dto = await _api.AuthLoginAsync(username, password);

                // Desktop access requires Admin/Owner
                if (!Enum.TryParse<TimesheetUserRole>(dto.Role, ignoreCase: true, out var role))
                    role = TimesheetUserRole.Worker;

                if (role != TimesheetUserRole.Admin && role != TimesheetUserRole.Owner)
                {
                    StatusText.Text = "Desktop access requires Admin or Owner.";
                    return;
                }

                // Set bearer token for all future API calls from this desktop client
                _api.SetBearerToken(dto.AccessToken);

                // Store some identity info in memory if you want it for UI display
                DesktopActorSession.SetFromJwtLogin(
                    workerId: dto.WorkerId,
                    username: dto.Username,
                    role: dto.Role,
                    name: dto.Name,
                    initials: dto.Initials,
                    mustChangePassword: dto.MustChangePassword,
                    expiresUtc: dto.ExpiresUtc
                );

                if (dto.MustChangePassword)
                {
                    StatusText.Text =
                        "Your password must be changed (MustChangePassword=true). " +
                        "Change it via the portal or add a desktop change-password screen.";
                    // Optional: block login here by returning.
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
