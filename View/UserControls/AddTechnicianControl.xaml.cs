using System;
using System.Data.SQLite;
using System.Windows;
using System.Windows.Controls;

namespace HouseholdMS.View.UserControls
{
    public partial class AddTechnicianControl : UserControl
    {
        private readonly Technician _technician;
        private readonly bool isEditMode;

        public event EventHandler OnSavedSuccessfully;
        public event EventHandler OnCancelRequested;

        public AddTechnicianControl()
        {
            InitializeComponent();
            FormHeader.Text = "➕ Add Technician";
            SaveButton.Content = "➕ Add";
        }

        public AddTechnicianControl(Technician techToEdit) : this()
        {
            _technician = techToEdit ?? throw new ArgumentNullException(nameof(techToEdit));
            isEditMode = true;

            FormHeader.Text = $"✏ Edit Technician #{_technician.TechnicianID}";
            SaveButton.Content = "✏ Save Changes";
            DeleteButton.Visibility = Visibility.Visible;

            NameBox.Text = _technician.Namee;
            ContactBox.Text = _technician.ContactNumm;
            AreaBox.Text = _technician.AssignedAreaa;
            AddressBox.Text = _technician.Addresss;
            NoteBox.Text = _technician.Notee;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string name = NameBox.Text.Trim();
            string contact = ContactBox.Text.Trim();
            string area = AreaBox.Text.Trim();
            string address = AddressBox.Text.Trim();
            string note = NoteBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(contact) ||
                string.IsNullOrWhiteSpace(area) ||
                string.IsNullOrWhiteSpace(address))
            {
                MessageBox.Show("Please fill in Name, Contact, Address, and Assigned Area.",
                                "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(ContactBox.Text, out _))
            {
                MessageBox.Show("Please enter valid contact number!", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (var conn = DatabaseHelper.GetConnection())
            {
                conn.Open();

                int? currentId = isEditMode ? _technician.TechnicianID : (int?)null;

                if (IsDuplicate(conn, "LOWER(Name)", name.ToLower(), currentId))
                {
                    MessageBox.Show("A technician with this name already exists.", "Duplicate Name",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (IsDuplicate(conn, "ContactNum", contact, currentId))
                {
                    MessageBox.Show("A technician with this contact number already exists.", "Duplicate Contact",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string query = isEditMode
                    ? @"UPDATE Technicians SET 
                            Name = @name,
                            ContactNum = @contact,
                            AssignedArea = @area,
                            Address = @address,
                            Note = @note
                        WHERE TechnicianID = @id"
                    : @"INSERT INTO Technicians (Name, ContactNum, AssignedArea, Address, Note)
                       VALUES (@name, @contact, @area, @address, @note)";

                using (var cmd = new SQLiteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@name", name);
                    cmd.Parameters.AddWithValue("@contact", contact);
                    cmd.Parameters.AddWithValue("@area", area);
                    cmd.Parameters.AddWithValue("@address", address);
                    cmd.Parameters.AddWithValue("@note", string.IsNullOrWhiteSpace(note) ? DBNull.Value : (object)note);

                    if (isEditMode)
                        cmd.Parameters.AddWithValue("@id", _technician.TechnicianID);

                    cmd.ExecuteNonQuery();
                }
            }

            MessageBox.Show(isEditMode ? "Technician updated successfully!" : "Technician added successfully!",
                            "Success", MessageBoxButton.OK, MessageBoxImage.Information);

            OnSavedSuccessfully?.Invoke(this, EventArgs.Empty);
        }

        private bool IsDuplicate(SQLiteConnection conn, string field, object value, int? ignoreId)
        {
            string query = $@"
                SELECT COUNT(*) FROM Technicians
                WHERE {field} = @value
                AND (@id IS NULL OR TechnicianID != @id)";

            using (var cmd = new SQLiteCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@value", value);
                cmd.Parameters.AddWithValue("@id", ignoreId ?? (object)DBNull.Value);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            OnCancelRequested?.Invoke(this, EventArgs.Empty);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (!isEditMode) return;

            var result = MessageBox.Show($"Are you sure you want to delete Technician '{_technician.Namee}'?",
                                         "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    var cmd = new SQLiteCommand("DELETE FROM Technicians WHERE TechnicianID = @id", conn);
                    cmd.Parameters.AddWithValue("@id", _technician.TechnicianID);
                    cmd.ExecuteNonQuery();
                }

                MessageBox.Show("Technician deleted successfully.", "Deleted", MessageBoxButton.OK, MessageBoxImage.Information);
                OnSavedSuccessfully?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
