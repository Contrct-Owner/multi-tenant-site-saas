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

public sealed record CreateScheduleRequest(
    string Name,
    string RRule,
    DateOnly AnchorDate,
    TimeOnly Opens,
    TimeOnly Closes,
    DateOnly[]? ExDates = null
);
