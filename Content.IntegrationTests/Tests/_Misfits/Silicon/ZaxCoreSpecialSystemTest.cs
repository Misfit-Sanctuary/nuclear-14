using System.Linq;
using Content.Server._Misfits.Special;
using Content.Server._Misfits.WastelandMap;
using Content.Server._Misfits.SpecialStats.Components;
using Content.Shared._Misfits.Silicon;
using Content.Shared._Misfits.Special;
using Content.Shared._Misfits.Special.Components;
using Content.Shared._Misfits.WastelandMap;
using Content.Shared.Silicons.StationAi;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Misfits.Silicon;

[TestFixture]
[TestOf(typeof(ZaxSpecialSystem))]
public sealed class ZaxCoreSpecialSystemTest
{
    [Test]
    public async Task CoreMindUsesFixedSpecialAndSynchronizesNames()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var containers = entities.System<SharedContainerSystem>();
        var special = entities.System<SharedSpecialSystem>();
        var metadata = entities.System<MetaDataSystem>();
        var wastelandMap = entities.System<WastelandMapSystem>();

        await server.WaitAssertion(() =>
        {
            var core = entities.SpawnEntity("PlayerZaxAiEmpty", map.GridCoords);
            var brain = entities.SpawnEntity("StationAiBrain", map.GridCoords);

            Assert.That(containers.TryGetContainer(core, StationAiCoreComponent.Container, out var mindSlot), Is.True);
            Assert.That(containers.Insert(brain, mindSlot!), Is.True);

            var linkedUnit = entities.SpawnEntity("N14MobZaxPlayerMrHandy", map.GridCoords);
            var unitMap = entities.GetComponent<WastelandMapComponent>(linkedUnit);
            var brainMap = entities.GetComponent<WastelandMapComponent>(brain);
            var mapId = entities.GetComponent<TransformComponent>(linkedUnit).MapID;

            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<ZaxCoreSpecialComponent>(core), Is.True);
                Assert.That(entities.HasComponent<ZaxCoreSpecialComponent>(brain), Is.True);
                Assert.That(entities.HasComponent<ZaxTacticalMapComponent>(brain), Is.True);
                Assert.That(wastelandMap.GetEffectiveFeed(brainMap, brain), Is.EqualTo(WastelandMapTacticalFeedKind.ZAX));
                Assert.That(wastelandMap.GetEffectiveFeed(unitMap, linkedUnit), Is.EqualTo(WastelandMapTacticalFeedKind.ZAX));
                Assert.That(special.UsesSpecialStats(core), Is.False);
                Assert.That(special.UsesSpecialStats(brain), Is.False);
                Assert.That(special.GetEffective(brain, SpecialStat.Charisma), Is.EqualTo(10));
                Assert.That(special.GetEffective(brain, SpecialStat.Intelligence), Is.EqualTo(10));
                Assert.That(special.GetEffective(brain, SpecialStat.Strength), Is.EqualTo(5));
                Assert.That(special.GetCharismaChatFontSize(brain, 12), Is.EqualTo(14));
                Assert.That(entities.HasComponent<SpecialAppliedMedicalHudComponent>(brain), Is.True);
            });

            Assert.That(special.TryModifyTemporary(brain, SpecialStat.Intelligence, -9), Is.False);
            Assert.That(special.GetEffective(brain, SpecialStat.Intelligence), Is.EqualTo(10));

            var marker = new WastelandMapAnnotation(
                WastelandMapAnnotationType.Marker,
                0.5f,
                0.5f,
                0.5f,
                0.5f,
                "ZAX test marker",
                WastelandMapAnnotation.DefaultPackedColor,
                WastelandMapAnnotation.DefaultStrokeWidth,
                null);
            Assert.That(wastelandMap.TryAddAnnotation(
                brain,
                brainMap,
                mapId,
                marker,
                WastelandMapTacticalFeedKind.ZAX), Is.True);

            var state = wastelandMap.BuildState(unitMap, mapId, actor: linkedUnit);
            Assert.Multiple(() =>
            {
                Assert.That(state.TrackedBlips.Any(blip => blip.Label == entities.GetComponent<MetaDataComponent>(linkedUnit).EntityName), Is.True);
                Assert.That(state.SharedAnnotations.Any(annotation => annotation.Label == marker.Label), Is.True);
            });

            metadata.SetEntityName(brain, "ZAX-RENAMED");
            Assert.That(entities.GetComponent<MetaDataComponent>(core).EntityName, Is.EqualTo("ZAX-RENAMED"));

            metadata.SetEntityName(core, "ZAX-CORE-ONE");
            Assert.That(entities.GetComponent<MetaDataComponent>(brain).EntityName, Is.EqualTo("ZAX-CORE-ONE"));
        });

        await pair.CleanReturnAsync();
    }
}
