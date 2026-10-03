using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.List;
using Robust.Shared.Prototypes;

namespace Content.Shared.Access.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedIdCardConsoleSystem))]
public sealed partial class IdCardConsoleComponent : Component
{
    public const int MaxFullNameLength = 30;
    public const int MaxJobTitleLength = 30;

    public static string PrivilegedIdCardSlotId = "IdCardConsole-privilegedId";
    public static string TargetIdCardSlotId = "IdCardConsole-targetId";

    [DataField]
    public ItemSlot PrivilegedIdSlot = new();

    [DataField]
    public ItemSlot TargetIdSlot = new();

    [Serializable, NetSerializable]
    public sealed class WriteToTargetIdMessage : BoundUserInterfaceMessage
    {
        public readonly string FullName;
        public readonly string JobTitle;
        public readonly List<ProtoId<AccessLevelPrototype>> AccessList;
        public readonly ProtoId<AccessLevelPrototype> JobPrototype;

        public WriteToTargetIdMessage(string fullName, string jobTitle, List<ProtoId<AccessLevelPrototype>> accessList, ProtoId<AccessLevelPrototype> jobPrototype)
        {
            FullName = fullName;
            JobTitle = jobTitle;
            AccessList = accessList;
            JobPrototype = jobPrototype;
        }
    }

    // Put this on shared so we just send the state once in PVS range rather than every time the UI updates.

    [DataField, AutoNetworkedField]
    public List<ProtoId<AccessLevelPrototype>> AccessLevels = new()
    {
        "TownieMechanic", // All N14 change for the access terminals.
        "TownieShopkeeper",
        "TownieDoctor",
        "TownieLaw",
        "TownieMayor",
        "TownsPerson",
        "WastelandReporter",
        "WastelandBartender",
        "InnRoomOne",
        "InnRoomTwo",
        "InnRoomThree",
        "FotA", // Misfit: FotA Start
        "FotADoctor",
        "FotAHead",
        "VaultDweller", // Misfit: Vault Start
        "VaultEngineer",
        "VaultMedical",
        "VaultSecurity",
        "VaultOverseer",
        "VaultCargo",
        "VaultCourier",
        "VaultSalvage",
        "TribeMember", // Misfit: Tribe Start
        "TribeChief",
        "WastelandChaplain", // Misfit: Wastelander
        "WastelandFarmer",
        "NCR", // Misfit: NCR Start
        "NCRSGT",
        "NCRMedic",
        "NCRLT",
        "NCRRanger",
        "NCRProvost",
        "Enclave", // Misfits: Enclave Start
        "EnclaveNCO",
        "EnclaveOfficer",
        "EnclaveCommand",
        "EnclaveScience",
        "EnclaveRoleAll",
        "EnclaveRoleScientist",
        "EnclaveRoleHeadScientist",
        "EnclaveRoleNCO",
        "EnclaveRoleJuniorOfficer",
        "EnclaveRoleSeniorOfficer",
        "EnclaveRoleCommander",
        "CaesarLegion", // Misfit: Legion Start
        "CaesarLegionRecruit", // #Misfits Add - Cell door gate access for Legion IDs
        "CaesarLegionSlave",
        "CaesarLegionFrumentarii",
        "CaesarLegionVexillarius",
        "CaesarLegionLegionnaireRecruit",
        "CaesarLegionLegionnaireWarrior",
        "CaesarLegionLegionnaireVeteran",
        "CaesarLegionDean",
        "CaesarLegionVeteranDecanus",
        "CaesarLegionOptio",
        "CaesarLegionCenturion",
        "BOS", // Misfit: BOS Start
        "BOSInitiate",
        "BOSKnight",
        "BOSScribe",
        "BOSPaladin",
        "BOSHeadPaladin",
        "80s", // Misfit: 80s Start
        "80sHead",
        "80sSlave",
    };

    [Serializable, NetSerializable]
    public sealed class IdCardConsoleBoundUserInterfaceState : BoundUserInterfaceState
    {
        public readonly string PrivilegedIdName;
        public readonly bool IsPrivilegedIdPresent;
        public readonly bool IsPrivilegedIdAuthorized;
        public readonly bool IsTargetIdPresent;
        public readonly string TargetIdName;
        public readonly string? TargetIdFullName;
        public readonly string? TargetIdJobTitle;
        public readonly List<ProtoId<AccessLevelPrototype>>? TargetIdAccessList;
        public readonly List<ProtoId<AccessLevelPrototype>>? AllowedModifyAccessList;
        public readonly ProtoId<AccessLevelPrototype> TargetIdJobPrototype;

        public IdCardConsoleBoundUserInterfaceState(bool isPrivilegedIdPresent,
            bool isPrivilegedIdAuthorized,
            bool isTargetIdPresent,
            string? targetIdFullName,
            string? targetIdJobTitle,
            List<ProtoId<AccessLevelPrototype>>? targetIdAccessList,
            List<ProtoId<AccessLevelPrototype>>? allowedModifyAccessList,
            ProtoId<AccessLevelPrototype> targetIdJobPrototype,
            string privilegedIdName,
            string targetIdName)
        {
            IsPrivilegedIdPresent = isPrivilegedIdPresent;
            IsPrivilegedIdAuthorized = isPrivilegedIdAuthorized;
            IsTargetIdPresent = isTargetIdPresent;
            TargetIdFullName = targetIdFullName;
            TargetIdJobTitle = targetIdJobTitle;
            TargetIdAccessList = targetIdAccessList;
            AllowedModifyAccessList = allowedModifyAccessList;
            TargetIdJobPrototype = targetIdJobPrototype;
            PrivilegedIdName = privilegedIdName;
            TargetIdName = targetIdName;
        }
    }

    [Serializable, NetSerializable]
    public enum IdCardConsoleUiKey : byte
    {
        Key,
    }
}
