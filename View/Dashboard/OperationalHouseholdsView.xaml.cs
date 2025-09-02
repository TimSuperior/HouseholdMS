using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using HouseholdMS;              // for MainWindow (role fetch fallback)
using HouseholdMS.Model;

namespace HouseholdMS.View.Dashboard
{
    public partial class OperationalHouseholdsView : UserControl
    {
        private const string OPERATIONAL = "Operational";
        private const string IN_SERVICE = "In Service";
        private const string NOT_OPERATIONAL = "Not Operational";

        private readonly ObservableCollection<Household> allHouseholds = new ObservableCollection<Household>();
        private ICollectionView view;

        private GridViewColumnHeader _lastHeaderClicked;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        private readonly Dictionary<string, string> _headerToProperty = new Dictionary<string, string>()
        {
            { "ID", "HouseholdID" },
            { "Owner Name", "OwnerName" },
            { "User Name", "UserName" },
            { "Municipality", "Municipality" },
            { "District", "District" },
            { "Contact", "ContactNum" },
            { "Installed", "InstallDate" },
            { "Last Inspect", "LastInspect" },
            { "Status", "Statuss" },
            { "Comment", "UserComm" }
        };

        private readonly string _currentUserRole;

        // Active embedded control (when service flow starts)
        private AddServiceControl _activeServiceControl;

        public OperationalHouseholdsView() : this(GetRoleFromMain()) { }

        public OperationalHouseholdsView(string userRole)
        {
            InitializeComponent();
            _currentUserRole = string.IsNullOrWhiteSpace(userRole) ? "User" : userRole;

            LoadHouseholds();
            HouseholdListView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(GridViewColumnHeader_Click));
            HouseholdListView.SelectionChanged += HouseholdListView_SelectionChanged;

            UpdateSearchPlaceholder();
            ApplyFilter();

            StartServiceCallButton.Visibility = Visibility.Collapsed;
            StartServiceCallButton.IsEnabled = false;
            StartServiceCallButton.ToolTip = "Select a household.";
        }

        private static string GetRoleFromMain()
        {
            try
            {
                var mw = Application.Current.MainWindow as MainWindow;
                if (mw != null)
                {
                    var roleProp = mw.GetType().GetProperty("CurrentUserRole");
                    if (roleProp != null)
                    {
                        var val = roleProp.GetValue(mw, null) as string;
                        if (!string.IsNullOrWhiteSpace(val)) return val;
                    }
                }
            }
            catch { }
            return "User";
        }

        public void LoadHouseholds()
        {
            allHouseholds.Clear();

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();

                string sql = @"
                    SELECT * FROM Households
                    WHERE
                        Statuss IS NULL
                        OR TRIM(Statuss) = ''
                        OR LOWER(REPLACE(REPLACE(TRIM(Statuss), '_',' '),'-',' ')) LIKE 'operational%'";
                using (var cmd = new SQLiteCommand(sql, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        DateTime installDate = DateTime.TryParse(reader["InstallDate"] == DBNull.Value ? null : reader["InstallDate"].ToString(), out DateTime dt1) ? dt1 : DateTime.MinValue;
                        DateTime lastInspect = DateTime.TryParse(reader["LastInspect"] == DBNull.Value ? null : reader["LastInspect"].ToString(), out DateTime dt2) ? dt2 : DateTime.MinValue;

                        allHouseholds.Add(new Household
                        {
                            HouseholdID = Convert.ToInt32(reader["HouseholdID"]),
                            OwnerName = reader["OwnerName"] == DBNull.Value ? null : reader["OwnerName"].ToString(),
                            UserName = reader["UserName"] == DBNull.Value ? null : reader["UserName"].ToString(),
                            Municipality = reader["Municipality"] == DBNull.Value ? null : reader["Municipality"].ToString(),
                            District = reader["District"] == DBNull.Value ? null : reader["District"].ToString(),
                            ContactNum = reader["ContactNum"] == DBNull.Value ? null : reader["ContactNum"].ToString(),
                            InstallDate = installDate,
                            LastInspect = lastInspect,
                            UserComm = reader["UserComm"] != DBNull.Value ? reader["UserComm"].ToString() : string.Empty,
                            Statuss = reader["Statuss"] != DBNull.Value ? reader["Statuss"].ToString() : string.Empty
                        });
                    }
                }
            }

            view = CollectionViewSource.GetDefaultView(allHouseholds);
            HouseholdListView.ItemsSource = view;
        }

        // ---- Search / filter ----
        private void UpdateSearchPlaceholder()
        {
            if (SearchBox == null) return;

            const string ph = "Search within \"Operational\"";
            if (string.IsNullOrWhiteSpace(SearchBox.Text) ||
                SearchBox.Text == "Search by owner, user, area or contact" ||
                SearchBox.Text == "Search all households")
            {
                SearchBox.Text = ph;
                SearchBox.Tag = ph;
                SearchBox.Foreground = Brushes.Gray;
                SearchBox.FontStyle = FontStyles.Italic;
            }
        }

        private void ApplyFilter()
        {
            if (view == null) return;

            string search = (SearchBox != null && SearchBox.Text != (string)SearchBox.Tag)
                ? (SearchBox.Text ?? string.Empty).Trim().ToLowerInvariant()
                : string.Empty;

            view.Filter = delegate (object obj)
            {
                var h = obj as Household;
                if (h == null) return false;

                if (string.IsNullOrEmpty(search)) return true;

                return (h.OwnerName != null && h.OwnerName.ToLowerInvariant().Contains(search))
                    || (h.ContactNum != null && h.ContactNum.ToLowerInvariant().Contains(search))
                    || (h.UserName != null && h.UserName.ToLowerInvariant().Contains(search))
                    || (h.Municipality != null && h.Municipality.ToLowerInvariant().Contains(search))
                    || (h.District != null && h.District.ToLowerInvariant().Contains(search));
            };

            view.Refresh();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void ResetText(object sender, RoutedEventArgs e)
        {
            var box = sender as TextBox;
            if (box != null && string.IsNullOrWhiteSpace(box.Text))
            {
                box.Text = box.Tag as string;
                box.Foreground = Brushes.Gray;
                box.FontStyle = FontStyles.Italic;
                ApplyFilter();
            }
        }

        private void ClearText(object sender, RoutedEventArgs e)
        {
            var box = sender as TextBox;
            if (box == null) return;

            if (box.Text == box.Tag as string)
                box.Text = string.Empty;

            box.Foreground = Brushes.Black;
            box.FontStyle = FontStyles.Normal;

            ApplyFilter();
        }

        // ---- Sorting ----
        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            var header = e.OriginalSource as GridViewColumnHeader;
            if (header == null) return;

            string headerText = header.Content == null ? null : header.Content.ToString();
            if (string.IsNullOrEmpty(headerText) || !_headerToProperty.ContainsKey(headerText)) return;

            string sortBy = _headerToProperty[headerText];
            ListSortDirection direction = (_lastHeaderClicked == header && _lastDirection == ListSortDirection.Ascending)
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            _lastHeaderClicked = header;
            _lastDirection = direction;

            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new SortDescription(sortBy, direction));
            view.Refresh();
        }

        // ---- Selection → show details on the right; enable/disable Start button ----
        private void HouseholdListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ClearEmbeddedServiceControl(); // if service UI was open, close it

            StartServiceCallButton.Visibility = Visibility.Collapsed;
            StartServiceCallButton.IsEnabled = false;
            StartServiceCallButton.ToolTip = "Select a household.";
            FormContent.Content = null;

            var selected = HouseholdListView.SelectedItem as Household;
            if (selected == null) return;

            // Check role & open-service state
            bool hasOpenService = false;
            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (var chk = new SQLiteCommand(
                    "SELECT 1 FROM Service WHERE HouseholdID=@hid AND FinishDate IS NULL LIMIT 1", conn))
                {
                    chk.Parameters.AddWithValue("@hid", selected.HouseholdID);
                    hasOpenService = chk.ExecuteScalar() != null;
                }
            }

            // Inject selection details into the right pane
            FormContent.Content = BuildHouseholdDetailsView(selected, hasOpenService);

            // Button visibility rules
            if (!IsAdminOrTech())
            {
                StartServiceCallButton.Visibility = Visibility.Collapsed;
                StartServiceCallButton.ToolTip = "Only Admin or Technician can start a service call.";
                return;
            }

            if (hasOpenService)
            {
                StartServiceCallButton.Visibility = Visibility.Collapsed;
                StartServiceCallButton.ToolTip = "There is already an open service for this household.";
            }
            else
            {
                StartServiceCallButton.Visibility = Visibility.Visible;
                StartServiceCallButton.IsEnabled = true;
                StartServiceCallButton.ToolTip = "Start a new service call.";
            }
        }

        private bool IsAdminOrTech()
        {
            string r = _currentUserRole == null ? "" : _currentUserRole.Trim();
            return r.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                || r.Equals("Technician", StringComparison.OrdinalIgnoreCase);
        }

        // ---- Service call flow: embed AddServiceControl in FormContent ----
        private void StartServiceCall_Click(object sender, RoutedEventArgs e)
        {
            var selected = HouseholdListView.SelectedItem as Household;
            if (selected == null) return;

            if (!IsAdminOrTech())
            {
                MessageBox.Show("Only Admin or Technician can start a service call.", "Access denied",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (var chk = new SQLiteCommand(
                    "SELECT 1 FROM Service WHERE HouseholdID=@hid AND FinishDate IS NULL LIMIT 1", conn))
                {
                    chk.Parameters.AddWithValue("@hid", selected.HouseholdID);
                    if (chk.ExecuteScalar() != null)
                    {
                        MessageBox.Show("An open service already exists for this household.",
                                        "Already Open", MessageBoxButton.OK, MessageBoxImage.Information);
                        HouseholdListView_SelectionChanged(null, null);
                        return;
                    }
                }
            }

            _activeServiceControl = new AddServiceControl(selected, _currentUserRole);
            _activeServiceControl.ServiceCreated += ServiceControl_ServiceCreated;
            _activeServiceControl.CancelRequested += ServiceControl_CancelRequested;

            FormContent.Content = _activeServiceControl;
            StartServiceCallButton.Visibility = Visibility.Collapsed;
        }

        private void ServiceControl_CancelRequested(object sender, EventArgs e)
        {
            ClearEmbeddedServiceControl();
            HouseholdListView_SelectionChanged(null, null);
        }

        private void ServiceControl_ServiceCreated(object sender, EventArgs e)
        {
            LoadHouseholds();
            ApplyFilter();

            HouseholdListView.SelectedItem = null;
            ClearEmbeddedServiceControl();
            StartServiceCallButton.Visibility = Visibility.Collapsed;
            StartServiceCallButton.IsEnabled = false;
            StartServiceCallButton.ToolTip = "Select a household.";
        }

        private void ClearEmbeddedServiceControl()
        {
            if (_activeServiceControl != null)
            {
                _activeServiceControl.ServiceCreated -= ServiceControl_ServiceCreated;
                _activeServiceControl.CancelRequested -= ServiceControl_CancelRequested;
                _activeServiceControl = null;
            }
            FormContent.Content = null;
        }

        // ===== Click on Status text in the list to cycle status (Admin/Technician) =====
        private void StatusText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var selected = HouseholdListView?.SelectedItem as Household;
            if (selected == null) return;

            if (!IsAdminOrTech())
            {
                MessageBox.Show("Only Admin or Technician can change status.",
                                "Access denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string next = NextStatus(selected.Statuss);

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (var cmd = new SQLiteCommand(
                        "UPDATE Households SET Statuss=@s WHERE HouseholdID=@id", conn))
                    {
                        cmd.Parameters.AddWithValue("@s", next);
                        cmd.Parameters.AddWithValue("@id", selected.HouseholdID);
                        cmd.ExecuteNonQuery();
                    }
                }

                selected.Statuss = next;
                view?.Refresh();

                // If it’s no longer "Operational", remove from this list
                if (!next.Equals(OPERATIONAL, StringComparison.OrdinalIgnoreCase))
                {
                    LoadHouseholds();
                    ApplyFilter();
                    HouseholdListView.SelectedItem = null;
                    FormContent.Content = null;
                    StartServiceCallButton.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // refresh details pane for the same selection
                    HouseholdListView_SelectionChanged(null, null);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to change status.\n" + ex.Message,
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string NextStatus(string current)
        {
            string c = (current ?? "").Trim();
            if (c.Equals(OPERATIONAL, StringComparison.OrdinalIgnoreCase)) return IN_SERVICE;
            if (c.Equals(IN_SERVICE, StringComparison.OrdinalIgnoreCase)) return NOT_OPERATIONAL;
            return OPERATIONAL;
        }

        // ======= UI helpers: details pane =======
        private UIElement BuildHouseholdDetailsView(Household h, bool hasOpenService)
        {
            var root = new StackPanel { Margin = new Thickness(6) };

            // Header line
            var header = new DockPanel { LastChildFill = true };
            var title = new TextBlock
            {
                Text = $"Household #{h.HouseholdID}",
                FontWeight = FontWeights.SemiBold,
                FontSize = 18,
                Margin = new Thickness(0, 0, 8, 6)
            };
            DockPanel.SetDock(title, Dock.Left);
            header.Children.Add(title);

            var statusChip = new Border
            {
                Background = GetStatusBrush(h.Statuss),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 0, 6),
                Child = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(h.Statuss) ? OPERATIONAL : h.Statuss,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold
                },
                HorizontalAlignment = HorizontalAlignment.Right
            };
            header.Children.Add(statusChip);
            root.Children.Add(header);

            if (hasOpenService)
            {
                root.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(255, 243, 205)), // soft yellow
                    BorderBrush = new SolidColorBrush(Color.FromRgb(250, 219, 141)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 0, 8),
                    Child = new TextBlock
                    {
                        Text = "An open service is already in progress for this household.",
                        Foreground = new SolidColorBrush(Color.FromRgb(102, 60, 0))
                    }
                });
            }

            // Details grid (labels + values)
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int r = 0;
            void AddRow(string label, string value)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var lbl = new TextBlock
                {
                    Text = label,
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(0, 2, 8, 2)
                };
                Grid.SetRow(lbl, r);
                Grid.SetColumn(lbl, 0);

                var val = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
                    Margin = new Thickness(0, 2, 0, 2),
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetRow(val, r);
                Grid.SetColumn(val, 1);

                grid.Children.Add(lbl);
                grid.Children.Add(val);
                r++;
            }

            AddRow("Owner Name", h.OwnerName);
            AddRow("User Name", h.UserName);
            AddRow("Contact", h.ContactNum);
            AddRow("Municipality", h.Municipality);
            AddRow("District", h.District);
            AddRow("Installed", FormatDate(h.InstallDate));
            AddRow("Last Inspect", FormatDate(h.LastInspect));
            AddRow("Comment", h.UserComm);

            root.Children.Add(grid);

            return root;
        }

        private static string FormatDate(DateTime dt)
        {
            return (dt.Year > 1900) ? dt.ToString("yyyy-MM-dd") : "—";
        }

        private static Brush GetStatusBrush(string status)
        {
            string s = (status ?? "").Trim().ToLowerInvariant();
            // green for operational; blue for in service; gray otherwise
            if (s.StartsWith("operational")) return new SolidColorBrush(Color.FromRgb(76, 175, 80));      // green
            if (s.StartsWith("in service")) return new SolidColorBrush(Color.FromRgb(25, 118, 210));     // blue
            if (s.StartsWith("not operational")) return new SolidColorBrush(Color.FromRgb(158, 158, 158)); // grey
            return new SolidColorBrush(Color.FromRgb(76, 175, 80)); // default to operational style
        }
    }
}
