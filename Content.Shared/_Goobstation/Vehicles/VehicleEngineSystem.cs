using Content.Shared.Popups;



namespace Content.Shared.Vehicles;
/// <summary>
/// basically "can we start/stop engine" but dont care about how/why it came to that conclusion
///
/// Place for systems to attach their own check events and/or
/// base their logic on if a vehicle engine can be enabled/disabled
///
/// Ideally systems that need to assess if any vehicle can be turned on/off
/// just need to check results from raising CheckCanEngineOnEv and CheckCanEngineOffEv
/// this way systems do not need to explicitly care about what comps the vehicle uses/relies on
/// they can just freely make their own logic compatible with engine logic
/// </summary>

public abstract partial class VehicleEngineSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IEntityManager _entMan = default!;
    public override void Initialize()
    {
        base.Initialize();
        _entArgs = new(EntityUid.Invalid, _entMan);
        SubscribeLocalEvent<VehicleEngineComponent, RequestEngineOnEv>(OnEngineRequestOn);
        SubscribeLocalEvent<VehicleEngineComponent, RequestEngineOffEv>(OnEngineRequestOff);

        // SubscribeLocalEvent<EngineNeedsFuelComponent, EngineOffEvent>(OnEngineOff);
        // SubscribeLocalEvent<EngineNeedsFuelComponent, EngineOnEvent>(OnEngineOn);

        EngineConditions();
        VehicleHandlers();
    }

    public bool TryTurnOnEngine(EntityUid ent, EntityUid? user)
    {
        var ev = new RequestEngineOnEv(user);
        RaiseLocalEvent(ent, ref ev);
        return ev.CanTurnOn;
    }
    public bool TryTurnOffEngine(EntityUid ent, EntityUid? user)
    {
        var ev = new RequestEngineOffEv(user);
        RaiseLocalEvent(ent, ref ev);
        return ev.CanTurnOff;
    }
    private void OnEngineRequestOn(Entity<VehicleEngineComponent> ent, ref RequestEngineOnEv ev)
    {

        var tryOnEv =  new EngineTryOnEvent(ent.Comp, ent.Comp.RequestOnByDefault);
        RaiseLocalEvent(ent, ref tryOnEv);
        if(ev.User is not null)
            RaiseLocalEvent(ev.User.Value, ref tryOnEv);

        ev.CanTurnOn = tryOnEv.CanTurnOn;
        if (!tryOnEv.CanTurnOn)
            return;

        ForceEngineOn(ent);
    }

    private void OnEngineRequestOff(Entity<VehicleEngineComponent> ent, ref RequestEngineOffEv ev)
    {

        var tryOffEv =  new EngineTryOffEvent(ent.Comp, ent.Comp.RequestOffByDefault);
        RaiseLocalEvent(ent, ref tryOffEv);
        if(ev.User is not null)
            RaiseLocalEvent(ev.User.Value, ref tryOffEv);

        ev.CanTurnOff = tryOffEv.CanTurnOff;
        if (!tryOffEv.CanTurnOff)
            return;

        ForceEngineOff(ent);
    }

    // force engine event
    private void ForceEngineOn(Entity<VehicleEngineComponent> ent)
    {
        var evEngine = new EngineOnEvent();
        RaiseLocalEvent(ent, ref evEngine);
    }
    private void ForceEngineOff(Entity<VehicleEngineComponent> ent)
    {
        var evEngine = new EngineOffEvent();
        RaiseLocalEvent(ent, ref evEngine);
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
}
