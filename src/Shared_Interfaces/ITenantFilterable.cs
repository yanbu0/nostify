
using System;

namespace nostify;

/// <summary>
/// Interface for tenant filterable aggregates/projections.
/// </summary>
public interface ITenantFilterable
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid tenantId { get; set; }
}