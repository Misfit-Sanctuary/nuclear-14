using Robust.Shared.Prototypes;
using Content.Shared.Mind;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Store;

namespace Content.Server.Store.Conditions;

/// <summary>
/// Allows a store entry to be filtered out based on the user's job.
/// Supports both blacklists and whitelists
/// </summary>
public sealed partial class BuyerJobCondition : ListingCondition
{
    /// <summary>
    /// A whitelist of jobs prototypes that can purchase this listing. Only one needs to be found.
    /// </summary>
    [DataField("whitelist")]
    public HashSet<ProtoId<JobPrototype>>? Whitelist;

    /// <summary>
    /// A blacklist of job prototypes that can purchase this listing. Only one needs to be found.
    /// </summary>
    [DataField("blacklist")]
    public HashSet<ProtoId<JobPrototype>>? Blacklist;

    public override bool Condition(ListingConditionArgs args)
    {
        var ent = args.EntityManager;
        var minds = ent.System<SharedMindSystem>();

        // this is for things like surplus crate
        if (!minds.TryGetMind(args.Buyer, out var mindId, out _))
            return true;

        var jobs = ent.System<SharedJobSystem>();
        jobs.MindTryGetJob(mindId, out var job, out _);

        if (Blacklist != null)
        {
            if (job?.Prototype != null && Blacklist.Contains(job.Prototype.Value))
                return false;
        }

        if (Whitelist != null)
        {
            if (job?.Prototype == null || !Whitelist.Contains(job.Prototype.Value))
                return false;
        }

        return true;
    }
}
