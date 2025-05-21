using System.Collections.ObjectModel;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;
using HouseholdMS.View.UserControls; // Make sure this is where UserFormControl is located

namespace HouseholdMS.View
{
    public partial class UserManagementView : UserControl
    {
        private ObservableCollection<User> users = new ObservableCollection<User>();

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
            var selectedUser = UserListView.SelectedItem as User;
            if (selectedUser == null)
            {
                FormContent.Content = null;
                return;
            }

            var form = new UserFormControl(selectedUser);

            form.OnSaveSuccess += (s, args) =>
            {
                FormContent.Content = null;
                LoadUsers();
                UserListView.SelectedItem = null;
            };

            form.OnCancel += (s, args) =>
            {
                FormContent.Content = null;
                UserListView.SelectedItem = null;
            };

            FormContent.Content = form;
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
