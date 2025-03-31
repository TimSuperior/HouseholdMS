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
        /* Коллекция всех домохозяйств, загружаемых из базы данных */
        private ObservableCollection<Household> allHouseholds = new ObservableCollection<Household>();

        /* Представление для сортировки и фильтрации данных */
        private ICollectionView view;

        /* Последний нажатый заголовок столбца (для сортировки) */
        private GridViewColumnHeader _lastHeaderClicked;

        /* Последнее направление сортировки */
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        public HouseholdsView()
        {
            InitializeComponent();
            LoadHouseholds();

            /* Привязка события клика по заголовкам столбцов */
            HouseholdListView.AddHandler(GridViewColumnHeader.ClickEvent,
                new RoutedEventHandler(GridViewColumnHeader_Click));
        }

        /* Загрузка данных из таблицы Households и установка источника */
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
                            LastInspDate = reader["LastInspect"].ToString()
                        });
                    }
                }
            }

            /* Установка источника данных и связывание с ListView */
            view = CollectionViewSource.GetDefaultView(allHouseholds);
            HouseholdListView.ItemsSource = view;
        }

        /* Фильтрация данных по тексту в строке поиска */
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
                }
                return false;
            };
        }

        /* Сброс текстового поля и фильтра при потере фокуса */
        private void ResetText(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox box && string.IsNullOrWhiteSpace(box.Text))
            {
                box.Text = box.Tag as string;
                box.Foreground = System.Windows.Media.Brushes.Gray;

                if (view != null) view.Filter = null;
            }
        }

        /* Очистка подсказки при получении фокуса */
        private void ClearText(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox box && box.Text == box.Tag as string)
            {
                box.Text = "";
                box.Foreground = System.Windows.Media.Brushes.Black;
            }
        }

        /* Обработка клика по заголовку столбца — сортировка */
        private void GridViewColumnHeader_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is GridViewColumnHeader header &&
                header.Column?.DisplayMemberBinding is Binding binding)
            {
                string sortBy = binding.Path.Path;

                ListSortDirection direction;

                // Переключение направления сортировки при повторном клике
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

        /* Открытие окна добавления новой записи */
        private void AddHouseholdButton_Click(object sender, RoutedEventArgs e)
        {
            var win = new AddHouseholdWindow();
            if (win.ShowDialog() == true)
            {
                LoadHouseholds();
            }
        }

        /* Открытие окна редактирования выбранной записи */
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

        /* Удаление выбранной записи после подтверждения */
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
