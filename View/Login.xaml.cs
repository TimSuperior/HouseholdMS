using System;
using System.Data.SQLite;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HouseholdMS;          // MainWindow
using HouseholdMS.Model;
using HouseholdMS.Resources;   // Strings.*

namespace HouseholdMS.View
{
    public partial class Login : Window
    {
        public Login()
        {
            InitializeComponent();

            // Keep window within screen’s usable area and center on render/resize
            var wa = SystemParameters.WorkArea;
            MaxWidth = Math.Max(400, wa.Width - 40);
            MaxHeight = Math.Max(300, wa.Height - 40);

            ShowLoginPanel();

            Loaded += (_, __) =>
            {
                Keyboard.Focus(LoginUsernameBox);
                LoginUsernameBox.SelectAll();
            };

            CenterInWorkArea();
            SizeChanged += (_, __) => CenterInWorkArea();
            ContentRendered += (_, __) => CenterInWorkArea();
        }

        private void CenterInWorkArea()
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + (wa.Width - ActualWidth) / 2;
            Top = wa.Top + (wa.Height - ActualHeight) / 2;
        }

        private void ClearRegisterForm()
        {
            if (RegNameBox != null) RegNameBox.Text = string.Empty;
            if (RegUsernameBox != null) RegUsernameBox.Text = string.Empty;
            if (RegPasswordBox != null) RegPasswordBox.Clear();
            if (RegConfirmPasswordBox != null) RegConfirmPasswordBox.Clear();
            if (RegPhoneBox != null) RegPhoneBox.Text = string.Empty;
            if (RegAddressBox != null) RegAddressBox.Text = string.Empty;
            if (RegAreaBox != null) RegAreaBox.Text = string.Empty;
            if (RegNoteBox != null) RegNoteBox.Text = string.Empty;
            if (IsTechnicianCheck != null) IsTechnicianCheck.IsChecked = false;
            if (TechDetailsPanel != null) TechDetailsPanel.Visibility = Visibility.Collapsed;
        }

        // ===== Panel toggles =====
        private void ShowLoginPanel()
        {
            Title = Strings.Login_Title;               // localized
            LoginCard.Visibility = Visibility.Visible;
            RegisterCard.Visibility = Visibility.Collapsed;

            LoginBtn.IsDefault = true;
            RegisterBtn.IsDefault = false;

            Keyboard.Focus(LoginUsernameBox);
            LoginUsernameBox.SelectAll();
        }

        private void ShowRegisterPanel()
        {
            Title = Strings.Register_Title;            // localized
            RegisterCard.Visibility = Visibility.Visible;
            LoginCard.Visibility = Visibility.Collapsed;

            LoginBtn.IsDefault = false;
            RegisterBtn.IsDefault = true;

            ClearRegisterForm();

            Keyboard.Focus(RegNameBox);
            RegNameBox.SelectAll();
        }

        // ===== Handlers =====
        private void ShowRegister_Click(object sender, RoutedEventArgs e) => ShowRegisterPanel();

        private void BackToLoginButton_Click(object sender, RoutedEventArgs e)
        {
            ClearRegisterForm();
            ShowLoginPanel();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            string username = (LoginUsernameBox.Text ?? "").Trim();
            string password = (LoginPasswordBox.Password ?? "").Trim();

            if (username.Length == 0 || password.Length == 0)
            {
                MessageBox.Show(
                    Strings.Login_MissingCredentials_Body,
                    Strings.Login_MissingCredentials_Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Keyboard.Focus(LoginUsernameBox);
                return;
            }

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();

                    const string query = "SELECT Role FROM Users WHERE LOWER(Username) = LOWER(@username) AND PasswordHash = @password";
                    using (var cmd = new SQLiteCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@username", username);
                        cmd.Parameters.AddWithValue("@password", password);

                        var roleObj = cmd.ExecuteScalar();
                        string role = (roleObj as string)?.Trim();

                        if (string.Equals(username, "root", StringComparison.OrdinalIgnoreCase))
                            role = "Admin";

                        if (!string.IsNullOrEmpty(role))
                        {
                            var mainWindow = new MainWindow(role, username);
                            mainWindow.Show();
                            Close();
                        }
                        else
                        {
                            MessageBox.Show(
                                Strings.Login_InvalidCredentials_Body,
                                Strings.Login_Failed_Title,
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                            LoginUsernameBox.Clear();
                            LoginPasswordBox.Clear();
                            Keyboard.Focus(LoginUsernameBox);
                        }
                    }
                }
            }
            catch (SQLiteException ex)
            {
                MessageBox.Show(
                    $"{Strings.Db_Error_Prefix}\n{ex.Message}",
                    Strings.Db_Error_Title,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"{Strings.Error_UnexpectedPrefix}\n{ex.Message}",
                    Strings.Error_Title,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // show/hide tech details
        private void Reg_TechCheck_Toggled(object sender, RoutedEventArgs e)
        {
            if (TechDetailsPanel == null || IsTechnicianCheck == null) return;
            TechDetailsPanel.Visibility = (IsTechnicianCheck.IsChecked == true)
                                          ? Visibility.Visible
                                          : Visibility.Collapsed;
        }

        // ---- Registration validation helpers ----

        private static bool HasDigit(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++) if (char.IsDigit(s[i])) return true;
            return false;
        }

        private static bool HasSymbol(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++) if (!char.IsLetterOrDigit(s[i])) return true;
            return false;
        }

        private static string DigitsOnly(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return Regex.Replace(s, @"\D", "");
        }

        private bool ValidateRegistrationInputs(
            string name, string username, string password, string confirmPassword, bool wantsTech,
            string phoneRaw, string address, string area,
            out string sanitizedPhone, out string errorMessage, out Control focusTarget)
        {
            sanitizedPhone = DigitsOnly(phoneRaw);
            var sb = new StringBuilder();

            // Avoid CS1628 by not capturing the out variable in a local function
            Control firstFocus = null;
            void AddMsg(string msg, Control focus)
            {
                sb.Append("• ");
                sb.AppendLine(msg);
                if (firstFocus == null) firstFocus = focus;
            }

            // Name
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2)
                AddMsg(Strings.Val_Name_Min2, RegNameBox);

            // Username rules (≥4 chars, no spaces, not equal to Name)
            if (string.IsNullOrWhiteSpace(username) || username.Trim().Length < 4)
                AddMsg(Strings.Val_Username_Min4, RegUsernameBox);

            if (!string.IsNullOrWhiteSpace(username) && username.IndexOf(' ') >= 0)
                AddMsg(Strings.Val_Username_NoSpaces, RegUsernameBox);

            if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(name) &&
                string.Equals(username.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
                AddMsg(Strings.Val_Username_NotEqualName, RegUsernameBox);

            // Password rules
            if (string.IsNullOrEmpty(password) || password.Length <= 6)
            {
                AddMsg(Strings.Val_Password_MinLen, RegPasswordBox);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(name) &&
                    string.Equals(password, name, StringComparison.OrdinalIgnoreCase))
                    AddMsg(Strings.Val_Password_NotEqualName, RegPasswordBox);

                if (!string.IsNullOrWhiteSpace(username) &&
                    string.Equals(password, username, StringComparison.OrdinalIgnoreCase))
                    AddMsg(Strings.Val_Password_NotEqualUsername, RegPasswordBox);

                if (!(HasDigit(password) || HasSymbol(password)))
                    AddMsg(Strings.Val_Password_MustContainDigitOrSymbol, RegPasswordBox);
            }

            // Confirm password
            if (string.IsNullOrEmpty(confirmPassword))
                AddMsg(Strings.Val_ConfirmPassword_Empty, RegConfirmPasswordBox);
            else if (!string.Equals(password, confirmPassword))
                AddMsg(Strings.Val_ConfirmPassword_MustMatch, RegConfirmPasswordBox);

            // Technician-only rules
            if (wantsTech)
            {
                if (sanitizedPhone.Length < 8 || sanitizedPhone.Length > 15)
                    AddMsg(Strings.Val_Phone_Range, RegPhoneBox);

                if (string.IsNullOrWhiteSpace(address) || address.Trim().Length < 5)
                    AddMsg(Strings.Val_Address_Min5, RegAddressBox);

                if (string.IsNullOrWhiteSpace(area) || area.Trim().Length < 2)
                    AddMsg(Strings.Val_Area_Min2, RegAreaBox);
            }
            else
            {
                if (sanitizedPhone.Length > 0 && (sanitizedPhone.Length < 8 || sanitizedPhone.Length > 15))
                    AddMsg(Strings.Val_Phone_OptionalRange, RegPhoneBox);
            }

            errorMessage = sb.ToString().Trim();
            focusTarget = firstFocus; // assign to out parameter at the end
            return errorMessage.Length == 0;
        }

        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            string name = (RegNameBox.Text ?? "").Trim();
            string username = (RegUsernameBox.Text ?? "").Trim();
            string password = (RegPasswordBox.Password ?? "").Trim();
            string confirm = (RegConfirmPasswordBox.Password ?? "").Trim();

            bool wantsTech = (IsTechnicianCheck != null && IsTechnicianCheck.IsChecked == true);
            string phoneRaw = RegPhoneBox != null ? RegPhoneBox.Text : null;
            string address = RegAddressBox != null ? RegAddressBox.Text : null;
            string area = RegAreaBox != null ? RegAreaBox.Text : null;
            string note = RegNoteBox != null ? RegNoteBox.Text : null;

            string sanitizedPhone, errors;
            Control focusTarget;

            if (!ValidateRegistrationInputs(name, username, password, confirm, wantsTech,
                                            phoneRaw, address, area,
                                            out sanitizedPhone, out errors, out focusTarget))
            {
                // reflect sanitized phone into UI if it changed
                if (RegPhoneBox != null && phoneRaw != sanitizedPhone)
                    RegPhoneBox.Text = sanitizedPhone;

                MessageBox.Show(
                    errors,
                    Strings.Val_FixIssues_Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);

                if (focusTarget != null)
                {
                    Keyboard.Focus(focusTarget);
                    if (focusTarget is TextBox tb) tb.SelectAll();
                }
                return;
            }

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();

                    // Unique username (case-insensitive)
                    using (var checkCmd = new SQLiteCommand(
                        "SELECT COUNT(*) FROM Users WHERE LOWER(Username) = LOWER(@username)", conn))
                    {
                        checkCmd.Parameters.AddWithValue("@username", username);
                        int existing = Convert.ToInt32(checkCmd.ExecuteScalar());
                        if (existing > 0)
                        {
                            MessageBox.Show(
                                Strings.Registration_UsernameTaken_Body,
                                Strings.Registration_UsernameTaken_Title,
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                            Keyboard.Focus(RegUsernameBox);
                            RegUsernameBox.SelectAll();
                            return;
                        }
                    }

                    // Role stays Guest on sign-up
                    const string role = "Guest";
                    int techApproved = wantsTech ? 0 : 1;
                    string pwHash = password; // NOTE: hash in production

                    using (var insertCmd = new SQLiteCommand(@"
INSERT INTO Users
(Name, Username, PasswordHash, Role, Phone, Address, AssignedArea, Note, IsActive, TechApproved)
VALUES
(@name, @username, @pwd, @role, @phone, @addr, @area, @note, 1, @techApproved);
", conn))
                    {
                        insertCmd.Parameters.AddWithValue("@name", name);
                        insertCmd.Parameters.AddWithValue("@username", username);
                        insertCmd.Parameters.AddWithValue("@pwd", pwHash);
                        insertCmd.Parameters.AddWithValue("@role", role);
                        insertCmd.Parameters.AddWithValue("@phone", (object)(sanitizedPhone.Length == 0 ? null : sanitizedPhone) ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@addr", (object)(string.IsNullOrWhiteSpace(address) ? null : address.Trim()) ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@area", (object)(string.IsNullOrWhiteSpace(area) ? null : area.Trim()) ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@note", (object)(string.IsNullOrWhiteSpace(note) ? null : note.Trim()) ?? DBNull.Value);
                        insertCmd.Parameters.AddWithValue("@techApproved", techApproved);
                        insertCmd.ExecuteNonQuery();
                    }
                }

                if (wantsTech)
                {
                    MessageBox.Show(
                        Strings.Register_Complete_TechPending,
                        Strings.Registration_Complete_Title,
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        Strings.Register_Complete_Guest,
                        Strings.Registration_Complete_Title,
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }

                LoginUsernameBox.Text = username;
                LoginPasswordBox.Clear();
                ClearRegisterForm();
                ShowLoginPanel();
                Keyboard.Focus(LoginPasswordBox);
            }
            catch (SQLiteException ex)
            {
                MessageBox.Show(
                    $"{Strings.Db_Error_Prefix} {ex.Message}",
                    Strings.Registration_Failed_Title,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"{Strings.Error_UnexpectedPrefix} {ex.Message}",
                    Strings.Registration_Failed_Title,
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (Application.Current.Windows.Count == 0)
                Application.Current.Shutdown();
        }

        // ===== Input filters (phone digits only) =====
        private void DigitsOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // allow only digits on typing
            e.Handled = !IsAllDigits(e.Text);
        }

        private static bool IsAllDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++) if (!char.IsDigit(s[i])) return false;
            return true;
        }

        private void DigitsOnly_OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            try
            {
                if (e.DataObject.GetDataPresent(typeof(string)))
                {
                    string text = (string)e.DataObject.GetData(typeof(string));
                    string digits = DigitsOnly(text);
                    if (sender is TextBox tb)
                    {
                        e.CancelCommand();
                        int selStart = tb.SelectionStart;
                        int selLen = tb.SelectionLength;

                        string before = tb.Text.Substring(0, selStart);
                        string after = tb.Text.Substring(selStart + selLen);
                        tb.Text = before + digits + after;
                        tb.SelectionStart = before.Length + digits.Length;
                    }
                }
                else
                {
                    e.CancelCommand();
                }
            }
            catch
            {
                e.CancelCommand();
            }
        }
    }
}
