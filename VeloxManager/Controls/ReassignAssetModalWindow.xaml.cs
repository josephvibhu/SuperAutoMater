using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Velox.Shared.Dtos;
using VeloxManager.Services;
using VeloxManager.ViewModels;

namespace VeloxManager.Controls;

public partial class ReassignAssetModalWindow : Window
{
    private readonly AssetItemViewModel _asset;
    private readonly List<EmployeeDto> _employees;
    private readonly ManagerApiClient _api;

    public bool Changed { get; private set; }

    public class TechComboItem
    {
        public string? EmployeeId { get; set; }
        public string DisplayText { get; set; } = "";
        public bool IsUnassign { get; set; }

        public override string ToString() => DisplayText;
    }

    public ReassignAssetModalWindow(AssetItemViewModel asset, IEnumerable<EmployeeDto> employees, ManagerApiClient api, string? preselectEmployeeId = null)
    {
        InitializeComponent();
        _asset = asset;
        _employees = employees.ToList();
        _api = api;

        TxtAssetTag.Text = !string.IsNullOrWhiteSpace(asset.AssetTag) ? asset.AssetTag : "NO TAG";
        TxtSerialNumber.Text = !string.IsNullOrWhiteSpace(asset.SerialNumber) ? asset.SerialNumber : "NO SERIAL";
        TxtModel.Text = !string.IsNullOrWhiteSpace(asset.ModelName) ? asset.ModelName : "Generic Asset";
        TxtCurrentAssigned.Text = !string.IsNullOrWhiteSpace(asset.AssignedEmployeeName)
            ? $"👤 {asset.AssignedEmployeeName}"
            : "📦 Unassigned (Depot Queue)";
        TxtQueueStatus.Text = asset.Status.ToString();

        // Populate Technicians dropdown
        var items = new List<TechComboItem>();

        // Option 1: Unassign
        items.Add(new TechComboItem
        {
            EmployeeId = null,
            DisplayText = "📦 [Unassign / Return to General Depot Queue]",
            IsUnassign = true
        });

        // Other options: Employees
        foreach (var emp in _employees.Where(e => e.IsActive).OrderBy(e => e.Name))
        {
            items.Add(new TechComboItem
            {
                EmployeeId = emp.Id,
                DisplayText = $"👤 {emp.Name}  [{emp.StationId} • {emp.Role} • {emp.WorkloadCount} WIP]",
                IsUnassign = false
            });
        }

        CmbTechnician.ItemsSource = items;

        // Select preselect or current or first
        if (!string.IsNullOrWhiteSpace(preselectEmployeeId))
        {
            var match = items.FirstOrDefault(i => i.EmployeeId == preselectEmployeeId);
            if (match != null) CmbTechnician.SelectedItem = match;
            else CmbTechnician.SelectedIndex = 0;
        }
        else if (!string.IsNullOrWhiteSpace(asset.AssignedEmployeeId))
        {
            var match = items.FirstOrDefault(i => i.EmployeeId == asset.AssignedEmployeeId);
            if (match != null) CmbTechnician.SelectedItem = match;
            else CmbTechnician.SelectedIndex = 0;
        }
        else
        {
            CmbTechnician.SelectedIndex = 0;
        }
    }

    private async void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (CmbTechnician.SelectedItem is not TechComboItem sel) return;

        BtnConfirm.IsEnabled = false;
        TxtFeedback.Text = "Applying assignment...";

        try
        {
            var targetEmpId = sel.IsUnassign ? "" : (sel.EmployeeId ?? "");
            var ok = await _api.AssignAssetAsync(_asset.Id, targetEmpId);

            if (ok)
            {
                Changed = true;

                // If dispatch note provided, append note/event
                var note = TxtDispatchNote.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(note))
                {
                    try
                    {
                        await _api.AppendAssetEventAsync(_asset.Id, new AppendEventRequest
                        {
                            EventType = "BenchInstruction",
                            Actor = ManagerConfigService.Current.ManagerName,
                            NotesJson = $"{{\"instruction\":\"{note.Replace("\"", "\\\"")}\"}}"
                        });
                    }
                    catch { }
                }

                // Update local model in memory
                if (sel.IsUnassign)
                {
                    _asset.AssignedEmployeeId = "";
                    _asset.AssignedEmployeeName = "";
                }
                else
                {
                    var emp = _employees.FirstOrDefault(em => em.Id == sel.EmployeeId);
                    _asset.AssignedEmployeeId = sel.EmployeeId ?? "";
                    _asset.AssignedEmployeeName = emp?.Name ?? "";
                }

                DialogResult = true;
                Close();
            }
            else
            {
                TxtFeedback.Text = "Failed to update assignment via Cloud API.";
                BtnConfirm.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            TxtFeedback.Text = $"Error: {ex.Message}";
            BtnConfirm.IsEnabled = true;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
