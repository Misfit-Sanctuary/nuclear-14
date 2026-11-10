// #Misfits Add - Stub enums for _RMC YAML prototype compatibility.
// These enums are referenced in RMC prototype YAML files as sprite layer maps
// and UI keys but have no backing C# systems in this fork.
using Robust.Shared.Serialization;

namespace Content.Shared._RMC.Stubs;

[Serializable, NetSerializable]
public enum AdjustableArmorUiKey { Key }

[Serializable, NetSerializable]
public enum AegisCrateVisualLayers { Base }

[Serializable, NetSerializable]
public enum AttachmentUI { ChooseSlotKey, StripKey }

[Serializable, NetSerializable]
public enum BarbedWireVisualLayers { Wire }

[Serializable, NetSerializable]
public enum BloodPackVisuals { Fill, Label }

[Serializable, NetSerializable]
public enum BreechVisuals { Open }

[Serializable, NetSerializable]
public enum BulletBoxLayers { Fill }

[Serializable, NetSerializable]
public enum BulletBoxLayers2 { Fill }

[Serializable, NetSerializable]
public enum CMAutomatedVendorUI { Key }

[Serializable, NetSerializable]
public enum CMMobStateActionsUI { Key }

[Serializable, NetSerializable]
public enum CMSurgeryUIKey { Key }

// #Misfits Change - CampfireVisuals moved to Content.Shared/_Misfits/Campfire/CampfireComponent.cs
// public enum CampfireVisuals { Lit }

[Serializable, NetSerializable]
public enum CommunicationsTowerLayers { Layer }

[Serializable, NetSerializable]
public enum DeployableItemVisuals { Deployed }

[Serializable, NetSerializable]
public enum DialogUiKey { Key }

[Serializable, NetSerializable]
public enum DialysisVisualLayers { Attachment, Battery, Effect, Filtering }

[Serializable, NetSerializable]
public enum DialysisVisuals { BatteryLevel }

[Serializable, NetSerializable]
public enum DogtagVisuals { Taken }

[Serializable, NetSerializable]
public enum DropshipAmmoVisuals { Fill }

[Serializable, NetSerializable]
public enum DropshipFabricatorState { Fabricating, Idle }

[Serializable, NetSerializable]
public enum DropshipFabricatorUi { Key }

[Serializable, NetSerializable]
public enum DropshipFabricatorVisuals { State }

[Serializable, NetSerializable]
public enum DropshipHijackerUiKey { Key }

[Serializable, NetSerializable]
public enum DropshipNavigationUiKey { Key }

[Serializable, NetSerializable]
public enum DropshipPointVisualsLayers { AttachedUtility, AttachmentBase }

[Serializable, NetSerializable]
public enum DropshipTerminalUiKey { Key }

[Serializable, NetSerializable]
public enum DropshipTerminalWeaponsUi { Key }

[Serializable, NetSerializable]
public enum DropshipWeaponPointLayers { Layer }

[Serializable, NetSerializable]
public enum EggmorpherOverlayLayers { Base, Overlay }

[Serializable, NetSerializable]
public enum EntrenchingToolComponentVisualLayers { Base, Dirt, Folded }

[Serializable, NetSerializable]
public enum EquipmentDeployState { Deployed, UnDeployed }

[Serializable, NetSerializable]
public enum EvacuationComputerUi { Key }

[Serializable, NetSerializable]
public enum EvacuationPumpLayers { Layer }

[Serializable, NetSerializable]
public enum FlashVisualLayers { LightLayer }

[Serializable, NetSerializable]
public enum ForTheHiveVisualLayers { Base }

[Serializable, NetSerializable]
public enum HealthScannerUIKey { Key }

[Serializable, NetSerializable]
public enum HolocardChangeUIKey { Key }

[Serializable, NetSerializable]
public enum HolocardContainerVisualLayers { Base }

[Serializable, NetSerializable]
public enum HyperSleepChamberLayers { Base }

[Serializable, NetSerializable]
public enum IVDripVisualLayers { Base, Reagent }

[Serializable, NetSerializable]
public enum IdModificationConsoleUIKey { Key }

[Serializable, NetSerializable]
public enum InfoTagVisuals { Number }

[Serializable, NetSerializable]
public enum IntelDetectorLayer { State }

[Serializable, NetSerializable]
public enum ItemCabinetVisuals { Layer }

[Serializable, NetSerializable]
public enum LandingLightVisuals { Off, On }

[Serializable, NetSerializable]
public enum LifeboatComputerUi { Key }

[Serializable, NetSerializable]
public enum MarineCommunicationsComputerUI { Key }

[Serializable, NetSerializable]
public enum MarineControlComputerUi { Key, MedalsPanel }

[Serializable, NetSerializable]
public enum MedalVisualLayers { Base, Base1 }

[Serializable, NetSerializable]
public enum MedevacStretcherVisuals { BeaconState, MedevacingState }

[Serializable, NetSerializable]
public enum MortarUiKey { Key }

[Serializable, NetSerializable]
public enum MortarVisualLayers { State }

[Serializable, NetSerializable]
public enum MotionDetectorLayer { Number, Setting }

[Serializable, NetSerializable]
public enum NameTunnelUI { Key }

[Serializable, NetSerializable]
public enum NewPlayerLayers { Layer }

[Serializable, NetSerializable]
public enum NightVisionItemVisuals { Active }

[Serializable, NetSerializable]
public enum OrbitalCannonComputerUI { Key }

[Serializable, NetSerializable]
public enum OrbitalCannonTrayVisuals { Fuel, Warhead }

[Serializable, NetSerializable]
public enum OverwatchConsoleUI { Key }

[Serializable, NetSerializable]
public enum ParasiteOverlayLayers { Back, Head, LeftArm, RightArm }

[Serializable, NetSerializable]
public enum PlantableFlagVisuals { Planted }

[Serializable, NetSerializable]
public enum PurifiableWaterLayers { Layer }

[Serializable, NetSerializable]
public enum RMCAlertLevelDisplayVisualLayers { HourOnes, HourTens, MinuteOnes, MinuteTens, Separator }

[Serializable, NetSerializable]
public enum RMCAlertLevelsVisuals { Alert }

[Serializable, NetSerializable]
public enum RMCApcUiKey { Key }

[Serializable, NetSerializable]
public enum RMCApcVisualsLayers { Layer, Lock, Power }

[Serializable, NetSerializable]
public enum RMCCameraUiKey { Key }

[Serializable, NetSerializable]
public enum RMCChemMasterUI { Key }

[Serializable, NetSerializable]
public enum RMCChemicalDispenserUi { Key }

[Serializable, NetSerializable]
public enum RMCConstructionUiKey { Key }

[Serializable, NetSerializable]
public enum RMCConstructionUpgradeUiKey { Key }

[Serializable, NetSerializable]
public enum RMCDamageVisualLayers { Base }

[Serializable, NetSerializable]
public enum RMCFlamerVisualLayers { Strip }

[Serializable, NetSerializable]
public enum RMCFusionReactorLayers { Layer }

[Serializable, NetSerializable]
public enum RMCGhostVisibleOnlyVisualLayers { Base }

[Serializable, NetSerializable]
public enum RMCHandLabelerUiKey { PillBottleColor }

[Serializable, NetSerializable]
public enum RMCIgniterVisuals { Ignited }

[Serializable, NetSerializable]
public enum RMCMagazineVisuals { SlideOpen }

[Serializable, NetSerializable]
public enum RMCPillBottleVisuals { Color }

[Serializable, NetSerializable]
public enum RMCPodDoorButtonLayers { Animation }

[Serializable, NetSerializable]
public enum RMCRadioFilterUI { Key }

[Serializable, NetSerializable]
public enum RMCShieldVisuals { Base }

[Serializable, NetSerializable]
public enum RMCSmartFridgeUI { Key }

[Serializable, NetSerializable]
public enum RMCTelephoneUiKey { Key }

[Serializable, NetSerializable]
public enum RangefinderLayers { Layer }

[Serializable, NetSerializable]
public enum RangefinderUiKey { Key }

[Serializable, NetSerializable]
public enum RequisitionsElevatorLayers { Base }

[Serializable, NetSerializable]
public enum RequisitionsGearLayers { Base }

[Serializable, NetSerializable]
public enum RequisitionsRailingLayers { Base }

[Serializable, NetSerializable]
public enum RequisitionsUIKey { Key }

[Serializable, NetSerializable]
public enum RotaryPhoneLayers { Layer }

[Serializable, NetSerializable]
public enum RotationVisualLayers { Layer }

[Serializable, NetSerializable]
public enum SelectDestinationTunnelUI { Key }

[Serializable, NetSerializable]
public enum SensorTowerLayers { Layer }

[Serializable, NetSerializable]
public enum SentryLaptopUiKey { Key }

[Serializable, NetSerializable]
public enum SentryLaptopVisualLayers { Base }

[Serializable, NetSerializable]
public enum SentryLaptopVisuals { State }

[Serializable, NetSerializable]
public enum SentryLayers { Layer }

[Serializable, NetSerializable]
public enum SentryUiKey { Key }

[Serializable, NetSerializable]
public enum SolutionContainerStoreVisuals { Base }

[Serializable, NetSerializable]
public enum SprayAcidedVisuals { Acided }

[Serializable, NetSerializable]
public enum SquadArmorLayers { Armor, Gloves, Helmet }

[Serializable, NetSerializable]
public enum SquadLeaderTrackerUI { Key }

[Serializable, NetSerializable]
public enum SupplyDropComputerUi { Key }

[Serializable, NetSerializable]
public enum TacticalMapComputerUi { Key }

[Serializable, NetSerializable]
public enum TacticalMapUserUi { Key }

[Serializable, NetSerializable]
public enum TechControlConsoleUI { Key }

[Serializable, NetSerializable]
public enum TileFireLayers { Base }

[Serializable, NetSerializable]
public enum TimerStartMode { OnHitGround }

[Serializable, NetSerializable]
public enum ToggleableVisuals { Enabled, Layer }

[Serializable, NetSerializable]
public enum UniformAccessoriesUi { Key }

[Serializable, NetSerializable]
public enum UserAcidedVisuals { Acided }

[Serializable, NetSerializable]
public enum VialVisuals { Occupied }

[Serializable, NetSerializable]
public enum ViewIntelObjectivesUI { Key }

[Serializable, NetSerializable]
public enum VisorVisualLayers { Base }

[Serializable, NetSerializable]
public enum WeaponMountComponentVisualLayers { Broken, Folded, FoldedAmmo, Mounted, MountedAmmo, Overheated }

[Serializable, NetSerializable]
public enum WeededEntityLayers { Layer }

[Serializable, NetSerializable]
public enum XenoAcidGeneratingVisualLayers { Base }

[Serializable, NetSerializable]
public enum XenoChooseStructureUI { Key }

[Serializable, NetSerializable]
public enum XenoDevolveUIKey { Key }

[Serializable, NetSerializable]
public enum XenoEggLayers { Base }

[Serializable, NetSerializable]
public enum XenoEggStorageVisualLayers { Base }

[Serializable, NetSerializable]
public enum XenoEvolutionUIKey { Key }

[Serializable, NetSerializable]
public enum XenoFruitChooseUI { Key }

[Serializable, NetSerializable]
public enum XenoFruitLayers { Base }

[Serializable, NetSerializable]
public enum XenoFruitVisualLayers { Base }

[Serializable, NetSerializable]
public enum XenoHealerVisualLayers { Goop }

[Serializable, NetSerializable]
public enum XenoInhandVisualLayers { Left, Right }

[Serializable, NetSerializable]
public enum XenoOrderConstructionUI { Key }

[Serializable, NetSerializable]
public enum XenoParasiteGhostUI { Key }

[Serializable, NetSerializable]
public enum XenoPheromonesUI { Key }

[Serializable, NetSerializable]
public enum XenoReserveParasiteChangeUI { Key }

[Serializable, NetSerializable]
public enum XenoResinHoleLayers { Base }

[Serializable, NetSerializable]
public enum XenoResinHoleVisuals { Contained }

[Serializable, NetSerializable]
public enum XenoShardVisualLayers { Base }

[Serializable, NetSerializable]
public enum XenoVisualLayers { Base, Ovipositor }

[Serializable, NetSerializable]
public enum XenoWatchUIKey { Key }

[Serializable, NetSerializable]
public enum XenoWordQueenUI { Key }
