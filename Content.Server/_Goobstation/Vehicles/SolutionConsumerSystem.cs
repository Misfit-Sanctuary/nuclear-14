using System.Diagnostics;
using Content.Server.Administration;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Content.Shared.Vehicles;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Utility;


namespace Content.Server.Vehicles;

public sealed partial class SolutionConsumerSystem : EntitySystem
{

    [Dependency] private MetaDataSystem _metaSystem = default!;
    [Dependency] private QuickDialogSystem _quickDialog = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private SharedVehicleSystem _vehicle = default!;
    [Dependency] private IGameTiming _time = default!;
    [Dependency] private DamageableSystem _dmg = default!;
    [Dependency] private IPrototypeManager _protoMan = default!;

    // private static readonly ProtoId<DamageTypePrototype> DmgType = "EngineDamage";
    // private static DamageTypePrototype _engineDmg = default!;
    // private static readonly ProtoId<DamageGroupPrototype> DmgGroup = "InternalMechanical";
    // private static DamageGroupPrototype _engineGroupDmg = default!;


//TODO: finish and make into general fuel consumer system/comp

    private EntityQueryEnumerator<ActiveSolutionConsumerComponent,SolutionConsumerComponent,SolutionContainerManagerComponent> _runningBikesQuery;
    public override void Initialize()
    {
        base.Initialize();
        _solutionTickEv = new();


        SubscribeLocalEvent<SolutionTickEv>(ProcessSolutionConsumption);
        SubscribeLocalEvent<DamageOnConsumeReagentComponent,SolutionConsumedEv>(OnEngineFuelTick);

    }

    private void OnEngineFuelTick(Entity<DamageOnConsumeReagentComponent> ent, ref SolutionConsumedEv ev)
    {
        var badQuant = ev.SolutionSource.Comp.Solution.Volume.Value;

        foreach (var sol in ev.AllowedReagents)
        {
            ReagentId id = new(sol, null);
            badQuant -= ev.SolutionSource.Comp.Solution.GetReagent(id).Quantity.Value;
        }

        badQuant = ent.Comp.DmgPerInvalidReagent*ev.AmountToConsume.Value*(badQuant / ev.SolutionSource.Comp.Solution.Volume.Value);
        var dmg = FixedPoint2.FromCents(badQuant);
        var dmgType = _protoMan.Index(ent.Comp.DmgType);
        _dmg.TryChangeDamage(ent,new(dmgType, dmg), true);
    }

    public sealed class SolutionTickEv : EntityEventArgs;
    private static SolutionTickEv _solutionTickEv = default!;


    private static SolutionConsumedEv _solutionConsumedEv = default!;
    private void ProcessSolutionConsumption(SolutionTickEv ev)
    {
        _runningBikesQuery = EntityQueryEnumerator<ActiveSolutionConsumerComponent,SolutionConsumerComponent,SolutionContainerManagerComponent>();
        while (_runningBikesQuery.MoveNext(out var uid, out _, out var consumeComp, out var sol))
        {
            if(!_solution.TryGetSolution((uid,sol),consumeComp.FuelSolution,out var fuelSolution))
            {
                DebugTools.Assert(true);
                continue;
            }
            var roundedNum = consumeComp.FuelUsePerSecond.Float();
            var solutionToConsume = FixedPoint2.Min(FixedPoint2.New(roundedNum),fuelSolution.Value.Comp.Solution.Volume);
            _solutionConsumedEv = new(fuelSolution.Value, solutionToConsume, consumeComp.AllowedReagents);
            RaiseLocalEvent(uid, ref _solutionConsumedEv);

            if (_solutionConsumedEv.Handled || !(solutionToConsume > 0))
                continue;
            fuelSolution.Value.Comp.Solution.RemoveSolution(solutionToConsume);
        }

    }


    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        // misfits: server tps is usually 30. So as an example
        //          this runs on the 30th tick. Runs every second
        var tickToRun = _time.CurTick.Value % MathF.Round(_time.TickRate);
        if (tickToRun != 0)
            return;
        QueueLocalEvent(_solutionTickEv);

    }




}
