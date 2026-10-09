using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Velox.Shared.Dtos;
using Velox.Shared.Models;

namespace VeloxManager.ViewModels;

public class AssetItemViewModel : INotifyPropertyChanged
{
    public AssetDto Model { get; }

    public string Id => Model.Id;
    public string SerialNumber => Model.SerialNumber;
    public string AssetTag => Model.AssetTag;
    public string ModelName => Model.Model;
    public string Supplier => Model.Supplier;
    public string Customer
    {
        get => Model.Customer;
        set
        {
            if (Model.Customer != value)
            {
                Model.Customer = value;
                OnPropertyChanged();
            }
        }
    }
    public string OutDate
    {
        get => Model.OutDate;
        set
        {
            if (Model.OutDate != value)
            {
                Model.OutDate = value;
                OnPropertyChanged();
            }
        }
    }
    public int StorageHealth => Model.StorageHealth ?? 100;
    public string MissingComponents => Model.MissingComponents;
    public string Notes
    {
        get => Model.Notes;
        set
        {
            if (Model.Notes != value)
            {
                Model.Notes = value;
                OnPropertyChanged();
            }
        }
    }
    public double? PurchasePrice => Model.PurchasePrice;
    public double? SalePrice => Model.SalePrice;
    public DateTimeOffset UpdatedAt => Model.UpdatedAtUtc;

    public AssetCategory Category
    {
        get => Model.Category;
        set
        {
            if (Model.Category != value)
            {
                Model.Category = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CategoryBadge));
                OnPropertyChanged(nameof(CategoryIcon));
                OnPropertyChanged(nameof(CategoryColorHex));
            }
        }
    }

    public int Quantity
    {
        get => Model.Quantity <= 0 ? 1 : Model.Quantity;
        set
        {
            if (Model.Quantity != value)
            {
                Model.Quantity = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(QuantityDisplay));
            }
        }
    }

    public string QuantityDisplay => Category switch
    {
        AssetCategory.Laptop or AssetCategory.Desktop => "1 unit",
        _ => $"{Quantity} pcs"
    };

    public string CategoryIcon => Category switch
    {
        AssetCategory.Laptop => "💻",
        AssetCategory.Desktop => "🖥️",
        AssetCategory.RAM => "🧠",
        AssetCategory.Storage => "💾",
        AssetCategory.Charger => "🔌",
        AssetCategory.Peripheral => "🖱️",
        AssetCategory.RepairSpare => "🔧",
        _ => "📦"
    };

    public string CategoryBadge => Category switch
    {
        AssetCategory.Laptop => "LAPTOP",
        AssetCategory.Desktop => "DESKTOP",
        AssetCategory.RAM => "RAM",
        AssetCategory.Storage => "STORAGE",
        AssetCategory.Charger => "CHARGER",
        AssetCategory.Peripheral => "PERIPHERAL",
        AssetCategory.RepairSpare => "SPARE PART",
        _ => Category.ToString().ToUpperInvariant()
    };

    public string CategoryColorHex => Category switch
    {
        AssetCategory.Laptop => "#58A6FF",
        AssetCategory.Desktop => "#79C0FF",
        AssetCategory.RAM => "#D2A8FF",
        AssetCategory.Storage => "#FFA657",
        AssetCategory.Charger => "#7EE787",
        AssetCategory.Peripheral => "#FF7B72",
        AssetCategory.RepairSpare => "#F2CC60",
        _ => "#8B949E"
    };

    public LifecycleQueue Status
    {
        get => Model.LifecycleQueue;
        set
        {
            if (Model.LifecycleQueue != value)
            {
                Model.LifecycleQueue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusBadge));
                OnPropertyChanged(nameof(StatusColorHex));
            }
        }
    }

    public AssetGrade Grade
    {
        get => Model.Grade;
        set
        {
            if (Model.Grade != value)
            {
                Model.Grade = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GradeBadge));
            }
        }
    }

    public string AssignedEmployeeId
    {
        get => Model.AssignedEmployeeId;
        set
        {
            if (Model.AssignedEmployeeId != value)
            {
                Model.AssignedEmployeeId = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTimeOffset UpdatedAtUtc => Model.UpdatedAtUtc;

    public string AssignedEmployeeName
    {
        get => Model.AssignedEmployeeName;
        set
        {
            if (Model.AssignedEmployeeName != value)
            {
                Model.AssignedEmployeeName = value;
                OnPropertyChanged();
            }
        }
    }

    public string CurrentLocation
    {
        get => Model.CurrentLocation;
        set
        {
            if (Model.CurrentLocation != value)
            {
                Model.CurrentLocation = value;
                OnPropertyChanged();
            }
        }
    }

    public string StatusBadge => Status switch
    {
        LifecycleQueue.IntakeStaging => "INTAKE",
        LifecycleQueue.ActiveTesting => "TESTING",
        LifecycleQueue.AwaitingParts => "AWAITING PARTS",
        LifecycleQueue.ExternalRepair => "IN REPAIR",
        LifecycleQueue.ReadyForSale => "READY FOR SALE",
        LifecycleQueue.Sold => "SOLD",
        LifecycleQueue.Disposed => "DISPOSED",
        LifecycleQueue.OnHold => "ON HOLD",
        _ => Status.ToString().ToUpperInvariant()
    };

    public string StatusColorHex => Status switch
    {
        LifecycleQueue.ReadyForSale => "#3FB950", // Green
        LifecycleQueue.ActiveTesting => "#58A6FF", // Blue
        LifecycleQueue.ExternalRepair => "#D29922", // Orange
        LifecycleQueue.AwaitingParts => "#DB6D28", // Deep orange
        LifecycleQueue.Disposed => "#F85149", // Red
        LifecycleQueue.Sold => "#8B949E", // Gray
        _ => "#58A6FF"
    };

    public string GradeBadge => Grade switch
    {
        AssetGrade.A => "GRADE A",
        AssetGrade.B => "GRADE B",
        AssetGrade.C => "GRADE C",
        AssetGrade.Faulty => "FAULTY",
        AssetGrade.ForParts => "PARTS",
        _ => "PENDING"
    };

    public AssetItemViewModel(AssetDto model)
    {
        Model = model;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
