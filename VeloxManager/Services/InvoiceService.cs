using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Velox.Shared.Models;

namespace VeloxManager.Services;

public enum InvoiceType
{
    SalesInvoice,
    PurchaseBill
}

public enum InvoiceItemCategory
{
    BaseMachine,
    RamUpgrade,
    StorageUpgrade,
    StorageDowngrade,
    Charger,
    Peripheral,
    SparePart,
    CustomLaborOrDiscount
}

public class InvoiceItem
{
    public int LineNumber { get; set; }
    public string AssetId { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string Model { get; set; } = "";
    public string Grade { get; set; } = "Grade A";
    public string Description { get; set; } = "";
    public InvoiceItemCategory ItemCategory { get; set; } = InvoiceItemCategory.BaseMachine;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal LineTotal => UnitPrice * Quantity;

    public string ItemBadge => ItemCategory switch
    {
        InvoiceItemCategory.BaseMachine => "BASE UNIT",
        InvoiceItemCategory.RamUpgrade => "RAM UPGRADE",
        InvoiceItemCategory.StorageUpgrade => "SSD UPGRADE",
        InvoiceItemCategory.StorageDowngrade => "SSD DEDUCTION",
        InvoiceItemCategory.Charger => "CHARGER",
        InvoiceItemCategory.Peripheral => "PERIPHERAL",
        InvoiceItemCategory.SparePart => "REPAIR SPARE",
        InvoiceItemCategory.CustomLaborOrDiscount => "CUSTOM / LABOR",
        _ => "ITEM"
    };

    public string BadgeColorHex => ItemCategory switch
    {
        InvoiceItemCategory.BaseMachine => "#0284c7",
        InvoiceItemCategory.RamUpgrade => "#7c3aed",
        InvoiceItemCategory.StorageUpgrade => "#ea580c",
        InvoiceItemCategory.StorageDowngrade => "#b91c1c",
        InvoiceItemCategory.Charger => "#16a34a",
        InvoiceItemCategory.Peripheral => "#db2777",
        InvoiceItemCategory.SparePart => "#ca8a04",
        InvoiceItemCategory.CustomLaborOrDiscount => "#475569",
        _ => "#475569"
    };
}

public class Invoice
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    public string InvoiceNumber { get; set; } = $"INV-{DateTime.Now:yyyyMMdd}-{new Random().Next(100, 999)}";
    public InvoiceType Type { get; set; } = InvoiceType.SalesInvoice;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime DueDate { get; set; } = DateTime.Now.AddDays(30);
    public string PartyName { get; set; } = "Customer / Retail Client";
    public string PartyPhone { get; set; } = "";
    public string PartyAddress { get; set; } = "Retail Depot Counter";
    public string PartyEmail { get; set; } = "counter@veloxdepot.internal";
    public string PaymentMode { get; set; } = "UPI / Cash / Card";
    public string Notes { get; set; } = "Refurbished Hardware Certified. 30 Days Replacement Depot Warranty Included.";
    public decimal TaxPercent { get; set; } = 18.0m;

    public List<InvoiceItem> Items { get; set; } = new();

    public decimal SubTotal => Items.Sum(i => i.LineTotal);
    public decimal TaxAmount => Math.Round(SubTotal * (TaxPercent / 100m), 2);
    public decimal TotalAmount => SubTotal + TaxAmount;
}

public static class InvoiceService
{
    public static string GenerateHtml(Invoice inv)
    {
        var typeTitle = inv.Type == InvoiceType.SalesInvoice ? "TAX INVOICE / SALE BILL" : "PURCHASE / INWARD BILL";
        var partyLabel = inv.Type == InvoiceType.SalesInvoice ? "Billed To (Customer)" : "Billed From (Supplier)";

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><meta charset='utf-8'><title>" + inv.InvoiceNumber + "</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; background: #f8fafc; color: #1e293b; margin: 0; padding: 30px; }");
        sb.AppendLine(".invoice-box { max-width: 880px; margin: auto; padding: 32px; border: 1px solid #e2e8f0; background: #ffffff; border-radius: 8px; box-shadow: 0 4px 6px -1px rgba(0,0,0,0.1); }");
        sb.AppendLine(".header { display: flex; justify-content: space-between; border-bottom: 2px solid #0284c7; padding-bottom: 20px; margin-bottom: 24px; }");
        sb.AppendLine(".brand h1 { margin: 0; color: #0284c7; font-size: 26px; font-weight: 800; letter-spacing: -0.5px; }");
        sb.AppendLine(".brand p { margin: 3px 0 0; color: #64748b; font-size: 12px; }");
        sb.AppendLine(".brand .gstin { margin-top: 6px; font-size: 11px; font-weight: 600; color: #0f172a; background: #f1f5f9; padding: 3px 8px; border-radius: 4px; display: inline-block; }");
        sb.AppendLine(".inv-meta { text-align: right; }");
        sb.AppendLine(".inv-meta h2 { margin: 0; color: #0f172a; font-size: 20px; }");
        sb.AppendLine(".inv-meta p { margin: 2px 0 0; color: #64748b; font-size: 12px; }");
        sb.AppendLine(".parties { display: flex; justify-content: space-between; margin-bottom: 24px; }");
        sb.AppendLine(".party { width: 46%; background: #f8fafc; padding: 14px; border-radius: 6px; border: 1px solid #f1f5f9; }");
        sb.AppendLine(".party h3 { margin: 0 0 6px; font-size: 12px; text-transform: uppercase; color: #64748b; letter-spacing: 0.5px; }");
        sb.AppendLine(".party p { margin: 2px 0; font-size: 13px; font-weight: 500; }");
        sb.AppendLine("table { width: 100%; border-collapse: collapse; margin-bottom: 24px; }");
        sb.AppendLine("th { background: #f1f5f9; color: #475569; font-size: 11px; text-transform: uppercase; padding: 10px 8px; text-align: left; border-bottom: 2px solid #cbd5e1; }");
        sb.AppendLine("td { padding: 12px 8px; border-bottom: 1px solid #e2e8f0; font-size: 13px; }");
        sb.AppendLine(".text-right { text-align: right; }");
        sb.AppendLine(".badge { display: inline-block; font-size: 10px; font-weight: 700; color: #ffffff; padding: 2px 8px; border-radius: 4px; text-transform: uppercase; }");
        sb.AppendLine(".totals-container { display: flex; justify-content: space-between; margin-bottom: 24px; }");
        sb.AppendLine(".payment-info { width: 45%; background: #f8fafc; padding: 14px; border-radius: 6px; border: 1px solid #f1f5f9; font-size: 12px; }");
        sb.AppendLine(".totals { width: 40%; }");
        sb.AppendLine(".totals table td { border: none; padding: 5px 8px; }");
        sb.AppendLine(".total-row td { font-size: 17px; font-weight: bold; color: #0284c7; border-top: 2px solid #e2e8f0; padding-top: 10px; }");
        sb.AppendLine(".footer { border-top: 1px solid #e2e8f0; padding-top: 16px; text-align: center; color: #94a3b8; font-size: 11px; }");
        sb.AppendLine("@media print { body { background: #fff; padding: 0; } .invoice-box { box-shadow: none; border: none; } }");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine("<div class='invoice-box'>");
        sb.AppendLine("<div class='header'>");
        sb.AppendLine("<div class='brand'>");
        sb.AppendLine("<h1>⚡ VELOX DEPOT OPERATIONS</h1>");
        sb.AppendLine("<p>Enterprise Hardware Refurbishment &amp; Diagnostics Suite</p>");
        sb.AppendLine("<div class='gstin'>DEPOT GSTIN: 29AAAAA0000A1Z5 | PAN: AAAAA0000A</div>");
        sb.AppendLine("</div>");

        sb.AppendLine($"<div class='inv-meta'><h2>{typeTitle}</h2><p><strong>Invoice #:</strong> {inv.InvoiceNumber}</p><p><strong>Date:</strong> {inv.CreatedAt:yyyy-MM-dd HH:mm}</p><p><strong>Due Date:</strong> {inv.DueDate:yyyy-MM-dd}</p></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class='parties'>");
        sb.AppendLine($"<div class='party'><h3>{partyLabel}</h3><p style='font-size:15px; font-weight:700; color:#0f172a;'>{inv.PartyName}</p>");
        if (!string.IsNullOrWhiteSpace(inv.PartyPhone)) sb.AppendLine($"<p>Phone: {inv.PartyPhone}</p>");
        sb.AppendLine($"<p>{inv.PartyAddress}</p><p>{inv.PartyEmail}</p></div>");

        sb.AppendLine("<div class='party'><h3>Depot Facility</h3><p style='font-size:15px; font-weight:700; color:#0f172a;'>Velox Operations Hub #1</p><p>Chassis Diagnostics &amp; Assembly Division</p><p>Email: ops@veloxdepot.internal</p><p>Phone: +91 (Depot Counter)</p></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<table>");
        sb.AppendLine("<thead><tr><th>#</th><th>Type</th><th>Serial / Tag</th><th>Item Description &amp; Configuration</th><th>Grade</th><th class='text-right'>Qty</th><th class='text-right'>Rate</th><th class='text-right'>Amount</th></tr></thead>");
        sb.AppendLine("<tbody>");

        int line = 1;
        foreach (var item in inv.Items)
        {
            var formattedPrice = item.UnitPrice < 0 ? $"-₹{Math.Abs(item.UnitPrice):N2}" : $"₹{item.UnitPrice:N2}";
            var formattedTotal = item.LineTotal < 0 ? $"-₹{Math.Abs(item.LineTotal):N2}" : $"₹{item.LineTotal:N2}";
            var totalColor = item.LineTotal < 0 ? "color:#b91c1c;" : "color:#0f172a;";

            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>{line++}</td>");
            sb.AppendLine($"<td><span class='badge' style='background:{item.BadgeColorHex};'>{item.ItemBadge}</span></td>");
            sb.AppendLine($"<td><strong>{(string.IsNullOrWhiteSpace(item.SerialNumber) ? "—" : item.SerialNumber)}</strong></td>");
            sb.AppendLine($"<td><strong>{item.Model}</strong><br><span style='color:#64748b; font-size:11px;'>{item.Description}</span></td>");
            sb.AppendLine($"<td><span style='background:#f1f5f9; color:#334155; padding:2px 6px; border-radius:4px; font-weight:600; font-size:10px;'>{item.Grade}</span></td>");
            sb.AppendLine($"<td class='text-right'>{item.Quantity}</td>");
            sb.AppendLine($"<td class='text-right'>{formattedPrice}</td>");
            sb.AppendLine($"<td class='text-right' style='font-weight:700; {totalColor}'>{formattedTotal}</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</tbody></table>");

        sb.AppendLine("<div class='totals-container'>");
        sb.AppendLine("<div class='payment-info'>");
        sb.AppendLine("<p><strong>Payment Mode:</strong> " + inv.PaymentMode + "</p>");
        sb.AppendLine("<p><strong>Terms:</strong> " + inv.Notes + "</p>");
        sb.AppendLine("<p style='margin-top:10px; color:#64748b;'>✓ Tested with VeloxBench Automated Hardware Diagnostics Suite.</p>");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class='totals'><table>");
        sb.AppendLine($"<tr><td>Taxable Subtotal:</td><td class='text-right'>₹{inv.SubTotal:N2}</td></tr>");
        if (inv.TaxPercent > 0)
        {
            var halfTax = Math.Round(inv.TaxAmount / 2m, 2);
            var halfPercent = inv.TaxPercent / 2m;
            sb.AppendLine($"<tr><td>CGST ({halfPercent:G}%):</td><td class='text-right'>₹{halfTax:N2}</td></tr>");
            sb.AppendLine($"<tr><td>SGST ({halfPercent:G}%):</td><td class='text-right'>₹{halfTax:N2}</td></tr>");
        }
        sb.AppendLine($"<tr class='total-row'><td>Grand Total:</td><td class='text-right'>₹{inv.TotalAmount:N2}</td></tr>");
        sb.AppendLine("</table></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class='footer'>");
        sb.AppendLine("<p>This is a computer generated invoice powered by VeloxManager v1.0.0 Refurbishment Suite.</p>");
        sb.AppendLine("</div>");

        sb.AppendLine("</div></body></html>");

        return sb.ToString();
    }
}
