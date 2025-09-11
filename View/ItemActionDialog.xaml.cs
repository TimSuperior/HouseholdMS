using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HouseholdMS.View
{
    public partial class ItemActionDialog : Window
    {
        public int? Quantity { get; private set; }
        public string Person { get; private set; }
        public string NoteOrReason { get; private set; }

        private readonly string _mode; // "Restock" or "Use"

        public ItemActionDialog(string mode, string itemType)
        {
            _mode = string.IsNullOrWhiteSpace(mode) ? "Action" : mode.Trim();
            InitializeComponent();

            HeaderText.Text = (_mode == "Restock") ? "Restock Item" : "Use Item";
            SubText.Text = string.IsNullOrWhiteSpace(itemType) ? "" : itemType;

            PersonLabel.Text = (_mode == "Restock") ? "Person Restocked" : "Person Using";
            NoteLabel.Text = (_mode == "Restock") ? "Restock Note" : "Reason of Usage";

            OkButton.Content = (_mode == "Restock") ? "✔ Restock" : "✔ Use";

            Loaded += (_, __) => QuantityBox.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(QuantityBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var q) || q <= 0)
            {
                MessageBox.Show("Please enter a valid positive number.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                QuantityBox.Focus();
                return;
            }

            Quantity = q;
            Person = string.IsNullOrWhiteSpace(PersonBox.Text) ? null : PersonBox.Text.Trim();
            NoteOrReason = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // numeric-only
        private static bool IsDigits(string s) => string.IsNullOrEmpty(s) || Regex.IsMatch(s, @"^\d+$");

        private void QuantityBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var tb = sender as TextBox;
            var proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, e.Text);
            e.Handled = !IsDigits(proposed);
        }

        private void QuantityBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.Text)) { e.CancelCommand(); return; }
            var paste = (string)e.SourceDataObject.GetData(DataFormats.Text);
            var tb = (TextBox)sender;
            var proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, paste);
            if (!IsDigits(proposed)) e.CancelCommand();
        }
    }
}
