// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Misfits.Genetics.Mutations;
using Robust.Shared.Analyzers;

namespace Content.Shared._Misfits.Genetics.Console;

/// <summary>
/// A disk storing genetics data.
/// It can store either a mutation or unique enzymes, but not both.
/// This is for the geneticist what an id card is for HoP.
/// </summary>
// Misfits Tweak - Other=ReadExecute so the requisitions terminal can read the stored
// mutation when appraising a disk. Writes stay restricted to GeneticsDiskSystem.
[RegisterComponent, NetworkedComponent, Access(typeof(GeneticsDiskSystem), Other = AccessPermissions.ReadExecute)]
[AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class GeneticsDiskComponent : Component
{
    /// <summary>
    /// The mutation stored on this disk.
    /// It can be set by a genetics console while a mutated mob is in the scanner.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntProtoId<MutationComponent>? Mutation;

    /// <summary>
    /// The unique enzymes stored on this disk.
    /// It can be set by a genetics console while a scanned mutatable mob is in the scanner.
    /// </summary>
    [DataField, AutoNetworkedField]
    public UniqueEnzymes? Enzymes;
}
