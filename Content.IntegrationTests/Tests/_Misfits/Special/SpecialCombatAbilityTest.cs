using Content.Server._Misfits.SpecialStats;
using Content.Server._Misfits.SpecialStats.Components;
using Content.Server.Station.Systems;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.SpecialStats;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;

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

            // The action must allow clicks beyond interaction range; the handler
            // clamps the dash distance itself. Regression: default checkCanAccess
            // limited the action to ~1.5 tiles.
            var abilities = server.EntMan.GetComponent<SpecialCombatAbilitiesComponent>(mob);
            var chargeAction = server.EntMan.GetComponent<Content.Shared.Actions.WorldTargetActionComponent>(abilities.ChargeActionEntity!.Value);
            Assert.Multiple(() =>
            {
                Assert.That(chargeAction.CheckCanAccess, Is.False, "charge must not be limited to interaction range");
                Assert.That(chargeAction.Range, Is.LessThanOrEqualTo(0f), "charge range gating is done by the handler clamp");
            });

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
    public async Task ParryReflectsAndNegatesMelee()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var spawning = server.EntMan.System<StationSpawningSystem>();
            var damageable = server.EntMan.System<Content.Shared.Damage.DamageableSystem>();

            var parrier = spawning.SpawnPlayerMob(map.GridCoords, null, ProfileWithStats(5, 8, 5), null);
            var attacker = spawning.SpawnPlayerMob(map.GridCoords.Offset(new System.Numerics.Vector2(1f, 0f)), null, ProfileWithStats(5, 5, 5), null);

            var ev = new SpecialParryActionEvent { Performer = parrier };
            server.EntMan.EventBus.RaiseLocalEvent(parrier, ev);
            Assert.That(ev.Handled, Is.True);

            Assert.That(server.EntMan.TryGetComponent<Content.Shared.Weapons.Reflect.ReflectComponent>(parrier, out var reflect), Is.True,
                "parry window should add reflect");
            Assert.That(reflect!.ReflectProb, Is.EqualTo(1f));

            // Simulate the melee path: AttackedEvent then damage with the attacker as origin.
            var attacked = new Content.Shared.Weapons.Melee.Events.AttackedEvent(attacker, attacker, server.EntMan.GetComponent<TransformComponent>(parrier).Coordinates);
            server.EntMan.EventBus.RaiseLocalEvent(parrier, attacked);

            var damage = new Content.Shared.Damage.DamageSpecifier();
            damage.DamageDict.Add("Blunt", 10);
            var result = damageable.TryChangeDamage(parrier, damage, origin: attacker);

            Assert.Multiple(() =>
            {
                Assert.That(result == null || result.GetTotal() == 0, "parried melee damage should be negated");
                Assert.That(server.EntMan.HasComponent<Content.Shared.Stunnable.StunnedComponent>(attacker), Is.True,
                    "parried attacker should be staggered");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ParryWindowExpires()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid parrier = default;
        await server.WaitAssertion(() =>
        {
            var spawning = server.EntMan.System<StationSpawningSystem>();
            parrier = spawning.SpawnPlayerMob(map.GridCoords, null, ProfileWithStats(5, 8, 5), null);

            var ev = new SpecialParryActionEvent { Performer = parrier };
            server.EntMan.EventBus.RaiseLocalEvent(parrier, ev);
            Assert.That(ev.Handled, Is.True);
        });

        // 2 second window at the test tickrate plus margin.
        await pair.RunTicksSync(80);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.HasComponent<SpecialParryActiveComponent>(parrier), Is.False,
                    "parry window should expire");
                Assert.That(server.EntMan.HasComponent<Content.Shared.Weapons.Reflect.ReflectComponent>(parrier), Is.False,
                    "granted reflect should be removed when the window ends");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CrippleSlowsTarget()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var spawning = server.EntMan.System<StationSpawningSystem>();
            var combat = server.EntMan.System<Content.Shared.CombatMode.SharedCombatModeSystem>();

            var user = spawning.SpawnPlayerMob(map.GridCoords, null, ProfileWithStats(8, 5, 5), null);
            var target = spawning.SpawnPlayerMob(map.GridCoords.Offset(new System.Numerics.Vector2(1f, 0f)), null, ProfileWithStats(5, 5, 5), null);

            combat.SetInCombatMode(user, true);

            var ev = new SpecialCrippleActionEvent { Performer = user, Target = target };
            server.EntMan.EventBus.RaiseLocalEvent(user, ev);

            Assert.Multiple(() =>
            {
                Assert.That(ev.Handled, Is.True, "cripple should land on an adjacent target in combat mode");
                Assert.That(server.EntMan.HasComponent<Content.Shared.Stunnable.SlowedDownComponent>(target), Is.True,
                    "cripple should slow the target");
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
