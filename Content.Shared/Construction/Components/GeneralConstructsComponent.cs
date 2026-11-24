namespace Content.Shared.Construction
{
    /// <summary>
    /// dictionary used by <see cref="GeneralNodeEntity"/> for making construct graphs
    /// essentially instead of duping yaml code for graphs that only change by entity,
    /// just need to type in the key for GeneralNodeEntity to lookup and provide entity ID
    ///  at runtime
    ///  see shields_broken_construction.yml for an example
    /// </summary>
    [RegisterComponent]
    public sealed partial class GeneralConstructsComponent : Component
    {

        [DataField, ViewVariables(VVAccess.ReadWrite)]
        public Dictionary<string, EntProtoId> GeneralEnts = new();
    }
}
