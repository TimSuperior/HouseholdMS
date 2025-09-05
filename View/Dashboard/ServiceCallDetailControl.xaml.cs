using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using HouseholdMS.Model;

namespace HouseholdMS.View.Dashboard
{
    public partial class ServiceCallDetailControl : UserControl
    {
        // ===== Row models =====
        public class TechRow : INotifyPropertyChanged
        {
            public int TechnicianID { get; set; }
            public string Name { get; set; }

            private bool _isSelected;
            public bool IsSelected
            {
                get { return _isSelected; }
                set
                {
                    if (_isSelected == value) return;
                    _isSelected = value;
                    OnPropertyChanged("IsSelected");
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged(string name)
            {
                var h = PropertyChanged;
                if (h != null) h(this, new PropertyChangedEventArgs(name));
            }
        }

        public class InvRow : INotifyPropertyChanged
        {
            public int ItemID { get; set; }
            public string ItemType { get; set; }

            private int _available;
            public int Available
            {
                get { return _available; }
                set
                {
                    _available = value;
                    OnPropertyChanged("Available");
                    OnPropertyChanged("CanSelect");
                    OnPropertyChanged("StockBadgeText");
                    OnPropertyChanged("StockBadgeBrush");
                    if (_available <= 0)
                    {
                        QuantityUsed = 0;
                        IsSelected = false;
                    }
                }
            }

            public bool CanSelect { get { return Available > 0; } }

            private bool _isSelected;
            public bool IsSelected
            {
                get { return _isSelected; }
                set
                {
                    bool newVal = CanSelect && value;
                    if (_isSelected == newVal) return;
                    _isSelected = newVal;
                    OnPropertyChanged("IsSelected");
                }
            }

            private int _quantityUsed = 1;
            public int QuantityUsed
            {
                get { return _quantityUsed; }
                set
                {
                    int v = value;
                    if (!CanSelect) v = 0;
                    else
                    {
                        if (v < 1) v = 1;
                        if (v > Available) v = Available;
                    }
                    if (_quantityUsed == v) return;
                    _quantityUsed = v;
                    OnPropertyChanged("QuantityUsed");
                }
            }

            // Badge visuals
            private static readonly SolidColorBrush BadgeGreen = Make("#D1FAE5");
            private static readonly SolidColorBrush BadgeYellow = Make("#FEF3C7");
            private static readonly SolidColorBrush BadgeRed = Make("#FEE2E2");
            private static SolidColorBrush Make(string hex)
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                var b = new SolidColorBrush(c); b.Freeze(); return b;
            }

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
                    if (Available <= 0) return BadgeRed;
                    if (Available <= 2) return BadgeYellow;
                    return BadgeGreen;
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged(string name)
            {
                var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(name));
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

        private HashSet<int> _preselectedTechIds = new HashSet<int>();
        private Dictionary<int, int> _preselectedInv = new Dictionary<int, int>();

        public event EventHandler ServiceFinished;

        public ServiceCallDetailControl(Household household, string userRole)
        {
            InitializeComponent();

            if (household == null) throw new ArgumentNullException("household");
            _householdId = household.HouseholdID;
            _userRole = string.IsNullOrWhiteSpace(userRole) ? "User" : userRole;

            // Summary
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
            SaveOpenBtn.IsEnabled = canProceed;
            if (!canProceed)
            {
                FinishBtn.ToolTip = "Only Admin or Technician can finish a service call.";
                SaveOpenBtn.ToolTip = "Only Admin or Technician can save a service call.";
            }

            // Bind UI collections
            TechList.ItemsSource = _techAll;
            TechChipList.ItemsSource = _techSelected;

            InvList.ItemsSource = _invAll;
            SelectedInvList.ItemsSource = _invSelected;

            LoadOpenService();
            LoadTechnicians();
            LoadInventory();

            UpdateTechButton();
            UpdateTechOverlayHeader();
            UpdateInvOverlayHeader();

            // Initial filters
            ApplyTechFilter();
            ApplyInvFilter();
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
                            SaveOpenBtn.IsEnabled = false;
                            return;
                        }

                        _serviceId = Convert.ToInt32(r1["ServiceID"]);
                        SvcIdText.Text = _serviceId.ToString();

                        DateTime dt;
                        DateTime.TryParse(r1["StartDate"] == DBNull.Value ? null : r1["StartDate"].ToString(), out dt);
                        _openedAt = dt;
                        OpenedAtText.Text = _openedAt == default(DateTime) ? "(unknown)" : _openedAt.ToString("yyyy-MM-dd HH:mm");
                        HeaderText.Text = "Service Call Details (Opened At: " + OpenedAtText.Text + ")";

                        var prevProblem = r1["Problem"] == DBNull.Value ? "" : r1["Problem"].ToString();
                        var prevAction = r1["Action"] == DBNull.Value ? "" : r1["Action"].ToString();

                        SetPrevBlock(ProblemPrevBox, ProblemPrevLabel, prevProblem, "Initial problem");
                        SetPrevBlock(ActionPrevBox, ActionPrevLabel, prevAction, "Initial action");

                        ProblemNewBox.Text = "";
                        ActionNewBox.Text = "";
                    }
                }

                using (var cmd2 = new SQLiteCommand(
                    "SELECT TechnicianID FROM ServiceTechnicians WHERE ServiceID=@sid;", conn))
                {
                    cmd2.Parameters.AddWithValue("@sid", _serviceId);
                    using (var r2 = cmd2.ExecuteReader())
                    {
                        var set = new HashSet<int>();
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
                        var map = new Dictionary<int, int>();
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

            UpdateTechButton();
            UpdateTechOverlayHeader();
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
                            IsSelected = (avail > 0) && _preselectedInv.ContainsKey(id),
                            QuantityUsed = (avail > 0)
                                ? Math.Min(avail, Math.Max(1, _preselectedInv.ContainsKey(id) ? _preselectedInv[id] : 1))
                                : 0
                        };
                        _invAll.Add(row);
                        if (row.IsSelected) _invSelected.Add(row);
                    }
                }
            }

            UpdateInvOverlayHeader();
        }

        // ===== UI helpers =====
        private void UpdateTechButton()
        {
            OpenTechPickerBtn.Content = string.Format("Select Technicians ({0})", _techSelected.Count);
        }

        private void UpdateTechOverlayHeader()
        {
            int sel = _techAll.Count(t => t.IsSelected);
            if (TechHeaderCountText != null) TechHeaderCountText.Text = "(" + sel + " selected)";
        }

        private void UpdateInvOverlayHeader()
        {
            int sel = _invAll.Count(i => i.IsSelected && i.CanSelect);
            if (InvHeaderCountText != null) InvHeaderCountText.Text = "(" + sel + " selected)";
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

        // ===== Previous/Initial block helpers =====
        private static bool LooksLikeMerged(string content)
        {
            var t = (content ?? "").TrimStart();
            return t.StartsWith("*previous*", StringComparison.OrdinalIgnoreCase)
                   || t.StartsWith("*update", StringComparison.OrdinalIgnoreCase);
        }

        private static string PickPrevLabel(string content, string initialLabel)
        {
            return LooksLikeMerged(content) ? "Previous" : initialLabel;
        }

        private static void SetPrevBlock(TextBox box, TextBlock label, string text, string initialLabel)
        {
            var trimmed = (text ?? "").Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                label.Visibility = Visibility.Collapsed;
                box.Visibility = Visibility.Collapsed;
                box.Text = "";
            }
            else
            {
                label.Text = PickPrevLabel(trimmed, initialLabel);
                label.Visibility = Visibility.Visible;
                box.Visibility = Visibility.Visible;
                box.Text = trimmed;
            }
        }

        // ===== Filters =====
        private void ApplyTechFilter()
        {
            string q = (TechSearchBox == null ? "" : (TechSearchBox.Text ?? "")).Trim().ToLowerInvariant();
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

        private void ApplyInvFilter()
        {
            string q = (InvSearchBox == null ? "" : (InvSearchBox.Text ?? "")).Trim().ToLowerInvariant();
            bool inStockOnly = (InStockOnlyCheck != null) && InStockOnlyCheck.IsChecked == true;

            ICollectionView view = CollectionViewSource.GetDefaultView(InvList.ItemsSource);
            if (view == null) return;

            view.Filter = delegate (object obj)
            {
                InvRow r = obj as InvRow;
                if (r == null) return false;

                if (inStockOnly && !r.CanSelect) return false;
                if (!string.IsNullOrEmpty(q) && !(r.ItemType ?? "").ToLowerInvariant().Contains(q)) return false;

                return true;
            };
        }

        // ===== Technician overlay =====
        private void OpenTechPickerBtn_Click(object sender, RoutedEventArgs e)
        {
            TechOverlay.Visibility = Visibility.Visible;
            ApplyTechFilter();
            UpdateTechOverlayHeader();
        }

        private void TechPickerClose_Click(object sender, RoutedEventArgs e)
        {
            TechOverlay.Visibility = Visibility.Collapsed;
        }

        private void TechPickerSave_Click(object sender, RoutedEventArgs e)
        {
            _techSelected.Clear();
            foreach (var t in _techAll.Where(x => x.IsSelected))
                _techSelected.Add(t);

            UpdateTechButton();
            TechOverlay.Visibility = Visibility.Collapsed;
        }

        private void TechSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyTechFilter();
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
            UpdateTechOverlayHeader();
        }

        private void TechSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var t in _techAll) t.IsSelected = true;
            UpdateTechOverlayHeader();
        }

        private void TechClear_Click(object sender, RoutedEventArgs e)
        {
            foreach (var t in _techAll) t.IsSelected = false;
            UpdateTechOverlayHeader();
        }

        private void ClearTechSelection_Click(object sender, RoutedEventArgs e)
        {
            foreach (var t in _techAll) t.IsSelected = false;
            _techSelected.Clear();
            UpdateTechButton();
            UpdateTechOverlayHeader();
        }

        // ===== Inventory overlay =====
        private void OpenInvPickerBtn_Click(object sender, RoutedEventArgs e)
        {
            InvOverlay.Visibility = Visibility.Visible;
            ApplyInvFilter();
            UpdateInvOverlayHeader();
        }

        private void InvPickerClose_Click(object sender, RoutedEventArgs e)
        {
            InvOverlay.Visibility = Visibility.Collapsed;
        }

        private void InvPickerAdd_Click(object sender, RoutedEventArgs e)
        {
            _invSelected.Clear();
            foreach (var item in _invAll.Where(x => x.CanSelect && x.IsSelected))
            {
                _invSelected.Add(item);
            }
            InvOverlay.Visibility = Visibility.Collapsed;
        }

        private void InvSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyInvFilter();
        }

        private void InStockOnlyCheck_Changed(object sender, RoutedEventArgs e)
        {
            ApplyInvFilter();
        }

        private void InvPickerMinus_Click(object sender, RoutedEventArgs e)
        {
            Button b = sender as Button;
            if (b == null) return;
            InvRow row = b.Tag as InvRow;
            if (row == null || !row.CanSelect) return;
            row.QuantityUsed = Math.Max(1, row.QuantityUsed - 1);
        }

        private void InvPickerPlus_Click(object sender, RoutedEventArgs e)
        {
            Button b = sender as Button;
            if (b == null) return;
            InvRow row = b.Tag as InvRow;
            if (row == null || !row.CanSelect) return;
            row.QuantityUsed = Math.Min(row.Available, row.QuantityUsed + 1);
        }

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

            InvRow master = _invAll.FirstOrDefault(x => x.ItemID == row.ItemID);
            if (master != null) master.IsSelected = false;
            _invSelected.Remove(row);
            UpdateInvOverlayHeader();
        }

        private void InvSelectAllInStock_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _invAll) r.IsSelected = r.CanSelect;
            UpdateInvOverlayHeader();
        }

        private void InvClear_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _invAll) r.IsSelected = false;
            UpdateInvOverlayHeader();
        }

        private void ClearInvSelection_Click(object sender, RoutedEventArgs e)
        {
            foreach (var r in _invAll) r.IsSelected = false;
            _invSelected.Clear();
            UpdateInvOverlayHeader();
        }

        // ===== Tap-to-open quantity popup =====
        private void ListItem_Toggle_OnClick(object sender, MouseButtonEventArgs e)
        {
            var lbi = sender as ListBoxItem;
            if (lbi == null) return;

            var tech = lbi.DataContext as TechRow;
            if (tech != null)
            {
                tech.IsSelected = !tech.IsSelected;
                UpdateTechOverlayHeader();
                return;
            }

            var inv = lbi.DataContext as InvRow;
            if (inv != null)
            {
                if (inv.CanSelect)
                {
                    inv.IsSelected = true;
                    OpenQtyPopup(inv, lbi);
                }
                UpdateInvOverlayHeader();
            }
        }

        private void ListItem_KeyDown_Toggle(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space && e.Key != Key.Enter) return;

            var lbi = sender as ListBoxItem;
            if (lbi == null) return;

            var tech = lbi.DataContext as TechRow;
            if (tech != null)
            {
                tech.IsSelected = !tech.IsSelected;
                UpdateTechOverlayHeader();
                e.Handled = true;
                return;
            }

            var inv = lbi.DataContext as InvRow;
            if (inv != null)
            {
                if (inv.CanSelect)
                {
                    inv.IsSelected = true;
                    OpenQtyPopup(inv, lbi);
                }
                UpdateInvOverlayHeader();
                e.Handled = true;
            }
        }

        private void OpenQtyPopup(InvRow row, FrameworkElement target)
        {
            if (row == null || target == null) return;
            if (row.QuantityUsed < 1 && row.CanSelect) row.QuantityUsed = 1;
            if (row.QuantityUsed > row.Available) row.QuantityUsed = row.Available;

            QtyPopup.DataContext = row;
            QtyPopup.PlacementTarget = target;
            QtyPopup.IsOpen = true;
        }

        private void QtyPreset_Click(object sender, RoutedEventArgs e)
        {
            var row = QtyPopup.DataContext as InvRow;
            if (row == null) return;
            var btn = sender as Button;
            if (btn == null) return;

            int val;
            if (int.TryParse((btn.Tag ?? "").ToString(), out val))
                row.QuantityUsed = Math.Min(Math.Max(1, val), row.Available);
        }

        private void QtyPopupMinus_Click(object sender, RoutedEventArgs e)
        {
            var row = QtyPopup.DataContext as InvRow;
            if (row == null || !row.CanSelect) return;
            row.QuantityUsed = Math.Max(1, row.QuantityUsed - 1);
        }

        private void QtyPopupPlus_Click(object sender, RoutedEventArgs e)
        {
            var row = QtyPopup.DataContext as InvRow;
            if (row == null || !row.CanSelect) return;
            row.QuantityUsed = Math.Min(row.Available, row.QuantityUsed + 1);
        }

        private void QtyPopup_OK_Click(object sender, RoutedEventArgs e)
        {
            QtyPopup.IsOpen = false;
        }

        private void QtyPopup_Cancel_Click(object sender, RoutedEventArgs e)
        {
            var row = QtyPopup.DataContext as InvRow;
            if (row != null && row.QuantityUsed <= 0) row.IsSelected = false;
            QtyPopup.IsOpen = false;
        }

        // ===== Confirm helper for missing selections =====
        private bool ConfirmProceedIfMissing()
        {
            var missing = new List<string>();
            if (_techSelected.Count == 0) missing.Add("technicians");
            if (_invSelected.Count == 0) missing.Add("inventory items");

            if (missing.Count == 0) return true;

            string msg;
            if (missing.Count == 2)
                msg = "No technicians and no inventory items are selected.\n\nDo you still want to proceed?";
            else
                msg = "No " + missing[0] + " are selected.\n\nDo you still want to proceed?";

            var res = MessageBox.Show(msg, "Proceed without selections?", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            return res == MessageBoxResult.Yes;
        }

        // ===== Merge helpers =====
        private static string MergeWithPrevious(string existing, string addition)
        {
            var add = (addition ?? "").Trim();
            var prev = (existing ?? "").Trim();
            if (string.IsNullOrEmpty(add)) return existing; // nothing new

            // First notes → just store the text (no headers)
            if (string.IsNullOrEmpty(prev))
                return add;

            bool alreadyTagged = prev.TrimStart().StartsWith("*previous*", StringComparison.OrdinalIgnoreCase);

            string prefixPrev = alreadyTagged ? (prev + "\n\n") : ("*previous*\n" + prev + "\n\n");
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            var block = "*update " + stamp + "*\n" + add;

            return prefixPrev + block;
        }

        // ===== Actions =====
        private void SaveOpenBtn_Click(object sender, RoutedEventArgs e)
        {
            HideValidation();

            if (_serviceId == 0)
            {
                MessageBox.Show("No open service ticket found.", "Service", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!ConfirmProceedIfMissing()) return;

            string mergedProblem = MergeWithPrevious(ProblemPrevBox.Text, ProblemNewBox.Text);
            string mergedAction = MergeWithPrevious(ActionPrevBox.Text, ActionNewBox.Text);

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        // Persist notes (keep ticket open)
                        using (var cmd = new SQLiteCommand(
                            "UPDATE Service SET Problem=@p, Action=@a WHERE ServiceID=@sid;", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@p", (object)mergedProblem ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@a", (object)mergedAction ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@sid", _serviceId);
                            cmd.ExecuteNonQuery();
                        }

                        // Update tech links
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

                        // Update inventory records (no deduction yet)
                        using (var cmdDelInv = new SQLiteCommand("DELETE FROM ServiceInventory WHERE ServiceID=@sid;", conn, tx))
                        {
                            cmdDelInv.Parameters.AddWithValue("@sid", _serviceId);
                            cmdDelInv.ExecuteNonQuery();
                        }
                        foreach (var inv in _invSelected)
                        {
                            using (var cmdInsInv = new SQLiteCommand(
                                "INSERT INTO ServiceInventory (ServiceID, ItemID, QuantityUsed) VALUES (@sid,@iid,@q);", conn, tx))
                            {
                                cmdInsInv.Parameters.AddWithValue("@sid", _serviceId);
                                cmdInsInv.Parameters.AddWithValue("@iid", inv.ItemID);
                                cmdInsInv.Parameters.AddWithValue("@q", inv.QuantityUsed);
                                cmdInsInv.ExecuteNonQuery();
                            }
                        }

                        // Keep household Out of Service (DB label "In Service")
                        using (var cmdUpdHh = new SQLiteCommand(
                            "UPDATE Households SET Statuss='In Service' WHERE HouseholdID=@hh;", conn, tx))
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
                MessageBox.Show("Failed to save service (kept open).\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Refresh previous blocks and clear new input
            SetPrevBlock(ProblemPrevBox, ProblemPrevLabel, mergedProblem, "Initial problem");
            SetPrevBlock(ActionPrevBox, ActionPrevLabel, mergedAction, "Initial action");
            ProblemNewBox.Text = "";
            ActionNewBox.Text = "";

            MessageBox.Show("Saved. Ticket remains open. No stock deducted.", "Service", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void FinishBtn_Click(object sender, RoutedEventArgs e)
        {
            HideValidation();

            if (_serviceId == 0)
            {
                MessageBox.Show("No open service ticket found.", "Service", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!ConfirmProceedIfMissing()) return;

            // Validate quantities
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

            string mergedProblem = MergeWithPrevious(ProblemPrevBox.Text, ProblemNewBox.Text);
            string mergedAction = MergeWithPrevious(ActionPrevBox.Text, ActionNewBox.Text);

            try
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    using (var tx = conn.BeginTransaction())
                    {
                        using (var cmd = new SQLiteCommand(
                            "UPDATE Service SET Problem=@p, Action=@a, FinishDate=datetime('now') WHERE ServiceID=@sid;", conn, tx))
                        {
                            cmd.Parameters.AddWithValue("@p", (object)mergedProblem ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@a", (object)mergedAction ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("@sid", _serviceId);
                            cmd.ExecuteNonQuery();
                        }

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

                        using (var cmdDelInv = new SQLiteCommand("DELETE FROM ServiceInventory WHERE ServiceID=@sid;", conn, tx))
                        {
                            cmdDelInv.Parameters.AddWithValue("@sid", _serviceId);
                            cmdDelInv.ExecuteNonQuery();
                        }

                        // Deduct stock for selected items
                        foreach (var inv in _invSelected)
                        {
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

            MessageBox.Show("Service finished. Household set to Operational and inventory updated.", "Service", MessageBoxButton.OK, MessageBoxImage.Information);

            var h = ServiceFinished; if (h != null) h(this, EventArgs.Empty);
        }
    }
}
