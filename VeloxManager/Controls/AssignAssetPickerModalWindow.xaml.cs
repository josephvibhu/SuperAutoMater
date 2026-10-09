using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Velox.Shared.Dtos;
using VeloxManager.Services;
using VeloxManager.ViewModels;

namespace VeloxManager.Controls;

public partial class AssignAssetPickerModalWindow : Window
{
    private readonly EmployeeDto _technician;
    private readonly List<AssetItemViewModel> _allPoolAssets;
    private List<AssetItemViewModel> _filteredAssets = new();
    private readonly ManagerApiClient _api;

    public AssetItemViewModel? AssignedAsset { get; private set; }

    public AssignAssetPickerModalWindow(EmployeeDto technician, IEnumerable<AssetItemViewModel> poolAssets, ManagerApiClient api)
    {
        InitializeComponent();
        _technician = technician;
        _api = api;

        // Assets that are not assigned to this tech, preferably unassigned or WIP
        _allPoolAssets = poolAssets
            .Where(a => a.AssignedEmployeeId != technician.Id && a.Status != Velox.Shared.Models.LifecycleQueue.Sold)
            .OrderBy(a => string.IsNullOrWhiteSpace(a.AssignedEmployeeId) ? 0 : 1) // Prioritize unassigned
            .ThenByDescending(a => a.UpdatedAtUtc)
            .ToList();

        TxtTechHeader.Text = $"{technician.Name} ({technician.StationId})";
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = TxtSearch.Text?.Trim().ToLowerInvariant() ?? "";
        if (string.IsNullOrWhiteSpace(q))
        {
            _filteredAssets = new List<AssetItemViewModel>(_allPoolAssets);
        }
        else
        {
            _filteredAssets = _allPoolAssets.Where(a =>
                (a.AssetTag?.ToLowerInvariant().Contains(q) == true) ||
                (a.SerialNumber?.ToLowerInvariant().Contains(q) == true) ||
                (a.ModelName?.ToLowerInvariant().Contains(q) == true) ||
                (a.CurrentLocation?.ToLowerInvariant().Contains(q) == true) ||
                (a.CategoryBadge?.ToLowerInvariant().Contains(q) == true)
            ).ToList();
        }

        GridAssets.ItemsSource = _filteredAssets;
        TxtAvailableCount.Text = $"{_filteredAssets.Count} Available Units";
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private async void DispatchSelected()
    {
        if (GridAssets.SelectedItem is not AssetItemViewModel sel)
        {
            TxtFeedback.Text = "Please select an asset from the list to assign.";
            return;
        }

        BtnAssign.IsEnabled = false;
        TxtFeedback.Text = $"Dispatching {sel.AssetTag} to {_technician.Name}...";

        try
        {
            var ok = await _api.AssignAssetAsync(sel.Id, _technician.Id);
            if (ok)
            {
                sel.AssignedEmployeeId = _technician.Id;
                sel.AssignedEmployeeName = _technician.Name;
                AssignedAsset = sel;
                DialogResult = true;
                Close();
            }
            else
            {
                TxtFeedback.Text = "Failed to dispatch asset via Cloud API.";
                BtnAssign.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            TxtFeedback.Text = $"Error: {ex.Message}";
            BtnAssign.IsEnabled = true;
        }
    }

    private void BtnAssign_Click(object sender, RoutedEventArgs e)
    {
        DispatchSelected();
    }

    private void GridAssets_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        DispatchSelected();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
