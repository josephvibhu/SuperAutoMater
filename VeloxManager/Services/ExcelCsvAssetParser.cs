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
        public string Grade { get; set; } = "Unknown";
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

        public bool IsValid => !string.IsNullOrWhiteSpace(SerialNumber) || !string.IsNullOrWhiteSpace(AssetTag);
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
    /// High-performance native parser for Excel (.xlsx) and CSV (.csv) files.
    /// Supports SuperAutoMater 15-column ITAM format as well as generic warehouse manifests.
    /// Zero third-party NuGet dependencies; utilizes standard BCL ZipArchive & XML Linq.
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

            // Detect delimiter (, or ; or \t)
            char delimiter = DetectDelimiter(lines[0]);
            var headers = ParseCsvLine(lines[0], delimiter);
            batch.HeadersFound = headers;

            var colMap = BuildColumnMapping(headers);

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var cells = ParseCsvLine(line, delimiter);
                var row = MapRow(cells, colMap, i + 1);
                batch.Rows.Add(row);
            }

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

            // 1. Load Shared Strings (if available)
            var sharedStrings = new List<string>();
            var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
            if (sharedStringsEntry != null)
            {
                using var ssStream = sharedStringsEntry.Open();
                var ssDoc = XDocument.Load(ssStream);
                XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                foreach (var si in ssDoc.Descendants(ns + "si"))
                {
                    // Handle single <t> or multiple run elements <r><t>
                    var texts = si.Descendants(ns + "t").Select(t => t.Value);
                    sharedStrings.Add(string.Concat(texts));
                }
            }

            // 2. Open first worksheet
            var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml") 
                          ?? archive.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase));

            if (sheetEntry == null)
                throw new InvalidOperationException("Could not find worksheet in .xlsx workbook.");

            using var sheetStream = sheetEntry.Open();
            var doc = XDocument.Load(sheetStream);
            XNamespace sheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var rowsXml = doc.Descendants(sheetNs + "row").ToList();
            if (rowsXml.Count == 0) return batch;

            // 3. Extract header row (first row)
            var firstRowXml = rowsXml[0];
            var headerCells = ExtractRowCells(firstRowXml, sheetNs, sharedStrings);
            var headers = headerCells.Select(c => c.Value?.Trim() ?? "").ToList();
            batch.HeadersFound = headers;

            var colMap = BuildColumnMapping(headers);

            // 4. Extract data rows
            for (int r = 1; r < rowsXml.Count; r++)
            {
                var rowXml = rowsXml[r];
                var cells = ExtractRowCells(rowXml, sheetNs, sharedStrings);
                
                // Map cells by column index
                int maxCol = cells.Count > 0 ? cells.Max(c => c.ColumnIndex) + 1 : 0;
                var cellList = new List<string>(new string[Math.Max(maxCol, headers.Count)]);
                for (int c = 0; c < cellList.Count; c++) cellList[c] = "";
                foreach (var cell in cells)
                {
                    if (cell.ColumnIndex >= 0 && cell.ColumnIndex < cellList.Count)
                    {
                        cellList[cell.ColumnIndex] = cell.Value ?? "";
                    }
                }

                if (cellList.All(string.IsNullOrWhiteSpace)) continue;

                var row = MapRow(cellList, colMap, r + 1);
                batch.Rows.Add(row);
            }

            return batch;
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

                if (t == "s") // shared string index
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
            // Extract letters only, e.g. "AA12" -> "AA"
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

        private static char DetectDelimiter(string headerLine)
        {
            int commas = headerLine.Count(c => c == ',');
            int semicolons = headerLine.Count(c => c == ';');
            int tabs = headerLine.Count(c => c == '\t');

            if (tabs > commas && tabs > semicolons) return '\t';
            if (semicolons > commas) return ';';
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

                // Memory / RAM
                else if (Matches(h, "memory", "ram", "memory [ram/storage]"))
                    AssignIfNotSet(map, "Memory", i);

                // Battery Health
                else if (Matches(h, "battery health", "battery", "batt health", "battery %", "batt"))
                    AssignIfNotSet(map, "BatteryHealth", i);

                // Storage Health
                else if (Matches(h, "storage health", "disk health", "ssd health", "hdd health", "hds"))
                    AssignIfNotSet(map, "StorageHealth", i);

                // Status / Queue
                else if (Matches(h, "status", "queue", "lifecycle", "lifecycle queue", "state"))
                    AssignIfNotSet(map, "Status", i);

                // Work In Progress / Defects
                else if (Matches(h, "work in progress", "wip", "work_in_progress", "defects", "issue"))
                    AssignIfNotSet(map, "WorkInProgress", i);

                // Physical Grade
                else if (Matches(h, "physical grade", "grade", "cosmetic grade", "physical_grade", "cosmetic"))
                    AssignIfNotSet(map, "Grade", i);

                // Remarks / Notes
                else if (Matches(h, "remarks", "notes", "comment", "comments", "technician notes"))
                    AssignIfNotSet(map, "Remarks", i);

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

                // Location / Shelf Bay
                else if (Matches(h, "location", "shelf", "shelf bay", "shelf_location", "bay", "bin", "rack"))
                    AssignIfNotSet(map, "Location", i);

                // Purchase Price / Cost
                else if (Matches(h, "purchase price", "cost", "buy price", "purchase_price"))
                    AssignIfNotSet(map, "PurchasePrice", i);

                // Sale Price / Price
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

            var row = new ParsedAssetRow
            {
                RowNumber = rowNum,
                SerialNumber = Get("SerialNumber"),
                AssetTag = Get("AssetTag"),
                Model = Get("Model"),
                Category = Get("Category"),
                CurrentLocation = Get("Location"),
                LifecycleQueue = Get("Status"),
                Grade = Get("Grade"),
                Supplier = Get("Supplier"),
                Customer = Get("Customer"),
                InDate = Get("InDate"),
                MissingComponents = Get("MissingComponents"),
                Notes = Get("Remarks")
            };

            // If SerialNumber is empty but Tag is present, or vice-versa
            if (string.IsNullOrWhiteSpace(row.SerialNumber) && !string.IsNullOrWhiteSpace(row.AssetTag))
                row.SerialNumber = row.AssetTag;
            if (string.IsNullOrWhiteSpace(row.AssetTag) && !string.IsNullOrWhiteSpace(row.SerialNumber))
                row.AssetTag = row.SerialNumber;

            // Combine Processor and Memory into Model if Model is empty
            if (string.IsNullOrWhiteSpace(row.Model))
            {
                string proc = Get("Processor");
                string mem = Get("Memory");
                if (!string.IsNullOrWhiteSpace(proc))
                    row.Model = proc;
                else if (!string.IsNullOrWhiteSpace(mem))
                    row.Model = $"Asset ({mem})";
                else
                    row.Model = "Refurb Laptop";
            }

            // Defaults
            if (string.IsNullOrWhiteSpace(row.Category)) row.Category = "Laptop";
            if (string.IsNullOrWhiteSpace(row.CurrentLocation)) row.CurrentLocation = "IntakeStaging";
            if (string.IsNullOrWhiteSpace(row.Grade)) row.Grade = "A+";
            if (string.IsNullOrWhiteSpace(row.InDate)) row.InDate = DateTime.Now.ToString("yyyy-MM-dd");

            // Battery Health
            string bStr = Get("BatteryHealth").Replace("%", "").Trim();
            if (int.TryParse(bStr, out int bh)) row.BatteryHealth = Math.Clamp(bh, 0, 100);

            // Storage Health
            string sStr = Get("StorageHealth").Replace("%", "").Trim();
            if (int.TryParse(sStr, out int sh)) row.StorageHealth = Math.Clamp(sh, 0, 100);

            // Purchase Price
            string purStr = Get("PurchasePrice").Replace("$", "").Replace("₹", "").Replace("£", "").Replace(",", "").Trim();
            if (double.TryParse(purStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double pur))
                row.PurchasePrice = pur;

            // Sale Price
            string salStr = Get("SalePrice").Replace("$", "").Replace("₹", "").Replace("£", "").Replace(",", "").Trim();
            if (double.TryParse(salStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double sal))
                row.SalePrice = sal;

            // Quantity
            string qtyStr = Get("Quantity").Trim();
            if (int.TryParse(qtyStr, out int qty) && qty > 0) row.Quantity = qty;

            // Notes append WIP if present
            string wip = Get("WorkInProgress");
            if (!string.IsNullOrWhiteSpace(wip) && wip != "All Okay")
            {
                row.Notes = string.IsNullOrWhiteSpace(row.Notes) ? $"WIP: {wip}" : $"{row.Notes} | WIP: {wip}";
            }

            return row;
        }
    }
}
