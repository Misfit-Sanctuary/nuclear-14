using System.Runtime.CompilerServices;
using Content.Client.Weapons.Ranged.Components;
using Content.Shared.Audio;
using Content.Shared.CCVar;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using static Robust.Client.Graphics.RSI;
using DependencyAttribute = Robust.Shared.IoC.DependencyAttribute;
using static Content.Client.Weapons.Ranged.Systems.GunSystem.CartridgeSettings;
using Robust.Client.ResourceManagement;
using System.ComponentModel;

namespace Content.Client.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{
    [Dependency] private IPrototypeManager _protoMan = default!;
    [Dependency] private IResourceCache _resCache = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IComponentFactory _compFactory = default!;
    [Dependency] IRobustRandom _rng = default!;
    private ISawmill _logCart = default!;
    public CartridgeSettings CVAR_CartridgeVisuals;
    private const string Proto_Physics = "ClientCartridgePhysics";
    private const string Proto_Static = "ClientCartridgeStatic";
    // maybe move these fields to their own page??
    private static readonly AudioParams AUDIO_PARAM = AudioParams.Default.WithVariation(SharedContentAudioSystem.DefaultVariation);
    private static readonly Vector2i SPRITE_SIZE = new(32, 32);
    private const string FAILRSI = "/Textures/Objects/Weapons/Guns/Ammunition/Casings/ammo_casing.rsi";
    private static readonly ResPath RSI_FAIL = new(FAILRSI);
    private static readonly RSI CRSI = new(SPRITE_SIZE, RSI_FAIL);
    private static readonly StateId BaseSpentID = new("base-spent");
    private static readonly StateId SpentID = new("spent");
    private static readonly SoundCollectionSpecifier SoundDefault = new("CasingEject");
    public enum CartridgeSettings
    {
        CART_VISUAL_OFF = 1,
        OLD_SCHOOL = 2,
        PHYSICS_ON = 3
    }
    private void InitializeSpentAmmo()
    {
        SubscribeLocalEvent<SpentAmmoVisualsComponent, AppearanceChangeEvent>(OnSpentAmmoAppearance);
        SubscribeNetworkEvent<SpentCartEvent>(EjectSpentCart);
        _logCart = _logMan.GetSawmill("client.gun.cartridge");
        _rng.SetSeed(666); // satan rng
        Subs.CVar(_config, CCVars.SpentCartridgeVisual, OnCartSetting, true);
        CVAR_CartridgeVisuals = (CartridgeSettings) _config.GetCVar(CCVars.SpentCartridgeVisual);
    }

    private void OnCartSetting(int value)
    {
        CVAR_CartridgeVisuals = (CartridgeSettings) value;
    }
    private void OnSpentAmmoAppearance(EntityUid uid, SpentAmmoVisualsComponent component, ref AppearanceChangeEvent args)
    {
        var sprite = args.Sprite;
        if (sprite == null) return;

        if (!args.AppearanceData.TryGetValue(AmmoVisuals.Spent, out var varSpent))
        {
            return;
        }

        var spent = (bool) varSpent;
        string state;

        if (spent)
            state = component.Suffix ? $"{component.State}-spent" : "spent";
        else
            state = component.State;

        sprite.LayerSetState(AmmoVisualLayers.Base, state);
        if (sprite.LayerExists(AmmoVisualLayers.Tip))
        {
            sprite.RemoveLayer(AmmoVisualLayers.Tip);
        }
    }

    // client only. Dont need to network this since attemptShoot from client already runs this on server
    // server override networks the event to other clients on the server
    public override void EjectSpentCart(SpentCartEvent ev)
    {
        // client effect called by shared code, so turn off prediction to not spawn dupes
        if (!_timing.IsFirstTimePredicted || CVAR_CartridgeVisuals == CART_VISUAL_OFF)
            return;
        SpawnClientCart(ev);
    }


    // TODO: Debug only checks to find bad yaml or json
    /// <summary>
    /// Method for spawning client side spent cartridge visual
    /// We read protoId from ev to retrieve sprite and sound to use
    /// else use defaults if those were incorrect
    /// should only be called once on client. Doesnt need prediction it's just a visual
    /// </summary>
    private EntityUid SpawnClientCart(SpentCartEvent ev)
    {
        var (rsi, sound, stateId) = GetRSIFromCartProto(ev.Proto);

        var spentCartVisual = CVAR_CartridgeVisuals == OLD_SCHOOL
        ? SpawnCartOldSchool(ev.Coords, stateId, rsi) :
          SpawnCartPhysics(ev.Coords, ev.Angle, stateId, rsi);

        _player.TryGetSessionById(ev.Sender, out var session);
        Audio.PlayLocal(sound, spentCartVisual, session?.AttachedEntity, AUDIO_PARAM);

        return spentCartVisual;
    }

    // I personally am proud that my syntax fu reduced this to a few lines
    /// <summary>
    /// Get cartridge RSI/Sound based on its protoId which we check
    /// since it might be from the network
    /// on fail at least return defaults
    /// </summary>
    /// <param name="protoId"/>
    /// <returns>RSI and sound of prototype or static defaults</returns>
    private (RSI, SoundSpecifier, StateId) GetRSIFromCartProto(string? protoId)
    {
        // protoId shouldnt be missing, cast to also avoid null check boiler plate
        if (!_protoMan.Resolve((EntProtoId?) protoId, out var proto))
            return (CRSI, SoundDefault, BaseSpentID);
        _ = proto.TryComp<SpriteComponent>(out var rsi, _compFactory);
        _ = proto.TryComp<CartridgeAmmoComponent>(out var sound, _compFactory);
        _ = proto.TryComp<SpentAmmoVisualsComponent>(out var spentVisuals, _compFactory);

        var baseRsi = rsi?.BaseRSI ?? CRSI;

        // #Misfits Change - use the cartridge's own spent state (e.g. "slug-spent") so an ejected
        // shell keeps the colour of the shell that was fired instead of always showing base-spent.
        var stateId = BaseSpentID;
        if (spentVisuals != null)
        {
            var named = spentVisuals.Suffix ? new StateId($"{spentVisuals.State}-spent") : SpentID;
            if (baseRsi.TryGetState(named, out _))
                stateId = named;
        }

        // on invalid carts we default to BaseSpentID anyway so we just need to check for SpentID
        if (stateId == BaseSpentID && baseRsi.TryGetState(SpentID, out _))
            stateId = SpentID;

        return (baseRsi, sound?.EjectSound ?? SoundDefault, stateId);
    }

    private const float MaxArc = 2.85f;
    private const float MinArc = 1.5f;
    private const float DistMax = .7f;
    private const float DistMin = .25f;
    private const float LandAngleMax = 6.3f; // little over 2*Pi
    private const float SpinMax = 1f;
    /// <summary>
    /// method where we achully spawn the cart. Prototype already has comp to
    /// make the visual work on the client without the server, so we just spawn it
    /// and apply a PULSE(what TryThrow does basically) using some rng
    /// </summary>
    private EntityUid SpawnCartPhysics(MapCoordinates basePos, Angle baseAngle, StateId state, RSI rsi)
    {
        var cartVisual = Spawn(Proto_Physics, basePos, rotation: _rng.NextAngle(LandAngleMax));
        _sprite.LayerSetRsi(cartVisual, 0, rsi, state);
        var angleRng = _rng.NextAngle(MinArc, MaxArc) + baseAngle;

        _physics.ApplyLinearImpulse(cartVisual, _rng.NextFloat(DistMin, DistMax) * angleRng.ToVec());
        _physics.ApplyAngularImpulse(cartVisual, _rng.NextFloat(SpinMax));
        return cartVisual;
    }
    /// <summary>
    /// Alt preformance version of above where we just spawn carts as a still visual
    /// this spawns cartridges in a radius rather than throwing them at an angle
    /// </summary>
    private EntityUid SpawnCartOldSchool(MapCoordinates basePos, StateId state, RSI rsi, int seed = 666)
    {
        var (posEjectRNG, angleEjectRNG) = GetRandVectAngle(seed, _timing.CurTime.Nanoseconds);
        var cartVisual = Spawn(Proto_Static, basePos.Offset(posEjectRNG), rotation: angleEjectRNG);
        _sprite.LayerSetRsi(cartVisual, 0, rsi, state);
        return cartVisual;
    }


}
