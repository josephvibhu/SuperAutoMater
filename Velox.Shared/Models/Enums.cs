namespace Velox.Shared.Models;

public enum LifecycleQueue
{
    IntakeStaging,
    ActiveTesting,
    AwaitingParts,
    ExternalRepair,
    ReadyForSale,
    Sold,
    Disposed,
    OnHold
}

public enum AssetGrade
{
    Unknown,
    A,
    B,
    C,
    Faulty,
    ForParts
}

public enum EmployeeRole
{
    Technician,
    Manager,
    Owner
}

public enum SseEventType
{
    AssetUpdated,
    AssetAssigned,
    AssetEventAdded,
    EmployeeUpdated
}
