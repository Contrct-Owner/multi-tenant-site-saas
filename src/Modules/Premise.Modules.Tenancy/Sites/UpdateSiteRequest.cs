using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Premise.Contracts;
using Premise.Modules.Tenancy.Data;
using Premise.Modules.Tenancy.Hierarchy;
using Premise.Platform.Data;
using Premise.Platform.Entitlements;
using Premise.Platform.Kernel;
using Premise.Platform.Messaging;
using Premise.Platform.Spatial;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Http;

namespace Premise.Modules.Tenancy.Sites;

/// <summary>
/// Patch semantics: null = unchanged. Address fields accept "" to CLEAR -
/// an address typo must be fixable, and so must an address that never
/// existed (finding 2 of the competitive review).
/// </summary>
public sealed record UpdateSiteRequest(
    string? Name,
    string? TimeZone,
    SiteStatus? Status,
    string? AddressLine1 = null,
    string? City = null,
    string? PostalCode = null,
    string? CountryCode = null,
    double? Latitude = null,
    double? Longitude = null,
    System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>? Attributes = null,
    uint? Version = null
);
