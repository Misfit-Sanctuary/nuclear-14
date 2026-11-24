// #Misfits Add - Marker component that grants Enclave recruitment privileges.
// #Cythisiax Edited - The Recruit verb was replaced by the faction roster GUI. Adding this
// component (addcomp) now hands the holder the Enclave roster with the job check skipped,
// so admins can still extend recruitment to non-Enclave roles (e.g. Brotherhood Elder).

namespace Content.Shared._Misfits.Enclave;

/// <summary>
/// When present on a player entity, grants the Enclave faction roster regardless of
/// job. Intended for admin use via <c>addcomp</c>.
/// </summary>
[RegisterComponent]
public sealed partial class EnclaveRecruiterComponent : Component
{
}
