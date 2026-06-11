using Content.Server.Body.Systems;
using Content.Server.Emp;
using Content.Server.Explosion.EntitySystems;
using Content.Server._Misfits.Chat.Systems;
using Content.Shared.Actions;
using Content.Shared.Inventory.Events;
using Content.Shared._Misfits.PowerArmor;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Server._Misfits.PowerArmor;

public sealed class PowerArmorSelfDestructSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;
    [Dependency] private readonly ExplosionSystem _explosion = default!;
    [Dependency] private readonly EmpSystem _emp = default!;
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPointLightSystem _pointLight = default!;
    [Dependency] private readonly MisfitsEmoteThrottleSystem _throttle = default!;

    private const int CountdownSeconds = 10;
    private static readonly SoundPathSpecifier ActivationSound = new("/Audio/Machines/Nuke/nuke_alarm.ogg");
    private static readonly SoundPathSpecifier BeepSound = new("/Audio/Effects/Grenades/SelfDestruct/SDS_Charge.ogg");

    private const float GlowRadiusStart = 2.0f;
    private const float GlowRadiusEnd = 14.0f;
    private const float GlowEnergyStart = 1.5f;
    private const float GlowEnergyEnd = 10.0f;
    private static readonly Color GlowColor = Color.FromHex("#3377FF");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PowerArmorSelfDestructComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<PowerArmorSelfDestructComponent, GetItemActionsEvent>(OnGetItemActions);
        SubscribeLocalEvent<PowerArmorSelfDestructComponent, ComponentRemove>(OnComponentRemove);
        SubscribeLocalEvent<PowerArmorSelfDestructComponent, PowerArmorSelfDestructEvent>(OnSelfDestruct);
    }

    private void OnMapInit(EntityUid uid, PowerArmorSelfDestructComponent component, MapInitEvent args)
    {
        _actionContainer.EnsureAction(uid, ref component.SelfDestructActionEntity, component.SelfDestructAction);
        Dirty(uid, component);
    }

    private void OnGetItemActions(EntityUid uid, PowerArmorSelfDestructComponent component, GetItemActionsEvent args)
    {
        if (args.SlotFlags != component.RequiredSlot || component.SelfDestructActionEntity == null)
            return;

        args.AddAction(component.SelfDestructActionEntity.Value);
    }

    private void OnComponentRemove(EntityUid uid, PowerArmorSelfDestructComponent component, ComponentRemove args)
    {
        _actions.RemoveAction(component.SelfDestructActionEntity);
    }

    private void OnSelfDestruct(EntityUid uid, PowerArmorSelfDestructComponent component, PowerArmorSelfDestructEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        var performer = args.Performer;
        if (TerminatingOrDeleted(performer))
            return;

        var fallbackCoords = _transform.GetMapCoordinates(performer);

        _popup.PopupCoordinates(
            Loc.GetString("power-armor-self-destruct-activated"),
            Transform(performer).Coordinates,
            PopupType.LargeCaution);

        _audio.PlayPvs(ActivationSound, performer);

        var armorName = Exists(uid) ? Name(uid) : "power armor";
        _throttle.SendThrottledEmote(
            performer,
            "pa-selfdestruct",
            Loc.GetString("power-armor-self-destruct-chat", ("armor", armorName)));

        var glowEnt = Spawn(null, Transform(performer).Coordinates);
        _transform.SetParent(glowEnt, performer);

        var light = EnsureComp<PointLightComponent>(glowEnt);
        _pointLight.SetColor(glowEnt, GlowColor, light);
        _pointLight.SetRadius(glowEnt, GlowRadiusStart, light);
        _pointLight.SetEnergy(glowEnt, GlowEnergyStart, light);
        _pointLight.SetEnabled(glowEnt, true, light);

        for (var i = 0; i < CountdownSeconds; i++)
        {
            var step = i;
            Timer.Spawn(TimeSpan.FromSeconds(step), () =>
            {
                if (!TerminatingOrDeleted(performer))
                    _audio.PlayPvs(BeepSound, performer);

                if (!TerminatingOrDeleted(glowEnt))
                {
                    var t = (float)step / CountdownSeconds;
                    _pointLight.SetRadius(glowEnt, MathHelper.Lerp(GlowRadiusStart, GlowRadiusEnd, t));
                    _pointLight.SetEnergy(glowEnt, MathHelper.Lerp(GlowEnergyStart, GlowEnergyEnd, t));
                }
            });
        }

        Timer.Spawn(TimeSpan.FromSeconds(CountdownSeconds), () =>
        {
            var detonationCoords = !TerminatingOrDeleted(performer)
                ? _transform.GetMapCoordinates(performer)
                : fallbackCoords;

            if (!TerminatingOrDeleted(glowEnt))
                QueueDel(glowEnt);

            if (!TerminatingOrDeleted(uid))
                QueueDel(uid);

            _explosion.QueueExplosion(detonationCoords, "N14DefaultStructural", totalIntensity: 2000f, slope: 10f, maxTileIntensity: 40f);

            _emp.EmpPulse(detonationCoords, range: 10f, energyConsumption: 1000f, duration: 30f);

            if (!TerminatingOrDeleted(performer))
                _body.GibBody(performer, launchGibs: true);
        });
    }
}
