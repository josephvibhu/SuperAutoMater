/**
 * ============================================================================
 *   AUTOMATER v6.3 — GOOGLE APPS SCRIPT INVENTORY WEBHOOK (code.gs)
 * ============================================================================
 * 
 * Column Order:
 *  1. Asset Tag
 *  2. Serial Number
 *  3. Model
 *  4. Processor
 *  5. Memory (RAM / Storage)
 *  6. Battery Health (0-100)
 *  7. Status (WIP, RTS, RFR, SOLD)
 *  8. Work In Progress (All Okay, Power On Issue, No OS, Undefined, Display Issue, etc.)
 *  9. Physical Grade (A+, A, B, C)
 * 10. Remarks
 * 11. Shelf Location
 * 12. Timestamp
 * 
 * Features:
 * - Atomic concurrency lock prevents simultaneous write conflicts.
 * - Deduplication engine: prevents duplicate entries if button is clicked twice.
 * - Updates existing record if the same serial was already cataloged.
 * - Auto-formats headers and status badge colors on row creation.
 */

function doPost(e) {
  var lock = LockService.getScriptLock();
  try {
    lock.waitLock(10000); // 10-second queue lock
  } catch (lockErr) {
    return ContentService.createTextOutput(JSON.stringify({
      status: "ERROR",
      message: "Server busy, please retry in a moment."
    })).setMimeType(ContentService.MimeType.JSON);
  }

  try {
    var ss = SpreadsheetApp.getActiveSpreadsheet();
    var sheet = ss.getSheetByName("AutoMater_Inventory");
    if (!sheet) {
      sheet = ss.getActiveSheet();
      if (sheet.getName() === "Sheet1") {
        sheet.setName("AutoMater_Inventory");
      }
    }

    // Step 1: Auto-initialize headers if the sheet is empty
    if (sheet.getLastRow() === 0) {
      var headers = [
        "Asset Tag",
        "Serial Number",
        "Model",
        "Processor",
        "Memory (RAM/SSD)",
        "Battery Health (%)",
        "Status",
        "Work In Progress Issue",
        "Physical Grade",
        "Remarks",
        "Shelf Location",
        "Timestamp"
      ];
      sheet.appendRow(headers);

      var headerRange = sheet.getRange(1, 1, 1, headers.length);
      headerRange.setBackground("#0F172A");     // Slate navy background
      headerRange.setFontColor("#38BDF8");      // Cyan bold text
      headerRange.setFontWeight("bold");
      headerRange.setFontFamily("Roboto");
      headerRange.setHorizontalAlignment("center");
      sheet.setFrozenRows(1);
    }

    // Step 2: Parse incoming JSON payload
    var payload = {};
    if (e && e.postData && e.postData.contents) {
      payload = JSON.parse(e.postData.contents);
    }

    var assetTag      = payload.Asset_Tag || payload.assetTag || "";
    var serialNo      = payload.Serial_Number || payload.serialNumber || "";
    var model         = payload.Model || payload.model || "";
    var processor     = payload.Processor || payload.processor || "";
    var memory        = payload.Memory || payload.ramStorage || "";
    var batteryHealth = payload.Battery_Health || payload.batteryHealth || "100";
    var status        = payload.Status || payload.status || "RTS";
    var wipIssue      = payload.Wip_Issue || payload.wipIssue || "All Okay";
    var physicalGrade = payload.Physical_Grade || payload.physicalGrade || "A+";
    var remarks       = payload.Remarks || payload.remarks || "";
    var shelf         = payload.Shelf_Location || payload.shelf || "";
    var timestamp     = payload.Timestamp || Utilities.formatDate(new Date(), Session.getScriptTimeZone(), "yyyy-MM-dd HH:mm:ss");

    // Clean battery health as plain number (0-100)
    var numBattery = parseInt(batteryHealth.toString().replace(/[^0-9]/g, ""), 10);
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

    // Step 3: Deduplication Guard — Check if serial or asset tag was added in the last 45 seconds or already exists
    if (lastRow > 1 && (serialNo || assetTag)) {
      // Check column A (Asset Tag) and column B (Serial Number)
      var rangeValues = sheet.getRange(2, 1, lastRow - 1, 2).getValues();
      var timeValues  = sheet.getRange(2, 12, lastRow - 1, 1).getValues();
      var nowMs = new Date().getTime();

      for (var i = rangeValues.length - 1; i >= 0; i--) {
        var rowAsset  = rangeValues[i][0] ? rangeValues[i][0].toString().trim().toUpperCase() : "";
        var rowSerial = rangeValues[i][1] ? rangeValues[i][1].toString().trim().toUpperCase() : "";
        var isMatch = false;

        if (serialNo && serialNo !== "N/A" && serialNo !== "UNKNOWN" && rowSerial === serialNo.trim().toUpperCase()) {
          isMatch = true;
        } else if (assetTag && assetTag !== "UNKNOWN" && rowAsset === assetTag.trim().toUpperCase()) {
          isMatch = true;
        }

        if (isMatch) {
          targetRow = i + 2;
          // Check if this row was posted very recently (within 45 seconds) to prevent double-click duplicates
          var rowTime = new Date(timeValues[i][0]).getTime();
          if (!isNaN(rowTime) && (nowMs - rowTime < 45000)) {
            // It's a duplicate request from rapid clicking — update row and return
            sheet.getRange(targetRow, 1, 1, newRow.length).setValues([newRow]);
            return ContentService.createTextOutput(JSON.stringify({
              status: "OK",
              action: "DEDUPLICATED",
              row: targetRow,
              message: "Duplicate prevented. Record updated at Row " + targetRow
            })).setMimeType(ContentService.MimeType.JSON);
          }

          // Otherwise update existing record
          sheet.getRange(targetRow, 1, 1, newRow.length).setValues([newRow]);
          updated = true;
          break;
        }
      }
    }

    // Step 4: If new entry, append to sheet
    if (!updated) {
      sheet.appendRow(newRow);
      targetRow = sheet.getLastRow();
    }

    // Step 5: Format data row styling & status badge colors
    var dataRowRange = sheet.getRange(targetRow, 1, 1, newRow.length);
    dataRowRange.setFontFamily("Roboto");
    dataRowRange.setVerticalAlignment("middle");

    // Colorize Status cell (Column 7)
    var statusCell = sheet.getRange(targetRow, 7);
    statusCell.setFontWeight("bold");
    statusCell.setHorizontalAlignment("center");
    if (status === "RTS" || status.indexOf("Ready") !== -1) {
      statusCell.setBackground("#DCFCE7").setFontColor("#15803D"); // Soft green
    } else if (status === "WIP" || status.indexOf("Progress") !== -1) {
      statusCell.setBackground("#FEF3C7").setFontColor("#B45309"); // Soft amber
    } else if (status === "RFR" || status.indexOf("Repair") !== -1 || status.indexOf("Flagged") !== -1) {
      statusCell.setBackground("#FEE2E2").setFontColor("#B91C1C"); // Soft red
    } else if (status === "SOLD") {
      statusCell.setBackground("#E0E7FF").setFontColor("#3730A3"); // Soft indigo
    }

    // Format Battery Health cell (Column 6)
    var battCell = sheet.getRange(targetRow, 6);
    battCell.setHorizontalAlignment("center");

    return ContentService
      .createTextOutput(JSON.stringify({
        status: "OK",
        action: updated ? "UPDATED" : "APPENDED",
        row: targetRow,
        asset: assetTag + " / " + serialNo,
        message: updated ? ("Updated record at Row " + targetRow) : ("Added new record at Row " + targetRow)
      }))
      .setMimeType(ContentService.MimeType.JSON);

  } catch (err) {
    return ContentService
      .createTextOutput(JSON.stringify({
        status: "ERROR",
        message: err.toString()
      }))
      .setMimeType(ContentService.MimeType.JSON);
  } finally {
    lock.releaseLock();
  }
}

/**
 * Health check / browser endpoint
 */
function doGet(e) {
  var html = '<!DOCTYPE html><html><head><title>AutoMater Sync Online</title>' +
    '<style>' +
    'body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; background: #0F172A; color: #F8FAFC; display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; }' +
    '.card { background: #1E293B; border: 1px solid #38BDF8; border-radius: 12px; padding: 36px; max-width: 480px; text-align: center; box-shadow: 0 10px 25px rgba(0,0,0,0.5); }' +
    'h2 { color: #38BDF8; margin: 0 0 12px 0; }' +
    'p { color: #94A3B8; font-size: 14px; line-height: 1.6; }' +
    '.badge { display: inline-block; background: #064E3B; color: #34D399; font-weight: bold; padding: 6px 14px; border-radius: 20px; font-size: 13px; margin-bottom: 16px; }' +
    '</style></head>' +
    '<body><div class="card">' +
    '<div class="badge">● ONLINE & ACTIVE</div>' +
    '<h2>AutoMater Google Sheets Webhook</h2>' +
    '<p>Ready to receive QC data with the updated column order and automatic duplicate protection.</p>' +
    '</div></body></html>';
  return HtmlService.createHtmlOutput(html).setTitle("AutoMater Google Sheets Webhook");
}
