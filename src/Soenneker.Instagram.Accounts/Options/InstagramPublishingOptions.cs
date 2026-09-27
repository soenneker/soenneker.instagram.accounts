using System;

namespace Soenneker.Instagram.Accounts.Options;

/// <summary>Controls cancellable processing checks for each media container.</summary>
public sealed class InstagramPublishingOptions
{
    /// <summary>Time between status checks. Defaults to one minute; must be positive and no greater than ProcessingTimeout.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Maximum processing wait per container, including status requests. Defaults to five minutes; must be positive.</summary>
    public TimeSpan ProcessingTimeout { get; init; } = TimeSpan.FromMinutes(5);
}
