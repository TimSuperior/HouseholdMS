using System.Windows;

namespace HouseholdMS.View
{
    public partial class InputDialogWindow : Window
    {
        public int? Quantity { get; private set; }

        public InputDialogWindow(string prompt = "Enter quantity:")
        {
            InitializeComponent();
            PromptText.Text = prompt;
            this.Loaded += (s, e) => QuantityBox.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(QuantityBox.Text.Trim(), out int value) && value > 0)
            {
                Quantity = value;
                this.DialogResult = true;
                this.Close();
            }
            else
            {
                MessageBox.Show("Please enter a valid positive number.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                QuantityBox.Focus();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
