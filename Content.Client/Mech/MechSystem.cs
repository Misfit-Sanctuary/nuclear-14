using Content.Shared.Mech;
using Content.Shared.Mech.Components;
// #Cythisiax Added - Render PA pilot hand items on the visible suit.
using Content.Shared._Misfits.PowerArmor;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Item;
using Content.Shared.Mech.EntitySystems;
using Robust.Client.GameObjects;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client.Mech;

/// <inheritdoc/>
public sealed class MechSystem : SharedMechSystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    // #Cythisiax Added - Cache hand contents so frame updates only rebuild changed layers.
    private readonly Dictionary<EntityUid, (EntityUid? Pilot, EntityUid? Left, EntityUid? Right)> _paHands = new();
    private readonly Dictionary<EntityUid, HashSet<string>> _paLayers = new();
    private float _paVisualTimer;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MechComponent, AppearanceChangeEvent>(OnAppearanceChanged);
        // #Cythisiax Added - Release cached PA sprite state when a suit is deleted.
        SubscribeLocalEvent<MechPowerArmorComponent, ComponentShutdown>(OnPowerArmorShutdown);
    }

    // #Cythisiax Added - Avoid retaining removed suits in the hand visual cache.
    private void OnPowerArmorShutdown(EntityUid uid, MechPowerArmorComponent component, ComponentShutdown args)
    {
        _paHands.Remove(uid);
        _paLayers.Remove(uid);
    }

    private void OnAppearanceChanged(EntityUid uid, MechComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (!args.Sprite.TryGetLayer((int) MechVisualLayers.Base, out var layer))
            return;

        var state = component.BaseState;
        var drawDepth = DrawDepth.Mobs;
        if (component.BrokenState != null && _appearance.TryGetData<bool>(uid, MechVisuals.Broken, out var broken, args.Component) && broken)
        {
            state = component.BrokenState;
            drawDepth = DrawDepth.SmallMobs;
        }
        else if (component.OpenState != null && _appearance.TryGetData<bool>(uid, MechVisuals.Open, out var open, args.Component) && open)
        {
            state = component.OpenState;
            drawDepth = DrawDepth.SmallMobs;
        }

        layer.SetState(state);
        args.Sprite.DrawDepth = (int) drawDepth;
    }

    // #Cythisiax Added - Pilot sprites are hidden by the mech container, so copy hand visuals onto the suit.
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _paVisualTimer += frameTime;
        if (_paVisualTimer < 0.1f)
            return;

        _paVisualTimer = 0;
        var query = EntityQueryEnumerator<MechPowerArmorComponent, MechComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out _, out var mech, out var sprite))
        {
            var pilot = mech.PilotSlot?.ContainedEntity;
            EntityUid? left = null;
            EntityUid? right = null;
            if (pilot != null && TryComp<HandsComponent>(pilot, out var hands))
            {
                foreach (var hand in hands.Hands.Values)
                {
                    if (hand.Location == HandLocation.Left)
                        left = hand.HeldEntity;
                    else if (hand.Location == HandLocation.Right)
                        right = hand.HeldEntity;
                }
            }

            var current = (pilot, left, right);
            if (_paHands.TryGetValue(uid, out var previous) && previous == current)
                continue;

            _paHands[uid] = current;
            UpdatePowerArmorHands(uid, pilot, sprite, left, right);
        }
    }

    // #Cythisiax Added - Reuse item in-hand layer data to match humanoid weapon sprites.
    private void UpdatePowerArmorHands(EntityUid uid, EntityUid? pilot, SpriteComponent sprite, EntityUid? left, EntityUid? right)
    {
        if (_paLayers.TryGetValue(uid, out var oldLayers))
        {
            foreach (var layer in oldLayers)
                sprite.RemoveLayer(layer);
            oldLayers.Clear();
        }
        else
        {
            oldLayers = new HashSet<string>();
            _paLayers[uid] = oldLayers;
        }

        AddHandLayers(pilot, sprite, left, HandLocation.Left, oldLayers);
        AddHandLayers(pilot, sprite, right, HandLocation.Right, oldLayers);
    }

    // #Cythisiax Added - Give each hand unique keys and use the item's RSI as the default.
    private void AddHandLayers(EntityUid? pilot, SpriteComponent sprite, EntityUid? held, HandLocation location, HashSet<string> layers)
    {
        if (pilot == null || held == null)
            return;

        var visuals = new GetInhandVisualsEvent(pilot.Value, location);
        RaiseLocalEvent(held.Value, visuals);
        foreach (var (key, data) in visuals.Layers)
        {
            var layerKey = $"mech-pa-{location}-{key}";
            if (!layers.Add(layerKey))
                continue;

            var index = sprite.LayerMapReserveBlank(layerKey);
            if (data.RsiPath == null && data.TexturePath == null && sprite[index].Rsi == null)
            {
                if (TryComp<ItemComponent>(held, out var item) && item.RsiPath != null)
                    sprite.LayerSetRSI(index, item.RsiPath);
                else if (TryComp<SpriteComponent>(held, out var heldSprite))
                    sprite.LayerSetRSI(index, heldSprite.BaseRSI);
            }

            sprite.LayerSetData(index, data);
        }
    }
}
