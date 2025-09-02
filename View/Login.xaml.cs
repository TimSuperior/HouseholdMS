using System;
using System.Windows;
using System.Data.SQLite;
using HouseholdMS.Model;

namespace HouseholdMS.View
{
    public partial class Login : Window
    {
        public Login()
        {
            InitializeComponent();
            ShowLoginPanel();
        }

        // ===== Panel toggles =====
        private void ShowLoginPanel()
        {
            Title = "Login";
            LoginCard.Visibility = Visibility.Visible;
            RegisterCard.Visibility = Visibility.Collapsed;

            // Focus username for quick typing
            LoginUsernameBox.Focus();
        }

        private void ShowRegisterPanel()
        {
            Title = "Register New User";
            RegisterCard.Visibility = Visibility.Visible;
            LoginCard.Visibility = Visibility.Collapsed;

            RegNameBox.Focus();
        }

        // ===== Handlers =====
        private void ShowRegister_Click(object sender, RoutedEventArgs e) => ShowRegisterPanel();

        private void BackToLoginButton_Click(object sender, RoutedEventArgs e) => ShowLoginPanel();

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            string username = LoginUsernameBox.Text.Trim();
            string password = LoginPasswordBox.Password.Trim();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Please enter both Username and Password.", "Missing Credentials",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();

                    string query = "SELECT Role FROM Users WHERE LOWER(Username) = LOWER(@username) AND PasswordHash = @password";
                    using (var cmd = new SQLiteCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@username", username);
                        cmd.Parameters.AddWithValue("@password", password);

                        var role = cmd.ExecuteScalar() as string;

                        if (!string.IsNullOrEmpty(role))
                        {
                            MessageBox.Show($"Welcome {username}!\nRole: {role}", "Login Successful",
                                MessageBoxButton.OK, MessageBoxImage.Information);

                            var mainWindow = new MainWindow(role);
                            mainWindow.Show();
                            Close();
                        }
                        else
                        {
                            MessageBox.Show("Invalid username or password.", "Login Failed",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                            LoginUsernameBox.Clear();
                            LoginPasswordBox.Clear();
                            LoginUsernameBox.Focus();
                        }
                    }
                }
            }
            catch (SQLiteException ex)
            {
                MessageBox.Show($"Database connection error:\n{ex.Message}", "Database Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An unexpected error occurred:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            string name = RegNameBox.Text.Trim();
            string username = RegUsernameBox.Text.Trim();
            string password = RegPasswordBox.Password.Trim();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("Please fill in all fields.", "Missing Information",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();

                    using (var checkCmd = new SQLiteCommand(
                        "SELECT COUNT(*) FROM Users WHERE LOWER(Username) = LOWER(@username)", conn))
                    {
                        checkCmd.Parameters.AddWithValue("@username", username);
                        int existing = Convert.ToInt32(checkCmd.ExecuteScalar());

                        if (existing > 0)
                        {
                            MessageBox.Show("Username already exists. Please choose another.",
                                "Username Taken", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                    }

                    using (var insertCmd = new SQLiteCommand(
                        "INSERT INTO Users (Name, Username, PasswordHash, Role) VALUES (@name, @username, @password, 'User')", conn))
                    {
                        insertCmd.Parameters.AddWithValue("@name", name);
                        insertCmd.Parameters.AddWithValue("@username", username);
                        insertCmd.Parameters.AddWithValue("@password", password); // TODO: hash in production

                        insertCmd.ExecuteNonQuery();
                    }
                }

                MessageBox.Show("User registered successfully!", "Registration Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                // Prefill login with the new username and switch back
                LoginUsernameBox.Text = username;
                LoginPasswordBox.Clear();
                ShowLoginPanel();
                LoginPasswordBox.Focus();
            }
            catch (SQLiteException ex)
            {
                MessageBox.Show($"Database error: {ex.Message}", "Registration Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Registration Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (Application.Current.Windows.Count == 0)
                Application.Current.Shutdown();
        }
    }
}
