using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS.View
{
    public partial class UserManagementView : UserControl
    {
        private ObservableCollection<User> users = new ObservableCollection<User>();
        private User selectedUser;

        public UserManagementView()
        {
            InitializeComponent();
            LoadUsers();
        }

        private void LoadUsers()
        {
            users.Clear();

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("SELECT UserID, Name, Username, Role FROM Users", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        users.Add(new User
                        {
                            UserID = reader.GetInt32(0),
                            Name = reader.GetString(1),
                            Username = reader.GetString(2),
                            Role = reader.GetString(3)
                        });
                    }
                }
            }

            UserListView.ItemsSource = users;
        }

        private void UserListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            selectedUser = (User)UserListView.SelectedItem;
            if (selectedUser != null)
            {
                NameBox.Text = selectedUser.Name;
                UsernameBox.Text = selectedUser.Username;
                RoleComboBox.SelectedValue = selectedUser.Role;

                if (selectedUser.UserID == 1)
                {
                    // 🔥 If root selected, disable form
                    NameBox.IsEnabled = false;
                    UsernameBox.IsEnabled = false;
                    RoleComboBox.IsEnabled = false;
                    SaveChangesButton.IsEnabled = false;
                }
                else
                {
                    // 🔥 If normal user, enable form
                    NameBox.IsEnabled = true;
                    UsernameBox.IsEnabled = false; // Username stays always locked
                    RoleComboBox.IsEnabled = true;
                    SaveChangesButton.IsEnabled = true;
                }
            }
        }

        private void SaveChangesButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedUser == null) return;

            if (selectedUser.UserID == 1)
            {
                MessageBox.Show("You cannot modify the root admin account.", "Action Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string newName = NameBox.Text.Trim();
            string newRole = (RoleComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("UPDATE Users SET Name = @name, Role = @role WHERE UserID = @userId", conn))
                {
                    cmd.Parameters.AddWithValue("@name", newName);
                    cmd.Parameters.AddWithValue("@role", newRole);
                    cmd.Parameters.AddWithValue("@userId", selectedUser.UserID);
                    cmd.ExecuteNonQuery();
                }
            }

            MessageBox.Show("User details updated successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            LoadUsers();
        }
    }

    public class User
    {
        public int UserID { get; set; }
        public string Name { get; set; }
        public string Username { get; set; }
        public string Role { get; set; }
    }
}
