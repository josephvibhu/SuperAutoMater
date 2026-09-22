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

public class InvoiceItem
{
    public int LineNumber { get; set; }
    public string AssetId { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string Model { get; set; } = "";
    public string Grade { get; set; } = "Grade A";
    public string Description { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal LineTotal => UnitPrice * Quantity;
}

public class Invoice
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    public string InvoiceNumber { get; set; } = $"INV-{DateTime.Now:yyyyMMdd}-{new Random().Next(100, 999)}";
    public InvoiceType Type { get; set; } = InvoiceType.SalesInvoice;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime DueDate { get; set; } = DateTime.Now.AddDays(30);
    public string PartyName { get; set; } = "Acme Corp Refurbishments";
    public string PartyAddress { get; set; } = "123 Business Way, Suite 400";
    public string PartyEmail { get; set; } = "billing@acmecorp.com";
    public string Notes { get; set; } = "Thank you for your business. Terms: Net 30.";
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
        var typeTitle = inv.Type == InvoiceType.SalesInvoice ? "SALES TAX INVOICE" : "PURCHASE BILL";
        var partyLabel = inv.Type == InvoiceType.SalesInvoice ? "Billed To (Customer)" : "Billed From (Supplier)";

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><meta charset='utf-8'><title>" + inv.InvoiceNumber + "</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; background: #f8fafc; color: #1e293b; margin: 0; padding: 40px; }");
        sb.AppendLine(".invoice-box { max-width: 850px; margin: auto; padding: 30px; border: 1px solid #e2e8f0; background: #ffffff; border-radius: 8px; box-shadow: 0 4px 6px -1px rgba(0,0,0,0.1); }");
        sb.AppendLine(".header { display: flex; justify-content: space-between; border-bottom: 2px solid #0284c7; padding-bottom: 20px; margin-bottom: 24px; }");
        sb.AppendLine(".brand h1 { margin: 0; color: #0284c7; font-size: 26px; }");
        sb.AppendLine(".brand p { margin: 2px 0 0; color: #64748b; font-size: 12px; }");
        sb.AppendLine(".inv-meta { text-align: right; }");
        sb.AppendLine(".inv-meta h2 { margin: 0; color: #0f172a; font-size: 20px; }");
        sb.AppendLine(".inv-meta p { margin: 2px 0 0; color: #64748b; font-size: 13px; }");
        sb.AppendLine(".parties { display: flex; justify-content: space-between; margin-bottom: 24px; }");
        sb.AppendLine(".party { width: 45%; }");
        sb.AppendLine(".party h3 { margin: 0 0 6px; font-size: 14px; text-transform: uppercase; color: #64748b; }");
        sb.AppendLine(".party p { margin: 2px 0; font-size: 14px; font-weight: 500; }");
        sb.AppendLine("table { width: 100%; border-collapse: collapse; margin-bottom: 24px; }");
        sb.AppendLine("th { background: #f1f5f9; color: #475569; font-size: 12px; text-transform: uppercase; padding: 10px; text-align: left; border-bottom: 1px solid #cbd5e1; }");
        sb.AppendLine("td { padding: 12px 10px; border-bottom: 1px solid #e2e8f0; font-size: 13px; }");
        sb.AppendLine(".text-right { text-align: right; }");
        sb.AppendLine(".totals { width: 300px; margin-left: auto; margin-bottom: 24px; }");
        sb.AppendLine(".totals table td { border: none; padding: 4px 10px; }");
        sb.AppendLine(".total-row td { font-size: 16px; font-weight: bold; color: #0284c7; border-top: 2px solid #e2e8f0; padding-top: 8px; }");
        sb.AppendLine(".footer { border-top: 1px solid #e2e8f0; padding-top: 16px; text-align: center; color: #94a3b8; font-size: 12px; }");
        sb.AppendLine("@media print { body { background: #fff; padding: 0; } .invoice-box { box-shadow: none; border: none; } }");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine("<div class='invoice-box'>");
        sb.AppendLine("<div class='header'>");
        sb.AppendLine("<div class='brand'><h1>⚡ VELOX DEPOT OPERATIONS</h1><p>Hardware Asset Management & Refurbishment</p></div>");
        sb.AppendLine($"<div class='inv-meta'><h2>{typeTitle}</h2><p><strong>Invoice #:</strong> {inv.InvoiceNumber}</p><p><strong>Date:</strong> {inv.CreatedAt:yyyy-MM-dd}</p><p><strong>Due:</strong> {inv.DueDate:yyyy-MM-dd}</p></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class='parties'>");
        sb.AppendLine($"<div class='party'><h3>{partyLabel}</h3><p><strong>{inv.PartyName}</strong></p><p>{inv.PartyAddress}</p><p>{inv.PartyEmail}</p></div>");
        sb.AppendLine("<div class='party'><h3>Depot Facility</h3><p><strong>Velox Operations Hub</strong></p><p>Facility Site: MAIN</p><p>ops@veloxplatform.internal</p></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<table>");
        sb.AppendLine("<thead><tr><th>#</th><th>Asset Tag / Serial</th><th>Model & Specifications</th><th>Grade</th><th class='text-right'>Qty</th><th class='text-right'>Unit Price</th><th class='text-right'>Line Total</th></tr></thead>");
        sb.AppendLine("<tbody>");

        int line = 1;
        foreach (var item in inv.Items)
        {
            sb.AppendLine($"<tr><td>{line++}</td><td><strong>{item.SerialNumber}</strong></td><td>{item.Model} - {item.Description}</td><td><span style='background:#e0f2fe; color:#0369a1; padding:2px 6px; border-radius:4px; font-weight:bold; font-size:11px;'>{item.Grade}</span></td><td class='text-right'>{item.Quantity}</td><td class='text-right'>${item.UnitPrice:N2}</td><td class='text-right'><strong>${item.LineTotal:N2}</strong></td></tr>");
        }

        sb.AppendLine("</tbody></table>");

        sb.AppendLine("<div class='totals'><table>");
        sb.AppendLine($"<tr><td>Subtotal:</td><td class='text-right'>${inv.SubTotal:N2}</td></tr>");
        sb.AppendLine($"<tr><td>Tax ({inv.TaxPercent}%):</td><td class='text-right'>${inv.TaxAmount:N2}</td></tr>");
        sb.AppendLine($"<tr class='total-row'><td>Grand Total:</td><td class='text-right'>${inv.TotalAmount:N2}</td></tr>");
        sb.AppendLine("</table></div>");

        sb.AppendLine("<div class='footer'>");
        sb.AppendLine($"<p>{inv.Notes}</p>");
        sb.AppendLine("<p>Generated by VeloxManager v1.0.0 — Certified Refurbishment Records</p>");
        sb.AppendLine("</div>");

        sb.AppendLine("</div></body></html>");

        return sb.ToString();
    }
}
