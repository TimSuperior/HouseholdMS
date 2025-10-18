using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using HouseholdMS.Model;
using System.Data.SQLite;
using HouseholdMS.Resources; // <-- for Strings.*

namespace HouseholdMS.View.UserControls
{
    public partial class UserFormControl : UserControl
    {
        private readonly User _user;
        private bool _canEdit;

        // widths for dialog; tweak if you want
        private const double NarrowDialogWidth = 760;  // single-column
        private const double WideDialogWidth = 1040;   // two-columns

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
                FormHeader.Text = Strings.UF_Header_Add;
                UsernameBox.IsReadOnly = false;
                PasswordLabel.Text = Strings.UF_Label_Password_Add;
            }
            else
            {
                FormHeader.Text = Strings.UF_Header_Edit;
                UsernameBox.IsReadOnly = true;
                PasswordLabel.Text = Strings.UF_Label_Password_Edit;
            }

            if (_user.UserID == 1) _canEdit = false; // root

            ApplyEditability();

            // Ensure correct state on load
            UpdateTechSectionVisibility();
        }

        private void ApplyEditability()
        {
            NameBox.IsEnabled = _canEdit;
            UsernameBox.IsEnabled = _canEdit && _user.UserID == 0;
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
            string role = "Guest";

            if (item != null)
            {
                // Prefer Tag (canonical), fallback to Content text
                var tag = item.Tag as string;
                role = !string.IsNullOrWhiteSpace(tag) ? tag : (item.Content as string ?? "Guest");
            }

            if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase)) role = "Guest";
            return role;
        }

        private void SelectRole(string role)
        {
            if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase)) role = "Guest";

            var target = RoleComboBox.Items
                .Cast<ComboBoxItem>()
                .FirstOrDefault(i =>
                {
                    var tag = i.Tag as string;
                    if (!string.IsNullOrEmpty(tag) &&
                        string.Equals(tag, role, StringComparison.OrdinalIgnoreCase))
                        return true;

                    var content = i.Content as string;
                    return !string.IsNullOrEmpty(content) &&
                           string.Equals(content, role, StringComparison.OrdinalIgnoreCase);
                });

            RoleComboBox.SelectedItem = target ?? RoleComboBox.Items.Cast<ComboBoxItem>().First();
        }

        private void RoleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateTechSectionVisibility();
        }

        private void UpdateTechSectionVisibility()
        {
            bool isTech = string.Equals(GetSelectedRole(), "Technician", StringComparison.OrdinalIgnoreCase);

            if (isTech)
            {
                // Force a 50/50 split: 1* | 24px | 1*
                ColLeft.Width = new GridLength(1, GridUnitType.Star);
                ColRight.Width = new GridLength(1, GridUnitType.Star);
                ColGap.Width = new GridLength(24);
                TechSection.Visibility = Visibility.Visible;
            }
            else
            {
                TechSection.Visibility = Visibility.Collapsed;
                ColRight.Width = new GridLength(0);
                ColGap.Width = new GridLength(0);
                ColLeft.Width = new GridLength(1, GridUnitType.Star);
            }

            AdjustWindowWidth(isTech);
        }

        private void AdjustWindowWidth(bool isTech)
        {
            var win = Window.GetWindow(this);
            if (win == null) return;

            double workW = SystemParameters.WorkArea.Width;
            double margin = 40; // breathing room

            double target = isTech ? WideDialogWidth : NarrowDialogWidth;
            target = Math.Min(target, workW - margin);

            if (win.Width < target && isTech) win.Width = target;
            if (!isTech && win.Width > target) win.Width = target;
        }

        // ===== Helpers & validation (unchanged logic) =====
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
                sb.AppendLine("• " + Strings.Val_Name_Min2);
            }

            // Username (add-only)
            if (isAdd)
            {
                if (string.IsNullOrWhiteSpace(username) || username.Length < 4)
                { UsernameBox.Tag = "error"; ok = false; sb.AppendLine("• " + Strings.Val_Username_Min4); }
                if (!string.IsNullOrEmpty(username) && username.IndexOf(' ') >= 0)
                { UsernameBox.Tag = "error"; ok = false; sb.AppendLine("• " + Strings.Val_Username_NoSpaces); }
                if (!string.IsNullOrEmpty(username) &&
                    string.Equals(username, name, StringComparison.OrdinalIgnoreCase))
                { UsernameBox.Tag = "error"; ok = false; sb.AppendLine("• " + Strings.Val_Username_NotEqualName); }
            }

            // Password rules
            if (isAdd)
            {
                if (string.IsNullOrEmpty(pwd) || pwd.Length <= 6)
                { PasswordBox.Tag = "error"; ok = false; sb.AppendLine("• " + Strings.Val_Password_MinLen); }
                else
                {
                    if (string.Equals(pwd, name, StringComparison.OrdinalIgnoreCase))
                    { PasswordBox.Tag = "error"; ok = false; sb.AppendLine("• " + Strings.Val_Password_NotEqualName); }
                    if (!string.IsNullOrEmpty(username) &&
                        string.Equals(pwd, username, StringComparison.OrdinalIgnoreCase))
                    { PasswordBox.Tag = "error"; ok = false; sb.AppendLine("• " + Strings.Val_Password_NotEqualUsername); }
                    if (!(HasDigit(pwd) || HasSymbol(pwd)))
                    { PasswordBox.Tag = "error"; ok = false; sb.AppendLine("• " + Strings.Val_Password_MustContainDigitOrSymbol); }
                }

                if (string.IsNullOrEmpty(confirm))
                { ConfirmPasswordBox.Tag = "error"; ok = false; sb.AppendLine("• " + Strings.Val_ConfirmPassword_Empty); }
                else if (!string.Equals(pwd, confirm))
                { ConfirmPasswordBox.Tag = "error"; ok = false; sb.AppendLine("• " + Strings.Val_ConfirmPassword_MustMatch); }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(pwd))
                {
                    if (pwd.Length <= 6)
                    { PasswordBox.Tag = "error"; ok = false; sb.AppendLine("• " + Strings.Val_Password_MinLen); }
                    if (string.Equals(pwd, name, StringComparison.OrdinalIgnoreCase))
                    { PasswordBox.Tag = "error"; ok = false; sb.AppendLine("• " + Strings.Val_Password_NotEqualName); }
                    if (!string.IsNullOrEmpty(username) &&
                        string.Equals(pwd, username, StringComparison.OrdinalIgnoreCase))
                    { PasswordBox.Tag = "error"; ok = false; sb.AppendLine("• " + Strings.Val_Password_NotEqualUsername); }
                    if (!(HasDigit(pwd) || HasSymbol(pwd)))
                    { PasswordBox.Tag = "error"; ok = false; sb.AppendLine("• " + Strings.Val_Password_MustContainDigitOrSymbol); }
                    if (string.IsNullOrEmpty(confirm) || !string.Equals(pwd, confirm))
                    { ConfirmPasswordBox.Tag = "error"; ok = false; sb.AppendLine("• " + Strings.Val_ConfirmPassword_MustMatch); }
                }
            }

            // Technician-specific requirements
            if (isTech)
            {
                if (sanitizedPhone.Length < 8 || sanitizedPhone.Length > 15)
                { PhoneBox.Tag = "error"; ok = false; sb.AppendLine("• " + Strings.Val_Phone_Range); }
                if (string.IsNullOrWhiteSpace(AddressBox.Text) || AddressBox.Text.Trim().Length < 5)
                { AddressBox.Tag = "error"; ok = false; sb.AppendLine("• " + Strings.Val_Address_Min5); }
                if (string.IsNullOrWhiteSpace(AreaBox.Text) || AreaBox.Text.Trim().Length < 2)
                { AreaBox.Tag = "error"; ok = false; sb.AppendLine("• " + Strings.Val_Area_Min2); }
            }
            else
            {
                if (sanitizedPhone.Length > 0 && (sanitizedPhone.Length < 8 || sanitizedPhone.Length > 15))
                { PhoneBox.Tag = "error"; ok = false; sb.AppendLine("• " + Strings.Val_Phone_OptionalRange); }
            }

            errorMessage = sb.ToString().Trim();
            return ok;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!_canEdit) { OnCancel?.Invoke(this, EventArgs.Empty); return; }

            if (!ValidateForm(out string errors, out string sanitizedPhone))
            {
                MessageBox.Show(errors.Length == 0 ? Strings.Val_FixIssues_Title : errors,
                                Strings.UF_Validation_Title,
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
                        using (var check = new SQLiteCommand(
                                   "SELECT COUNT(*) FROM Users WHERE LOWER(Username)=LOWER(@u);", conn))
                        {
                            check.Parameters.AddWithValue("@u", usern);
                            int exists = Convert.ToInt32(check.ExecuteScalar());
                            if (exists > 0)
                            {
                                UsernameBox.Tag = "error";
                                MessageBox.Show(Strings.Registration_UsernameTaken_Body,
                                                Strings.Registration_UsernameTaken_Title,
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

                MessageBox.Show(Strings.UF_Save_Success, Strings.UF_Success_Title,
                                MessageBoxButton.OK, MessageBoxImage.Information);
                OnSaveSuccess?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Strings.UF_Error_Save_Prefix + "\n" + ex.Message,
                                Strings.Error_Title,
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
            => OnCancel?.Invoke(this, EventArgs.Empty);

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (!_canEdit || _user.UserID <= 0 || _user.UserID == 1) return;

            var result = MessageBox.Show(
                string.Format(Strings.UF_Delete_Confirm_Text_Format, _user.Username),
                Strings.UF_Delete_Confirm_Title,
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

                MessageBox.Show(Strings.UF_Delete_Success_Text,
                                Strings.UF_Delete_Success_Title,
                                MessageBoxButton.OK, MessageBoxImage.Information);
                OnSaveSuccess?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Strings.UF_Error_Delete_Prefix + "\n" + ex.Message,
                                Strings.Error_Title,
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
