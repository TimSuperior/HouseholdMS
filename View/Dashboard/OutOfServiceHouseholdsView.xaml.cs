using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using HouseholdMS.View.UserControls;
using HouseholdMS.Model;
using System.Data.SQLite;
using System.Windows.Media;

namespace HouseholdMS.View.Dashboard
{
    public partial class OutOfServiceHouseholdsView : UserControl
    {
        private const string OPERATIONAL = "Operational";
        private const string IN_SERVICE = "In Service"; // DB label for your "Out of Service"
        private const string NOT_OPERATIONAL = "Not Operational";

        private readonly ObservableCollection<Household> allHouseholds = new ObservableCollection<Household>();
        private ICollectionView view;
        private GridViewColumnHeader _lastHeaderClicked;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        private readonly Dictionary<string, string> _headerToProperty = new Dictionary<string, string>
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

        private readonly string _currentUserRole = "Admin";

        private string _normalizedStatusFilter = IN_SERVICE; // filter
        private bool _categoryFilterActive = true;           // category mode
        private string _searchText = string.Empty;

        public OutOfServiceHouseholdsView(string userRole)
        {
            InitializeComponent();

            LoadHouseholds();
            HouseholdListView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(GridViewColumnHeader_Click));
            HouseholdListView.SelectionChanged += HouseholdListView_SelectionChanged;

            ApplyAccessRestrictions();
            UpdateSearchPlaceholder();
            ApplyFilter(); // apply category filter
        }

        private void ApplyAccessRestrictions()
        {
            bool isAdmin = _currentUserRole == "Admin";

            var addBtn = FindName("AddHouseholdButton") as Button;
            if (addBtn != null) addBtn.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

            var editBtn = FindName("EditHouseholdButton") as Button;
            if (editBtn != null) editBtn.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

            var deleteBtn = FindName("DeleteHouseholdButton") as Button;
            if (deleteBtn != null) deleteBtn.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
        }

        public void LoadHouseholds()
        {
            allHouseholds.Clear();

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("SELECT * FROM Households", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        DateTime installDate = DateTime.TryParse(reader["InstallDate"] == DBNull.Value ? null : reader["InstallDate"].ToString(), out DateTime dt1) ? dt1 : DateTime.MinValue;
                        DateTime lastInspect = DateTime.TryParse(reader["LastInspect"] == DBNull.Value ? null : reader["LastInspect"].ToString(), out DateTime dt2) ? dt2 : DateTime.MinValue;

                        allHouseholds.Add(new Household
                        {
                            HouseholdID = Convert.ToInt32(reader["HouseholdID"]),
                            OwnerName = reader["OwnerName"]?.ToString(),
                            UserName = reader["UserName"]?.ToString(),
                            Municipality = reader["Municipality"]?.ToString(),
                            District = reader["District"]?.ToString(),
                            ContactNum = reader["ContactNum"]?.ToString(),
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

        private static string NormalizeStatus(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var t = s.Trim().ToLowerInvariant();
            t = t.Replace('_', ' ').Replace('-', ' ');
            while (t.Contains("  ")) t = t.Replace("  ", " ");
            if (t.StartsWith("operational")) return OPERATIONAL;
            if (t.StartsWith("in service") || t.StartsWith("service")) return IN_SERVICE;
            if (t.StartsWith("not operational") || t.StartsWith("notoperational")) return NOT_OPERATIONAL;
            return s.Trim();
        }

        private static string DisplayStatusLabel(string normalized)
            => string.Equals(normalized, IN_SERVICE, StringComparison.Ordinal) ? "Out of Service" : normalized;

        private void UpdateSearchPlaceholder()
        {
            if (SearchBox == null) return;
            string ph = _categoryFilterActive ? $"Search within \"{DisplayStatusLabel(_normalizedStatusFilter)}\"" : "Search all households";
            SearchBox.Tag = ph;

            if (string.IsNullOrWhiteSpace(SearchBox.Text) ||
                SearchBox.Text == "Search by owner, user, area or contact" ||
                SearchBox.Text == "Search all households")
            {
                SearchBox.Text = ph;
                SearchBox.Foreground = Brushes.Gray;
                SearchBox.FontStyle = FontStyles.Italic;
            }
        }

        private void ApplyFilter()
        {
            if (view == null) return;

            string search = _searchText == null ? string.Empty : _searchText.Trim().ToLowerInvariant();
            bool useCategory = _categoryFilterActive && !string.IsNullOrWhiteSpace(_normalizedStatusFilter);

            view.Filter = obj =>
            {
                var h = obj as Household;
                if (h == null) return false;

                bool categoryOk = true;
                if (useCategory)
                {
                    var hn = NormalizeStatus(h.Statuss);
                    categoryOk = hn == _normalizedStatusFilter;
                }
                if (!categoryOk) return false;

                if (string.IsNullOrEmpty(search)) return true;

                return (h.OwnerName != null && h.OwnerName.ToLowerInvariant().Contains(search))
                    || (h.ContactNum != null && h.ContactNum.ToLowerInvariant().Contains(search))
                    || (h.UserName != null && h.UserName.ToLowerInvariant().Contains(search))
                    || (h.Municipality != null && h.Municipality.ToLowerInvariant().Contains(search))
                    || (h.District != null && h.District.ToLowerInvariant().Contains(search));
            };

            view.Refresh();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchBox != null && SearchBox.Text == (SearchBox.Tag as string))
                _searchText = string.Empty;
            else
                _searchText = SearchBox?.Text ?? string.Empty;

            ApplyFilter();
        }

        private void ResetText(object sender, RoutedEventArgs e)
        {
            var box = sender as TextBox;
            if (box != null && string.IsNullOrWhiteSpace(box.Text))
            {
                box.Text = box.Tag as string;
                box.Foreground = Brushes.Gray;
                box.FontStyle = FontStyles.Italic;
                _searchText = string.Empty;
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

            _searchText = string.Empty;
            ApplyFilter();
        }

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

        private void AddHouseholdButton_Click(object sender, RoutedEventArgs e)
        {
            var form = new AddHouseholdControl();
            form.OnSavedSuccessfully += delegate
            {
                FormContent.Content = null;
                LoadHouseholds();
                ApplyFilter();
            };
            form.OnCancelRequested += delegate
            {
                FormContent.Content = null;
                HouseholdListView.SelectedItem = null;
            };
            FormContent.Content = form;
        }

        private void HouseholdListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HouseholdListView.SelectedItem is Household selected)
            {
                // Normalize to your DB labels
                var norm = NormalizeStatus(selected.Statuss);

                if (norm == IN_SERVICE) // UI: "Out of Service" bucket
                {
                    var form = new ServiceCallDetailControl(selected, _currentUserRole);
                    form.ServiceFinished += delegate
                    {
                        FormContent.Content = null;
                        LoadHouseholds();
                        ApplyFilter();
                        HouseholdListView.SelectedItem = null;
                    };
                    form.CancelRequested += delegate
                    {
                        FormContent.Content = null;
                        HouseholdListView.SelectedItem = null;
                    };
                    FormContent.Content = form;
                }
                else
                {
                    // Your existing edit (unchanged)
                    var form = new AddHouseholdControl(selected);
                    form.OnSavedSuccessfully += delegate
                    {
                        FormContent.Content = null;
                        LoadHouseholds();
                        ApplyFilter();
                        HouseholdListView.SelectedItem = null;
                    };
                    form.OnCancelRequested += delegate
                    {
                        FormContent.Content = null;
                        HouseholdListView.SelectedItem = null;
                    };
                    FormContent.Content = form;
                }
            }
            else
            {
                FormContent.Content = null;
            }
        }


        private void EditHousehold_Click(object sender, RoutedEventArgs e)
        {
            if (HouseholdListView.SelectedItem is Household selected)
            {
                var form = new AddHouseholdControl(selected);
                form.OnSavedSuccessfully += delegate
                {
                    FormContent.Content = null;
                    LoadHouseholds();
                    ApplyFilter();
                    HouseholdListView.SelectedItem = null;
                };
                form.OnCancelRequested += delegate
                {
                    FormContent.Content = null;
                    HouseholdListView.SelectedItem = null;
                };
                FormContent.Content = form;
            }
            else
            {
                MessageBox.Show("Please select a household to edit.", "Edit Household", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void DeleteHousehold_Click(object sender, RoutedEventArgs e)
        {
            if (HouseholdListView.SelectedItem is Household selected)
            {
                var confirm = MessageBox.Show(
                    $"Are you sure you want to delete household \"{selected.OwnerName}\"?",
                    "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (confirm == MessageBoxResult.Yes)
                {
                    using (var conn = DatabaseHelper.GetConnection())
                    {
                        conn.Open();
                        using (var cmd = new SQLiteCommand("DELETE FROM Households WHERE HouseholdID = @id", conn))
                        {
                            cmd.Parameters.AddWithValue("@id", selected.HouseholdID);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    LoadHouseholds();
                    ApplyFilter();
                    FormContent.Content = null;
                    HouseholdListView.SelectedItem = null;
                }
            }
            else
            {
                MessageBox.Show("Please select a household to delete.", "Delete Household", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void StatusText_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var tb = sender as TextBlock;
            if (tb == null) return;
            var h = tb.DataContext as Household;
            if (h == null) return;

            var cm = new ContextMenu();
            void addItem(string text)
            {
                var mi = new MenuItem { Header = text };
                mi.Click += (s, _) => ChangeStatus(h, text);
                cm.Items.Add(mi);
            }
            addItem(OPERATIONAL);
            addItem(IN_SERVICE);
            addItem(NOT_OPERATIONAL);

            cm.PlacementTarget = tb;
            cm.IsOpen = true;
        }

        private void ChangeStatus(Household h, string newStatus)
        {
            if (string.Equals(NormalizeStatus(h.Statuss), NormalizeStatus(newStatus), StringComparison.Ordinal))
                return;

            if (NormalizeStatus(newStatus) == IN_SERVICE)
            {
                var confirm = MessageBox.Show(
                    $"Move household \"{h.OwnerName}\" to In Service?\n\nThis will create/open a Service ticket.",
                    "Confirm Status Change", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes) return;
            }

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        using (var cmd = new SQLiteCommand(
                            "UPDATE Households SET Statuss=@st WHERE HouseholdID=@id;", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@st", newStatus);
                            cmd.Parameters.AddWithValue("@id", h.HouseholdID);
                            cmd.ExecuteNonQuery();
                        }
                        tx.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to change status.\n" + ex.Message, "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            LoadHouseholds();
            ApplyFilter();
        }
    }
}
