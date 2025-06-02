using System;
using System.Windows;
using System.Data.SqlClient;
using HouseholdMS.Properties;
using HouseholdMS.Model;

namespace HouseholdMS.View
{
    public partial class Login : Window
    {
        public Login()
        {
            InitializeComponent();
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            string username = UsernameBox.Text.Trim();
            string password = PasswordBox.Password.Trim();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Please enter both Username and Password.", "Missing Credentials", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();

                    string query = "SELECT Role FROM Users WHERE LOWER(Username) = LOWER(@username) AND PasswordHash = @password";
                    using (var cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@username", username);
                        cmd.Parameters.AddWithValue("@password", password);

                        var role = cmd.ExecuteScalar() as string;

                        if (!string.IsNullOrEmpty(role))
                        {
                            MessageBox.Show($"Welcome {username}!\nRole: {role}", "Login Successful", MessageBoxButton.OK, MessageBoxImage.Information);

                            var mainWindow = new MainWindow(role);
                            mainWindow.Show();
                            this.Close();
                        }
                        else
                        {
                            MessageBox.Show("Invalid username or password.", "Login Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                            UsernameBox.Clear();
                            PasswordBox.Clear();
                            UsernameBox.Focus();
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                MessageBox.Show($"Database connection error:\n{ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An unexpected error occurred:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Register_Click(object sender, RoutedEventArgs e)
        {
            var registerWindow = new RegisterUser();
            registerWindow.Show();
            this.Close();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            if (Application.Current.Windows.Count == 0)
            {
                Application.Current.Shutdown();
            }
        }
    }
}
