using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Velox.Shared.Dtos;
using Velox.Shared.Models;

namespace VeloxManager.Services
{
    public sealed class ParsedAssetRow
    {
        public int RowNumber { get; set; }
        public string SerialNumber { get; set; } = "";
        public string AssetTag { get; set; } = "";
        public string Model { get; set; } = "";
        public string Category { get; set; } = "Laptop";
        public string CurrentLocation { get; set; } = "IntakeStaging";
        public string LifecycleQueue { get; set; } = "IntakeStaging";
        public string Grade { get; set; } = "A+";
        public string Supplier { get; set; } = "";
        public string Customer { get; set; } = "";
        public int? BatteryHealth { get; set; }
        public int? StorageHealth { get; set; }
        public double? PurchasePrice { get; set; }
        public double? SalePrice { get; set; }
        public int Quantity { get; set; } = 1;
        public string MissingComponents { get; set; } = "";
        public string Notes { get; set; } = "";
        public string InDate { get; set; } = "";
        public string OutDate { get; set; } = "";

        public bool IsValid => (!string.IsNullOrWhiteSpace(SerialNumber) || !string.IsNullOrWhiteSpace(AssetTag))
                               && !string.Equals(SerialNumber, "SERIAL", StringComparison.OrdinalIgnoreCase)
                               && !string.Equals(AssetTag, "TAG", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class ParsedAssetBatch
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public List<string> HeadersFound { get; set; } = new();
        public List<ParsedAssetRow> Rows { get; set; } = new();
        public int TotalRows => Rows.Count;
        public int ValidCount => Rows.Count(r => r.IsValid);
        public int InvalidCount => Rows.Count(r => !r.IsValid);
    }

    /// <summary>
    /// Multi-Section Adaptive Parser for Excel (.xlsx) and CSV (.csv) warehouse manifests.
    /// Handles embedded section headers (HP, Lenovo, Dell, Microsoft Tablet, Surface, Invoices),
    /// mixed column layouts (VYP, PLACE, IN, OUT, TAG, SERIAL, Model, Processor, MEM, B+, Status, WIP, PHY, Remarks, etc.),
    /// and auto-infers categories, locations, specifications, and defect attributions.
    /// Zero external NuGet dependencies.
    /// </summary>
    public static class ExcelCsvAssetParser
    {
        public static ParsedAssetBatch ParseFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("Specified Excel / CSV file does not exist.", filePath);

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".xlsx")
            {
                return ParseXlsx(filePath);
            }
            else if (ext == ".csv" || ext == ".tsv" || ext == ".txt")
            {
                return ParseCsv(filePath);
            }
            else
            {
                throw new NotSupportedException($"Unsupported file format '{ext}'. Please select an .xlsx or .csv file.");
            }
        }

        public static ParsedAssetBatch ParseCsv(string filePath)
        {
            var batch = new ParsedAssetBatch
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath)
            };

            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            if (lines.Length == 0) return batch;

            char delimiter = DetectDelimiter(lines);

            var rawRows = new List<List<string>>();
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                rawRows.Add(ParseCsvLine(line, delimiter));
            }

            batch.Rows = ProcessMultiSectionRows(rawRows, batch.HeadersFound);
            return batch;
        }

        public static ParsedAssetBatch ParseXlsx(string filePath)
        {
            var batch = new ParsedAssetBatch
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath)
            };

            using var archive = ZipFile.OpenRead(filePath);

            // 1. Shared Strings
            var sharedStrings = new List<string>();
            var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
            if (sharedStringsEntry != null)
            {
                using var ssStream = sharedStringsEntry.Open();
                var ssDoc = XDocument.Load(ssStream);
                XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                foreach (var si in ssDoc.Descendants(ns + "si"))
                {
                    var texts = si.Descendants(ns + "t").Select(t => t.Value);
                    sharedStrings.Add(string.Concat(texts));
                }
            }

            // 2. Worksheets (process sheet1 or all sheets)
            var sheetEntries = archive.Entries
                .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml"))
                .ToList();

            if (sheetEntries.Count == 0)
                throw new InvalidOperationException("No worksheet found in .xlsx workbook.");

            var allRawRows = new List<List<string>>();

            foreach (var sheetEntry in sheetEntries)
            {
                using var sheetStream = sheetEntry.Open();
                var doc = XDocument.Load(sheetStream);
                XNamespace sheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

                var rowsXml = doc.Descendants(sheetNs + "row").ToList();
                foreach (var rowXml in rowsXml)
                {
                    var cells = ExtractRowCells(rowXml, sheetNs, sharedStrings);
                    if (cells.Count == 0) continue;

                    int maxCol = cells.Max(c => c.ColumnIndex) + 1;
                    var cellList = new List<string>(new string[maxCol]);
                    for (int c = 0; c < cellList.Count; c++) cellList[c] = "";

                    foreach (var cell in cells)
                    {
                        if (cell.ColumnIndex >= 0 && cell.ColumnIndex < cellList.Count)
                            cellList[cell.ColumnIndex] = cell.Value ?? "";
                    }

                    if (!cellList.All(string.IsNullOrWhiteSpace))
                    {
                        allRawRows.Add(cellList);
                    }
                }
            }

            batch.Rows = ProcessMultiSectionRows(allRawRows, batch.HeadersFound);
            return batch;
        }

        private static List<ParsedAssetRow> ProcessMultiSectionRows(List<List<string>> rawRows, List<string> allHeadersOut)
        {
            var result = new List<ParsedAssetRow>();
            Dictionary<string, int>? currentMap = null;
            int rowNumber = 0;

            foreach (var cells in rawRows)
            {
                rowNumber++;
                if (cells.Count == 0 || cells.All(string.IsNullOrWhiteSpace))
                    continue;

                // Check if this row is an asset table header (e.g. VYP, PLACE, TAG, SERIAL, Model, Processor...)
                if (IsAssetTableHeader(cells))
                {
                    currentMap = BuildColumnMapping(cells);
                    foreach (var c in cells)
                    {
                        string trimmed = c?.Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(trimmed) && !allHeadersOut.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                        {
                            allHeadersOut.Add(trimmed);
                        }
                    }
                    continue;
                }

                // Check if this row is an invoice or non-asset table header (e.g. "Invoice Number,In Date,Out Date...")
                if (IsStopSectionHeader(cells))
                {
                    currentMap = null;
                    continue;
                }

                // If currently inside an active asset section:
                if (currentMap != null)
                {
                    var item = MapRow(cells, currentMap, rowNumber);
                    if (item.IsValid)
                    {
                        result.Add(item);
                    }
                }
            }

            // Fallback: If no embedded section header was matched, check if row 0 could be a standard single header
            if (result.Count == 0 && rawRows.Count > 1)
            {
                var row0 = rawRows[0];
                var singleMap = BuildColumnMapping(row0);
                allHeadersOut.AddRange(row0.Where(h => !string.IsNullOrWhiteSpace(h)));

                for (int i = 1; i < rawRows.Count; i++)
                {
                    var item = MapRow(rawRows[i], singleMap, i + 1);
                    if (item.IsValid)
                    {
                        result.Add(item);
                    }
                }
            }

            return result;
        }

        private static bool IsAssetTableHeader(List<string> cells)
        {
            int score = 0;
            foreach (var c in cells)
            {
                string h = NormalizeHeader(c);
                if (string.IsNullOrWhiteSpace(h)) continue;

                if (Matches(h, "tag", "asset tag", "asset_tag")) score += 3;
                else if (Matches(h, "serial", "serial number", "serial_number", "sn")) score += 3;
                else if (Matches(h, "model", "model name", "laptop model")) score += 2;
                else if (Matches(h, "processor", "cpu", "mem", "memory")) score += 2;
                else if (Matches(h, "status", "phy", "b+", "place", "work in progress")) score += 2;
            }

            // Must have high confidence of being an asset inventory table header
            return score >= 6;
        }

        private static bool IsStopSectionHeader(List<string> cells)
        {
            return cells.Any(c => Matches(NormalizeHeader(c), "invoice number", "invoice", "payment status"));
        }

        private struct CellData
        {
            public int ColumnIndex;
            public string Value;
        }

        private static List<CellData> ExtractRowCells(XElement rowXml, XNamespace ns, List<string> sharedStrings)
        {
            var list = new List<CellData>();
            foreach (var c in rowXml.Elements(ns + "c"))
            {
                string r = (string?)c.Attribute("r") ?? "";
                int colIdx = ColumnNameToIndex(r);
                string t = (string?)c.Attribute("t") ?? "";
                string val = "";

                if (t == "s")
                {
                    string v = c.Element(ns + "v")?.Value ?? "";
                    if (int.TryParse(v, out int sIdx) && sIdx >= 0 && sIdx < sharedStrings.Count)
                    {
                        val = sharedStrings[sIdx];
                    }
                }
                else if (t == "inlineStr")
                {
                    val = c.Element(ns + "is")?.Element(ns + "t")?.Value ?? "";
                }
                else
                {
                    val = c.Element(ns + "v")?.Value ?? "";
                }

                list.Add(new CellData { ColumnIndex = colIdx, Value = val });
            }
            return list;
        }

        private static int ColumnNameToIndex(string cellRef)
        {
            if (string.IsNullOrWhiteSpace(cellRef)) return 0;
            int sum = 0;
            foreach (char ch in cellRef)
            {
                if (char.IsLetter(ch))
                {
                    sum = sum * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
                }
                else break;
            }
            return Math.Max(0, sum - 1);
        }

        private static char DetectDelimiter(string[] lines)
        {
            // Sample lines that might contain table content
            foreach (var line in lines)
            {
                if (line.Contains("TAG", StringComparison.OrdinalIgnoreCase) || line.Contains("SERIAL", StringComparison.OrdinalIgnoreCase))
                {
                    int commas = line.Count(c => c == ',');
                    int semicolons = line.Count(c => c == ';');
                    int tabs = line.Count(c => c == '\t');
                    if (tabs > commas && tabs > semicolons) return '\t';
                    if (semicolons > commas) return ';';
                    return ',';
                }
            }
            return ',';
        }

        private static List<string> ParseCsvLine(string line, char delimiter)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '\"')
                    {
                        current.Append('\"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == delimiter && !inQuotes)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString().Trim());
            return result;
        }

        private static Dictionary<string, int> BuildColumnMapping(List<string> headers)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < headers.Count; i++)
            {
                string h = NormalizeHeader(headers[i]);
                if (string.IsNullOrWhiteSpace(h)) continue;

                // Serial Number
                if (Matches(h, "serial", "serial number", "serial_number", "sn", "service tag", "service_tag", "st"))
                    AssignIfNotSet(map, "SerialNumber", i);

                // Asset Tag
                else if (Matches(h, "tag", "asset tag", "asset_tag", "asset id", "barcode", "tag id", "id"))
                    AssignIfNotSet(map, "AssetTag", i);

                // Model
                else if (Matches(h, "model", "product", "machine", "item", "description", "laptop model", "device", "model name"))
                    AssignIfNotSet(map, "Model", i);

                // Category
                else if (Matches(h, "category", "type", "device type", "asset type"))
                    AssignIfNotSet(map, "Category", i);

                // Processor / CPU
                else if (Matches(h, "processor", "cpu", "processor model"))
                    AssignIfNotSet(map, "Processor", i);

                // Memory / RAM / Storage
                else if (Matches(h, "mem", "memory", "ram", "memory [ram/storage]"))
                    AssignIfNotSet(map, "Memory", i);

                // Battery Health (B+)
                else if (Matches(h, "b+", "b +", "battery health", "battery", "batt health", "battery %", "batt"))
                    AssignIfNotSet(map, "BatteryHealth", i);

                // Storage Health
                else if (Matches(h, "storage health", "disk health", "ssd health", "hdd health", "hds", "storage %"))
                    AssignIfNotSet(map, "StorageHealth", i);

                // Status / Lifecycle Queue
                else if (Matches(h, "status", "queue", "lifecycle", "lifecycle queue", "state"))
                    AssignIfNotSet(map, "Status", i);

                // Work In Progress / Defects / Issue
                else if (Matches(h, "work in progress", "wip", "work_in_progress", "defects", "issue", "issues"))
                    AssignIfNotSet(map, "WorkInProgress", i);

                // Physical / Cosmetic Grade (PHY)
                else if (Matches(h, "phy", "physical grade", "grade", "cosmetic grade", "physical_grade", "cosmetic"))
                    AssignIfNotSet(map, "Grade", i);

                // Remarks / Technician Notes
                else if (Matches(h, "remarks", "notes", "comment", "comments", "technician notes"))
                    AssignIfNotSet(map, "Remarks", i);

                // Location / Shelf (PLACE)
                else if (Matches(h, "place", "location", "shelf", "shelf bay", "shelf_location", "bay", "bin", "rack"))
                    AssignIfNotSet(map, "Location", i);

                // Technician Name or ID
                else if (Matches(h, "technician", "technician name or id", "tech", "qc tech", "assigned to", "operator"))
                    AssignIfNotSet(map, "Technician", i);

                // In Date
                else if (Matches(h, "in date", "in_date", "intake date", "date received", "date"))
                    AssignIfNotSet(map, "InDate", i);

                // Supplier
                else if (Matches(h, "supplier", "vendor", "source", "donor"))
                    AssignIfNotSet(map, "Supplier", i);

                // Out Date
                else if (Matches(h, "out date", "out_date", "sale date", "dispatch date"))
                    AssignIfNotSet(map, "OutDate", i);

                // Customer
                else if (Matches(h, "customer", "client", "buyer", "account"))
                    AssignIfNotSet(map, "Customer", i);

                // Purchase Price
                else if (Matches(h, "purchase price", "cost", "buy price", "purchase_price"))
                    AssignIfNotSet(map, "PurchasePrice", i);

                // Sale Price
                else if (Matches(h, "sale price", "price", "selling price", "sale_price", "msrp"))
                    AssignIfNotSet(map, "SalePrice", i);

                // Quantity
                else if (Matches(h, "quantity", "qty", "count"))
                    AssignIfNotSet(map, "Quantity", i);

                // Missing Components
                else if (Matches(h, "missing components", "missing_components", "missing"))
                    AssignIfNotSet(map, "MissingComponents", i);
            }

            return map;
        }

        private static string NormalizeHeader(string header)
        {
            if (string.IsNullOrWhiteSpace(header)) return "";
            return Regex.Replace(header.Trim().ToLowerInvariant(), @"[\-_]", " ");
        }

        private static bool Matches(string header, params string[] candidates)
        {
            return candidates.Any(c => string.Equals(header, c, StringComparison.OrdinalIgnoreCase) ||
                                       header.StartsWith(c + " ", StringComparison.OrdinalIgnoreCase) ||
                                       header.EndsWith(" " + c, StringComparison.OrdinalIgnoreCase));
        }

        private static void AssignIfNotSet(Dictionary<string, int> map, string key, int index)
        {
            if (!map.ContainsKey(key)) map[key] = index;
        }

        private static ParsedAssetRow MapRow(List<string> cells, Dictionary<string, int> map, int rowNum)
        {
            string Get(string key)
            {
                if (map.TryGetValue(key, out int idx) && idx >= 0 && idx < cells.Count)
                {
                    return cells[idx]?.Trim() ?? "";
                }
                return "";
            }

            string rawSerial = Get("SerialNumber");
            string rawTag = Get("AssetTag");
            string rawModel = Get("Model");
            string rawPlace = Get("Location");
            string rawStatus = Get("Status");
            string rawWip = Get("WorkInProgress");
            string rawGrade = Get("Grade");
            string rawRemarks = Get("Remarks");
            string rawProc = Get("Processor");
            string rawMem = Get("Memory");
            string rawBatt = Get("BatteryHealth");
            string rawSupplier = Get("Supplier");
            string rawCustomer = Get("Customer");
            string rawInDate = Get("InDate");
            string rawOutDate = Get("OutDate");

            // Filter out counter / subtotal lines like ",,122,24,98,,,,,,,,,,,,,," where serial & tag are blank or purely counters
            if (string.IsNullOrWhiteSpace(rawSerial) && string.IsNullOrWhiteSpace(rawTag) && string.IsNullOrWhiteSpace(rawModel))
            {
                return new ParsedAssetRow { RowNumber = rowNum };
            }

            // Normalization of Serial & Tag
            string sn = rawSerial.Trim();
            string tag = rawTag.Trim();

            // If Serial is empty but Tag is present, fall back to Tag (e.g. "HP100", "DL28", "MST-9")
            if (string.IsNullOrWhiteSpace(sn) && !string.IsNullOrWhiteSpace(tag))
                sn = tag;
            if (string.IsNullOrWhiteSpace(tag) && !string.IsNullOrWhiteSpace(sn))
                tag = sn;

            // Model
            string model = !string.IsNullOrWhiteSpace(rawModel) ? rawModel : "Refurb Laptop";

            // Category Auto-Inference
            string category = "Laptop";
            string upperTag = tag.ToUpperInvariant();
            string upperModel = model.ToUpperInvariant();

            if (upperTag.StartsWith("MST-") || upperModel.Contains("TABLET") || upperModel.Contains("TAB") || upperModel.Contains("SURFACE PRO"))
            {
                category = "Tablet";
            }
            else if (upperTag.StartsWith("MSL-") || upperModel.Contains("SURFACE LAPTOP"))
            {
                category = "Laptop";
            }
            else if (upperModel.Contains("PRECISION") || upperModel.Contains("WORKSTATION"))
            {
                category = "Workstation";
            }
            else if (upperModel.Contains("DESKTOP") || upperModel.Contains("OPTIPLEX") || upperModel.Contains("ELITEDESK"))
            {
                category = "Desktop";
            }

            // Location from "PLACE"
            string location = !string.IsNullOrWhiteSpace(rawPlace) ? rawPlace : "IntakeStaging";

            // Lifecycle Queue from "Status"
            string queue = "IntakeStaging";
            string upperStatus = rawStatus.ToUpperInvariant();
            if (upperStatus == "RTS" || upperStatus == "READY")
            {
                queue = "ReadyForSale";
            }
            else if (upperStatus == "SOLD")
            {
                queue = "Sold";
            }
            else if (upperStatus == "WIP" || upperStatus.Contains("PROGRESS") || upperStatus == "RFR")
            {
                queue = "ActiveTesting";
            }
            else if (upperStatus == "RENT" || upperStatus == "DEMO")
            {
                queue = "OnHold";
            }
            else if (upperStatus == "NTC" || upperStatus.Contains("PARTS"))
            {
                queue = "AwaitingParts";
            }

            // Grade Normalization (e.g. "A+ GRADE" -> "A+", "B GRADE" -> "B", "A" -> "A")
            string grade = "A";
            string upperGrade = rawGrade.ToUpperInvariant();
            if (upperGrade.Contains("A+")) grade = "A+";
            else if (upperGrade.Contains("B")) grade = "B";
            else if (upperGrade.Contains("C")) grade = "C";
            else if (upperGrade.Contains("D") || upperGrade.Contains("FAULTY")) grade = "Faulty";
            else if (upperGrade.Contains("A")) grade = "A";

            // Battery Health (parse first integer 0-100)
            int? battery = null;
            var matchB = Regex.Match(rawBatt, @"\b([0-9]{1,3})\b");
            if (matchB.Success && int.TryParse(matchB.Groups[1].Value, out int bVal))
            {
                battery = Math.Clamp(bVal, 0, 100);
            }

            // Consolidated Notes: preserve Processor, Memory, Defect Issue, Remarks, Sale flags
            var notesList = new List<string>();
            if (!string.IsNullOrWhiteSpace(rawProc)) notesList.Add($"CPU: {rawProc}");
            if (!string.IsNullOrWhiteSpace(rawMem)) notesList.Add($"RAM/SSD: {rawMem}");
            if (upperStatus == "SOLD") notesList.Add("[SOLD]");
            if (upperStatus == "RENT") notesList.Add("[RENTED]");
            if (upperStatus == "DEMO") notesList.Add("[DEMO UNIT]");
            if (!string.IsNullOrWhiteSpace(rawWip) && rawWip != "All Okay" && rawWip != "Undefined")
            {
                notesList.Add($"Issue: {rawWip}");
            }
            if (!string.IsNullOrWhiteSpace(rawRemarks) && rawRemarks != "PROBLEM")
            {
                notesList.Add(rawRemarks);
            }

            string combinedNotes = string.Join(" | ", notesList);

            // Date parsing
            string inDate = ParseNormalizedDate(rawInDate);
            string outDate = ParseNormalizedDate(rawOutDate);

            return new ParsedAssetRow
            {
                RowNumber = rowNum,
                SerialNumber = sn,
                AssetTag = tag,
                Model = model,
                Category = category,
                CurrentLocation = location,
                LifecycleQueue = queue,
                Grade = grade,
                BatteryHealth = battery,
                StorageHealth = 100,
                Supplier = rawSupplier,
                Customer = rawCustomer,
                InDate = inDate,
                OutDate = outDate,
                Notes = combinedNotes,
                Quantity = 1
            };
        }

        private static string ParseNormalizedDate(string rawDate)
        {
            if (string.IsNullOrWhiteSpace(rawDate)) return "";
            string trimmed = rawDate.Trim();

            // Try standard formats: yyyy-MM-dd, dd/MM/yy, dd/MM/yyyy, d/M/yy, d/M/yyyy
            string[] formats = { "yyyy-MM-dd", "d/M/yy", "dd/MM/yy", "d/M/yyyy", "dd/MM/yyyy", "yyyy/MM/dd", "MM/dd/yyyy" };
            if (DateTime.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                return dt.ToString("yyyy-MM-dd");
            }

            if (DateTime.TryParse(trimmed, out var dtLoose))
            {
                return dtLoose.ToString("yyyy-MM-dd");
            }

            return trimmed;
        }
    }
}
