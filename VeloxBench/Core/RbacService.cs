using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace SuperAutoMater.Wpf.Core
{
    /// <summary>
    /// Canonical action identifiers for Depot OS role-based permission checks.
    /// </summary>
    public static class DepotAction
    {
        public const string Intake = "Intake";
        public const string ExecuteTest = "ExecuteTest";
        public const string RouteRepair = "RouteRepair";
        public const string AuthorizeRetest = "AuthorizeRetest";
        public const string OverrideDispositions = "OverrideDispositions";
        public const string AuthorizeRelease = "AuthorizeRelease";
        public const string AuthorizeDisposal = "AuthorizeDisposal";
        public const string ConfigureSite = "ConfigureSite";
        public const string ManageUsers = "ManageUsers";
        public const string BackupRestore = "BackupRestore";
        public const string ViewAnalytics = "ViewAnalytics";
        public const string ViewKanban = "ViewKanban";
        public const string ViewEvidence = "ViewEvidence";
    }

    /// <summary>
    /// Role-based access control engine enforcing permissions across the 5 canonical roles:
    /// Technician, Supervisor, WarehouseManager, Viewer, Administrator.
    /// Strictly guarantees that Viewer cannot execute any mutate operations.
    /// </summary>
    public sealed class RbacService
    {
        private readonly QcRunStore _store;

        private static readonly HashSet<string> ViewerAllowedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DepotAction.ViewAnalytics,
            DepotAction.ViewKanban,
            DepotAction.ViewEvidence
        };

        private static readonly HashSet<string> TechnicianAllowedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DepotAction.Intake,
            DepotAction.ExecuteTest,
            DepotAction.RouteRepair,
            DepotAction.ViewAnalytics,
            DepotAction.ViewKanban,
            DepotAction.ViewEvidence
        };

        private static readonly HashSet<string> SupervisorAllowedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DepotAction.Intake,
            DepotAction.ExecuteTest,
            DepotAction.RouteRepair,
            DepotAction.AuthorizeRetest,
            DepotAction.OverrideDispositions,
            DepotAction.ViewAnalytics,
            DepotAction.ViewKanban,
            DepotAction.ViewEvidence
        };

        private static readonly HashSet<string> WarehouseManagerAllowedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DepotAction.Intake,
            DepotAction.ExecuteTest,
            DepotAction.RouteRepair,
            DepotAction.AuthorizeRetest,
            DepotAction.OverrideDispositions,
            DepotAction.AuthorizeRelease,
            DepotAction.AuthorizeDisposal,
            DepotAction.ViewAnalytics,
            DepotAction.ViewKanban,
            DepotAction.ViewEvidence
        };

        public RbacService(QcRunStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>
        /// Evaluates whether the given role is authorized to perform the requested action.
        /// </summary>
        public static bool HasPermission(DepotRole role, string action)
        {
            if (string.IsNullOrWhiteSpace(action)) return false;

            return role switch
            {
                DepotRole.Administrator => true, // Full system authority
                DepotRole.WarehouseManager => WarehouseManagerAllowedActions.Contains(action),
                DepotRole.Supervisor => SupervisorAllowedActions.Contains(action),
                DepotRole.Technician => TechnicianAllowedActions.Contains(action),
                DepotRole.Viewer => ViewerAllowedActions.Contains(action),
                _ => false
            };
        }

        /// <summary>
        /// Demands that the user possesses the required permission. Throws UnauthorizedAccessException if denied,
        /// and automatically creates an audit entry for security compliance.
        /// </summary>
        public void DemandPermission(DepotUserRecord user, string action, string entityType = "", string entityId = "", string detailsJson = "")
        {
            if (user == null)
            {
                _store.RecordAuditLog("ANONYMOUS", "None", "ACCESS_DENIED_UNAUTHENTICATED", entityType, entityId, $"{{\"action\":\"{action}\"}}");
                throw new UnauthorizedAccessException("Authentication required: no active user context.");
            }

            if (!user.IsActive)
            {
                _store.RecordAuditLog(user.Username, user.Role.ToString(), "ACCESS_DENIED_INACTIVE_USER", entityType, entityId, $"{{\"action\":\"{action}\"}}");
                throw new UnauthorizedAccessException($"User account '{user.Username}' is deactivated.");
            }

            if (!HasPermission(user.Role, action))
            {
                _store.RecordAuditLog(user.Username, user.Role.ToString(), "ACCESS_DENIED", entityType, entityId, $"{{\"action\":\"{action}\",\"reason\":\"Role '{user.Role}' has insufficient privileges.\"}}");
                throw new UnauthorizedAccessException($"Role '{user.Role}' is not authorized to perform action '{action}'.");
            }

            // If allowed and it's a mutating action, record in audit log
            if (!ViewerAllowedActions.Contains(action))
            {
                _store.RecordAuditLog(user.Username, user.Role.ToString(), action, entityType, entityId, detailsJson ?? "{}");
            }
        }

        /// <summary>
        /// Authenticates user credentials against the canonical SQLite depot_users store.
        /// </summary>
        public DepotUserRecord Authenticate(string username, string pinOrToken = "")
        {
            if (string.IsNullOrWhiteSpace(username)) return null;

            var user = _store.GetDepotUser(username);
            if (user == null || !user.IsActive) return null;

            string expectedHash = user.PinOrTokenHash ?? "";
            string actualHash = HashPin(pinOrToken ?? "");

            // If empty hash was seeded or provided hash matches, authenticate
            if (string.IsNullOrEmpty(expectedHash) || string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                _store.RecordAuditLog(user.Username, user.Role.ToString(), "USER_LOGIN_SUCCESS", "User", user.Id, "{}");
                return user;
            }

            _store.RecordAuditLog(username, user.Role.ToString(), "USER_LOGIN_FAILED_INVALID_CREDENTIALS", "User", user.Id, "{}");
            return null;
        }

        /// <summary>
        /// Computes SHA-256 hash for PIN or token storage and comparison.
        /// </summary>
        public static string HashPin(string pin)
        {
            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(pin ?? ""));
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// Administrative user management with role enforcement.
        /// </summary>
        public void CreateOrUpdateUser(DepotUserRecord adminUser, DepotUserRecord targetUser)
        {
            DemandPermission(adminUser, DepotAction.ManageUsers, "User", targetUser?.Username);
            _store.SaveDepotUser(targetUser);
            _store.RecordAuditLog(adminUser.Username, adminUser.Role.ToString(), "USER_SAVED", "User", targetUser?.Id, $"{{\"targetUser\":\"{targetUser?.Username}\",\"role\":\"{targetUser?.Role}\"}}");
        }
    }
}
