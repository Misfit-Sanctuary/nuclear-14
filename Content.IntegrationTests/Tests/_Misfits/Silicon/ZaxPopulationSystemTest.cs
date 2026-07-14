using Content.Server._Misfits.Silicon;
using Content.Server.Maps;
using Content.Server.Station.Systems;
using Content.Shared._Misfits.Silicon;
using Content.Shared.Lathe;
using Content.Shared.NPC.Systems;
using Content.Shared.Research.Prototypes;
using Content.Shared.Roles;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Misfits.Silicon;

[TestFixture]
[TestOf(typeof(ZaxPopulationSystem))]
public sealed class ZaxPopulationSystemTest
{
    [TestPrototypes]
    private const string CoreSlotPrototypes = @"
- type: gameMap
  id: ZaxCoreSlotTestStation
  minPlayers: 0
  mapName: ZaxCoreSlotTestStation
  mapPath: /Maps/Test/empty.yml
  stations:
    Station:
      mapNameTemplate: ZaxCoreSlotTestStation
      stationProto: StandardNanotrasenStation
      components:
      - type: StationJobs
        overflowJobs: []
        availableJobs:
          ZaxCore: [0, 0]
          C27ZAX: [0, 0]
";

    [Test]
    public async Task JobReadyCoreControlsSlotAndRegistersFactionLeader()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var stations = entities.System<StationSystem>();
        var stationJobs = entities.System<StationJobsSystem>();
        var containers = entities.System<SharedContainerSystem>();
        var factions = entities.System<NpcFactionSystem>();

        await server.WaitAssertion(() =>
        {
            var mapPrototype = prototypes.Index<GameMapPrototype>("ZaxCoreSlotTestStation");
            var coreJob = prototypes.Index<JobPrototype>("ZaxCore");
            Assert.Multiple(() =>
            {
                Assert.That(coreJob.JobEntity, Is.EqualTo("StationAiBrain"));
                Assert.That(coreJob.NameDataset, Is.Null, "Core must inherit the selected character name.");
                Assert.That(coreJob.SetPreference, Is.True);
                Assert.That(coreJob.Whitelisted, Is.True);
            });

            var station = stations.InitializeNewStation(
                mapPrototype.Stations["Station"], [map.Grid], "Z.A.X Core Slot Test");

            var core = entities.SpawnEntity("PlayerZaxAi", map.GridCoords);
            Assert.That(stationJobs.TryGetJobSlot(station, "ZaxCore", out var openSlots), Is.True);
            Assert.That(openSlots, Is.EqualTo(1));
            Assert.That(stationJobs.GetRoundStartJobs(station)["ZaxCore"], Is.EqualTo(1));

            // A fabricated-but-unclaimed C-27 slot consumes the global C-27 cap.
            Assert.That(stationJobs.TryAdjustJobSlot(station, "C27ZAX", 1, createSlot: true), Is.True);
            var foundry = entities.SpawnEntity("MisfitsZaxMachineFoundry", map.GridCoords);
            var c27Recipe = prototypes.Index<LatheRecipePrototype>("MisfitsZaxFoundryPlayerC27");
            var c27Attempt = new LatheQueueAttemptEvent(c27Recipe, null);
            entities.EventBus.RaiseLocalEvent(foundry, ref c27Attempt);
            Assert.That(c27Attempt.Cancelled, Is.True);

            var brain = entities.SpawnEntity("StationAiBrain", map.GridCoords);
            Assert.That(containers.TryGetContainer(core, StationAiCoreComponent.Container, out var mindSlot), Is.True);
            Assert.That(containers.Insert(brain, mindSlot!), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(stationJobs.TryGetJobSlot(station, "ZaxCore", out var occupiedSlots), Is.True);
                Assert.That(occupiedSlots, Is.EqualTo(0));
                Assert.That(factions.IsMember(core, "ZAX"), Is.True);
                Assert.That(factions.IsMember(brain, "ZAX"), Is.True);
                Assert.That(entities.HasComponent<ZaxFactionLeaderComponent>(brain), Is.True);
            });

            Assert.That(containers.Remove(brain, mindSlot!), Is.True);
            Assert.That(stationJobs.TryGetJobSlot(station, "ZaxCore", out var reopenedSlots), Is.True);
            Assert.That(reopenedSlots, Is.EqualTo(1));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NonC27CapCountsNpcGhostRolesButNotPlayerChassis()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var population = entityManager.System<ZaxPopulationSystem>();

        var baseline = 0;
        EntityUid playerChassis = default;
        EntityUid npcGhostRole = default;

        await server.WaitAssertion(() =>
        {
            baseline = population.GetActiveUnitCount();
            playerChassis = entityManager.SpawnEntity("N14MobZaxPlayerMrHandy", map.GridCoords);

            Assert.Multiple(() =>
            {
                Assert.That(entityManager.HasComponent<ZaxLinkedUnitComponent>(playerChassis), Is.True);
                Assert.That(entityManager.HasComponent<ZaxUnitComponent>(playerChassis), Is.False);
                Assert.That(population.GetActiveUnitCount(), Is.EqualTo(baseline));
            });

            npcGhostRole = entityManager.SpawnEntity("N14MobZaxMrHandy", map.GridCoords);

            Assert.Multiple(() =>
            {
                Assert.That(entityManager.HasComponent<ZaxLinkedUnitComponent>(npcGhostRole), Is.True);
                Assert.That(entityManager.HasComponent<ZaxUnitComponent>(npcGhostRole), Is.True);
                Assert.That(population.GetActiveUnitCount(), Is.EqualTo(baseline + 1));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PlayerChassisCapsGroupVariantsAndRejectExcessFoundrySlots()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entityManager = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var population = entityManager.System<ZaxPopulationSystem>();

        await server.WaitAssertion(() =>
        {
            var foundry = entityManager.SpawnEntity("MisfitsZaxMachineFoundry", map.GridCoords);
            var foundryComp = entityManager.GetComponent<ZaxMachineFoundryComponent>(foundry);

            Assert.Multiple(() =>
            {
                Assert.That(foundryComp.PlayerJobCaps["ZaxSecuritron"], Is.EqualTo(6));
                Assert.That(foundryComp.PlayerJobCaps["ZaxAssaultron"], Is.EqualTo(4));
                Assert.That(foundryComp.PlayerJobCaps["ZaxProtectron"], Is.EqualTo(5));
                Assert.That(foundryComp.PlayerJobCaps["ZaxMrGutsy"], Is.EqualTo(3));
                Assert.That(foundryComp.PlayerJobCaps["ZaxMrHandy"], Is.EqualTo(3));
            });

            var cases = new[]
            {
                ("N14MobZaxPlayerSecuritron", ZaxPlayerChassisKind.Securitron, "ZaxSecuritron", "MisfitsZaxFoundryPlayerSecuritron"),
                ("N14MobZaxPlayerAssaultronTesla", ZaxPlayerChassisKind.Assaultron, "ZaxAssaultron", "MisfitsZaxFoundryPlayerAssaultron"),
                ("N14MobZaxPlayerProtectronFire", ZaxPlayerChassisKind.Protectron, "ZaxProtectron", "MisfitsZaxFoundryPlayerProtectron"),
                ("N14MobZaxPlayerMrGutsy", ZaxPlayerChassisKind.MrGutsy, "ZaxMrGutsy", "MisfitsZaxFoundryPlayerMrGutsy"),
                ("N14MobZaxPlayerMrHandy", ZaxPlayerChassisKind.MrHandy, "ZaxMrHandy", "MisfitsZaxFoundryPlayerMrHandy"),
            };

            foreach (var (entityPrototype, kind, job, recipeId) in cases)
            {
                foundryComp.PlayerJobCaps[job] = 1;
                var chassis = entityManager.SpawnEntity(entityPrototype, map.GridCoords);
                Assert.That(entityManager.GetComponent<ZaxPlayerChassisComponent>(chassis).Kind, Is.EqualTo(kind));
                Assert.That(population.GetActivePlayerChassisCount(kind), Is.EqualTo(1));

                var recipe = prototypes.Index<LatheRecipePrototype>(recipeId);
                var attempt = new LatheQueueAttemptEvent(recipe, null);
                entityManager.EventBus.RaiseLocalEvent(foundry, ref attempt);
                Assert.That(attempt.Cancelled, Is.True, $"{job} should reject a Foundry slot at its cap.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
