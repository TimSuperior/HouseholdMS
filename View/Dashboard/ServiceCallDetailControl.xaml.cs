using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using HouseholdMS.Model;

namespace HouseholdMS.View.Dashboard
{
    public partial class ServiceCallDetailControl : UserControl
    {
        // ===== Row models =====
        public class TechRow
        {
            public int TechnicianID { get; set; }
            public string Name { get; set; }
            public bool IsSelected { get; set; }
        }

        public class InvRow : INotifyPropertyChanged
        {
            public int ItemID { get; set; }
            public string ItemType { get; set; }
            public int Available { get; set; }

            private bool _isSelected;
            public bool IsSelected
            {
                get { return _isSelected; }
                set { _isSelected = value; OnPropertyChanged("IsSelected"); }
            }

            private int _quantityUsed = 1;
            public int QuantityUsed
            {
                get { return _quantityUsed; }
                set
                {
                    int v = value;
                    if (v < 1) v = 1;
                    if (v > Available) v = Available;
                    _quantityUsed = v;
                    OnPropertyChanged("QuantityUsed");
                }
            }

            // UI helpers for badges
            public string StockBadgeText
            {
                get
                {
                    if (Available <= 0) return "Out of stock";
                    if (Available <= 2) return "Low";
                    return "OK";
                }
            }

            public Brush StockBadgeBrush
            {
                get
                {
                    if (Available <= 0) return (Brush)Application.Current.FindResource("Col.BadgeRed");
                    if (Available <= 2) return (Brush)Application.Current.FindResource("Col.BadgeYellow");
                    return (Brush)Application.Current.FindResource("Col.BadgeGreen");
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged(string name)
            {
                var h = PropertyChanged;
                if (h != null) h(this, new PropertyChangedEventArgs(name));
            }
        }

        // ===== Fields =====
        private readonly int _householdId;
        private readonly string _userRole;
        private int _serviceId;
        private DateTime _openedAt;

        private readonly ObservableCollection<TechRow> _techAll = new ObservableCollection<TechRow>();
        private readonly ObservableCollection<TechRow> _techSelected = new ObservableCollection<TechRow>();

        private readonly ObservableCollection<InvRow> _invAll = new ObservableCollection<InvRow>();
        private readonly ObservableCollection<InvRow> _invSelected = new ObservableCollection<InvRow>();

        private System.Collections.Generic.HashSet<int> _preselectedTechIds =
            new System.Collections.Generic.HashSet<int>();
        private System.Collections.Generic.Dictionary<int, int> _preselectedInv =
            new System.Collections.Generic.Dictionary<int, int>();

        public event EventHandler ServiceFinished;
        public event EventHandler CancelRequested;

        public ServiceCallDetailControl(Household household, string userRole)
        {
            InitializeComponent();

            if (household == null) throw new ArgumentNullException("household");
            _householdId = household.HouseholdID;
            _userRole = string.IsNullOrWhiteSpace(userRole) ? "User" : userRole;

            // Fill summary labels
            HHIdText.Text = household.HouseholdID.ToString();
            OwnerText.Text = household.OwnerName ?? "";
            UserText.Text = household.UserName ?? "";
            MunicipalityText.Text = household.Municipality ?? "";
            DistrictText.Text = household.District ?? "";
            ContactText.Text = household.ContactNum ?? "";
            StatusText.Text = string.IsNullOrWhiteSpace(household.Statuss) ? "Operational" : household.Statuss;

            // Permissions
            bool canProceed = string.Equals(_userRole, "Admin", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(_userRole, "Technician", StringComparison.OrdinalIgnoreCase);
            FinishBtn.IsEnabled = canProceed;
            if (!canProceed) FinishBtn.ToolTip = "Only Admin or Technician can finish a service call.";

            // Bind UI collections
            TechList.ItemsSource = _techAll;
            TechChipList.ItemsSource = _techSelected;

            InvList.ItemsSource = _invAll;
            SelectedInvList.ItemsSource = _invSelected;

            LoadOpenService();
            LoadTechnicians();
            LoadInventory();

            UpdateTechButton();
        }

        // ===== Loaders =====
        private void LoadOpenService()
        {
            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();

                using (var cmd1 = new SQLiteCommand(
                    "SELECT ServiceID, StartDate, Problem, Action FROM Service WHERE HouseholdID=@hh AND FinishDate IS NULL ORDER BY StartDate DESC LIMIT 1;", conn))
                {
                    cmd1.Parameters.AddWithValue("@hh", _householdId);
                    using (var r1 = cmd1.ExecuteReader())
                    {
                        if (!r1.Read())
                        {
                            MessageBox.Show("No open service ticket for this household.", "Service", MessageBoxButton.OK, MessageBoxImage.Information);
                            FinishBtn.IsEnabled = false;
                            return;
                        }

                        _serviceId = Convert.ToInt32(r1["ServiceID"]);
                        SvcIdText.Text = _serviceId.ToString();

                        DateTime dt;
                        DateTime.TryParse(r1["StartDate"] == DBNull.Value ? null : r1["StartDate"].ToString(), out dt);
                        _openedAt = dt;
                        OpenedAtText.Text = _openedAt == default(DateTime) ? "(unknown)" : _openedAt.ToString("yyyy-MM-dd HH:mm");
                        HeaderText.Text = "Service Call Details (Opened At: " + OpenedAtText.Text + ")";

                        ProblemBox.Text = r1["Problem"] == DBNull.Value ? "" : r1["Problem"].ToString();
                        ActionBox.Text = r1["Action"] == DBNull.Value ? "" : r1["Action"].ToString();
                    }
                }

                using (var cmd2 = new SQLiteCommand(
                    "SELECT TechnicianID FROM ServiceTechnicians WHERE ServiceID=@sid;", conn))
                {
                    cmd2.Parameters.AddWithValue("@sid", _serviceId);
                    using (var r2 = cmd2.ExecuteReader())
                    {
                        var set = new System.Collections.Generic.HashSet<int>();
                        while (r2.Read()) set.Add(Convert.ToInt32(r2["TechnicianID"]));
                        _preselectedTechIds = set;
                    }
                }

                using (var cmd3 = new SQLiteCommand(
                    "SELECT ItemID, QuantityUsed FROM ServiceInventory WHERE ServiceID=@sid;", conn))
                {
                    cmd3.Parameters.AddWithValue("@sid", _serviceId);
                    using (var r3 = cmd3.ExecuteReader())
                    {
                        var map = new System.Collections.Generic.Dictionary<int, int>();
                        while (r3.Read()) map[Convert.ToInt32(r3["ItemID"])] = Convert.ToInt32(r3["QuantityUsed"]);
                        _preselectedInv = map;
                    }
                }
            }
        }

        private void LoadTechnicians()
        {
            _techAll.Clear();
            _techSelected.Clear();

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("SELECT TechnicianID, Name FROM Technicians ORDER BY Name;", conn))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        int id = Convert.ToInt32(r["TechnicianID"]);
                        string name = r["Name"] == DBNull.Value ? ("Technician #" + id) : r["Name"].ToString();

                        var row = new TechRow
                        {
                            TechnicianID = id,
                            Name = name,
                            IsSelected = _preselectedTechIds.Contains(id)
                        };
                        _techAll.Add(row);
                        if (row.IsSelected) _techSelected.Add(row);
                    }
                }
            }
        }

        private void LoadInventory()
        {
            _invAll.Clear();
            _invSelected.Clear();

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();
                using (var cmd = new SQLiteCommand(
                    "SELECT ItemID, ItemType, TotalQuantity, UsedQuantity, LowStockThreshold FROM StockInventory ORDER BY ItemType;", conn))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        int id = Convert.ToInt32(r["ItemID"]);
                        string type = r["ItemType"] == DBNull.Value ? ("Item #" + id) : r["ItemType"].ToString();
                        int total = Convert.ToInt32(r["TotalQuantity"]);
                        int used = Convert.ToInt32(r["UsedQuantity"]);
                        int avail = Math.Max(0, total - used);

                        var row = new InvRow
                        {
                            ItemID = id,
                            ItemType = type,
                            Available = avail,
                            IsSelected = _preselectedInv.ContainsKey(id),
                            QuantityUsed = _preselectedInv.ContainsKey(id)
                                ? Math.Min(avail, Math.Max(1, _preselectedInv[id]))
                                : Math.Min(avail, 1)
                        };
                        _invAll.Add(row);
                        if (row.IsSelected) _invSelected.Add(row);
                    }
                }
            }
        }

        // ===== UI helpers =====
        private void UpdateTechButton()
        {
            OpenTechPickerBtn.Content = string.Format("Select Technicians ({0})", _techSelected.Count);
        }

        private void ShowValidation(string msg)
        {
            ValidationText.Text = msg;
            ValidationText.Visibility = Visibility.Visible;
        }

        private void HideValidation()
        {
            ValidationText.Text = "";
            ValidationText.Visibility = Visibility.Collapsed;
        }

        // ===== Technician overlay =====
        private void OpenTechPickerBtn_Click(object sender, RoutedEventArgs e)
        {
            TechOverlay.Visibility = Visibility.Visible;

            // Attach a view with filter
            ICollectionView view = CollectionViewSource.GetDefaultView(TechList.ItemsSource);
            if (view != null) view.Filter = null;
            TechSearchBox.Text = "";
        }

        private void TechPickerClose_Click(object sender, RoutedEventArgs e)
        {
            TechOverlay.Visibility = Visibility.Collapsed;
        }

        private void TechPickerSave_Click(object sender, RoutedEventArgs e)
        {
            // Sync selected list
            _techSelected.Clear();
            foreach (var t in _techAll.Where(x => x.IsSelected))
                _techSelected.Add(t);

            UpdateTechButton();
            TechOverlay.Visibility = Visibility.Collapsed;
        }

        private void TechSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string q = (TechSearchBox.Text ?? "").Trim().ToLowerInvariant();
            ICollectionView view = CollectionViewSource.GetDefaultView(TechList.ItemsSource);
            if (view == null) return;

            if (string.IsNullOrEmpty(q))
            {
                view.Filter = null;
            }
            else
            {
                view.Filter = delegate (object obj)
                {
                    TechRow t = obj as TechRow;
                    if (t == null) return false;
                    return (t.Name ?? "").ToLowerInvariant().Contains(q);
                };
            }
        }

        private void RemoveTechChip_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            if (btn == null) return;
            TechRow row = btn.Tag as TechRow;
            if (row == null) return;

            row.IsSelected = false;
            _techSelected.Remove(row);
            UpdateTechButton();
        }

        // ===== Inventory overlay =====
        private void OpenInvPickerBtn_Click(object sender, RoutedEventArgs e)
        {
            InvOverlay.Visibility = Visibility.Visible;

            ICollectionView view = CollectionViewSource.GetDefaultView(InvList.ItemsSource);
            if (view != null) view.Filter = null;
            InvSearchBox.Text = "";
        }

        private void InvPickerClose_Click(object sender, RoutedEventArgs e)
        {
            InvOverlay.Visibility = Visibility.Collapsed;
        }

        private void InvPickerAdd_Click(object sender, RoutedEventArgs e)
        {
            // Update selected collection to reflect IsSelected in _invAll
            // Add new
            foreach (var item in _invAll.Where(x => x.IsSelected))
            {
                if (!_invSelected.Any(s => s.ItemID == item.ItemID))
                {
                    _invSelected.Add(item);
                }
            }
            // Remove deselected
            for (int i = _invSelected.Count - 1; i >= 0; i--)
            {
                if (!_invAll.First(x => x.ItemID == _invSelected[i].ItemID).IsSelected)
                    _invSelected.RemoveAt(i);
            }

            InvOverlay.Visibility = Visibility.Collapsed;
        }

        private void InvSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string q = (InvSearchBox.Text ?? "").Trim().ToLowerInvariant();
            ICollectionView view = CollectionViewSource.GetDefaultView(InvList.ItemsSource);
            if (view == null) return;

            if (string.IsNullOrEmpty(q))
            {
                view.Filter = null;
            }
            else
            {
                view.Filter = delegate (object obj)
                {
                    InvRow r = obj as InvRow;
                    if (r == null) return false;
                    return (r.ItemType ?? "").ToLowerInvariant().Contains(q);
                };
            }
        }

        private void InvPickerMinus_Click(object sender, RoutedEventArgs e)
        {
            Button b = sender as Button;
            if (b == null) return;
            InvRow row = b.Tag as InvRow;
            if (row == null) return;
            row.QuantityUsed = Math.Max(1, row.QuantityUsed - 1);
        }

        private void InvPickerPlus_Click(object sender, RoutedEventArgs e)
        {
            Button b = sender as Button;
            if (b == null) return;
            InvRow row = b.Tag as InvRow;
            if (row == null) return;
            row.QuantityUsed = Math.Min(row.Available, row.QuantityUsed + 1);
        }

        // Selected list stepper
        private void QtyMinus_Click(object sender, RoutedEventArgs e)
        {
            Button b = sender as Button;
            if (b == null) return;
            InvRow row = b.Tag as InvRow;
            if (row == null) return;
            row.QuantityUsed = Math.Max(1, row.QuantityUsed - 1);
        }

        private void QtyPlus_Click(object sender, RoutedEventArgs e)
        {
            Button b = sender as Button;
            if (b == null) return;
            InvRow row = b.Tag as InvRow;
            if (row == null) return;
            row.QuantityUsed = Math.Min(row.Available, row.QuantityUsed + 1);
        }

        private void RemoveInv_Click(object sender, RoutedEventArgs e)
        {
            Button b = sender as Button;
            if (b == null) return;
            InvRow row = b.Tag as InvRow;
            if (row == null) return;

            // Uncheck in master list and remove from selected
            InvRow master = _invAll.FirstOrDefault(x => x.ItemID == row.ItemID);
            if (master != null) master.IsSelected = false;
            _invSelected.Remove(row);
        }

        // ===== Actions =====
        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            var h = CancelRequested;
            if (h != null) h(this, EventArgs.Empty);
        }

        private void FinishBtn_Click(object sender, RoutedEventArgs e)
        {
            HideValidation();

            if (_serviceId == 0)
            {
                MessageBox.Show("No open service ticket found.", "Service", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Validate inventory quantities
            foreach (var i in _invSelected)
            {
                if (i.QuantityUsed < 1)
                {
                    ShowValidation("Quantity for \"" + i.ItemType + "\" must be at least 1.");
                    return;
                }
                if (i.QuantityUsed > i.Available)
                {
                    ShowValidation("Not enough stock for \"" + i.ItemType + "\". Available: " + i.Available);
                    return;
                }
            }

            string problem = (ProblemBox.Text ?? "").Trim();
            string action = (ActionBox.Text ?? "").Trim();

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        // Update base Service (close it)
                        using (var cmd = new SQLiteCommand(
                            "UPDATE Service SET Problem=@p, Action=@a, FinishDate=datetime('now') WHERE ServiceID=@sid;", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@p", (object)problem ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@a", (object)action ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@sid", _serviceId);
                            cmd.ExecuteNonQuery();
                        }

                        // Technicians (reset then insert)
                        using (var cmdDelTech = new SQLiteCommand("DELETE FROM ServiceTechnicians WHERE ServiceID=@sid;", conn, tx))
                        {
                            cmdDelTech.Parameters.AddWithValue("@sid", _serviceId);
                            cmdDelTech.ExecuteNonQuery();
                        }
                        foreach (var t in _techSelected)
                        {
                            using (var cmdInsTech = new SQLiteCommand(
                                "INSERT OR IGNORE INTO ServiceTechnicians (ServiceID, TechnicianID) VALUES (@sid,@tid);", conn, tx))
                            {
                                cmdInsTech.Parameters.AddWithValue("@sid", _serviceId);
                                cmdInsTech.Parameters.AddWithValue("@tid", t.TechnicianID);
                                cmdInsTech.ExecuteNonQuery();
                            }
                        }

                        // Inventory (reset then insert + deduct)
                        using (var cmdDelInv = new SQLiteCommand("DELETE FROM ServiceInventory WHERE ServiceID=@sid;", conn, tx))
                        {
                            cmdDelInv.Parameters.AddWithValue("@sid", _serviceId);
                            cmdDelInv.ExecuteNonQuery();
                        }

                        foreach (var inv in _invSelected)
                        {
                            // Recheck availability now
                            int currentAvail;
                            using (var check = new SQLiteCommand(
                                "SELECT (TotalQuantity-UsedQuantity) FROM StockInventory WHERE ItemID=@id;", conn, tx))
                            {
                                check.Parameters.AddWithValue("@id", inv.ItemID);
                                object obj = check.ExecuteScalar();
                                currentAvail = Convert.ToInt32(obj);
                            }
                            if (inv.QuantityUsed > currentAvail)
                                throw new InvalidOperationException("Insufficient stock for \"" + inv.ItemType + "\". Available now: " + currentAvail);

                            using (var cmdInsInv = new SQLiteCommand(
                                "INSERT INTO ServiceInventory (ServiceID, ItemID, QuantityUsed) VALUES (@sid,@iid,@q);", conn, tx))
                            {
                                cmdInsInv.Parameters.AddWithValue("@sid", _serviceId);
                                cmdInsInv.Parameters.AddWithValue("@iid", inv.ItemID);
                                cmdInsInv.Parameters.AddWithValue("@q", inv.QuantityUsed);
                                cmdInsInv.ExecuteNonQuery();
                            }

                            using (var cmdUpdStock = new SQLiteCommand(
                                "UPDATE StockInventory SET UsedQuantity = UsedQuantity + @q WHERE ItemID=@iid;", conn, tx))
                            {
                                cmdUpdStock.Parameters.AddWithValue("@q", inv.QuantityUsed);
                                cmdUpdStock.Parameters.AddWithValue("@iid", inv.ItemID);
                                cmdUpdStock.ExecuteNonQuery();
                            }
                        }

                        // Move household back to Operational
                        using (var cmdUpdHh = new SQLiteCommand(
                            "UPDATE Households SET Statuss='Operational' WHERE HouseholdID=@hh;", conn, tx))
                        {
                            cmdUpdHh.Parameters.AddWithValue("@hh", _householdId);
                            cmdUpdHh.ExecuteNonQuery();
                        }

                        tx.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to finish service.\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            MessageBox.Show("Service call finished and inventory updated.", "Service", MessageBoxButton.OK, MessageBoxImage.Information);
            var h = ServiceFinished;
            if (h != null) h(this, EventArgs.Empty);
        }
    }
}
