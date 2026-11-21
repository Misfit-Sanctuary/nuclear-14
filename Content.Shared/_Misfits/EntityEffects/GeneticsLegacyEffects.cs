// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Chat.Prototypes;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.Damage;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Jittering;
using Content.Shared.Popups;
using Content.Shared.Polymorph;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared._Misfits.EntityEffects;

/// <summary>
/// Shared equivalents of effects that are server-only in this fork. Genetics effects are serialized on clients
/// because they can be embedded in shared action and mutation prototypes, so they cannot use the legacy types.
/// </summary>
public sealed partial class GeneticsPopupMessage : EntityEffect
{
    [DataField(required: true)] public string[] Messages = default!;
    [DataField] public GeneticsPopupRecipients Type = GeneticsPopupRecipients.Local;
    [DataField] public PopupType VisualType = PopupType.Small;
    [DataField] public string? Method;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var message = IoCManager.Resolve<IRobustRandom>().Pick(Messages);
        var locArgs = args is EntityEffectReagentArgs reagentArgs
            ? new (string, object)[] { ("entity", args.TargetEntity), ("organ", reagentArgs.OrganEntity.GetValueOrDefault()) }
            : new (string, object)[] { ("entity", args.TargetEntity) };

        args.EntityManager.EventBus.RaiseLocalEvent(args.TargetEntity,
            new GeneticsPopupEffectEvent(Loc.GetString(message, locArgs), Type, VisualType, Method));
    }
}

public enum GeneticsPopupRecipients
{
    Pvs,
    Local,
}

public sealed class GeneticsPopupEffectEvent(string message, GeneticsPopupRecipients recipients, PopupType visualType, string? method) : EntityEventArgs
{
    public readonly string Message = message;
    public readonly GeneticsPopupRecipients Recipients = recipients;
    public readonly PopupType VisualType = visualType;
    public readonly string? Method = method;
}

public sealed partial class GeneticsEmote : EntityEffect
{
    [DataField("emote", customTypeSerializer: typeof(PrototypeIdSerializer<EmotePrototype>))]
    public string? EmoteId;
    [DataField] public bool ShowInChat;
    [DataField] public bool Force;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (EmoteId != null)
            args.EntityManager.EventBus.RaiseLocalEvent(args.TargetEntity, new GeneticsEmoteEffectEvent(EmoteId, ShowInChat, Force));
    }
}

public sealed class GeneticsEmoteEffectEvent(string emoteId, bool showInChat, bool force) : EntityEventArgs
{
    public readonly string EmoteId = emoteId;
    public readonly bool ShowInChat = showInChat;
    public readonly bool Force = force;
}

public sealed partial class GeneticsHealthChange : EntityEffect
{
    [DataField(required: true)] public DamageSpecifier Damage = default!;
    [DataField] public bool ScaleByQuantity;
    [DataField] public bool IgnoreResistances = true;
    [DataField] public bool UseTargeting = true;
    [DataField] public TargetBodyPart TargetPart = TargetBodyPart.All;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var scale = FixedPoint2.New(1);
        if (args is EntityEffectReagentArgs reagentArgs)
            scale = ScaleByQuantity ? reagentArgs.Quantity * reagentArgs.Scale : reagentArgs.Scale;

        args.EntityManager.EventBus.RaiseLocalEvent(args.TargetEntity,
            new GeneticsHealthChangeEffectEvent(
                Damage * scale,
                IgnoreResistances,
                UseTargeting ? TargetPart : null));
    }
}

public sealed class GeneticsHealthChangeEffectEvent(DamageSpecifier damage, bool ignoreResistances, TargetBodyPart? targetPart) : EntityEventArgs
{
    public readonly DamageSpecifier Damage = damage;
    public readonly bool IgnoreResistances = ignoreResistances;
    public readonly TargetBodyPart? TargetPart = targetPart;
}

public sealed partial class GeneticsIgnite : EntityEffect
{
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var source = args is EntityEffectReagentArgs reagentArgs
            ? reagentArgs.OrganEntity ?? reagentArgs.TargetEntity
            : args.TargetEntity;
        args.EntityManager.EventBus.RaiseLocalEvent(args.TargetEntity, new GeneticsIgniteEffectEvent(source));
    }
}

public sealed class GeneticsIgniteEffectEvent(EntityUid source) : EntityEventArgs
{
    public readonly EntityUid Source = source;
}

public sealed partial class GeneticsElectrocute : EntityEffect
{
    [DataField] public int ElectrocuteTime = 2;
    [DataField] public int ElectrocuteDamageScale = 5;
    [DataField] public bool Refresh = true;
    [DataField] public bool BypassInsulation = true;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var damage = Math.Max(ElectrocuteDamageScale, 1);
        if (args is EntityEffectReagentArgs reagentArgs)
        {
            damage = Math.Max((reagentArgs.Quantity * ElectrocuteDamageScale).Int(), 1);
            if (reagentArgs.Reagent != null)
                reagentArgs.Source?.RemoveReagent(reagentArgs.Reagent.ID, reagentArgs.Quantity);
        }

        args.EntityManager.EventBus.RaiseLocalEvent(args.TargetEntity,
            new GeneticsElectrocuteEffectEvent(damage, TimeSpan.FromSeconds(ElectrocuteTime), Refresh, BypassInsulation));
    }
}

public sealed class GeneticsElectrocuteEffectEvent(int damage, TimeSpan duration, bool refresh, bool bypassInsulation) : EntityEventArgs
{
    public readonly int Damage = damage;
    public readonly TimeSpan Duration = duration;
    public readonly bool Refresh = refresh;
    public readonly bool BypassInsulation = bypassInsulation;
}

public sealed partial class GeneticsTakeStaminaDamage : EntityEffect
{
    [DataField] public int Amount = 10;
    [DataField] public bool Immediate;
    [DataField] public bool IgnoreResist;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        if (!args.EntityManager.TryGetComponent<StaminaComponent>(args.TargetEntity, out var stamina))
            return;

        var scale = args is EntityEffectReagentArgs reagentArgs ? reagentArgs.Scale.Float() : 1f;
        args.EntityManager.System<StaminaSystem>()
            .TakeStaminaDamage(args.TargetEntity, Amount * scale, stamina, visual: false);
    }
}

public sealed partial class GeneticsJitter : EntityEffect
{
    [DataField] public float Amplitude = 10f;
    [DataField] public float Frequency = 4f;
    [DataField] public float Time = 2f;
    [DataField] public bool Refresh = true;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var time = Time;
        if (args is EntityEffectReagentArgs reagentArgs)
            time *= reagentArgs.Scale.Float();

        args.EntityManager.System<SharedJitteringSystem>()
            .DoJitter(args.TargetEntity, TimeSpan.FromSeconds(time), Refresh, Amplitude, Frequency);
    }
}

public sealed partial class GeneticsPolymorph : EntityEffect
{
    [DataField("prototype", required: true)]
    public ProtoId<PolymorphPrototype> Prototype;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;

    public override void Effect(EntityEffectBaseArgs args)
    {
        args.EntityManager.EventBus.RaiseLocalEvent(args.TargetEntity, new GeneticsPolymorphEffectEvent(Prototype));
    }
}

public sealed class GeneticsPolymorphEffectEvent(ProtoId<PolymorphPrototype> prototype) : EntityEventArgs
{
    public readonly ProtoId<PolymorphPrototype> Prototype = prototype;
}
