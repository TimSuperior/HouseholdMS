using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using HouseholdMS.View.UserControls;

namespace HouseholdMS.View
{
    public partial class HouseholdsView : UserControl
    {
        private readonly ObservableCollection<Household> allHouseholds = new ObservableCollection<Household>();
        private ICollectionView view;
        private GridViewColumnHeader _lastHeaderClicked;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        private readonly string _currentUserRole;

        public HouseholdsView(string userRole = "User")
        {
            InitializeComponent();
            _currentUserRole = userRole;
            LoadHouseholds();
            HouseholdListView.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(GridViewColumnHeader_Click));
            HouseholdListView.SelectionChanged += HouseholdListView_SelectionChanged;

            ApplyAccessRestrictions();
        }

        private void ApplyAccessRestrictions()
        {
            bool isAdmin = _currentUserRole == "Admin";

            if (FindName("AddHouseholdButton") is Button addBtn)
                addBtn.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

            if (FindName("EditHouseholdButton") is Button editBtn)
                editBtn.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

            if (FindName("DeleteHouseholdButton") is Button deleteBtn)
                deleteBtn.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
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
                        allHouseholds.Add(new Household
                        {
                            HouseholdID = Convert.ToInt32(reader["HouseholdID"]),
                            OwnerName = reader["OwnerName"].ToString(),
                            Address = reader["Address"].ToString(),
                            ContactNum = reader["ContactNum"].ToString(),
                            InstDate = reader["InstallDate"].ToString(),
                            LastInspDate = reader["LastInspect"].ToString(),
                            Note = reader["Note"] != DBNull.Value ? reader["Note"].ToString() : string.Empty
                        });
                    }
                }
            }

            view = CollectionViewSource.GetDefaultView(allHouseholds);
            HouseholdListView.ItemsSource = view;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (view == null) return;

            string search = SearchBox.Text?.Trim().ToLower() ?? string.Empty;
            view.Filter = obj => obj is Household h &&
                (h.OwnerName.ToLower().Contains(search) ||
                 h.Address.ToLower().Contains(search) ||
                 h.ContactNum.ToLower().Contains(search));
        }

        private void ResetText(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox box && string.IsNullOrWhiteSpace(box.Text))
            {
                box.Text = box.Tag as string;
                box.Foreground = System.Windows.Media.Brushes.Gray;
                view.Filter = null;
            }
        }

        private void ClearText(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox box && box.Text == box.Tag as string)
            {
                box.Text = string.Empty;
                box.Foreground = System.Windows.Media.Brushes.Black;
            }
        }

        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is GridViewColumnHeader header &&
                header.Column?.DisplayMemberBinding is Binding binding)
            {
                string sortBy = binding.Path.Path;
                ListSortDirection direction = (_lastHeaderClicked == header && _lastDirection == ListSortDirection.Ascending)
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;

                _lastHeaderClicked = header;
                _lastDirection = direction;

                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new SortDescription(sortBy, direction));
                view.Refresh();
            }
        }

        private void AddHouseholdButton_Click(object sender, RoutedEventArgs e)
        {
            var form = new AddHouseholdControl();
            form.OnSavedSuccessfully += (s, args) =>
            {
                FormContent.Content = null;
                LoadHouseholds();
            };
            form.OnCancelRequested += (s, args) =>
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
                if (_currentUserRole == "Admin")
                {
                    var form = new AddHouseholdControl(selected);
                    form.OnSavedSuccessfully += (s, args) =>
                    {
                        FormContent.Content = null;
                        LoadHouseholds();
                        HouseholdListView.SelectedItem = null;
                    };
                    form.OnCancelRequested += (s, args) =>
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
                form.OnSavedSuccessfully += (s, args) =>
                {
                    FormContent.Content = null;
                    LoadHouseholds();
                    HouseholdListView.SelectedItem = null;
                };
                form.OnCancelRequested += (s, args) =>
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
                        var cmd = new SQLiteCommand("DELETE FROM Households WHERE HouseholdID = @id", conn);
                        cmd.Parameters.AddWithValue("@id", selected.HouseholdID);
                        cmd.ExecuteNonQuery();
                    }

                    LoadHouseholds();
                    FormContent.Content = null;
                    HouseholdListView.SelectedItem = null;
                }
            }
            else
            {
                MessageBox.Show("Please select a household to delete.", "Delete Household", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
