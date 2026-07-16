using Content.Server._Misfits.Silicon;
using Content.Shared._Misfits.Silicon;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Misfits.Silicon;

[TestFixture]
[TestOf(typeof(ZaxPopulationSystem))]
public sealed class ZaxPopulationSystemTest
{
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
}
