using Content.Server._Misfits.SpecialStats;
using Content.Server._Misfits.SpecialStats.Components;
using Content.Server.Station.Systems;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.SpecialStats;
using Content.Shared.Preferences;

namespace Content.IntegrationTests.Tests._Misfits.Special;

[TestFixture]
[TestOf(typeof(SpecialCombatAbilitySystem))]
public sealed class SpecialCombatAbilityTest
{
    private static HumanoidCharacterProfile ProfileWithStats(int strength, int endurance, int agility)
    {
        // Charisma is dumped to 1 so a triple-8 build stays inside the
        // SpecialProfile.MaxTotal point budget; over-budget profiles are
        // silently replaced with all-5 defaults by EnsureValid.
        return HumanoidCharacterProfile.DefaultWithSpecies()
            .WithName("Ability Tester")
            .WithSpecial(new SpecialProfile
            {
                Strength = strength,
                Perception = 5,
                Endurance = endurance,
                Charisma = 1,
                Intelligence = 5,
                Agility = agility,
                Luck = 5,
            });
    }

    [Test]
    public async Task HighStatsGrantAbilitiesAndDropRevokes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var spawning = server.EntMan.System<StationSpawningSystem>();
            var special = server.EntMan.System<SharedSpecialSystem>();

            var mob = spawning.SpawnPlayerMob(map.GridCoords, null, ProfileWithStats(8, 8, 8), null);

            Assert.That(server.EntMan.TryGetComponent<SpecialCombatAbilitiesComponent>(mob, out var abilities), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(abilities!.ChargeActionEntity, Is.Not.Null, "Agility 8 should grant charge");
                Assert.That(abilities.ParryActionEntity, Is.Not.Null, "Endurance 8 should grant parry");
                Assert.That(abilities.CrippleActionEntity, Is.Not.Null, "Strength 8 should grant cripple");
            });

            // Dropping one stat below the threshold revokes only that ability.
            Assert.That(special.TryModifyTemporary(mob, SpecialStat.Agility, -3, source: "ability-test"), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(abilities!.ChargeActionEntity, Is.Null, "Agility 5 should revoke charge");
                Assert.That(abilities.ParryActionEntity, Is.Not.Null);
                Assert.That(abilities.CrippleActionEntity, Is.Not.Null);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ChargeThrowsUserTowardTarget()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var spawning = server.EntMan.System<StationSpawningSystem>();
            var mob = spawning.SpawnPlayerMob(map.GridCoords, null, ProfileWithStats(5, 5, 8), null);

            var destination = map.GridCoords.Offset(new System.Numerics.Vector2(3f, 0f));
            var ev = new SpecialChargeActionEvent { Performer = mob, Target = destination };
            server.EntMan.EventBus.RaiseLocalEvent(mob, ev);

            Assert.Multiple(() =>
            {
                Assert.That(ev.Handled, Is.True, "charge event should be handled");
                Assert.That(server.EntMan.HasComponent<Content.Shared.Throwing.ThrownItemComponent>(mob), Is.True,
                    "charging should put the user in thrown (lunging) state");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DefaultStatsGrantNothing()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var spawning = server.EntMan.System<StationSpawningSystem>();
            var mob = spawning.SpawnPlayerMob(map.GridCoords, null, ProfileWithStats(5, 5, 5), null);

            if (server.EntMan.TryGetComponent<SpecialCombatAbilitiesComponent>(mob, out var abilities))
            {
                Assert.Multiple(() =>
                {
                    Assert.That(abilities.ChargeActionEntity, Is.Null);
                    Assert.That(abilities.ParryActionEntity, Is.Null);
                    Assert.That(abilities.CrippleActionEntity, Is.Null);
                });
            }
        });

        await pair.CleanReturnAsync();
    }
}
