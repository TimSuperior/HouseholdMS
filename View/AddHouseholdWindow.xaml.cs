using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace HouseholdMS.View
{
    public partial class HouseholdsView : UserControl
    {
        /* Collection of all households loaded from the database */
        private ObservableCollection<Household> allHouseholds = new ObservableCollection<Household>();

        /* View for sorting and filtering data */
        private ICollectionView view;

        /* Last clicked column header (for sorting) */
        private GridViewColumnHeader _lastHeaderClicked;

        /* Last sort direction */
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        public HouseholdsView()
        {
            InitializeComponent();
            LoadHouseholds();

            /* Bind event for column header clicks */
            HouseholdListView.AddHandler(GridViewColumnHeader.ClickEvent,
                new RoutedEventHandler(GridViewColumnHeader_Click));
        }

        /* Load data from the Households table and set the data source for the ListView */
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
                            // New: Load the Note field (if null, assign an empty string)
                            Note = reader["Note"] != DBNull.Value ? reader["Note"].ToString() : string.Empty
                        });
                    }
                }
            }

            /* Set the data source and bind to the ListView */
            view = CollectionViewSource.GetDefaultView(allHouseholds);
            HouseholdListView.ItemsSource = view;
        }

        /* Filter data based on text input in the search box */
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (view == null) return;

            string search = SearchBox.Text.Trim().ToLower();

            view.Filter = obj =>
            {
                if (obj is Household h)
                {
                    return h.OwnerName.ToLower().Contains(search) ||
                           h.Address.ToLower().Contains(search) ||
                           h.ContactNum.ToLower().Contains(search);
                    // Optionally include: || h.Note.ToLower().Contains(search);
                }
                return false;
            };
        }

        /* Reset the search box text and clear filter when losing focus */
        private void ResetText(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox box && string.IsNullOrWhiteSpace(box.Text))
            {
                box.Text = box.Tag as string;
                box.Foreground = System.Windows.Media.Brushes.Gray;

                if (view != null)
                    view.Filter = null;
            }
        }

        /* Clear placeholder text when the search box gains focus */
        private void ClearText(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox box && box.Text == box.Tag as string)
            {
                box.Text = "";
                box.Foreground = System.Windows.Media.Brushes.Black;
            }
        }

        /* Handle column header clicks for sorting */
        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is GridViewColumnHeader header &&
                header.Column?.DisplayMemberBinding is Binding binding)
            {
                string sortBy = binding.Path.Path;
                ListSortDirection direction;

                // Toggle sort direction if the same header is clicked again
                if (_lastHeaderClicked == header)
                {
                    direction = _lastDirection == ListSortDirection.Ascending
                        ? ListSortDirection.Descending
                        : ListSortDirection.Ascending;
                }
                else
                {
                    direction = ListSortDirection.Ascending;
                }

                _lastHeaderClicked = header;
                _lastDirection = direction;

                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new SortDescription(sortBy, direction));
                view.Refresh();
            }
        }

        /* Open the window to add a new household record */
        private void AddHouseholdButton_Click(object sender, RoutedEventArgs e)
        {
            var win = new AddHouseholdWindow();
            if (win.ShowDialog() == true)
            {
                LoadHouseholds();
            }
        }

        /* Open the window to edit the selected household record */
        private void EditHousehold_Click(object sender, RoutedEventArgs e)
        {
            if (HouseholdListView.SelectedItem is Household selected)
            {
                var win = new AddHouseholdWindow(selected);
                if (win.ShowDialog() == true)
                {
                    LoadHouseholds();
                }
            }
            else
            {
                MessageBox.Show("Please select a household to edit.", "Edit Household", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /* Delete the selected household record after confirmation */
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
                }
            }
            else
            {
                MessageBox.Show("Please select a household to delete.", "Delete Household", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}