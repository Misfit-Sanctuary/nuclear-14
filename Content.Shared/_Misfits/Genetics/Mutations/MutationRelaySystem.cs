// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared._Misfits.Viewcone;
using Content.Shared._Misfits.Common.Speech;

namespace Content.Shared._Misfits.Genetics.Mutations;

/// <summary>
/// Relays some events from the mutated mob to the mutation entities.
/// </summary>
public sealed class MutationRelaySystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MutatableComponent, MobStateChangedEvent>(RelayEvent);
        SubscribeLocalEvent<MutatableComponent, DamageModifyEvent>(RelayEvent);
        SubscribeLocalEvent<MutatableComponent, ModifyViewconeAngleEvent>(RelayEvent);
        SubscribeLocalEvent<MutatableComponent, SpeechFontOverrideEvent>(RelayEvent);
        SubscribeLocalEvent<MutatableComponent, SpeechFontSizeOverrideEvent>(RelayEvent);
    }

    public void RelayEvent<T>(Entity<MutatableComponent> ent, ref T args) where T: notnull
    {
        foreach (var uid in ent.Comp.Mutations.Values)
        {
            RaiseLocalEvent(uid, ref args);
        }
    }
}
