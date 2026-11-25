// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._Misfits.Speech;

[RegisterComponent, NetworkedComponent]
public sealed partial class SpeechFontOverrideComponent : Component
{
    [DataField]
    public string? Font;

    // Added to the chat font size of whatever this entity says.
    [DataField]
    public int FontSizeBonus;

    [DataField]
    public bool SourceOnly = true;
}
