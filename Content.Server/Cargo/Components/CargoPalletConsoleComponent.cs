using Robust.Shared.Prototypes;
using Content.Server.Cargo.Systems;
using Content.Shared.Stacks;

namespace Content.Server.Cargo.Components;

[RegisterComponent]
[Access(typeof(CargoSystem))]
public sealed partial class CargoPalletConsoleComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite), DataField("cashType")]
    public ProtoId<StackPrototype> CashType = "Caps";
}
