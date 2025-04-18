﻿using System;
using System.Data.SQLite;
using System.Windows;

namespace HouseholdMS.View
{
    public partial class AddTechnicianWindow : Window
    {
        public bool Saved { get; private set; } = false;

        public AddTechnicianWindow()
        {
            InitializeComponent();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            string name = NameBox.Text.Trim();
            string contact = ContactBox.Text.Trim();
            string area = AreaBox.Text.Trim();

            // ✅ Validate input fields
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(contact) ||
                string.IsNullOrWhiteSpace(area))
            {
                MessageBox.Show("Please fill in all fields.", "Input Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // ✅ Insert technician into database
                using (var conn = DatabaseHelper.GetConnection())
                {
                    conn.Open();
                    var cmd = new SQLiteCommand(@"
                        INSERT INTO Technicians (Name, ContactNum, Address, AssignedArea)
                        VALUES (@name, @contact, '', @area)", conn);

                    cmd.Parameters.AddWithValue("@name", name);
                    cmd.Parameters.AddWithValue("@contact", contact);
                    cmd.Parameters.AddWithValue("@area", area);
                    cmd.ExecuteNonQuery();
                }

                Saved = true;
                MessageBox.Show("Technician added successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving technician:\n{ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
