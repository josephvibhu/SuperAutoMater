using ItamService.Data;
using ItamService.Models;

namespace ItamService.Domain;

/// <summary>
/// Reconciliation engine: after each observation batch is written,
/// compares each new observation against the previous one for the same
/// (asset, component_type, attribute_key) triplet.
///
/// Design rules enforced here:
/// - Raw observations are NEVER mutated.
/// - A difference is only recorded when value_b != value_a.
/// - Recording is idempotent: UNIQUE(obs_a_id, obs_b_id, attribute_key).
/// - Approving a replacement resolves the alert without deleting any history.
/// </summary>
public sealed class ReconciliationEngine(ItamDb db)
{
    /// <summary>
    /// Called after one or more observations have been appended for an asset.
    /// Returns the list of newly created ComponentDifference records (may be empty).
    /// </summary>
    public List<ComponentDifference> ReconcileObservations(
        string tenantId, string assetId,
        IEnumerable<ComponentObservation> newObservations)
    {
        var detected = new List<ComponentDifference>();

        foreach (var obs in newObservations)
        {
            var (previous, _) = db.GetPreviousAndLatest(tenantId, assetId, obs.ComponentType, obs.AttributeKey);
            if (previous is null) continue; // first ever observation — nothing to compare

            // Only flag if the value actually changed
            if (string.Equals(previous.AttributeValue, obs.AttributeValue, StringComparison.Ordinal))
                continue;

            var diff = new ComponentDifference
            {
                Id             = Guid.NewGuid().ToString("N"),
                AssetId        = assetId,
                TenantId       = tenantId,
                ComponentType  = obs.ComponentType,
                AttributeKey   = obs.AttributeKey,
                ObservationAId = previous.Id,
                ObservationBId = obs.Id,
                ValueA         = previous.AttributeValue,
                ValueB         = obs.AttributeValue,
                SourceA        = previous.Source,
                SourceB        = obs.Source,
                ConfidenceA    = previous.Confidence,
                ConfidenceB    = obs.Confidence,
                DetectedAtUtc  = DateTimeOffset.UtcNow
            };

            var inserted = db.UpsertDifference(diff);
            if (inserted is not null) detected.Add(inserted);
        }

        return detected;
    }
}
