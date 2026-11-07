using Robust.Shared.Prototypes;
using Content.Shared.DeviceLinking;

namespace Content.Server.Explosion.Components
{
    /// <summary>
    /// Sends a trigger when signal is received.
    /// </summary>
    [RegisterComponent]
    public sealed partial class TriggerOnSignalComponent : Component
    {
        [DataField("port")]
        public ProtoId<SinkPortPrototype> Port = "Trigger";
    }
}
