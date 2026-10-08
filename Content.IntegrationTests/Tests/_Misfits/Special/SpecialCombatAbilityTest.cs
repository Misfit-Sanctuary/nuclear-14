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

    private static HumanoidCharacterProfile ProfileWithMentalStats(int perception, int charisma, int luck)
    {
        // Intelligence is dumped to 1 to keep triple-8 mental builds inside
        // the point budget, mirroring ProfileWithStats.
        return HumanoidCharacterProfile.DefaultWithSpecies()
            .WithName("Mental Tester")
            .WithSpecial(new SpecialProfile
            {
                Strength = 5,
                Perception = perception,
                Endurance = 5,
                Charisma = charisma,
                Intelligence = 1,
                Agility = 5,
                Luck = luck,
            });
    }

    private static void GiveMeleeWeapon(Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server, EntityUid user)
    {
        var knife = server.EntMan.SpawnEntity("CombatKnife", server.EntMan.GetComponent<TransformComponent>(user).Coordinates);
        var hands = server.EntMan.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>();
        Assert.That(hands.TryPickupAnyHand(user, knife), Is.True, "test mob should pick up the knife");
    }

    [Test]
    public async Task ChargeAndParryRequireMeleeWeapon()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var spawning = server.EntMan.System<StationSpawningSystem>();

            // Bare hands: both abilities should refuse without consuming anything.
            var mob = spawning.SpawnPlayerMob(map.GridCoords, null, ProfileWithStats(5, 8, 8), null);

            var charge = new SpecialChargeActionEvent
            {
                Performer = mob,
                Target = map.GridCoords.Offset(new System.Numerics.Vector2(3f, 0f)),
            };
            server.EntMan.EventBus.RaiseLocalEvent(mob, charge);

            var parry = new SpecialParryActionEvent { Performer = mob };
            server.EntMan.EventBus.RaiseLocalEvent(mob, parry);

            Assert.Multiple(() =>
            {
                Assert.That(charge.Handled, Is.False, "charge should require a melee weapon in hand");
                Assert.That(parry.Handled, Is.False, "parry should require a melee weapon in hand");
                Assert.That(server.EntMan.HasComponent<SpecialChargingComponent>(mob), Is.False);
                Assert.That(server.EntMan.HasComponent<SpecialParryActiveComponent>(mob), Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task KeenEyeScopesBlocksMovementAndDelaysExit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid user = default;

        await server.WaitAssertion(() =>
        {
            var spawning = server.EntMan.System<StationSpawningSystem>();
            user = spawning.SpawnPlayerMob(map.GridCoords, null, ProfileWithMentalStats(8, 5, 5), null);

            var abilities = server.EntMan.GetComponent<SpecialCombatAbilitiesComponent>(user);
            Assert.That(abilities.KeenEyeActionEntity, Is.Not.Null, "Perception 8 should grant keen eye");

            var scopeOn = new SpecialKeenEyeActionEvent { Performer = user };
            server.EntMan.EventBus.RaiseLocalEvent(user, scopeOn);
            Assert.That(scopeOn.Handled, Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.HasComponent<Content.Shared.Interaction.Components.BlockMovementComponent>(user), Is.True,
                    "scoping in should immobilize");
                var eye = server.EntMan.GetComponent<Content.Shared.Movement.Components.ContentEyeComponent>(user);
                Assert.That(eye.TargetZoom.X, Is.GreaterThan(1f), "scoping in should zoom out");
            });

            // Request scope-out: starts the 2 second do-after, still immobile.
            var scopeOff = new SpecialKeenEyeActionEvent { Performer = user };
            server.EntMan.EventBus.RaiseLocalEvent(user, scopeOff);
            Assert.That(server.EntMan.HasComponent<Content.Shared.Interaction.Components.BlockMovementComponent>(user), Is.True,
                "scope-out is not instant");
        });

        // 2 s do-after at 30 tps plus margin.
        await pair.RunTicksSync(75);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.HasComponent<Content.Shared.Interaction.Components.BlockMovementComponent>(user), Is.False,
                    "movement should be restored after the exit delay");
                var eye = server.EntMan.GetComponent<Content.Shared.Movement.Components.ContentEyeComponent>(user);
                Assert.That(eye.TargetZoom.X, Is.EqualTo(1f), "zoom should reset after scoping out");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RallyBuffsSelfAndFriendlyAllies()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var spawning = server.EntMan.System<StationSpawningSystem>();
            var factions = server.EntMan.System<Content.Shared.NPC.Systems.NpcFactionSystem>();

            var user = spawning.SpawnPlayerMob(map.GridCoords, null, ProfileWithMentalStats(5, 8, 5), null);
            var ally = spawning.SpawnPlayerMob(map.GridCoords.Offset(new System.Numerics.Vector2(2f, 0f)), null, ProfileWithMentalStats(5, 5, 5), null);
            var stranger = spawning.SpawnPlayerMob(map.GridCoords.Offset(new System.Numerics.Vector2(-2f, 0f)), null, ProfileWithMentalStats(5, 5, 5), null);

            factions.AddFaction(user, "NanoTrasen");
            factions.AddFaction(ally, "NanoTrasen");
            // Spawned humans share a default faction, so strip the stranger's
            // to make it genuinely unaffiliated.
            factions.ClearFactions(stranger);

            var ev = new SpecialRallyActionEvent { Performer = user };
            server.EntMan.EventBus.RaiseLocalEvent(user, ev);

            Assert.Multiple(() =>
            {
                Assert.That(ev.Handled, Is.True);
                Assert.That(server.EntMan.HasComponent<Content.Shared._Misfits.Warcry.WarcryBuffComponent>(user), Is.True,
                    "rally should buff the user");
                Assert.That(server.EntMan.HasComponent<Content.Shared._Misfits.Warcry.WarcryBuffComponent>(ally), Is.True,
                    "rally should buff same-faction allies");
                Assert.That(server.EntMan.HasComponent<Content.Shared._Misfits.Warcry.WarcryBuffComponent>(stranger), Is.False,
                    "rally should not buff non-allied mobs");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LuckyBreakTemporarilyBoostsLuck()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var spawning = server.EntMan.System<StationSpawningSystem>();
            var special = server.EntMan.System<SharedSpecialSystem>();

            var user = spawning.SpawnPlayerMob(map.GridCoords, null, ProfileWithMentalStats(5, 5, 8), null);

            var ev = new SpecialLuckyBreakActionEvent { Performer = user };
            server.EntMan.EventBus.RaiseLocalEvent(user, ev);

            Assert.Multiple(() =>
            {
                Assert.That(ev.Handled, Is.True);
                Assert.That(special.GetEffective(user, SpecialStat.Luck), Is.EqualTo(10),
                    "lucky break should boost effective luck to the cap");
            });
        });

        await pair.CleanReturnAsync();
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
            GiveMeleeWeapon(server, mob);

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
    public async Task ChargeResistsDamageAndStaggersOnImpact()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        EntityUid charger = default;
        EntityUid victim = default;

        await server.WaitAssertion(() =>
        {
            var spawning = server.EntMan.System<StationSpawningSystem>();
            var damageable = server.EntMan.System<Content.Shared.Damage.DamageableSystem>();

            charger = spawning.SpawnPlayerMob(map.GridCoords, null, ProfileWithStats(5, 5, 8), null);
            GiveMeleeWeapon(server, charger);
            victim = spawning.SpawnPlayerMob(map.GridCoords.Offset(new System.Numerics.Vector2(2f, 0f)), null, ProfileWithStats(5, 5, 5), null);

            var ev = new SpecialChargeActionEvent
            {
                Performer = charger,
                Target = map.GridCoords.Offset(new System.Numerics.Vector2(4f, 0f)),
            };
            server.EntMan.EventBus.RaiseLocalEvent(charger, ev);
            Assert.That(ev.Handled, Is.True);

            Assert.That(server.EntMan.HasComponent<SpecialChargingComponent>(charger), Is.True,
                "charging state should be active during the lunge");

            var damage = new Content.Shared.Damage.DamageSpecifier();
            damage.DamageDict.Add("Blunt", 10);
            var result = damageable.TryChangeDamage(charger, damage, origin: victim);

            Assert.That(result, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(result!.GetTotal(), Is.GreaterThan(Content.Shared.FixedPoint.FixedPoint2.Zero),
                    "charging is resistance, not immunity");
                Assert.That(result.GetTotal(), Is.LessThan(Content.Shared.FixedPoint.FixedPoint2.New(10)),
                    "damage should be reduced while charging");
            });
        });

        // Collision with the victim happens ~4 ticks in (2 tiles at speed 15);
        // check the stagger before its 0.5 s stun expires.
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.HasComponent<Content.Shared.Stunnable.StunnedComponent>(victim), Is.True,
                "mob hit by the charge should be staggered");
        });

        // Then let the lunge finish and the safety expiry run.
        await pair.RunTicksSync(70);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.HasComponent<SpecialChargingComponent>(charger), Is.False,
                "charging state should end when the lunge stops");
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
            GiveMeleeWeapon(server, parrier);
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
            GiveMeleeWeapon(server, parrier);

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
