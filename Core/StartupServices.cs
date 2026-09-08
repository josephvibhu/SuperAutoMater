using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace ITAS_QC_Tool
{
    /// <summary>
    /// Startup helpers: WiFi auto-connect and Google Sheets code.gs bundling.
    /// </summary>
    public static class StartupServices
    {
        // ── WiFi Auto-Connect ──────────────────────────────────────────────────
        private const string WifiSsid = "GTW";
        private const string WifiPass = "john@123";

        /// <summary>
        /// Silently connects to the GTW access point on startup.
        /// Creates a WPA2-PSK profile XML and connects via netsh.
        /// Runs on a background thread — never blocks the UI.
        /// </summary>
        public static async Task ConnectToGtwWifiAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    // Step 1: Build WPA2-PSK profile XML
                    string profileXml = $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{WifiSsid}</name>
    <SSIDConfig>
        <SSID>
            <name>{WifiSsid}</name>
        </SSID>
    </SSIDConfig>
    <connectionType>ESS</connectionType>
    <connectionMode>auto</connectionMode>
    <MSM>
        <security>
            <authEncryption>
                <authentication>WPA2PSK</authentication>
                <encryption>AES</encryption>
                <useOneX>false</useOneX>
            </authEncryption>
            <sharedKey>
                <keyType>passPhrase</keyType>
                <protected>false</protected>
                <keyMaterial>{WifiPass}</keyMaterial>
            </sharedKey>
        </security>
    </MSM>
</WLANProfile>";

                    // Step 2: Write profile to temp file
                    string profilePath = Path.Combine(Path.GetTempPath(), $"AutoMater_WiFi_{WifiSsid}.xml");
                    File.WriteAllText(profilePath, profileXml);

                    // Step 3: Add profile (add if missing, update if exists)
                    RunNetsh($"wlan add profile filename=\"{profilePath}\" user=current", waitMs: 3000);

                    // Step 4: Try connecting
                    RunNetsh($"wlan connect name=\"{WifiSsid}\"", waitMs: 4000);

                    // Step 5: Cleanup temp file
                    try { File.Delete(profilePath); } catch { }

                    // Step 6: Flush any queued offline asset sync records after obtaining IP
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(3000);
                            if (OfflineSyncQueue.Instance.PendingCount > 0)
                            {
                                await OfflineSyncQueue.Instance.FlushQueueAsync(AssetCsvExportForm.DefaultEmbeddedSheetsUrl);
                            }
                        }
                        catch { }
                    });
                }
                catch { /* Non-critical — never crash the app */ }
            });
        }

        private static void RunNetsh(string args, int waitMs)
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", args)
                {
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };
                using (var p = Process.Start(psi))
                {
                    if (p != null && !p.WaitForExit(waitMs))
                        p.Kill();
                }
            }
            catch { }
        }

        // ── Google Sheets code.gs Bundler ─────────────────────────────────────

        public static readonly string CodeGsPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sheets_Setup", "code.gs");

        public static readonly string ReadmePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sheets_Setup", "SETUP_GUIDE.txt");

        public const string CodeGsContent = @"/**
 * AutoMater v6.3 — Google Sheets Inventory Webhook
 * 
 * Automatically receives hardware QC test data from AutoMater Diagnostic Tool
 * and records it into this Google Sheet.
 * 
 * ONE-TIME SETUP:
 * 1. In your Google Sheet, click Extensions → Apps Script.
 * 2. Delete all existing code and paste this entire file.
 * 3. Click Deploy → New Deployment.
 * 4. Choose type 'Web app'.
 * 5. Set 'Execute as: Me' and 'Who has access: Anyone'.
 * 6. Click Deploy, grant permissions, and copy the Web App URL.
 * 7. In AutoMater, paste the URL into the 'Apps Script Web App URL' field.
 */

function doPost(e) {
  var lock = LockService.getScriptLock();
  try {
    lock.waitLock(10000); // 10-second lock to prevent concurrent write collisions
  } catch (lockErr) {
    return ContentService.createTextOutput(JSON.stringify({
      status: 'ERROR',
      message: 'Server busy, please retry in a moment.'
    })).setMimeType(ContentService.MimeType.JSON);
  }

  try {
    var ss = SpreadsheetApp.getActiveSpreadsheet();
    var sheet = ss.getSheetByName('AutoMater_Inventory');
    if (!sheet) {
      sheet = ss.getActiveSheet();
      if (sheet.getName() === 'Sheet1') {
        sheet.setName('AutoMater_Inventory');
      }
    }

    // Setup headers and format on first run
    if (sheet.getLastRow() === 0) {
      var headers = [
        'Asset Tag',
        'Serial Number',
        'Model',
        'Processor',
        'Memory (RAM/SSD)',
        'Battery Health (%)',
        'Status',
        'Work In Progress Issue',
        'Physical Grade',
        'Remarks',
        'Shelf Location',
        'Timestamp'
      ];
      sheet.appendRow(headers);

      var headerRange = sheet.getRange(1, 1, 1, headers.length);
      headerRange.setBackground('#0F172A');
      headerRange.setFontColor('#38BDF8');
      headerRange.setFontWeight('bold');
      headerRange.setFontFamily('Roboto');
      headerRange.setHorizontalAlignment('center');
      sheet.setFrozenRows(1);
    }

    // Parse incoming JSON payload
    var payload = {};
    if (e && e.postData && e.postData.contents) {
      payload = JSON.parse(e.postData.contents);
    }

    var assetTag      = payload.Asset_Tag || payload.assetTag || '';
    var serialNo      = payload.Serial_Number || payload.serialNumber || '';
    var model         = payload.Model || payload.model || '';
    var processor     = payload.Processor || payload.processor || '';
    var memory        = payload.Memory || payload.ramStorage || '';
    var batteryHealth = payload.Battery_Health || payload.batteryHealth || '100';
    var status        = payload.Status || payload.status || 'RTS';
    var wipIssue      = payload.Wip_Issue || payload.wipIssue || 'All Okay';
    var physicalGrade = payload.Physical_Grade || payload.physicalGrade || 'A+';
    var remarks       = payload.Remarks || payload.remarks || '';
    var shelf         = payload.Shelf_Location || payload.shelf || '';
    var timestamp     = payload.Timestamp || Utilities.formatDate(new Date(), Session.getScriptTimeZone(), 'yyyy-MM-dd HH:mm:ss');

    var numBattery = parseInt(batteryHealth.toString().replace(/[^0-9]/g, ''), 10);
    if (isNaN(numBattery) || numBattery < 0) numBattery = 100;
    if (numBattery > 100) numBattery = 100;

    var newRow = [
      assetTag,
      serialNo,
      model,
      processor,
      memory,
      numBattery,
      status,
      wipIssue,
      physicalGrade,
      remarks,
      shelf,
      timestamp
    ];

    var lastRow = sheet.getLastRow();
    var targetRow = -1;
    var updated = false;

    // Deduplication Guard: if serial or asset was added in last 45s, update and avoid duplicate
    if (lastRow > 1 && (serialNo || assetTag)) {
      var rangeValues = sheet.getRange(2, 1, lastRow - 1, 2).getValues();
      var timeValues  = sheet.getRange(2, 12, lastRow - 1, 1).getValues();
      var nowMs = new Date().getTime();

      for (var i = rangeValues.length - 1; i >= 0; i--) {
        var rowAsset  = rangeValues[i][0] ? rangeValues[i][0].toString().trim().toUpperCase() : '';
        var rowSerial = rangeValues[i][1] ? rangeValues[i][1].toString().trim().toUpperCase() : '';
        var isMatch = false;

        if (serialNo && serialNo !== 'N/A' && serialNo !== 'UNKNOWN' && rowSerial === serialNo.trim().toUpperCase()) {
          isMatch = true;
        } else if (assetTag && assetTag !== 'UNKNOWN' && rowAsset === assetTag.trim().toUpperCase()) {
          isMatch = true;
        }

        if (isMatch) {
          targetRow = i + 2;
          var rowTime = new Date(timeValues[i][0]).getTime();
          if (!isNaN(rowTime) && (nowMs - rowTime < 45000)) {
            sheet.getRange(targetRow, 1, 1, newRow.length).setValues([newRow]);
            return ContentService.createTextOutput(JSON.stringify({
              status: 'OK',
              action: 'DEDUPLICATED',
              row: targetRow,
              message: 'Duplicate prevented. Record updated at Row ' + targetRow
            })).setMimeType(ContentService.MimeType.JSON);
          }

          sheet.getRange(targetRow, 1, 1, newRow.length).setValues([newRow]);
          updated = true;
          break;
        }
      }
    }

    if (!updated) {
      sheet.appendRow(newRow);
      targetRow = sheet.getLastRow();
    }

    var dataRowRange = sheet.getRange(targetRow, 1, 1, newRow.length);
    dataRowRange.setFontFamily('Roboto');
    dataRowRange.setVerticalAlignment('middle');

    var statusCell = sheet.getRange(targetRow, 7);
    statusCell.setFontWeight('bold');
    statusCell.setHorizontalAlignment('center');
    if (status === 'RTS' || status.indexOf('Ready') !== -1) {
      statusCell.setBackground('#DCFCE7').setFontColor('#15803D');
    } else if (status === 'WIP' || status.indexOf('Progress') !== -1) {
      statusCell.setBackground('#FEF3C7').setFontColor('#B45309');
    } else if (status === 'RFR' || status.indexOf('Repair') !== -1 || status.indexOf('Flagged') !== -1) {
      statusCell.setBackground('#FEE2E2').setFontColor('#B91C1C');
    } else if (status === 'SOLD') {
      statusCell.setBackground('#E0E7FF').setFontColor('#3730A3');
    }

    var battCell = sheet.getRange(targetRow, 6);
    battCell.setHorizontalAlignment('center');

    return ContentService
      .createTextOutput(JSON.stringify({
        status: 'OK',
        action: updated ? 'UPDATED' : 'APPENDED',
        row: targetRow,
        asset: assetTag + ' / ' + serialNo,
        message: updated ? ('Updated record at Row ' + targetRow) : ('Added new record at Row ' + targetRow)
      }))
      .setMimeType(ContentService.MimeType.JSON);

  } catch (err) {
    return ContentService
      .createTextOutput(JSON.stringify({
        status: 'ERROR',
        message: err.toString()
      }))
      .setMimeType(ContentService.MimeType.JSON);
  } finally {
    lock.releaseLock();
  }
}

/**
 * Health check endpoint for browser inspection
 */
function doGet(e) {
  var html = '<!DOCTYPE html><html><head><title>AutoMater Sync Online</title>' +
    '<style>body{font-family:sans-serif;background:#0f172a;color:#f8fafc;padding:40px;text-align:center;}' +
    '.card{background:#1e293b;border-radius:12px;padding:30px;display:inline-block;border:1px solid #38bdf8;max-width:520px;}' +
    'h2{color:#38bdf8;margin-top:0;}p{color:#94a3b8;font-size:14px;}code{color:#4ade80;background:#0f172a;padding:4px 8px;border-radius:4px;word-break:break-all;}</style></head>' +
    '<body><div class=""card"">' +
    '<h2>AutoMater Google Sheets Webhook</h2>' +
    '<p>Status: <b style=""color:#4ade80;"">ONLINE & ACTIVE</b></p>' +
    '<p>Ready to receive QC data with the updated column order and automatic duplicate protection.</p>' +
    '</div></body></html>';
  return HtmlService.createHtmlOutput(html).setTitle('AutoMater Google Sheets Webhook');
}
";

        public const string SetupGuideContent = @"================================================================================
  AUTOMATER v6.3 — GOOGLE SHEETS LIVE INVENTORY SYNC SETUP GUIDE
================================================================================

This guide explains how to connect AutoMater Diagnostic Tool to your Google Sheet.
Setup takes about 2 minutes and only needs to be done once.

--------------------------------------------------------------------------------
STEP 1: CREATE OR OPEN YOUR GOOGLE SHEET
--------------------------------------------------------------------------------
1. Go to: https://sheets.new (or open your existing inventory spreadsheet).
2. Give your sheet a name (e.g., 'Laptop QC Inventory').

--------------------------------------------------------------------------------
STEP 2: OPEN APPS SCRIPT
--------------------------------------------------------------------------------
1. In the Google Sheets top menu, click:
      Extensions  →  Apps Script
2. A new tab will open with a code editor showing:
      function myFunction() { ... }
3. Delete ALL text in the editor so it is completely blank.

--------------------------------------------------------------------------------
STEP 3: PASTE THE code.gs SCRIPT
--------------------------------------------------------------------------------
1. Open the file 'code.gs' located in this folder (Sheets_Setup/code.gs)
   - OR click 'VIEW & COPY SCRIPT' inside AutoMater to copy it directly.
2. Paste the entire code into the Google Apps Script editor.
3. Click the Save icon (💾) or press Ctrl+S.

--------------------------------------------------------------------------------
STEP 4: DEPLOY AS A WEB APP
--------------------------------------------------------------------------------
1. At the top-right of the Apps Script window, click the blue button:
      Deploy  →  New deployment
2. In the modal that appears:
   - Next to 'Select type', click the gear icon (⚙) and choose: Web app
   - Description: AutoMater Sync (optional)
   - Execute as: Me (your Google account)
   - Who has access: Anyone  <-- (IMPORTANT: MUST be 'Anyone' so the app can upload)
3. Click 'Deploy'.
4. Google will ask you to authorize access:
   - Click 'Authorize access'
   - Select your Google account
   - If Google shows 'Google hasn't verified this app', click 'Advanced',
     then click 'Go to Untitled project (unsafe)'
   - Click 'Allow'

--------------------------------------------------------------------------------
STEP 5: COPY AND SAVE YOUR WEB APP URL
--------------------------------------------------------------------------------
1. After deploying, Google displays:
      Web app URL:  https://script.google.com/macros/s/AKfycb.../exec
2. Click 'Copy' next to the Web app URL.
3. In AutoMater, click 'ASSET CSV / QR'.
4. Paste the URL into the 'Apps Script Web App URL' box.
   (AutoMater will save it permanently in sheets_url.txt — you never need to re-enter it).

--------------------------------------------------------------------------------
STEP 6: TEST THE SYNC
--------------------------------------------------------------------------------
1. In AutoMater, click 'UPLOAD TO GOOGLE SHEETS'.
2. The button will report: '✓ Uploaded to Google Sheets (Row 2)'
3. Check your Google Sheet — a new row will appear with headers automatically formatted!

--------------------------------------------------------------------------------
HOW IT WORKS:
--------------------------------------------------------------------------------
- Each time you test a laptop, open 'ASSET CSV / QR', verify the Asset Tag & Serial.
- Click 'UPLOAD TO GOOGLE SHEETS'.
- If the serial number was already recorded, the script automatically UPDATES that row.
- If it's a new laptop, it appends a new row and color-codes the status!
================================================================================
";

        /// <summary>
        /// Writes code.gs and SETUP_GUIDE.txt to Sheets_Setup folder beside the exe on first run.
        /// </summary>
        public static void EnsureSheetsSetupFiles()
        {
            try
            {
                string dir = Path.GetDirectoryName(CodeGsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(CodeGsPath, CodeGsContent);
                File.WriteAllText(ReadmePath, SetupGuideContent);
            }
            catch { }
        }

        /// <summary>
        /// Opens the Sheets_Setup folder in Explorer so the user can grab code.gs.
        /// </summary>
        public static void OpenSheetsSetupFolder()
        {
            try
            {
                EnsureSheetsSetupFiles();
                string dir = Path.GetDirectoryName(CodeGsPath);
                if (Directory.Exists(dir))
                    Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            }
            catch { }
        }

        /// <summary>
        /// Opens Google Sheets in the default browser.
        /// </summary>
        public static void OpenGoogleSheetsInBrowser()
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://sheets.new") { UseShellExecute = true });
            }
            catch { }
        }
    }
}
