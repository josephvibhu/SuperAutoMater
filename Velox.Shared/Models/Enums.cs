using System.Text.Json.Serialization;

namespace Velox.Shared.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
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

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AssetGrade
{
    Unknown,
    A,
    B,
    C,
    Faulty,
    ForParts
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EmployeeRole
{
    Technician,
    Manager,
    Owner
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SseEventType
{
    AssetUpdated,
    AssetAssigned,
    AssetEventAdded,
    EmployeeUpdated
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AssetCategory
{
    Laptop,
    Desktop,
    RAM,
    Storage,
    Charger,
    Peripheral,
    RepairSpare
}

