using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data; // for IValueConverter
using HouseholdMS.Model;
using System.Data.SQLite;

namespace HouseholdMS.View.UserControls
{
    // Responsive width converter: returns true if the control is narrower than Threshold
    public class IsNarrowConverter : IValueConverter
    {
        public double Threshold { get; set; } = 1100;
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is double d) return d < Threshold;
            return false;
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }

    public partial class UserFormControl : UserControl
    {
        private readonly User _user;
        private bool _canEdit; // non-admin or root → read-only

        public event EventHandler OnSaveSuccess;
        public event EventHandler OnCancel;

        public UserFormControl(User user, bool canEdit = true)
        {
            InitializeComponent();
            _user = user ?? new User();
            _canEdit = canEdit;

            // Fill fields
            NameBox.Text = _user.Name;
            UsernameBox.Text = _user.Username;
            SelectRole(_user.Role ?? "Guest");
            PhoneBox.Text = _user.Phone;
            AddressBox.Text = _user.Address;
            AreaBox.Text = _user.AssignedArea;
            NoteBox.Text = _user.Note;

            // Header & editability
            if (_user.UserID == 0)
            {
                FormHeader.Text = "➕ Add User";
                UsernameBox.IsReadOnly = false;
                PasswordLabel.Text = "Password (required on add)";
            }
            else
            {
                FormHeader.Text = "✏ Edit User";
                UsernameBox.IsReadOnly = true;
                PasswordLabel.Text = "New Password (optional)";
            }

            // Root admin cannot be edited/deleted
            if (_user.UserID == 1) _canEdit = false;

            ApplyEditability();
        }

        private void ApplyEditability()
        {
            NameBox.IsEnabled = _canEdit;
            UsernameBox.IsEnabled = _canEdit && _user.UserID == 0; // editable only on add
            RoleComboBox.IsEnabled = _canEdit;
            PasswordBox.IsEnabled = _canEdit;
            ConfirmPasswordBox.IsEnabled = _canEdit;
            PhoneBox.IsEnabled = _canEdit;
            AddressBox.IsEnabled = _canEdit;
            AreaBox.IsEnabled = _canEdit;
            NoteBox.IsEnabled = _canEdit;

            SaveButton.IsEnabled = _canEdit;
            DeleteButton.Visibility = (_canEdit && _user.UserID > 0 && _user.UserID != 1)
                                      ? Visibility.Visible
                                      : Visibility.Collapsed;
        }

        private string GetSelectedRole()
        {
            var item = RoleComboBox.SelectedItem as ComboBoxItem;
            var role = (item?.Content as string) ?? "Guest";
            if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase)) role = "Guest";
            return role;
        }

        private void SelectRole(string role)
        {
            if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase)) role = "Guest";
            var target = RoleComboBox.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i => string.Equals((string)i.Content, role, StringComparison.OrdinalIgnoreCase));
            RoleComboBox.SelectedItem = target ?? RoleComboBox.Items.Cast<ComboBoxItem>().First();
        }

        // ===== Helpers =====
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

        private void ResetErrorTags()
        {
            NameBox.Tag = UsernameBox.Tag = RoleComboBox.Tag = null;
            PhoneBox.Tag = AddressBox.Tag = AreaBox.Tag = null;
            PasswordBox.Tag = ConfirmPasswordBox.Tag = null;
        }

        /// <summary>
        /// Validates inputs. For ADD: password required + confirm; For EDIT: password optional (but if provided, must pass + confirm).
        /// Technician requires Phone(8–15 digits), Address(>=5), Area(>=2).
        /// </summary>
        private bool ValidateForm(out string errorMessage, out string sanitizedPhone)
        {
            ResetErrorTags();
            bool ok = true;
            sanitizedPhone = DigitsOnly(PhoneBox.Text ?? "");

            var name = (NameBox.Text ?? "").Trim();
            var username = (UsernameBox.Text ?? "").Trim();
            var role = GetSelectedRole();
            var pwd = PasswordBox.Password ?? "";
            var confirm = ConfirmPasswordBox.Password ?? "";
            bool isAdd = (_user.UserID == 0);
            bool isTech = string.Equals(role, "Technician", StringComparison.OrdinalIgnoreCase);

            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            // Name
            if (string.IsNullOrWhiteSpace(name) || name.Length < 2)
            {
                NameBox.Tag = "error"; ok = false;
                sb.AppendLine("• Name must be at least 2 characters.");
            }

            // Username (add-only)
            if (isAdd)
            {
                if (string.IsNullOrWhiteSpace(username) || username.Length < 4)
                {
                    UsernameBox.Tag = "error"; ok = false;
                    sb.AppendLine("• Username must be at least 4 characters.");
                }
                if (!string.IsNullOrEmpty(username) && username.IndexOf(' ') >= 0)
                {
                    UsernameBox.Tag = "error"; ok = false;
                    sb.AppendLine("• Username cannot contain spaces.");
                }
                if (!string.IsNullOrEmpty(username) &&
                    string.Equals(username, name, StringComparison.OrdinalIgnoreCase))
                {
                    UsernameBox.Tag = "error"; ok = false;
                    sb.AppendLine("• Username must be different from Name.");
                }
            }

            // Password rules
            if (isAdd)
            {
                if (string.IsNullOrEmpty(pwd) || pwd.Length <= 6)
                {
                    PasswordBox.Tag = "error"; ok = false;
                    sb.AppendLine("• Password must be longer than 6 characters.");
                }
                else
                {
                    if (string.Equals(pwd, name, StringComparison.OrdinalIgnoreCase))
                    {
                        PasswordBox.Tag = "error"; ok = false;
                        sb.AppendLine("• Password must be different from Name.");
                    }
                    if (!string.IsNullOrEmpty(username) &&
                        string.Equals(pwd, username, StringComparison.OrdinalIgnoreCase))
                    {
                        PasswordBox.Tag = "error"; ok = false;
                        sb.AppendLine("• Password must be different from Username.");
                    }
                    if (!(HasDigit(pwd) || HasSymbol(pwd)))
                    {
                        PasswordBox.Tag = "error"; ok = false;
                        sb.AppendLine("• Password must include at least one number or symbol.");
                    }
                }

                if (string.IsNullOrEmpty(confirm))
                {
                    ConfirmPasswordBox.Tag = "error"; ok = false;
                    sb.AppendLine("• Please confirm the password.");
                }
                else if (!string.Equals(pwd, confirm))
                {
                    ConfirmPasswordBox.Tag = "error"; ok = false;
                    sb.AppendLine("• Confirm Password must match Password exactly.");
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(pwd))
                {
                    if (pwd.Length <= 6)
                    {
                        PasswordBox.Tag = "error"; ok = false;
                        sb.AppendLine("• New Password must be longer than 6 characters.");
                    }
                    if (string.Equals(pwd, name, StringComparison.OrdinalIgnoreCase))
                    {
                        PasswordBox.Tag = "error"; ok = false;
                        sb.AppendLine("• New Password must be different from Name.");
                    }
                    if (!string.IsNullOrEmpty(username) &&
                        string.Equals(pwd, username, StringComparison.OrdinalIgnoreCase))
                    {
                        PasswordBox.Tag = "error"; ok = false;
                        sb.AppendLine("• New Password must be different from Username.");
                    }
                    if (!(HasDigit(pwd) || HasSymbol(pwd)))
                    {
                        PasswordBox.Tag = "error"; ok = false;
                        sb.AppendLine("• New Password must include at least one number or symbol.");
                    }
                    if (string.IsNullOrEmpty(confirm) || !string.Equals(pwd, confirm))
                    {
                        ConfirmPasswordBox.Tag = "error"; ok = false;
                        sb.AppendLine("• Confirm Password must match the new Password exactly.");
                    }
                }
            }

            // Technician-specific requirements
            if (isTech)
            {
                if (sanitizedPhone.Length < 8 || sanitizedPhone.Length > 15)
                {
                    PhoneBox.Tag = "error"; ok = false;
                    sb.AppendLine("• Phone must be 8–15 digits (digits only).");
                }
                if (string.IsNullOrWhiteSpace(AddressBox.Text) || AddressBox.Text.Trim().Length < 5)
                {
                    AddressBox.Tag = "error"; ok = false;
                    sb.AppendLine("• Address must be at least 5 characters.");
                }
                if (string.IsNullOrWhiteSpace(AreaBox.Text) || AreaBox.Text.Trim().Length < 2)
                {
                    AreaBox.Tag = "error"; ok = false;
                    sb.AppendLine("• Assigned Area must be at least 2 characters.");
                }
            }
            else
            {
                if (sanitizedPhone.Length > 0 && (sanitizedPhone.Length < 8 || sanitizedPhone.Length > 15))
                {
                    PhoneBox.Tag = "error"; ok = false;
                    sb.AppendLine("• Phone (optional) must be 8–15 digits if provided.");
                }
            }

            errorMessage = sb.ToString().Trim();
            return ok;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!_canEdit) { OnCancel?.Invoke(this, EventArgs.Empty); return; }

            if (!ValidateForm(out string errors, out string sanitizedPhone))
            {
                MessageBox.Show(errors.Length == 0 ? "Please fix highlighted fields." : errors,
                                "Validation",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }

            string name = NameBox.Text.Trim();
            string usern = UsernameBox.Text.Trim();
            string role = GetSelectedRole();
            string phone = sanitizedPhone;
            string addr = (AddressBox.Text ?? "").Trim();
            string area = (AreaBox.Text ?? "").Trim();
            string note = (NoteBox.Text ?? "").Trim();
            string pwd = PasswordBox.Password;

            bool isAdd = (_user.UserID == 0);

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();

                    if (isAdd)
                    {
                        using (var check = new System.Data.SQLite.SQLiteCommand(
                                   "SELECT COUNT(*) FROM Users WHERE LOWER(Username)=LOWER(@u);", conn))
                        {
                            check.Parameters.AddWithValue("@u", usern);
                            int exists = Convert.ToInt32(check.ExecuteScalar());
                            if (exists > 0)
                            {
                                UsernameBox.Tag = "error";
                                MessageBox.Show("Username already exists. Choose another.", "Username Taken",
                                                MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }
                        }

                        using (var cmd = new SQLiteCommand(@"
INSERT INTO Users
(Name, Username, PasswordHash, Role, Phone, Address, AssignedArea, Note, IsActive, TechApproved)
VALUES
(@n, @u, @p, @r, @ph, @ad, @ar, @no, 1, @ta);", conn))
                        {
                            cmd.Parameters.AddWithValue("@n", name);
                            cmd.Parameters.AddWithValue("@u", usern);
                            cmd.Parameters.AddWithValue("@p", pwd);
                            cmd.Parameters.AddWithValue("@r", role);
                            cmd.Parameters.AddWithValue("@ph", (object)(phone.Length == 0 ? null : phone) ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@ad", (object)(string.IsNullOrWhiteSpace(addr) ? null : addr) ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@ar", (object)(string.IsNullOrWhiteSpace(area) ? null : area) ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@no", (object)(string.IsNullOrWhiteSpace(note) ? null : note) ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@ta", 1);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        using (var cmd = new SQLiteCommand(@"
UPDATE Users
SET Name=@n, Role=@r, Phone=@ph, Address=@ad, AssignedArea=@ar, Note=@no
WHERE UserID=@id;", conn))
                        {
                            cmd.Parameters.AddWithValue("@n", name);
                            cmd.Parameters.AddWithValue("@r", role);
                            cmd.Parameters.AddWithValue("@ph", (object)(phone.Length == 0 ? null : phone) ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@ad", (object)(string.IsNullOrWhiteSpace(addr) ? null : addr) ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@ar", (object)(string.IsNullOrWhiteSpace(area) ? null : area) ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@no", (object)(string.IsNullOrWhiteSpace(note) ? null : note) ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@id", _user.UserID);
                            cmd.ExecuteNonQuery();
                        }

                        using (var cmd = new SQLiteCommand(
                                   "UPDATE Users SET TechApproved=1 WHERE UserID=@id;", conn))
                        {
                            cmd.Parameters.AddWithValue("@id", _user.UserID);
                            cmd.ExecuteNonQuery();
                        }

                        if (!string.IsNullOrWhiteSpace(pwd))
                        {
                            using (var cmd = new SQLiteCommand(
                                       "UPDATE Users SET PasswordHash=@p WHERE UserID=@id;", conn))
                            {
                                cmd.Parameters.AddWithValue("@p", pwd);
                                cmd.Parameters.AddWithValue("@id", _user.UserID);
                                cmd.ExecuteNonQuery();
                            }
                        }
                    }
                }

                MessageBox.Show("Saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                OnSaveSuccess?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving user:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            OnCancel?.Invoke(this, EventArgs.Empty);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (!_canEdit || _user.UserID <= 0 || _user.UserID == 1) return;

            var result = MessageBox.Show(
                $"Delete user '{_user.Username}'?",
                "Confirm Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand("DELETE FROM Users WHERE UserID=@id;", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", _user.UserID);
                        cmd.ExecuteNonQuery();
                    }
                }

                MessageBox.Show("User deleted.", "Deleted", MessageBoxButton.OK, MessageBoxImage.Information);
                OnSaveSuccess?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error deleting user:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
