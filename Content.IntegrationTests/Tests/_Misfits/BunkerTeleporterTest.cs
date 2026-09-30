// #Misfits Add - Integration tests for the bunker hatch tunnels.
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Misfits.Warps;
using Content.Server.Carrying;
using Content.Server.Roles.Jobs;
using Content.Shared.Access.Components;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Misfits;

/// <summary>
/// Covers where the bunker hatches and tunnel door send people, the hatch lock, and round-start
/// spawning. Every test puts its entities on their own channel, so leftovers from another test can
/// never be picked up by mistake.
/// </summary>
[TestFixture]
public sealed class BunkerTeleporterTest
{
    private const string HatchProto = "N14BunkerHatchTunnel";
    private const string DoorProto = "N14BunkerTunnelDoor";
    private const string ExitProto = "N14BunkerTunnelExit";
    private const string SpawnPointProto = "N14BunkerHatchSpawnPoint";
    private const string HumanProto = "MobHuman";

    [Test]
    public async Task EnclaveGoDownAHatchToTheDoorAndItLocksBehindThem()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var xform = server.System<SharedTransformSystem>();
        var hatchLock = server.System<BunkerHatchLockSystem>();
        var map = await pair.CreateTestMap();
        const string channel = "test-enclave-down";

        EntityUid hatch = default, door = default, user = default;

        await server.WaitPost(() =>
        {
            RunMap(server, map.MapId);
            door = SpawnOn(entMan, DoorProto, channel, new EntityCoordinates(map.Grid, 12f, 3f));
            hatch = SpawnOn(entMan, HatchProto, channel, map.GridCoords);
            SpawnExit(entMan, channel, new EntityCoordinates(map.Grid, 20f, 20f));
            user = SpawnWithJob(server, map.GridCoords, "EnclaveEnlisted");
        });

        await server.WaitAssertion(() =>
        {
            // Locked or not, Enclave get through.
            Assert.That(hatchLock.IsLocked(hatch), "hatch should start locked");
            Use(entMan, hatch, user);
            AssertSamePlace(xform, user, door, "Enclave member did not arrive at the tunnel door");

            hatchLock.SetLocked(hatch, false);
            xform.SetCoordinates(user, map.GridCoords);
            Use(entMan, hatch, user);
            AssertSamePlace(xform, user, door, "Enclave member did not arrive at the door through an open hatch");
            Assert.That(hatchLock.IsLocked(hatch), "hatch did not lock behind the Enclave member");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OutsidersAreKeptOutByALockedHatch()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var xform = server.System<SharedTransformSystem>();
        var map = await pair.CreateTestMap();
        const string channel = "test-outsider-locked";

        EntityUid hatch = default, user = default;

        await server.WaitPost(() =>
        {
            RunMap(server, map.MapId);
            SpawnOn(entMan, DoorProto, channel, new EntityCoordinates(map.Grid, 12f, 3f));
            SpawnExit(entMan, channel, new EntityCoordinates(map.Grid, 20f, 20f));
            hatch = SpawnOn(entMan, HatchProto, channel, map.GridCoords);
            user = SpawnWithJob(server, map.GridCoords, null);
        });

        await server.WaitAssertion(() =>
        {
            var before = xform.GetMapCoordinates(user).Position;
            Use(entMan, hatch, user);
            Assert.That(Vector2.Distance(before, xform.GetMapCoordinates(user).Position), Is.LessThan(0.01f),
                "an outsider got through a locked hatch");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OutsidersLandInTheMinesOrGetLostAndComeBackOutTheSameHatch()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var xform = server.System<SharedTransformSystem>();
        var hatchLock = server.System<BunkerHatchLockSystem>();
        var timing = server.ResolveDependency<IGameTiming>();
        var map = await pair.CreateTestMap();
        const string channel = "test-outsider-open";

        EntityUid hatch = default, user = default, door = default;
        var exits = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            RunMap(server, map.MapId);
            door = SpawnOn(entMan, DoorProto, channel, new EntityCoordinates(map.Grid, 12f, 3f));
            hatch = SpawnOn(entMan, HatchProto, channel, new EntityCoordinates(map.Grid, 0f, -6f));
            SpawnOn(entMan, HatchProto, channel, new EntityCoordinates(map.Grid, -15f, 0f));
            for (var i = 0; i < 3; i++)
                exits.Add(SpawnExit(entMan, channel, new EntityCoordinates(map.Grid, 5f + i * 3f, 8f)));
            user = SpawnWithJob(server, new EntityCoordinates(map.Grid, 0f, -6f), null);
        });

        await server.WaitAssertion(() =>
        {
            hatchLock.SetLocked(hatch, false);
            var teleporter = entMan.GetComponent<BunkerTeleporterComponent>(hatch);

            // Never lost: always a mine marker, never the Enclave door.
            teleporter.OutsiderLostChance = 0f;
            for (var i = 0; i < 10; i++)
            {
                xform.SetCoordinates(user, new EntityCoordinates(map.Grid, 0f, -6f));
                Use(entMan, hatch, user);
                Assert.That(exits.Any(e => SamePlace(xform, user, e)), "outsider did not land on a mine marker");
                Assert.That(SamePlace(xform, user, door), Is.False, "outsider was let into the Enclave base");
            }

            // Always lost: held inside the hatch for a while.
            teleporter.OutsiderLostChance = 1f;
            teleporter.OutsiderLostTime = TimeSpan.FromSeconds(1);
            xform.SetCoordinates(user, new EntityCoordinates(map.Grid, 0f, -6f));
            Use(entMan, hatch, user);
            Assert.That(entMan.HasComponent<BunkerTunnelLostComponent>(user), "outsider did not get lost in the tunnels");
            Assert.That(entMan.System<SharedContainerSystem>().IsEntityInContainer(user), "lost outsider is not held in the hatch");

            // The hatch keeps working for everyone else, and none of it frees the lost person.
            var enclave = SpawnWithJob(server, new EntityCoordinates(map.Grid, 0f, -6f), "EnclaveNCO");
            Use(entMan, hatch, enclave);
            AssertSamePlace(xform, enclave, door, "Enclave could not use the hatch while someone was lost in it");

            hatchLock.SetLocked(hatch, false);
            teleporter.OutsiderLostChance = 0f;
            var otherOutsider = SpawnWithJob(server, new EntityCoordinates(map.Grid, 0f, -6f), null);
            Use(entMan, hatch, otherOutsider);
            Assert.That(exits.Any(e => SamePlace(xform, otherOutsider, e)), "another outsider could not use the hatch");

            // Clicking the hatch from inside does nothing either.
            Use(entMan, hatch, user);
            Assert.That(entMan.HasComponent<BunkerTunnelLostComponent>(user), "the lost person popped out early");
            Assert.That(entMan.System<SharedContainerSystem>().IsEntityInContainer(user), "the lost person popped out early");
        });

        await pair.RunTicksSync((int) (timing.TickRate * 2));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.HasComponent<BunkerTunnelLostComponent>(user), Is.False, "outsider never came back out");
            Assert.That(entMan.System<SharedContainerSystem>().IsEntityInContainer(user), Is.False, "outsider is still stuck in the hatch");
            AssertSamePlace(xform, user, hatch, "outsider did not come back out of the same hatch");

            // Straight back down: still on their 45 s cooldown, so the mines even at a 100% lost chance.
            var teleporter = entMan.GetComponent<BunkerTeleporterComponent>(hatch);
            teleporter.OutsiderLostChance = 1f;
            hatchLock.SetLocked(hatch, false);
            Use(entMan, hatch, user);
            Assert.That(entMan.HasComponent<BunkerTunnelLostComponent>(user), Is.False, "outsider got lost again during their cooldown");
            Assert.That(exits.Any(e => SamePlace(xform, user, e)), "outsider on cooldown did not land in the mines");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeletingAHatchDropsLostPeopleInsteadOfDeletingThem()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var lost = server.System<BunkerTunnelLostSystem>();
        var map = await pair.CreateTestMap();

        EntityUid hatch = default, user = default;

        await server.WaitPost(() =>
        {
            RunMap(server, map.MapId);
            hatch = SpawnOn(entMan, HatchProto, "test-lost-delete", map.GridCoords);
            user = SpawnWithJob(server, map.GridCoords, null);
            Assert.That(lost.TryLose(user, hatch, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(45)));
            entMan.DeleteEntity(hatch);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.EntityExists(user), "the lost person was deleted along with the hatch");
            Assert.That(entMan.System<SharedContainerSystem>().IsEntityInContainer(user), Is.False);
            Assert.That(entMan.HasComponent<BunkerTunnelLostComponent>(user), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DoorRefusesOutsidersAndSendsEnclaveUpTheChosenHatch()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var xform = server.System<SharedTransformSystem>();
        var hatchLock = server.System<BunkerHatchLockSystem>();
        var map = await pair.CreateTestMap();
        const string channel = "test-door";

        EntityUid door = default, chosen = default, other = default, outsider = default, enclave = default;

        await server.WaitPost(() =>
        {
            RunMap(server, map.MapId);
            // Users stand next to the door, not inside it: it is a solid wall and would push them out.
            door = SpawnOn(entMan, DoorProto, channel, new EntityCoordinates(map.Grid, 0f, 3f));
            other = SpawnOn(entMan, HatchProto, channel, new EntityCoordinates(map.Grid, 6f, 0f));
            chosen = SpawnOn(entMan, HatchProto, channel, new EntityCoordinates(map.Grid, 20f, 0f));
            outsider = SpawnWithJob(server, map.GridCoords, null);
            enclave = SpawnWithJob(server, map.GridCoords, "EnclaveNCO");
        });

        await server.WaitAssertion(() =>
        {
            Use(entMan, door, outsider);
            Assert.That(SamePlace(xform, outsider, chosen) || SamePlace(xform, outsider, other), Is.False,
                "the door sent an outsider up a hatch");

            hatchLock.SetLocked(chosen, false);
            var message = new Content.Shared._Misfits.Warps.BunkerTunnelDoorGoMessage(entMan.GetNetEntity(chosen)) { Actor = enclave };
            entMan.EventBus.RaiseLocalEvent(door, message);

            AssertSamePlace(xform, enclave, chosen, "Enclave member did not come out of the hatch they picked");
            Assert.That(hatchLock.IsLocked(chosen), "the hatch did not lock behind the Enclave member");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OnlyNcoAccessWorksTheLockAndLockingHasACooldown()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var hatchLock = server.System<BunkerHatchLockSystem>();
        var map = await pair.CreateTestMap();

        EntityUid hatch = default, nobody = default, nco = default;

        await server.WaitPost(() =>
        {
            RunMap(server, map.MapId);
            hatch = SpawnOn(entMan, HatchProto, "test-lock", map.GridCoords);
            nobody = entMan.SpawnEntity(null, map.GridCoords);
            nco = entMan.SpawnEntity(null, map.GridCoords);
            entMan.EnsureComponent<AccessComponent>(nco).Tags.Add("EnclaveNCO");
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(hatchLock.TryUnlock(hatch, nobody), Is.False, "someone without NCO access unlocked the hatch");
            Assert.That(hatchLock.TryUnlock(hatch, nco), "an NCO could not unlock the hatch");
            Assert.That(hatchLock.TryLock(hatch, nco), "an NCO could not lock the hatch");
            Assert.That(hatchLock.TryUnlock(hatch, nco), "an NCO could not unlock the hatch again");
            Assert.That(hatchLock.TryLock(hatch, nco), Is.False, "the hatch locked again inside its 30 s cooldown");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OpenHatchLocksItselfAfterTenMinutes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var hatchLock = server.System<BunkerHatchLockSystem>();
        var map = await pair.CreateTestMap();

        EntityUid hatch = default;

        await server.WaitPost(() =>
        {
            RunMap(server, map.MapId);
            hatch = SpawnOn(entMan, HatchProto, "test-autolock", map.GridCoords);
            var comp = entMan.GetComponent<BunkerHatchLockComponent>(hatch);
            comp.AutoLockDelay = TimeSpan.FromSeconds(1);
            hatchLock.SetLocked(hatch, false);
        });

        await server.WaitAssertion(() => Assert.That(hatchLock.IsLocked(hatch), Is.False));
        await pair.RunTicksSync((int) (timing.TickRate * 2));
        await server.WaitAssertion(() => Assert.That(hatchLock.IsLocked(hatch), "the open hatch never locked itself"));

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RoundStartSpawnsTwoLabelledHatchesFromThePool()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var spawner = server.System<BunkerHatchSpawnSystem>();
        var map = await pair.CreateTestMap();
        const string channel = "test-spawn";

        await server.WaitPost(() =>
        {
            RunMap(server, map.MapId);
            for (var i = 0; i < 5; i++)
            {
                var point = entMan.SpawnEntity(SpawnPointProto, new EntityCoordinates(map.Grid, i * 4f, 0f));
                entMan.GetComponent<BunkerHatchSpawnPointComponent>(point).Channel = channel;
            }

            spawner.SpawnRoundHatches();
        });

        await server.WaitAssertion(() =>
        {
            var labels = new List<string>();
            var query = entMan.EntityQueryEnumerator<BunkerTeleporterComponent>();
            while (query.MoveNext(out _, out var teleporter))
            {
                if (teleporter.IsSurface && teleporter.Channel == channel)
                    labels.Add(teleporter.Label ?? "");
            }

            Assert.That(labels, Has.Count.EqualTo(BunkerHatchSpawnSystem.HatchesPerRound));
            Assert.That(labels, Is.EquivalentTo(new[] { "Hatch A", "Hatch B" }));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CarriedPeopleComeAlongAcrossMaps()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var xform = server.System<SharedTransformSystem>();
        var carrying = server.System<CarryingSystem>();
        var surface = await pair.CreateTestMap();
        var below = await pair.CreateTestMap();
        const string channel = "test-carry";

        EntityUid hatch = default, door = default, carrier = default, carried = default;

        await server.WaitPost(() =>
        {
            RunMap(server, surface.MapId);
            RunMap(server, below.MapId);
            hatch = SpawnOn(entMan, HatchProto, channel, surface.GridCoords);
            door = SpawnOn(entMan, DoorProto, channel, below.GridCoords);
            carrier = SpawnWithJob(server, surface.GridCoords, "EnclaveNCO");
            carried = entMan.SpawnEntity(HumanProto, surface.GridCoords);
            // Carrying needs the carrier to be at least twice as heavy; make the test carrier bulky.
            var physics = server.System<Robust.Shared.Physics.Systems.SharedPhysicsSystem>();
            var fixtures = entMan.GetComponent<Robust.Shared.Physics.FixturesComponent>(carrier);
            foreach (var (id, fixture) in fixtures.Fixtures)
                physics.SetDensity(carrier, id, fixture, fixture.Density * 4f, manager: fixtures);
            Assert.That(carrying.TryCarry(carrier, carried), "test setup: could not pick the passenger up");
        });

        await server.WaitAssertion(() =>
        {
            Use(entMan, hatch, carrier);
            AssertSamePlace(xform, carrier, door, "carrier did not arrive at the door");
            AssertSamePlace(xform, carried, door, "the carried person was left behind");
            Assert.That(entMan.HasComponent<CarryingComponent>(carrier), "the carrier dropped the person on the way");
        });

        await pair.CleanReturnAsync();
    }

    private static EntityUid SpawnOn(IEntityManager entMan, string proto, string channel, EntityCoordinates coords)
    {
        var uid = entMan.SpawnEntity(proto, coords);
        entMan.GetComponent<BunkerTeleporterComponent>(uid).Channel = channel;
        return uid;
    }

    private static EntityUid SpawnExit(IEntityManager entMan, string channel, EntityCoordinates coords)
    {
        var uid = entMan.SpawnEntity(ExitProto, coords);
        entMan.GetComponent<BunkerTunnelExitComponent>(uid).Channel = channel;
        return uid;
    }

    /// <summary>
    /// A human with a mind, and a job if one is given. No job means an outsider.
    /// </summary>
    private static EntityUid SpawnWithJob(Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server,
        EntityCoordinates coords, string? job)
    {
        var entMan = server.ResolveDependency<IEntityManager>();
        var minds = server.System<SharedMindSystem>();
        var uid = entMan.SpawnEntity(HumanProto, coords);
        var mind = minds.CreateMind(null);
        minds.TransferTo(mind, uid);
        if (job != null)
            server.System<JobSystem>().MindAddJob(mind, job);

        return uid;
    }

    private static void Use(IEntityManager entMan, EntityUid target, EntityUid user)
    {
        entMan.EventBus.RaiseLocalEvent(target, new InteractHandEvent(user, target));
    }

    /// <summary>
    /// A warp is refused when the destination map is not running, so make sure the test map is.
    /// </summary>
    private static void RunMap(Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server, MapId mapId)
    {
        server.System<SharedMapSystem>().SetPaused(mapId, false);
    }

    /// <summary>
    /// Compares world positions rather than EntityCoordinates: warping re-parents the entity, so the
    /// same spot can be expressed relative to the grid or to the map depending on what is underfoot.
    /// </summary>
    private static bool SamePlace(SharedTransformSystem xform, EntityUid moved, EntityUid destination)
    {
        var actual = xform.GetMapCoordinates(moved);
        var expected = xform.GetMapCoordinates(destination);
        return actual.MapId == expected.MapId && Vector2.Distance(actual.Position, expected.Position) < 0.01f;
    }

    private static void AssertSamePlace(SharedTransformSystem xform, EntityUid moved, EntityUid destination, string message)
    {
        Assert.That(SamePlace(xform, moved, destination), message);
    }
}
