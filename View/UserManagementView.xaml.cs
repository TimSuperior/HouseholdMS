using System;
using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HouseholdMS.Model;
using HouseholdMS.View.UserControls;

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

            ApplySearchFilter(SearchBox?.Text);
        }

        private void ComputePermissions(UserRow row)
        {
            bool targetIsAdmin = string.Equals(row.Role, "Admin", StringComparison.OrdinalIgnoreCase);
            row.CanSetAdmin = _actorIsRoot && !row.IsRootUser;
            row.CanSetTech = (_actorIsRoot || _actorIsAdmin) && !row.IsRootUser;
            row.CanSetGuest = (_actorIsRoot || _actorIsAdmin) && !row.IsRootUser && !targetIsAdmin;
            row.CanDelete = _actorIsRoot || (_actorIsAdmin && !targetIsAdmin && !row.IsRootUser);
        }

        // Search
        private void ApplySearchFilter(string raw)
        {
            if (UserListView == null) return;

            string term = (raw ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(term) ||
                term == "search name/username/role/phone/address/area/note/status")
            {
                UserListView.ItemsSource = _all;
                return;
            }

            var filtered = _all.Where(u =>
                    (u.Name ?? "").ToLower().Contains(term) ||
                    (u.Username ?? "").ToLower().Contains(term) ||
                    (u.Role ?? "").ToLower().Contains(term) ||
                    (u.Phone ?? "").ToLower().Contains(term) ||
                    (u.Address ?? "").ToLower().Contains(term) ||
                    (u.AssignedArea ?? "").ToLower().Contains(term) ||
                    (u.Note ?? "").ToLower().Contains(term) ||
                    (u.StatusLabel ?? "").ToLower().Contains(term))
                .ToList();

            UserListView.ItemsSource = filtered;
        }

        // Events
        private void SearchBox_KeyUp(object sender, System.Windows.Input.KeyEventArgs e) => ApplySearchFilter(SearchBox.Text);

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (SearchBox.Text == "Search name/username/role/phone/address/area/note/status")
            {
                SearchBox.Text = "";
                SearchBox.Foreground = new SolidColorBrush(Colors.Black);
                SearchBox.FontStyle = FontStyles.Normal;
            }
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Text = "Search name/username/role/phone/address/area/note/status";
                SearchBox.Foreground = new SolidColorBrush(Colors.Gray);
                SearchBox.FontStyle = FontStyles.Italic;
                ApplySearchFilter(SearchBox.Text);
            }
        }

        private void UserListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var row = UserListView.SelectedItem as UserRow;
            if (row == null) return;

            bool targetIsAdmin = string.Equals(row.Role, "Admin", StringComparison.OrdinalIgnoreCase);
            bool canEdit = _actorIsRoot || (_actorIsAdmin && !targetIsAdmin);

            OpenUserWindow(ToModel(row), canEdit);
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

            var win = new Window
            {
                Title = (user.UserID == 0) ? "Add User" : (canEdit ? "Edit User" : "User Details"),
                Content = form,
                Owner = Window.GetWindow(this),
                Width = 520,
                Height = 640,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Background = Brushes.White
            };
            win.ShowDialog();
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
                $"Delete user '{row.Username}'? This will deactivate the account.",
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

                Info($"Role updated to {newRole}.");
                LoadUsers();
            }
            catch (Exception ex) { Error($"Error changing role:\n{ex.Message}"); }
        }

        // Helpers
        private static bool TryGetUserId(object parameter, out int userId)
            => int.TryParse(parameter?.ToString(), out userId);

        private static void Info(string msg) => MessageBox.Show(msg, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        private static void Warn(string msg) => MessageBox.Show(msg, "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
        private static void Error(string msg) => MessageBox.Show(msg, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    // === Converter lives in the same namespace and is public ===
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

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var role = (value as string ?? "").Trim();
            if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)) return Blue;
            if (string.Equals(role, "Technician", StringComparison.OrdinalIgnoreCase)) return Green;
            return Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => System.Windows.Data.Binding.DoNothing;   // fully qualified to avoid "Binding does not exist"
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
