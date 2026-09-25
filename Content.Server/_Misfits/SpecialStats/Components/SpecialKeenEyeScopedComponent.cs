namespace Content.Server._Misfits.SpecialStats.Components;

/// <summary>
/// Present while the keen eye scoped stance is active.
/// </summary>
[RegisterComponent]
public sealed partial class SpecialKeenEyeScopedComponent : Component
{
    /// <summary>
    /// True once the exit do-after has started, so repeated presses don't
    /// stack do-afters.
    /// </summary>
    public bool Stopping;
}
