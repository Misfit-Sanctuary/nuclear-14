// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Radio.EntitySystems;
using Content.Server.Research.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Radio;
using Content.Shared.Research.Components;
using Content.Shared._Misfits.Genetics.Console;
using Content.Shared._Misfits.Genetics.Mutations;

namespace Content.Server._Misfits.Genetics.Console;

public sealed partial class GeneticsResearchConsoleSystem : EntitySystem
{
    [Dependency] private MutationSystem _mutation = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private ResearchSystem _research = default!;
    [Dependency] private EntityQuery<ResearchClientComponent> _clientQuery = default!;

    private readonly Dictionary<EntityUid, HashSet<EntProtoId<MutationComponent>>> _rewarded = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GeneticsResearchConsoleComponent, MutationSequencedEvent>(OnSequenced);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnSequenced(Entity<GeneticsResearchConsoleComponent> ent, ref MutationSequencedEvent args)
    {
        if (_clientQuery.CompOrNull(ent)?.Server is not {} server)
            return;

        if (!_rewarded.TryGetValue(server, out var rewarded))
            _rewarded[server] = rewarded = new();

        if (!rewarded.Add(args.Mutation)) // no infinite point farming chud
            return;

        var difficulty = _mutation.AllMutations[args.Mutation].Difficulty;
        var points = difficulty * ent.Comp.PointsPerDifficulty;
        _research.ModifyServerPoints(server, points);
        var name = _prototype.Index(args.Mutation).Name;
        var msg = Loc.GetString("genetics-console-radio-message", ("points", points), ("mutation", name));
        _radio.SendRadioMessage(ent, msg, ent.Comp.Channel, ent);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _rewarded.Clear();
    }
}
