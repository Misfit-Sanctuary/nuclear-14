using Content.Server.GameTicking;
using Content.Shared._Misfits.PersonalLoadouts;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Roles;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Misfits.PersonalLoadouts;

/// <summary>
/// Gives a profile's configured personal kits to its approved account and character
/// only. Empty account or character lists deliberately grant nothing.
/// </summary>
public sealed class PersonalLoadoutGrantSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly ClothingSystem _clothing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (args.JobId == null)
            return;

        foreach (var profile in _prototypes.EnumeratePrototypes<PersonalLoadoutProfilePrototype>())
        {
            if (profile.AccountNames.Count == 0
                || profile.CharacterNames.Count == 0
                || !Matches(profile.AccountNames, args.Player.Name)
                || !Matches(profile.CharacterNames, args.Profile.Name))
            {
                continue;
            }

            foreach (var skin in profile.PowerArmorSkins)
            {
                if (skin.Jobs.Any(job => job.Id == args.JobId))
                    SpawnSkinnedPowerArmor(args.JobId, skin, Transform(args.Mob).Coordinates);
            }
        }
    }

    private void SpawnSkinnedPowerArmor(
        ProtoId<JobPrototype> jobId,
        PersonalLoadoutPowerArmorSkin skin,
        EntityCoordinates coordinates)
    {
        var job = _prototypes.Index<JobPrototype>(jobId);
        if (job.StartingGear == null)
            return;

        var gear = _prototypes.Index<StartingGearPrototype>(job.StartingGear);
        if (!gear.Equipment.TryGetValue("outerClothing", out var outerPrototype))
            return;

        var armor = Spawn(outerPrototype, coordinates);
        if (!TryComp<ClothingComponent>(armor, out var outerClothing)
            || !TryComp<ToggleableClothingComponent>(armor, out var toggleable))
        {
            Del(armor);
            return;
        }

        _clothing.SetSprite(armor, skin.OuterSprite, outerClothing);

        foreach (var (uid, slot) in toggleable.ClothingUids)
        {
            if (slot == "head" && TryComp<ClothingComponent>(uid, out var helmetClothing))
            {
                _clothing.SetSprite(uid, skin.HelmetSprite, helmetClothing);

                if (skin.HelmetClothingVisuals != null)
                    _clothing.SetClothingVisuals(uid, skin.HelmetClothingVisuals, helmetClothing);
            }
        }
    }

    private static bool Matches(List<string> allowedNames, string value)
    {
        return allowedNames.Any(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase));
    }
}
