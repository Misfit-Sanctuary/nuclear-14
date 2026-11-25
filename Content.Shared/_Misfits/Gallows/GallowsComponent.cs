using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Misfits.Gallows;

[RegisterComponent, NetworkedComponent, Access(typeof(SharedGallowsSystem))]
public sealed partial class GallowsComponent : Component
{
    [DataField]
    public TimeSpan AttachDuration = TimeSpan.FromSeconds(4);

    [DataField]
    public float SuffocationDamagePerSecond = 6f;

    [DataField]
    public TimeSpan HangDuration = TimeSpan.FromSeconds(4);

    [DataField]
    public ProtoId<DamageTypePrototype> DamageType = "Asphyxiation";

    [DataField]
    public SoundSpecifier HangSound = new SoundPathSpecifier("/Audio/Effects/snap.ogg");

    [DataField]
    public bool Hanging;

    [DataField]
    public SoundSpecifier IdleSound = new SoundPathSpecifier("/Audio/_Misfits/Effects/noose_idle.ogg");

    [DataField]
    public List<string> StruggleEmotes = new()
    {
        "gallows-struggle-gasp",
        "gallows-struggle-choke",
        "gallows-struggle-claw",
        "gallows-struggle-convulse",
    };

    [DataField]
    public TimeSpan StruggleCooldown = TimeSpan.FromSeconds(6);

    [DataField]
    public TimeSpan NextStruggleTime;

    [DataField]
    public FixedPoint2 CoughBloodAmount = FixedPoint2.New(5);

    [DataField]
    public TimeSpan CoughBloodCooldown = TimeSpan.FromSeconds(4);

    [DataField]
    public TimeSpan NextCoughBloodTime;

    [DataField]
    public float BlurMagnitude = 4f;

    [DataField]
    public TimeSpan StutterDuration = TimeSpan.FromMinutes(30);

    [DataField]
    public DoAfterId? AttachDoAfter;

    [DataField]
    public DoAfterId? HangDoAfter;

    [DataField]
    public bool ForceBuckle;
}
