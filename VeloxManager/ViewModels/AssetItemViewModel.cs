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
    public string Customer => Model.Customer;
    public int StorageHealth => Model.StorageHealth ?? 100;
    public string MissingComponents => Model.MissingComponents;
    public string Notes => Model.Notes;
    public DateTimeOffset UpdatedAt => Model.UpdatedAtUtc;

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
