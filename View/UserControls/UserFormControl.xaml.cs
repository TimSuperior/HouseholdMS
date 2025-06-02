using System;
using System.Data.SqlClient;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HouseholdMS.Model;

namespace HouseholdMS.View.UserControls
{
    public partial class UserFormControl : UserControl
    {
        private readonly User user;

        public event EventHandler OnSaveSuccess;
        public event EventHandler OnCancel;

        public UserFormControl(User selectedUser)
        {
            InitializeComponent();
            user = selectedUser;

            // Pre-fill form fields
            NameBox.Text = user.Name;
            UsernameBox.Text = user.Username;
            RoleComboBox.SelectedItem = RoleComboBox.Items
                .Cast<ComboBoxItem>()
                .FirstOrDefault(i => (string)i.Content == user.Role);

            // Special handling for root admin
            if (user.UserID == 1)
            {
                NameBox.IsEnabled = false;
                RoleComboBox.IsEnabled = false;
                SaveButton.IsEnabled = false;
                DeleteButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                DeleteButton.Visibility = Visibility.Visible;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string newName = NameBox.Text.Trim();
            string newRole = (RoleComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();

            if (string.IsNullOrWhiteSpace(newName))
            {
                NameBox.Tag = "error";
                return;
            }
            else
            {
                NameBox.Tag = null;
            }

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("UPDATE Users SET Name = @name, Role = @role WHERE UserID = @userId", conn))
                    {
                        cmd.Parameters.AddWithValue("@name", newName);
                        cmd.Parameters.AddWithValue("@role", newRole);
                        cmd.Parameters.AddWithValue("@userId", user.UserID);
                        cmd.ExecuteNonQuery();
                    }
                }

                MessageBox.Show("User details updated successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                OnSaveSuccess?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating user:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            OnCancel?.Invoke(this, EventArgs.Empty);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                $"Are you sure you want to delete user '{user.Username}'?",
                "Confirm Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("DELETE FROM Users WHERE UserID = @userId", conn))
                    {
                        cmd.Parameters.AddWithValue("@userId", user.UserID);
                        cmd.ExecuteNonQuery();
                    }
                }

                MessageBox.Show("User deleted successfully.", "Deleted", MessageBoxButton.OK, MessageBoxImage.Information);
                OnSaveSuccess?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error deleting user:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
