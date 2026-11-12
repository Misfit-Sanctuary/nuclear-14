// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Misfits.Common.Speech;

namespace Content.Shared._Misfits.Speech;

public sealed class SpeechFontOverrideSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SpeechFontOverrideComponent, SpeechFontOverrideEvent>(OnOverride);
        SubscribeLocalEvent<SpeechFontOverrideComponent, SpeechFontSizeOverrideEvent>(OnFontSizeOverride);
    }

    private void OnOverride(Entity<SpeechFontOverrideComponent> ent, ref SpeechFontOverrideEvent args)
    {
        if (ent.Comp.Font is not { } font || (ent.Comp.SourceOnly && args.Source != ent.Owner))
            return;

        args.Font = font;
    }

    private void OnFontSizeOverride(Entity<SpeechFontOverrideComponent> ent, ref SpeechFontSizeOverrideEvent args)
    {
        if (ent.Comp.FontSizeBonus == 0)
            return;

        args.FontSize += ent.Comp.FontSizeBonus;
        args.IsActive = true;
    }
}
