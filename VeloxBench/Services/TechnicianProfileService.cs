using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SuperAutoMater.Wpf.Services
{
    public class TechnicianProfile
    {
        public string Name { get; set; } = "Lead Refurb Tech";
        public string Id { get; set; } = "TECH-01";
        public string StationBay { get; set; } = "QC-01";
        public DateTime LastUpdated { get; set; } = DateTime.Now;

        public string DisplayBadge => string.IsNullOrWhiteSpace(Id)
            ? (string.IsNullOrWhiteSpace(Name) ? "TECH-01" : Name)
            : $"{Id} ({Name})";
    }

    public class TechnicianProfileService
    {
        private static readonly Lazy<TechnicianProfileService> _instance =
            new Lazy<TechnicianProfileService>(() => new TechnicianProfileService());
        public static TechnicianProfileService Instance => _instance.Value;

        private const string ProfileFileName = "technician_profile.json";
        private TechnicianProfile _currentProfile = new TechnicianProfile();

        public event Action<TechnicianProfile> ProfileChanged;

        public TechnicianProfile CurrentProfile => _currentProfile;

        private TechnicianProfileService()
        {
            LoadProfile();
        }

        private string GetProfilePath()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(baseDir, ProfileFileName);
            }
            catch
            {
                return ProfileFileName;
            }
        }

        public void LoadProfile()
        {
            try
            {
                string path = GetProfilePath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var loaded = JsonSerializer.Deserialize<TechnicianProfile>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (loaded != null)
                    {
                        _currentProfile = loaded;
                        ProfileChanged?.Invoke(_currentProfile);
                        return;
                    }
                }
            }
            catch { }

            // Default fallback
            _currentProfile = new TechnicianProfile();
        }

        public void SaveProfile(string name, string id, string stationBay)
        {
            try
            {
                _currentProfile.Name = string.IsNullOrWhiteSpace(name) ? "Lead Refurb Tech" : name.Trim();
                _currentProfile.Id = string.IsNullOrWhiteSpace(id) ? "TECH-01" : id.Trim().ToUpperInvariant();
                _currentProfile.StationBay = string.IsNullOrWhiteSpace(stationBay) ? "QC-01" : stationBay.Trim().ToUpperInvariant();
                _currentProfile.LastUpdated = DateTime.Now;

                string path = GetProfilePath();
                string json = JsonSerializer.Serialize(_currentProfile, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(path, json);

                ProfileChanged?.Invoke(_currentProfile);
            }
            catch { }
        }

        public (string Name, string Id, string Station) ParseBadgeText(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return ("Lead Refurb Tech", "TECH-01", "QC-01");

            string text = rawText.Trim();

            // Format 1: JSON payload
            if (text.StartsWith("{") && text.EndsWith("}"))
            {
                try
                {
                    using (var doc = JsonDocument.Parse(text))
                    {
                        var root = doc.RootElement;
                        string id = "";
                        string name = "";
                        string station = "";

                        if (root.TryGetProperty("id", out var idProp)) id = idProp.GetString();
                        else if (root.TryGetProperty("Id", out idProp)) id = idProp.GetString();
                        else if (root.TryGetProperty("tech_id", out idProp)) id = idProp.GetString();

                        if (root.TryGetProperty("name", out var nameProp)) name = nameProp.GetString();
                        else if (root.TryGetProperty("Name", out nameProp)) name = nameProp.GetString();
                        else if (root.TryGetProperty("tech_name", out nameProp)) name = nameProp.GetString();

                        if (root.TryGetProperty("station", out var stProp)) station = stProp.GetString();
                        else if (root.TryGetProperty("bay", out stProp)) station = stProp.GetString();
                        else if (root.TryGetProperty("Station", out stProp)) station = stProp.GetString();

                        if (!string.IsNullOrWhiteSpace(id) || !string.IsNullOrWhiteSpace(name))
                        {
                            return (
                                string.IsNullOrWhiteSpace(name) ? _currentProfile.Name : name.Trim(),
                                string.IsNullOrWhiteSpace(id) ? _currentProfile.Id : id.Trim().ToUpperInvariant(),
                                string.IsNullOrWhiteSpace(station) ? _currentProfile.StationBay : station.Trim().ToUpperInvariant()
                            );
                        }
                    }
                }
                catch { }
            }

            // Format 2: Delimited Key-Value pairs e.g. "ID:TECH-402;NAME:Joseph Vibhu;BAY:QC-02" or "ID=TECH-402|NAME=Joseph"
            if (text.Contains(";") || text.Contains("|") || (text.Contains(":") && (text.Contains("ID") || text.Contains("NAME"))))
            {
                string parsedId = "";
                string parsedName = "";
                string parsedStation = "";

                var tokens = text.Split(new[] { ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var token in tokens)
                {
                    int sepIdx = token.IndexOfAny(new[] { ':', '=' });
                    if (sepIdx > 0)
                    {
                        string k = token.Substring(0, sepIdx).Trim().ToUpperInvariant();
                        string v = token.Substring(sepIdx + 1).Trim();
                        if (k == "ID" || k == "TECH_ID" || k == "TECHID" || k == "BADGE") parsedId = v;
                        else if (k == "NAME" || k == "TECH_NAME" || k == "TECH") parsedName = v;
                        else if (k == "STATION" || k == "BAY" || k == "BENCH") parsedStation = v;
                    }
                }

                if (!string.IsNullOrWhiteSpace(parsedId) || !string.IsNullOrWhiteSpace(parsedName))
                {
                    return (
                        string.IsNullOrWhiteSpace(parsedName) ? _currentProfile.Name : parsedName,
                        string.IsNullOrWhiteSpace(parsedId) ? _currentProfile.Id : parsedId.ToUpperInvariant(),
                        string.IsNullOrWhiteSpace(parsedStation) ? _currentProfile.StationBay : parsedStation.ToUpperInvariant()
                    );
                }
            }

            // Format 3: Single token Badge ID e.g. "TECH-402", "EMP-40912", "QC-01"
            if (Regex.IsMatch(text, @"^(TECH[-_]?\d+|EMP[-_]?\d+|QC[-_]?\d+|T\d{3,5})$", RegexOptions.IgnoreCase))
            {
                return (_currentProfile.Name, text.ToUpperInvariant(), _currentProfile.StationBay);
            }

            // Format 4: "TECH-402: Joseph Vibhu" or "TECH-402 - Joseph Vibhu"
            var colonMatch = Regex.Match(text, @"^(TECH[-_]?\d+|EMP[-_]?\d+|[A-Z0-9]{3,8})\s*(?::|\s+[-–])\s*(.+)$", RegexOptions.IgnoreCase);
            if (colonMatch.Success)
            {
                string id = colonMatch.Groups[1].Value.Trim().ToUpperInvariant();
                string name = colonMatch.Groups[2].Value.Trim();
                return (name, id, _currentProfile.StationBay);
            }

            // Format 5: Name only
            if (text.Length < 40 && !text.Contains("{") && !text.Contains("/"))
            {
                return (text, _currentProfile.Id, _currentProfile.StationBay);
            }

            return (_currentProfile.Name, _currentProfile.Id, _currentProfile.StationBay);
        }
    }
}
