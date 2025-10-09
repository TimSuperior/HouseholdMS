using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HouseholdMS.Model;
using HouseholdMS.View.UserControls;
using System.Globalization;
using HouseholdMS.Resources; // Strings.*

namespace HouseholdMS.View
{
    public partial class UserManagementView : UserControl
    {
        // Commands (referenced in XAML via local:UserManagementView.*)
        public static readonly RoutedUICommand ApproveCommand = new RoutedUICommand("Approve", "ApproveCommand", typeof(UserManagementView));
        public static readonly RoutedUICommand DeclineCommand = new RoutedUICommand("Decline", "DeclineCommand", typeof(UserManagementView));
        public static readonly RoutedUICommand DeleteUserCommand = new RoutedUICommand("DeleteUser", "DeleteUserCommand", typeof(UserManagementView));
        public static readonly RoutedUICommand RoleToGuestCommand = new RoutedUICommand("RoleToGuest", "RoleToGuestCommand", typeof(UserManagementView));
        public static readonly RoutedUICommand RoleToTechCommand = new RoutedUICommand("RoleToTech", "RoleToTechCommand", typeof(UserManagementView));
        public static readonly RoutedUICommand RoleToAdminCommand = new RoutedUICommand("RoleToAdmin", "RoleToAdminCommand", typeof(UserManagementView));

        // ===== Row VM =====
        public class UserRow
        {
            public int UserID { get; set; }
            public string Name { get; set; }
            public string Username { get; set; }
            public string Role { get; set; }              // Admin | Technician | Guest
            public string Phone { get; set; }
            public string Address { get; set; }
            public string AssignedArea { get; set; }
            public string Note { get; set; }
            public int TechApproved { get; set; }
            public int IsActive { get; set; }

            public bool IsRootUser => string.Equals(Username, "root", StringComparison.OrdinalIgnoreCase);
            public bool ShowApprovalButtons { get; set; }

            // ContextMenu enables
            public bool CanSetGuest { get; set; }
            public bool CanSetTech { get; set; }
            public bool CanSetAdmin { get; set; }
            public bool CanDelete { get; set; }

            public string StatusLabel
            {
                get
                {
                    string role = (Role ?? "").Trim();
                    if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)) return "admin";
                    if (string.Equals(role, "Technician", StringComparison.OrdinalIgnoreCase))
                        return TechApproved == 1 ? "approved" : (IsActive == 0 ? "declined" : "pending");
                    if (string.Equals(role, "Guest", StringComparison.OrdinalIgnoreCase))
                        return (TechApproved == 0 && IsActive == 1) ? "pending" : "user";
                    return role.ToLowerInvariant();
                }
            }
        }

        private readonly string _actorRole;
        private readonly string _actorUsername;
        private readonly bool _actorIsAdmin;
        private readonly bool _actorIsRoot;

        private readonly ObservableCollection<UserRow> _all = new ObservableCollection<UserRow>();

        // ===== Column-based search support (empty set = search all) =====
        private static readonly string[] AllColumnKeys = new[]
        {
            nameof(UserRow.UserID),
            nameof(UserRow.Name),
            nameof(UserRow.Username),
            nameof(UserRow.Role),
            nameof(UserRow.Phone),
            nameof(UserRow.Address),
            nameof(UserRow.AssignedArea),
            nameof(UserRow.Note),
            nameof(UserRow.StatusLabel)
        };

        private readonly HashSet<string> _selectedColumnKeys = new HashSet<string>(StringComparer.Ordinal);

        public UserManagementView(string actorRole = "User", string actorUsername = "")
        {
            InitializeComponent();

            _actorRole = string.IsNullOrWhiteSpace(actorRole) ? "User" : actorRole.Trim();
            _actorUsername = actorUsername ?? "";
            _actorIsAdmin = string.Equals(_actorRole, "Admin", StringComparison.OrdinalIgnoreCase);
            _actorIsRoot = string.Equals(_actorUsername, "root", StringComparison.OrdinalIgnoreCase);

            // Command bindings
            CommandBindings.Add(new CommandBinding(ApproveCommand, ExecuteApprove));
            CommandBindings.Add(new CommandBinding(DeclineCommand, ExecuteDecline));
            CommandBindings.Add(new CommandBinding(DeleteUserCommand, ExecuteDeleteUser));
            CommandBindings.Add(new CommandBinding(RoleToGuestCommand, (s, e) => ExecuteChangeRole(e, "Guest")));
            CommandBindings.Add(new CommandBinding(RoleToTechCommand, (s, e) => ExecuteChangeRole(e, "Technician")));
            CommandBindings.Add(new CommandBinding(RoleToAdminCommand, (s, e) => ExecuteChangeRole(e, "Admin")));

            LoadUsers();
            UserListView.ItemsSource = _all;

            UpdateColumnFilterButtonContent();
        }

        private void LoadUsers()
        {
            _all.Clear();

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (var cmd = new SQLiteCommand(
                    @"SELECT UserID, Name, Username, Role, Phone, Address, AssignedArea, Note, TechApproved, IsActive
                      FROM Users
                      WHERE LOWER(Username) <> 'root'
                        AND IsActive IN (0,1)
                      ORDER BY Name ASC;", conn))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var row = new UserRow
                        {
                            UserID = Convert.ToInt32(r["UserID"]),
                            Name = r["Name"]?.ToString(),
                            Username = r["Username"]?.ToString(),
                            Role = r["Role"]?.ToString(),
                            Phone = r["Phone"]?.ToString(),
                            Address = r["Address"]?.ToString(),
                            AssignedArea = r["AssignedArea"]?.ToString(),
                            Note = r["Note"]?.ToString(),
                            TechApproved = Convert.ToInt32(r["TechApproved"]),
                            IsActive = Convert.ToInt32(r["IsActive"])
                        };

                        row.ShowApprovalButtons =
                            string.Equals(row.Role, "Guest", StringComparison.OrdinalIgnoreCase)
                            && row.TechApproved == 0
                            && row.IsActive == 1;

                        ComputePermissions(row);
                        _all.Add(row);
                    }
                }
            }

            ApplySearchFilter(SearchBox != null ? SearchBox.Text : null);
        }

        private void ComputePermissions(UserRow row)
        {
            bool targetIsAdmin = string.Equals(row.Role, "Admin", StringComparison.OrdinalIgnoreCase);
            row.CanSetAdmin = _actorIsRoot && !row.IsRootUser;
            row.CanSetTech = (_actorIsRoot || _actorIsAdmin) && !row.IsRootUser;
            row.CanSetGuest = (_actorIsRoot || _actorIsAdmin) && !row.IsRootUser && !targetIsAdmin;
            row.CanDelete = _actorIsRoot || (_actorIsAdmin && !targetIsAdmin && !row.IsRootUser);
        }

        // ===== Search (respects selected columns) =====
        private void ApplySearchFilter(string raw)
        {
            if (UserListView == null) return;

            string placeholder = SearchBox != null ? (SearchBox.Tag as string ?? string.Empty) : string.Empty;
            string term = (raw ?? "").Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(term) || term == placeholder)
            {
                UserListView.ItemsSource = _all;
                return;
            }

            var keys = _selectedColumnKeys.Count == 0 ? AllColumnKeys : _selectedColumnKeys.ToArray();

            var filtered = _all.Where(u =>
            {
                for (int i = 0; i < keys.Length; i++)
                {
                    string cell = GetCellString(u, keys[i]);
                    if (!string.IsNullOrEmpty(cell) && cell.ToLowerInvariant().Contains(term))
                        return true;
                }
                return false;
            }).ToList();

            UserListView.ItemsSource = filtered;
        }

        private static string GetCellString(UserRow u, string key)
        {
            switch (key)
            {
                case nameof(UserRow.UserID): return u.UserID.ToString();
                case nameof(UserRow.Name): return u.Name ?? string.Empty;
                case nameof(UserRow.Username): return u.Username ?? string.Empty;
                case nameof(UserRow.Role): return u.Role ?? string.Empty;
                case nameof(UserRow.Phone): return u.Phone ?? string.Empty;
                case nameof(UserRow.Address): return u.Address ?? string.Empty;
                case nameof(UserRow.AssignedArea): return u.AssignedArea ?? string.Empty;
                case nameof(UserRow.Note): return u.Note ?? string.Empty;
                case nameof(UserRow.StatusLabel): return u.StatusLabel ?? string.Empty;
                default: return string.Empty;
            }
        }

        // Events
        private void SearchBox_KeyUp(object sender, KeyEventArgs e) => ApplySearchFilter(SearchBox.Text);

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            string placeholder = SearchBox.Tag as string ?? "Search name/username/role/phone/address/area/note/status";
            if (SearchBox.Text == placeholder)
            {
                SearchBox.Text = "";
                SearchBox.Foreground = new SolidColorBrush(Colors.Black);
                SearchBox.FontStyle = FontStyles.Normal;
            }
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string placeholder = SearchBox.Tag as string ?? "Search name/username/role/phone/address/area/note/status";
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Text = placeholder;
                SearchBox.Foreground = new SolidColorBrush(Colors.Gray);
                SearchBox.FontStyle = FontStyles.Italic;
                ApplySearchFilter(SearchBox.Text);
            }
        }

        private void UserListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var row = UserListView.SelectedItem as UserRow;
            if (row == null) return;

            bool targetIsAdmin = string.Equals(row.Role, "Admin", StringComparison.OrdinalIgnoreCase);
            bool canEdit = _actorIsRoot || (_actorIsAdmin && !targetIsAdmin);

            OpenUserWindow(ToModel(row), canEdit);
        }

        // Hook kept for parity with XAML; no functional change besides optional recompute
        private void UserListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UserListView != null && UserListView.SelectedItem is UserRow row) ComputePermissions(row);
        }

        private static User ToModel(UserRow r) => new User
        {
            UserID = r.UserID,
            Name = r.Name,
            Username = r.Username,
            Role = r.Role,
            Phone = r.Phone,
            Address = r.Address,
            AssignedArea = r.AssignedArea,
            Note = r.Note
        };

        private void AddUserButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_actorIsAdmin && !_actorIsRoot)
            {
                MessageBox.Show("Access Denied: Only Admins can add users.", "Access Denied",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var newUser = new User { UserID = 0, Role = "Guest" };
            OpenUserWindow(newUser, canEdit: true);
        }

        private void OpenUserWindow(User user, bool canEdit)
        {
            var form = new UserFormControl(user, canEdit);

            form.OnSaveSuccess += (s, args) =>
            {
                Window.GetWindow(form)?.Close();
                LoadUsers();
            };
            form.OnCancel += (s, args) =>
            {
                Window.GetWindow(form)?.Close();
            };

            // Respect the form's min size; cap to screen
            double workW = SystemParameters.WorkArea.Width;
            double workH = SystemParameters.WorkArea.Height;

            const double chromeW = 32;
            const double chromeH = 48;

            double minW = Math.Max(form.MinWidth, form.MinWidth) + chromeW; // form.MinWidth is 640
            double minH = Math.Max(form.MinHeight, form.MinHeight) + chromeH; // form.MinHeight is 520

            var win = new Window
            {
                Title = (user.UserID == 0) ? "Add User" : (canEdit ? "Edit User" : "User Details"),
                Content = form,
                Owner = Window.GetWindow(this),
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResize,
                MinWidth = Math.Min(minW, workW - 40),
                MinHeight = Math.Min(minH, workH - 40),
                MaxWidth = workW - 40,
                MaxHeight = workH - 40,
                ShowInTaskbar = false,
                Background = Brushes.White
            };

            win.Loaded += (_, __) =>
            {
                if (win.ActualWidth > win.MaxWidth) win.Width = win.MaxWidth;
                if (win.ActualHeight > win.MaxHeight) win.Height = win.MaxHeight;
            };

            win.ShowDialog();
        }

        // ===== Column chooser UI handlers =====
        private void ColumnFilterButton_Click(object sender, RoutedEventArgs e)
        {
            ColumnPopup.IsOpen = true;
        }

        private void ColumnPopup_Closed(object sender, EventArgs e)
        {
            UpdateColumnFilterButtonContent();

            string placeholder = SearchBox != null ? (SearchBox.Tag as string ?? string.Empty) : string.Empty;
            string text = SearchBox != null ? (SearchBox.Text ?? string.Empty) : string.Empty;

            if (!string.IsNullOrWhiteSpace(text) && text != placeholder)
                ApplySearchFilter(text);
        }

        private void ColumnCheckBox_Click(object sender, RoutedEventArgs e)
        {
            var cb = sender as CheckBox;
            var key = cb != null ? cb.Tag as string : null;
            if (string.IsNullOrWhiteSpace(key)) return;

            if (cb.IsChecked == true) _selectedColumnKeys.Add(key);
            else _selectedColumnKeys.Remove(key);
        }

        private void SelectAllColumns_Click(object sender, RoutedEventArgs e)
        {
            _selectedColumnKeys.Clear();
            foreach (var child in FindPopupCheckBoxes()) child.IsChecked = true;
            for (int i = 0; i < AllColumnKeys.Length; i++) _selectedColumnKeys.Add(AllColumnKeys[i]);
        }

        private void ClearAllColumns_Click(object sender, RoutedEventArgs e)
        {
            _selectedColumnKeys.Clear(); // empty => "All columns"
            foreach (var child in FindPopupCheckBoxes()) child.IsChecked = false;
        }

        private void OkColumns_Click(object sender, RoutedEventArgs e)
        {
            // reflect current selection count on the chip
            UpdateColumnFilterButtonContent();

            // if search box has user text (not placeholder), apply the filter now
            var tagText = SearchBox?.Tag as string ?? string.Empty;
            var text = SearchBox?.Text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(text) && !string.Equals(text, tagText, StringComparison.Ordinal))
            {
                ApplySearchFilter(text); // apply current search with the selected columns
            }


            // close the popup
            ColumnPopup.IsOpen = false;
        }


        private IEnumerable<CheckBox> FindPopupCheckBoxes()
        {
            var border = ColumnPopup.Child as Border;
            if (border == null) yield break;

            var sp = border.Child as StackPanel;
            if (sp == null) yield break;

            var sv = sp.Children.OfType<ScrollViewer>().FirstOrDefault();
            if (sv == null) yield break;

            var inner = sv.Content as StackPanel;
            if (inner == null) yield break;

            foreach (var child in inner.Children)
            {
                var cb = child as CheckBox;
                if (cb != null) yield return cb;
            }
        }

        private void UpdateColumnFilterButtonContent()
        {
            if (_selectedColumnKeys.Count == 0)
                ColumnFilterButton.Content = "All ▾";
            else
                ColumnFilterButton.Content = string.Format("{0} selected ▾", _selectedColumnKeys.Count);
        }

        // Command handlers
        private void ExecuteApprove(object sender, ExecutedRoutedEventArgs e)
        {
            if (!_actorIsAdmin && !_actorIsRoot) { Warn("Only admins can approve/decline technicians."); return; }
            if (!TryGetUserId(e.Parameter, out int userId)) return;

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand(
                        "UPDATE Users SET Role='Technician', TechApproved=1, IsActive=1 WHERE UserID=@id AND Role='Guest';", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", userId);
                        int n = cmd.ExecuteNonQuery();
                        if (n > 0) Info("Technician approved.");
                    }
                }
                LoadUsers();
            }
            catch (Exception ex) { Error($"Error approving technician:\n{ex.Message}"); }
        }

        private void ExecuteDecline(object sender, ExecutedRoutedEventArgs e)
        {
            if (!_actorIsAdmin && !_actorIsRoot) { Warn("Only admins can approve/decline technicians."); return; }
            if (!TryGetUserId(e.Parameter, out int userId)) return;

            var confirm = MessageBox.Show("Decline this technician application?\nUser will be marked inactive.",
                                          "Confirm Decline", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand(
                        "UPDATE Users SET TechApproved=0, IsActive=0 WHERE UserID=@id AND Role='Guest';", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", userId);
                        int n = cmd.ExecuteNonQuery();
                        if (n > 0) Info("Request declined.");
                    }
                }
                LoadUsers();
            }
            catch (Exception ex) { Error($"Error declining request:\n{ex.Message}"); }
        }

        private void ExecuteDeleteUser(object sender, ExecutedRoutedEventArgs e)
        {
            if (!TryGetUserId(e.Parameter, out int userId)) return;

            var row = _all.FirstOrDefault(x => x.UserID == userId);
            if (row == null) return;

            if (row.IsRootUser) { Warn("Root user cannot be deleted."); return; }

            bool targetIsAdmin = string.Equals(row.Role, "Admin", StringComparison.OrdinalIgnoreCase);

            if (!_actorIsRoot)
            {
                if (!_actorIsAdmin) { Warn("Only admins can delete users."); return; }
                if (targetIsAdmin) { Warn("Admins cannot delete other admins."); return; }
            }

            var confirm = MessageBox.Show(
                string.Format("Delete user '{0}'? This will deactivate the account.", row.Username),
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand("UPDATE Users SET IsActive=0 WHERE UserID=@id;", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", userId);
                        cmd.ExecuteNonQuery();
                    }
                }
                LoadUsers();
            }
            catch (Exception ex) { Error($"Error deleting user:\n{ex.Message}"); }
        }

        private void ExecuteChangeRole(ExecutedRoutedEventArgs e, string newRole)
        {
            if (!TryGetUserId(e.Parameter, out int userId)) return;

            var row = _all.FirstOrDefault(x => x.UserID == userId);
            if (row == null) return;

            if (row.IsRootUser) { Warn("Root user cannot be modified."); return; }

            bool targetIsAdmin = string.Equals(row.Role, "Admin", StringComparison.OrdinalIgnoreCase);

            if (!_actorIsRoot)
            {
                if (!_actorIsAdmin) { Warn("Only admins can change roles."); return; }
                if (targetIsAdmin) { Warn("Admins cannot modify other admins."); return; }
            }

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand(
                        "UPDATE Users SET Role=@role, TechApproved=@ta, IsActive=1 WHERE UserID=@id;", conn))
                    {
                        cmd.Parameters.AddWithValue("@role", newRole);
                        cmd.Parameters.AddWithValue("@ta", 1);
                        cmd.Parameters.AddWithValue("@id", userId);
                        cmd.ExecuteNonQuery();
                    }
                }

                Info(string.Format("Role updated to {0}.", newRole));
                LoadUsers();
            }
            catch (Exception ex) { Error($"Error changing role:\n{ex.Message}"); }
        }

        // Helpers
        private static bool TryGetUserId(object parameter, out int userId)
            => int.TryParse(parameter != null ? parameter.ToString() : null, out userId);

        private static void Info(string msg) => MessageBox.Show(msg, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        private static void Warn(string msg) => MessageBox.Show(msg, "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
        private static void Error(string msg) => MessageBox.Show(msg, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    // === Converters live in the same file & namespace ===

    // Role -> background brush (unchanged)
    public sealed class RoleToBrushConverter : System.Windows.Data.IValueConverter
    {
        private static Brush Make(string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var b = new SolidColorBrush(c); b.Freeze(); return b;
        }

        private static readonly Brush Blue = Make("#DBEAFE");
        private static readonly Brush Green = Make("#D1FAE5");
        private static readonly Brush Gray = Make("#E5E7EB");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var role = (value as string ?? "").Trim();
            if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)) return Blue;
            if (string.Equals(role, "Technician", StringComparison.OrdinalIgnoreCase)) return Green;
            return Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => System.Windows.Data.Binding.DoNothing;
    }

    // Role -> localized display text (uses Strings.resx)
    public sealed class RoleToDisplayConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = value == null ? string.Empty : value.ToString().Trim();
            switch (s.ToLowerInvariant())
            {
                case "admin": return Strings.Role_Admin;
                case "technician": return Strings.Role_Technician;
                case "guest": return Strings.Role_Guest;
                default: return s;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => System.Windows.Data.Binding.DoNothing; // display-only
    }

    // StatusLabel -> localized display text (uses Strings.resx)
    public sealed class StatusToDisplayConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = value == null ? string.Empty : value.ToString().Trim();
            switch (s.ToLowerInvariant())
            {
                case "approved": return Strings.Status_Approved;
                case "pending": return Strings.Status_Pending;
                case "declined": return Strings.Status_Blocked; // reuse "blocked" text if you like
                case "active": return Strings.Status_Active;
                case "inactive": return Strings.Status_Inactive;
                case "user": return Strings.Status_User;
                case "admin": return Strings.Role_Admin;
                default: return s;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => System.Windows.Data.Binding.DoNothing;
    }

    // Model used by form & list
    public class User
    {
        public int UserID { get; set; }
        public string Name { get; set; }
        public string Username { get; set; }
        public string Role { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        public string AssignedArea { get; set; }
        public string Note { get; set; }
    }
}
