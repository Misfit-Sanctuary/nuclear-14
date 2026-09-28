// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Shared._Misfits.Factory;
using Content.Shared.Administration.Logs;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Robust.Shared.Map;

namespace Content.Server._Misfits.Factory;

public sealed partial class ConstructorSystem : EntitySystem
{
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private ConstructionSystem _construction = default!;
    [Dependency] private StartableMachineSystem _machine = default!;

    private EntityQuery<ActiveDoAfterComponent> _activeQuery;

    public override void Initialize()
    {
        base.Initialize();

        _activeQuery = GetEntityQuery<ActiveDoAfterComponent>();

        SubscribeLocalEvent<ConstructorComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ConstructorComponent, MachineStartedEvent>(OnStarted);
        Subs.BuiEvents<ConstructorComponent>(ConstructorUiKey.Key, subs =>
        {
            subs.Event<ConstructorSetProtoMessage>(OnSetProto);
        });
    }

    private void OnExamined(Entity<ConstructorComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var msg = ent.Comp.Construction is {} id
            ? Loc.GetString("constructor-examine", ("name", ProtoMan.Index(id).Name ?? id))
            : Loc.GetString("constructor-examine-unset");
        args.PushMarkup(msg);
    }

    private void OnSetProto(Entity<ConstructorComponent> ent, ref ConstructorSetProtoMessage args)
    {
        if (ent.Comp.Construction == args.Id
            || !ProtoMan.HasIndex(args.Id))
            return;

        ent.Comp.Construction = args.Id;
        Dirty(ent);
        _adminLogger.Add(LogType.Construction, LogImpact.Low, $"{args.Actor:user} set {ent.Owner:target} construction to {args.Id}");
    }

    private void OnStarted(Entity<ConstructorComponent> ent, ref MachineStartedEvent args)
    {
        // can't start if it's already building something
        if (_activeQuery.HasComp(ent))
            _machine.Failed(ent.Owner);
        else
            Construct(ent);
    }

    // async because construction shitcode
    private async void Construct(Entity<ConstructorComponent> ent)
    {
        var uid = ent.Owner;
        if (ent.Comp.Construction is not {} id)
        {
            _machine.Failed(uid);
            return;
        }

        var proto = ProtoMan.Index(id);

        _machine.Started(uid);

        var completed = proto.Type switch
        {
            ConstructionType.Structure => await _construction.TryStartStructureConstruction(uid, id, OutputPosition(uid), Angle.Zero),
            ConstructionType.Item => await _construction.TryStartItemConstruction(id, uid),
            _ => false
        };

        if (completed)
            _machine.Completed(uid);
        else
            _machine.Failed(uid);
    }

    private EntityCoordinates OutputPosition(EntityUid uid)
    {
        var xform = Transform(uid);
        var offset = xform.LocalRotation.ToVec();
        return xform.Coordinates.Offset(offset);
    }
}
