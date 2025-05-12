using System;
using System.Windows;
using System.Data.SQLite;

namespace HouseholdMS.View // << MATCH this to your namespace
{
    public partial class RegisterUser : Window
    {
        public RegisterUser()
        {
            InitializeComponent();
        }

        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            string name = NameBox.Text.Trim();
            string username = UsernameBox.Text.Trim();
            string password = PasswordBox.Password.Trim();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Please fill in all fields.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();

                    using (var checkCmd = new SQLiteCommand("SELECT COUNT(*) FROM Users WHERE LOWER(Username) = LOWER(@username)", conn))
                    {
                        checkCmd.Parameters.AddWithValue("@username", username);
                        long existing = (long)checkCmd.ExecuteScalar();

                        if (existing > 0)
                        {
                            MessageBox.Show("Username already exists. Please choose another.", "Username Taken", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                    }

                    using (var insertCmd = new SQLiteCommand("INSERT INTO Users (Name, Username, PasswordHash, Role) VALUES (@name, @username, @password, 'User')", conn))
                    {
                        insertCmd.Parameters.AddWithValue("@name", name);
                        insertCmd.Parameters.AddWithValue("@username", username);
                        insertCmd.Parameters.AddWithValue("@password", password);

                        insertCmd.ExecuteNonQuery();
                    }
                }

                MessageBox.Show("User registered successfully!", "Registration Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                var loginWindow = new Login();
                loginWindow.Show();
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Registration Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BackToLoginButton_Click(object sender, RoutedEventArgs e)
        {
            var loginWindow = new Login();
            loginWindow.Show();
            this.Close();
        }
    }
}
