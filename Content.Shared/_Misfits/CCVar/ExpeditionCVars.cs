using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._Misfits.CCVar;

/// <summary>Runtime controls for the alpha expedition feature.</summary>
[CVarDefs]
public sealed class ExpeditionCVars : CVars
{
    /// <summary>
    /// Allows new expedition entrances and launches. Defaults on; no TOML entry
    /// is required. Existing expeditions may still finish and extract safely.
    /// </summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("misfits.expeditions.enabled", true, CVar.SERVERONLY);
}
