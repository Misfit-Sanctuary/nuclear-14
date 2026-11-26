using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared.Weapons.Ranged.Components;
/// <summary>
/// <see cref="Systems.SharedGunSystem.Ballistic"/> has events that this comp listens for
///
/// Things with this comp is anything that holds bullets aka entities that implment <see cref="IShootable"/>
/// So usually ammo boxes, but also guns themselves that hold ammo RAW(doesn't use a mag)
/// Ie... revolvers, pump shotguns, some rifles ect...
///
/// Note ammo is only spawned as needed for preformance
/// "Amount" of ammo is kept track as unspawned + spawned
/// spawned ammo usually from other ammo providers
///
/// ammo is also refered to as cartridge which is the technically correct term for the "bullet"
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BallisticAmmoProviderComponent : Component
{

    /// <summary>
    /// What ammo prototype to spawn or "fill" entity with
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public EntProtoId? Proto;

    /// <summary>
    /// Number of yet to be spawned ammo.
    /// tradiationally thisll be how much ammo you'll want in the entity
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public int UnspawnedCount = -1;


    /// <summary>
    /// Container isnt predicted, so getting spawned item count off it
    /// will cause visual delays and bugs.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public int SpawnedCountPredict = -1;

    /// <summary>
    /// For stuff like revolvers and maybe other shit
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public int IndexPredict
    {
        get => _curIndex;
        set => _curIndex = value % Capacity;
    }

    public List<EntProtoId?> ClientPredictedAmmoVisual = new();
    //public List<(EntProtoId?, MapCoordinates, Angle)> PredictedEjects = default;

    private int _curIndex = 0;
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public EntityWhitelist? Whitelist;
    /// <summary>
    /// Container that holds any spawned ammo
    /// </summary>

    [DataField(tag: "ballistic-ammo")]
    public Container Container = new();

    /// <summary>
    /// Is the magazine allowed to be manually cycled to eject a cartridge.
    /// </summary>
    /// <remarks>
    /// Set to false for entities like turrets to avoid users being able to cycle them.
    /// </remarks>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public bool Cycleable = true;
    /// <summary>
    ///  max amount of ammo
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public int Capacity = 30;
    /// <summary>
    /// total shots comp has
    /// basically: (yet to be spawned ammo) + (spawned ammo)
    /// </summary>
    public int AmmoCount => UnspawnedCount + SpawnedCountPredict;
    /// <summary>
    /// can this entity transfer its ammo into another ballistic ammo provider?
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public bool MayTransfer = true;
    /// <summary>
    /// DoAfter delay for filling a bullet into another ballistic ammo provider.
    /// </summary>
    [DataField]
    public TimeSpan FillDelay = TimeSpan.FromSeconds(0.5);

    [ViewVariables(VVAccess.ReadWrite), DataField]
    public SoundSpecifier? SoundRack = new SoundPathSpecifier("/Audio/Weapons/Guns/Cock/smg_cock.ogg");

    [ViewVariables(VVAccess.ReadWrite), DataField]
    public SoundSpecifier? SoundInsert = new SoundPathSpecifier("/Audio/Weapons/Guns/MagIn/bullet_insert.ogg");

    /// <summary>Rounds moved per fill do-after when poured into from a box or drum.</summary>
    [DataField]
    public int FillTransferAmount = 5;

}
