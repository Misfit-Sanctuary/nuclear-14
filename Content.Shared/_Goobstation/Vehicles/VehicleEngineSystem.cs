using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Popups;



namespace Content.Shared.Vehicles;


public abstract partial class VehicleEngineSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IEntityManager _entMan = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<VehicleEngineComponent, CheckCanEngineOnEv>(EngineRequestOn);
        SubscribeLocalEvent<VehicleEngineComponent, CheckCanEngineOffEv>(EngineRequestOff);

        SubscribeLocalEvent<EngineNeedsFuelComponent, EngineOffEvent>(OnEngineOff);
        SubscribeLocalEvent<EngineNeedsFuelComponent, EngineOnEvent>(OnEngineOn);

        InternalLogic();
        CheckEvents();
        DirectEngineEvents();
    }
    private void OnEngineOff(Entity<EngineNeedsFuelComponent> ent, ref EngineOffEvent args)
    {
        RemComp<ActiveSolutionConsumerComponent>(ent);
    }
    private void OnEngineOn(Entity<EngineNeedsFuelComponent> ent, ref EngineOnEvent args)
    {
        EnsureComp<ActiveSolutionConsumerComponent>(ent);
    }

    private void EngineRequestOn(Entity<VehicleEngineComponent> ent, ref CheckCanEngineOnEv args)
    {
        var tryOnEv =  new EngineInternalTryOnEvent(ent.Comp);
        RaiseLocalEvent(ent, ref tryOnEv);
        if(args.User is not null)
            RaiseLocalEvent(args.User.Value, ref tryOnEv);

        args.CanTurnOn = !tryOnEv.CanTurnOn;
    }

    private void EngineRequestOff(Entity<VehicleEngineComponent> ent, ref CheckCanEngineOffEv args)
    {
        var tryOffEv =  new EngineInternalTryOffEvent(ent.Comp);
        RaiseLocalEvent(ent, ref tryOffEv);
        if(args.User is not null)
            RaiseLocalEvent(args.User.Value, ref tryOffEv);

        args.CanTurnOff = !tryOffEv.CanTurnOff;
    }
    // Adds a Rename verb to motorbikes, allowing players to give them a custom name.
    // private void OnGetVerbs(Entity<SolutionConsumerComponent> ent, ref GetVerbsEvent<Verb> args)
    // {
    //     if (!TryComp<ActorComponent>(args.User, out var actor))
    //         return;
    //
    //     var player = actor.PlayerSession;
    //
    //     var rename = new Verb
    //     {
    //         Text = "Rename",
    //         Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/rename.svg.192dpi.png")),
    //         Act = () =>
    //         {
    //             _quickDialog.OpenDialog(player, "Rename", "Name", (string newName) =>
    //             {
    //                 _metaSystem.SetEntityName(ent.Owner, newName);
    //             });
    //         },
    //         Impact = LogImpact.Low,
    //     };
    //
    //     args.Verbs.Add(rename);
    // }
    // private void OnInit(Entity<SolutionConsumerComponent> ent, ref ComponentInit args)
    // {
    //     _appearance.SetData(ent, MotorbikeVisuals.Burning, ent.Comp.Burning);
    // }


    // private void DestroyEngine(EntityUid uid)
    // {
    //
    //     var bike = (uid,Comp<VehicleComponent>(uid));
    //     _vehicle.TryTurnOffEngine(bike);
    //     EnsureComp<DisabledEntityComponent>(uid);
    //
    //     //_popup.PopupEntity(Loc.GetString(locMsg), uid);
    // }
}
