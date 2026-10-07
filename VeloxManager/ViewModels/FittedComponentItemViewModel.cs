using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Velox.Shared.Dtos;
using Velox.Shared.Models;

namespace VeloxManager.ViewModels;

public class FittedComponentItemViewModel : INotifyPropertyChanged
{
    public FittedComponentDto Model { get; }

    public FittedComponentItemViewModel(FittedComponentDto model)
    {
        Model = model;
    }

    public string Id => Model.Id;
    public string Description => Model.Description;
    public string SerialNumber => !string.IsNullOrWhiteSpace(Model.SerialNumber) ? Model.SerialNumber : "Untracked";
    public AssetCategory Category => Model.Category;
    public double CostDelta => Model.CostDelta;
    public double PriceDelta => Model.PriceDelta;
    public string InstalledAtDisplay => Model.InstalledAtUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    public string InstalledBy => Model.InstalledBy ?? "Technician";

    public string CategoryIcon => Category switch
    {
        AssetCategory.RAM => "🧠",
        AssetCategory.Storage => "💾",
        AssetCategory.Battery => "🔋",
        AssetCategory.Display => "🖥️",
        AssetCategory.Charger => "🔌",
        AssetCategory.Peripheral => "🖱️",
        AssetCategory.RepairSpare => "🔧",
        _ => "🧩"
    };

    public string CategoryColorHex => Category switch
    {
        AssetCategory.RAM => "#D29922",
        AssetCategory.Storage => "#58A6FF",
        AssetCategory.Battery => "#3FB950",
        AssetCategory.Display => "#A371F7",
        AssetCategory.Charger => "#F0883E",
        AssetCategory.RepairSpare => "#F85149",
        _ => "#8B949E"
    };

    public string PriceDeltaDisplay => PriceDelta != 0 ? $"₹{PriceDelta:N0}" : "Included (₹0)";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
