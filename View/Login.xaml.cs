using System;
using System.Windows;
using System.Windows.Input; // for Keyboard.Focus
using System.Data.SQLite;
using HouseholdMS.Model;

namespace HouseholdMS.View
{
    public partial class Login : Window
    {
        public Login()
        {
            InitializeComponent();

            // Keep window within screen’s usable area and center on render/resize
            var wa = SystemParameters.WorkArea;
            MaxWidth = Math.Max(400, wa.Width - 40);
            MaxHeight = Math.Max(300, wa.Height - 40);

            ShowLoginPanel();

            // Ensure initial keyboard focus actually lands in username
            Loaded += (_, __) => {
                Keyboard.Focus(LoginUsernameBox);
                LoginUsernameBox.SelectAll();
            };

            CenterInWorkArea();
            SizeChanged += (_, __) => CenterInWorkArea();
            ContentRendered += (_, __) => CenterInWorkArea();
        }

        private void CenterInWorkArea()
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + (wa.Width - ActualWidth) / 2;
            Top = wa.Top + (wa.Height - ActualHeight) / 2;
        }

        // ===== Panel toggles =====
        private void ShowLoginPanel()
        {
            Title = "Login";
            LoginCard.Visibility = Visibility.Visible;
            RegisterCard.Visibility = Visibility.Collapsed;

            // Make Enter trigger Login while on this panel
            LoginBtn.IsDefault = true;
            RegisterBtn.IsDefault = false;

            // Put caret in username
            Keyboard.Focus(LoginUsernameBox);
            LoginUsernameBox.SelectAll();
        }

        private void ShowRegisterPanel()
        {
            Title = "Register New User";
            RegisterCard.Visibility = Visibility.Visible;
            LoginCard.Visibility = Visibility.Collapsed;

            // Make Enter trigger Register while on this panel
            LoginBtn.IsDefault = false;
            RegisterBtn.IsDefault = true;

            Keyboard.Focus(RegNameBox);
            RegNameBox.SelectAll();
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
                Keyboard.Focus(LoginUsernameBox);
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
                            Keyboard.Focus(LoginUsernameBox);
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
                Keyboard.Focus(RegNameBox);
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
                            Keyboard.Focus(RegUsernameBox);
                            RegUsernameBox.SelectAll();
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

                LoginUsernameBox.Text = username;
                LoginPasswordBox.Clear();
                ShowLoginPanel();
                Keyboard.Focus(LoginPasswordBox);
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
