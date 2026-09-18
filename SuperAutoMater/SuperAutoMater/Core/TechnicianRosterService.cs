using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SuperAutoMater.Core
{
    public class TechnicianMember
    {
        public string Id { get; set; } = "TECH-01";
        public string Name { get; set; } = "Lead Refurb Tech";
        public string Role { get; set; } = "General"; // Intake, Hardware, QC, Lead
        public bool IsActive { get; set; } = true;

        public string DisplayName => $"{Id} - {Name}";

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Central roster management service for technicians across SuperAutoMater and SuperManager.
    /// Provides dropdown lists for soft assignments, intake, service, and QC attribution.
    /// </summary>
    public class TechnicianRosterService
    {
        private static readonly Lazy<TechnicianRosterService> _instance =
            new Lazy<TechnicianRosterService>(() => new TechnicianRosterService());

        public static TechnicianRosterService Instance => _instance.Value;

        private const string RosterFileName = "technicians.json";
        private readonly object _lock = new object();
        private List<TechnicianMember> _members = new List<TechnicianMember>();

        public event Action RosterChanged;

        private TechnicianRosterService()
        {
            LoadRoster();
        }

        private string GetRosterPath()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string localPath = Path.Combine(baseDir, RosterFileName);
                if (File.Exists(localPath)) return localPath;

                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuperAutoMater");
                if (!Directory.Exists(appData)) Directory.CreateDirectory(appData);
                return Path.Combine(appData, RosterFileName);
            }
            catch
            {
                return RosterFileName;
            }
        }

        public void LoadRoster()
        {
            lock (_lock)
            {
                try
                {
                    string path = GetRosterPath();
                    if (File.Exists(path))
                    {
                        string json = File.ReadAllText(path);
                        var loaded = JsonSerializer.Deserialize<List<TechnicianMember>>(json, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (loaded != null && loaded.Count > 0)
                        {
                            _members = loaded;
                            RosterChanged?.Invoke();
                            return;
                        }
                    }
                }
                catch { }

                // Default seed team roster
                _members = new List<TechnicianMember>
                {
                    new TechnicianMember { Id = "TECH-01", Name = "Lead Refurb Tech", Role = "Lead" },
                    new TechnicianMember { Id = "TECH-02", Name = "Marcus Vance", Role = "Hardware" },
                    new TechnicianMember { Id = "TECH-03", Name = "Elena Rostova", Role = "QC" },
                    new TechnicianMember { Id = "TECH-04", Name = "Sarah Jenkins", Role = "Intake" },
                    new TechnicianMember { Id = "TECH-05", Name = "Joseph Vibhu", Role = "Lead" }
                };

                SaveRoster();
            }
        }

        public void SaveRoster()
        {
            lock (_lock)
            {
                try
                {
                    string path = GetRosterPath();
                    string json = JsonSerializer.Serialize(_members, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(path, json);
                }
                catch { }
            }
            RosterChanged?.Invoke();
        }

        public IReadOnlyList<TechnicianMember> GetAll()
        {
            lock (_lock)
            {
                return _members.Where(m => m.IsActive).ToList().AsReadOnly();
            }
        }

        public List<string> GetNamesList()
        {
            lock (_lock)
            {
                return _members.Where(m => m.IsActive).Select(m => m.Name).ToList();
            }
        }

        public List<string> GetDisplayList()
        {
            lock (_lock)
            {
                return _members.Where(m => m.IsActive).Select(m => m.DisplayName).ToList();
            }
        }

        public void AddOrUpdateTechnician(string id, string name, string role = "General")
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            string cleanName = name.Trim();
            string cleanId = string.IsNullOrWhiteSpace(id) ? $"TECH-{_members.Count + 1:D2}" : id.Trim().ToUpperInvariant();

            lock (_lock)
            {
                var existing = _members.FirstOrDefault(m =>
                    string.Equals(m.Id, cleanId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(m.Name, cleanName, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    existing.Name = cleanName;
                    existing.Role = role;
                    existing.IsActive = true;
                }
                else
                {
                    _members.Add(new TechnicianMember
                    {
                        Id = cleanId,
                        Name = cleanName,
                        Role = role,
                        IsActive = true
                    });
                }

                SaveRoster();
            }
        }

        public TechnicianMember FindByNameOrId(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;
            string q = query.Trim();

            lock (_lock)
            {
                return _members.FirstOrDefault(m =>
                    string.Equals(m.Id, q, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(m.Name, q, StringComparison.OrdinalIgnoreCase) ||
                    m.DisplayName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }
    }
}
