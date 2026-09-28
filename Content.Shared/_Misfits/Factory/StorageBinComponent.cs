// SPDX-License-Identifier: AGPL-3.0-or-later
// Adapted from Goob Station / Trauma Station

using Content.Shared.DeviceLinking;

namespace Content.Shared._Misfits.Factory;

/// <summary>
/// Makes a storage check filter slot and invoke signals.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class StorageBinComponent : Component
{
    /// <summary>
    /// Signal port invoked after inserting an item.
    /// </summary>
    [DataField]
    public ProtoId<SourcePortPrototype> InsertedPort = "StorageInserted";

    /// <summary>
    /// Signal port invoked after removing an item.
    /// </summary>
    [DataField]
    public ProtoId<SourcePortPrototype> RemovedPort = "StorageRemoved";
}

[Serializable, NetSerializable]
public enum StorageBinLayers : byte
{
    Powered
}
