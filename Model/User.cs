using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HouseholdMS.Models
{
    public class User
    {
        public int UserID { get; set; }          // Primary Key
        public string Name { get; set; }          // Full Name
        public string Username { get; set; }      // Unique username
        public string PasswordHash { get; set; }  // Stored password (can rename to just 'Password' if plain text)
        public string Role { get; set; }          // Admin, Technician, User
        public string Note { get; set; }          // Optional note for user (NEW)
    }
}

