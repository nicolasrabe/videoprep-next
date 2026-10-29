using Videoprep.Core.Scraping;

namespace Videoprep.Core.Profiles;

/// <summary>How subscriptions and profiles meet (F6): which values a check uses, who is affected by a change.</summary>
public static class ProfileLinks
{
    /// <summary>
    /// The values a subscription downloads with: the profile's current ones when it follows one that still exists,
    /// else its own stored values (a deleted profile falls back to them).
    /// </summary>
    public static SubscriptionProfile Effective(SubscriptionProfile own, ProfileStore profiles)
    {
        if (profiles.Find(own.ProfileId) is not { } p) return own;
        return own with { Format = p.Format, Cap = p.Cap, Naming = p.Naming, Prefix = p.IsSeries ? p.Prefix : "" };
    }

    /// <summary>Subscriptions that follow this profile.</summary>
    public static IReadOnlyList<Subscription> Using(string profileId, IEnumerable<Subscription> subs) =>
        subs.Where(s => s.Profile.ProfileId == profileId).ToList();

    /// <summary>The same subscriptions with the profile's values copied in (after the profile was edited), so a later delete keeps them.</summary>
    public static Subscription Sync(Subscription s, FormatProfile p) => s with
    {
        Profile = s.Profile with { Format = p.Format, Cap = p.Cap, Naming = p.Naming, Prefix = p.IsSeries ? p.Prefix : "", ProfileId = p.Id },
    };

    /// <summary>The subscription stops following the profile and keeps the values it has.</summary>
    public static Subscription Detach(Subscription s) => s with { Profile = s.Profile with { ProfileId = null } };
}
