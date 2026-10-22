using System;

namespace Velox.Shared.Common;

/// <summary>
/// Standardized diagnostic, licensing, network, and cloud error codes across VeloxBench, VeloxAPI, and VeloxManager.
/// </summary>
public enum VeloxErrorCode
{
    // 000 - 099: Success
    OK_SUCCESS = 0,

    // 100 - 199: Network & Connectivity
    ERR_NET_UNREACHABLE = 100, // No internet, Wi-Fi adapter down, or gateway unreachable
    ERR_NET_TIMEOUT = 101,     // Network request timed out (e.g. > 5 seconds)
    ERR_NET_DNS_FAILURE = 102, // Failed to resolve cloud host (e.g. api.veloxbench.com)
    ERR_NET_TLS_FAILURE = 103, // SSL/TLS handshake failed or self-signed cert rejected

    // 200 - 299: Authentication & Licensing
    ERR_AUTH_INVALID_KEY = 200,   // HTTP 401: Invalid API key or missing authorization header
    ERR_AUTH_FORBIDDEN = 201,     // HTTP 403: Tenant mismatch or disabled account
    ERR_LIC_EXPIRED = 202,        // License validity expired
    ERR_LIC_QUOTA_EXCEEDED = 203, // Monthly certified laptop scan limit reached
    ERR_LIC_LEASE_INVALID = 204,  // Offline lease HMAC signature invalid / tampered
    ERR_LIC_NOT_FOUND = 205,      // license.key missing beside portable executable
    ERR_LIC_SEATS_EXCEEDED = 206, // Maximum workstation seats exceeded for plan
    ERR_LIC_FEATURE_UNAVAILABLE = 207, // Feature not included in current plan (e.g. cloud sync on BenchSolo)

    // 300 - 399: Cloud Server & Payload Validation
    ERR_CLOUD_SERVER_ERROR = 300, // HTTP 500: Server internal error
    ERR_CLOUD_BAD_GATEWAY = 302,  // HTTP 502/504: Reverse proxy or container offline
    ERR_CLOUD_BAD_REQUEST = 304,  // HTTP 400: Malformed JSON or invalid serial number

    // 400 - 499: Hardware Diagnostics & Peripheral
    ERR_PRINTER_NOT_FOUND = 400,    // Thermal label printer disconnected or offline
    ERR_PRINTER_SPOOL_FAILED = 401, // Windows print spooler error
    ERR_BATTERY_COMM_LOST = 402,    // WMI/ACPI battery bus returned invalid charge/wear
    ERR_SMART_READ_FAILED = 403,    // NVMe/SATA controller denied SMART health telemetry

    // 500 - 599: Auto-Updater
    ERR_UPD_CHECK_FAILED = 500,       // Unable to check for releases
    ERR_UPD_DOWNLOAD_FAILED = 501,    // Download stream interrupted
    ERR_UPD_CHECKSUM_MISMATCH = 502,  // SHA-256 hash mismatch (corrupt payload)
    ERR_UPD_FILE_LOCKED = 503,        // Windows locked running executable from rename

    // 900 - 999: Internal / Unhandled
    ERR_INTERNAL_EXCEPTION = 999
}

public class VeloxResult
{
    public bool Success { get; set; }
    public VeloxErrorCode Code { get; set; } = VeloxErrorCode.OK_SUCCESS;
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }

    public string CodeString => Code switch
    {
        VeloxErrorCode.OK_SUCCESS => "VLX-OK-200",
        VeloxErrorCode.ERR_NET_UNREACHABLE => "VLX-NET-100",
        VeloxErrorCode.ERR_NET_TIMEOUT => "VLX-NET-101",
        VeloxErrorCode.ERR_NET_DNS_FAILURE => "VLX-NET-102",
        VeloxErrorCode.ERR_NET_TLS_FAILURE => "VLX-NET-103",
        VeloxErrorCode.ERR_AUTH_INVALID_KEY => "VLX-AUTH-200",
        VeloxErrorCode.ERR_AUTH_FORBIDDEN => "VLX-AUTH-201",
        VeloxErrorCode.ERR_LIC_EXPIRED => "VLX-LIC-202",
        VeloxErrorCode.ERR_LIC_QUOTA_EXCEEDED => "VLX-LIC-203",
        VeloxErrorCode.ERR_LIC_LEASE_INVALID => "VLX-LIC-204",
        VeloxErrorCode.ERR_LIC_NOT_FOUND => "VLX-LIC-205",
        VeloxErrorCode.ERR_LIC_SEATS_EXCEEDED => "VLX-LIC-206",
        VeloxErrorCode.ERR_LIC_FEATURE_UNAVAILABLE => "VLX-LIC-207",
        VeloxErrorCode.ERR_CLOUD_SERVER_ERROR => "VLX-SRV-300",
        VeloxErrorCode.ERR_CLOUD_BAD_GATEWAY => "VLX-SRV-302",
        VeloxErrorCode.ERR_CLOUD_BAD_REQUEST => "VLX-DATA-304",
        VeloxErrorCode.ERR_PRINTER_NOT_FOUND => "VLX-PRN-400",
        VeloxErrorCode.ERR_PRINTER_SPOOL_FAILED => "VLX-PRN-401",
        VeloxErrorCode.ERR_BATTERY_COMM_LOST => "VLX-DIAG-402",
        VeloxErrorCode.ERR_SMART_READ_FAILED => "VLX-DIAG-403",
        VeloxErrorCode.ERR_UPD_CHECK_FAILED => "VLX-UPD-500",
        VeloxErrorCode.ERR_UPD_DOWNLOAD_FAILED => "VLX-UPD-501",
        VeloxErrorCode.ERR_UPD_CHECKSUM_MISMATCH => "VLX-UPD-502",
        VeloxErrorCode.ERR_UPD_FILE_LOCKED => "VLX-UPD-503",
        _ => "VLX-ERR-999"
    };

    public string UserDisplayTitle => Success ? $"SUCCESS [{CodeString}]" : $"FAILED [{CodeString}]";

    public static VeloxResult Ok(string message = "Operation completed successfully") =>
        new() { Success = true, Code = VeloxErrorCode.OK_SUCCESS, Message = message };

    public static VeloxResult Fail(VeloxErrorCode code, string message, string? details = null) =>
        new() { Success = false, Code = code, Message = message, Details = details };
}

public class VeloxResult<T> : VeloxResult
{
    public T? Data { get; set; }

    public static VeloxResult<T> Ok(T data, string message = "Operation completed successfully") =>
        new() { Success = true, Code = VeloxErrorCode.OK_SUCCESS, Message = message, Data = data };

    public new static VeloxResult<T> Fail(VeloxErrorCode code, string message, string? details = null) =>
        new() { Success = false, Code = code, Message = message, Details = details, Data = default };
}
