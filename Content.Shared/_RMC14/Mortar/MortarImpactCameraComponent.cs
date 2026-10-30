namespace Content.Shared._RMC14.Mortar;

/// Marks the camera an illumination round leaves at its impact point.

[RegisterComponent]
[Access(typeof(SharedMortarSystem))]
public sealed partial class MortarImpactCameraComponent : Component;
