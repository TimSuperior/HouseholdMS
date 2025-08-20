using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SQLite;
using System.Threading.Tasks;

namespace HouseholdMS.Services
{
    /// <summary>
    /// One class for all quick-lookups used around the app (Households, Technicians, etc.).
    /// No MVVM: call from code-behind. Thread-safe per call.
    /// </summary>
    public sealed class LookupSearch
    {
        private readonly Func<SQLiteConnection> _connectionFactory;

        public LookupSearch(Func<SQLiteConnection> connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        // ------------ Public DTOs ------------
        public sealed class PickItem { public int Id { get; set; } public string Display { get; set; } }

        public sealed class HouseRow
        {
            public int Id;
            public string OwnerName;
            public string ContactNum;
            public string Municipality;
            public string District;
            public string UserName;

            public PickItem ToPick() => new PickItem
            {
                Id = Id,
                Display = $"{OwnerName} · {ContactNum} · {Municipality}/{District} · {UserName}"
            };
        }

        public sealed class TechRow
        {
            public int Id;
            public string Name;
            public string ContactNum;
            public string Address;

            public PickItem ToPick() => new PickItem
            {
                Id = Id,
                Display = $"{Name} · {ContactNum} · {Address}"
            };
        }

        // ------------ Household search ------------
        public async Task<List<HouseRow>> SearchHouseholdsAsync(string query, int limit = 50)
        {
            query = (query ?? "").Trim();
            var list = new List<HouseRow>();
            if (query.Length == 0) return list;

            using (var conn = _connectionFactory())
            {
                await conn.OpenAsync().ConfigureAwait(false);

                using (var cmd = new SQLiteCommand(@"
SELECT HouseholdID, OwnerName, ContactNum, Municipality, District, UserName
FROM Households
WHERE OwnerName    LIKE @q
   OR ContactNum   LIKE @q
   OR Municipality LIKE @q
   OR District     LIKE @q
   OR UserName     LIKE @q
ORDER BY OwnerName
LIMIT @lim;", conn))
                {
                    cmd.Parameters.AddWithValue("@q", "%" + query + "%");
                    cmd.Parameters.AddWithValue("@lim", limit);

                    using (DbDataReader rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        while (await rd.ReadAsync().ConfigureAwait(false))
                        {
                            list.Add(new HouseRow
                            {
                                Id = rd.GetInt32(0),
                                OwnerName = rd.GetString(1),
                                ContactNum = SafeGet(rd, 2),
                                Municipality = SafeGet(rd, 3),
                                District = SafeGet(rd, 4),
                                UserName = SafeGet(rd, 5)
                            });
                        }
                    }
                }
            }
            return list;
        }

        // ------------ Technician search ------------
        public async Task<List<TechRow>> SearchTechniciansAsync(string query, int limit = 50)
        {
            query = (query ?? "").Trim();
            var list = new List<TechRow>();
            if (query.Length == 0) return list;

            using (var conn = _connectionFactory())
            {
                await conn.OpenAsync().ConfigureAwait(false);

                using (var cmd = new SQLiteCommand(@"
SELECT TechnicianID, Name, ContactNum, Address
FROM Technicians
WHERE Name       LIKE @q
   OR ContactNum LIKE @q
   OR Address    LIKE @q
ORDER BY Name
LIMIT @lim;", conn))
                {
                    cmd.Parameters.AddWithValue("@q", "%" + query + "%");
                    cmd.Parameters.AddWithValue("@lim", limit);

                    using (DbDataReader rd = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        while (await rd.ReadAsync().ConfigureAwait(false))
                        {
                            list.Add(new TechRow
                            {
                                Id = rd.GetInt32(0),
                                Name = rd.GetString(1),
                                ContactNum = SafeGet(rd, 2),
                                Address = SafeGet(rd, 3)
                            });
                        }
                    }
                }
            }
            return list;
        }

        // ------------ Convenience helpers ------------
        public async Task<List<PickItem>> SearchHouseholdPicksAsync(string query, int limit = 50)
        {
            var rows = await SearchHouseholdsAsync(query, limit).ConfigureAwait(false);
            var items = new List<PickItem>(rows.Count);
            foreach (var r in rows) items.Add(r.ToPick());
            return items;
        }

        public async Task<List<PickItem>> SearchTechnicianPicksAsync(string query, int limit = 50)
        {
            var rows = await SearchTechniciansAsync(query, limit).ConfigureAwait(false);
            var items = new List<PickItem>(rows.Count);
            foreach (var r in rows) items.Add(r.ToPick());
            return items;
        }

        // Use DbDataReader so ExecuteReaderAsync() fits without casts
        private static string SafeGet(DbDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetString(i);
    }
}
