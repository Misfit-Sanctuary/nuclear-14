using System.Linq;
using Content.Server.Body.Systems;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Misfits.Silicon;

[TestFixture]
[TestOf(typeof(BodyPartComponent))]
public sealed class C27LimbIntegrityTest
{
    [Test]
    public async Task EveryVariantHasIndestructibleButSurgicallyRemovableParts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var body = entities.System<BodySystem>();
        var damage = entities.System<DamageableSystem>();

        await server.WaitAssertion(() =>
        {
            var brute = new DamageSpecifier(prototypes.Index<DamageGroupPrototype>("Brute"), 3000);
            var heat = new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Heat"), 1000);
            var shock = new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Shock"), 1000);

            foreach (var prototype in new[] { "N14MobC27", "N14MobC27NCR", "N14MobC27BoS", "N14MobC27ZAX" })
            {
                var mob = entities.SpawnEntity(prototype, map.GridCoords);
                var parts = body.GetBodyChildren(mob).ToArray();
                Assert.That(parts, Has.Length.EqualTo(8), $"{prototype} should use the complete C-27 body.");

                foreach (var part in parts)
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(part.Component.CanSever, Is.False);
                        Assert.That(entities.GetComponent<DamageableComponent>(part.Id).DamageModifierSetId,
                            Is.EqualTo("MisfitsC27IndestructiblePart"));
                    });

                    damage.TryChangeDamage(part.Id, brute, canSever: true);
                    damage.TryChangeDamage(part.Id, heat, canSever: true);
                    damage.TryChangeDamage(part.Id, shock, canSever: true);

                    Assert.Multiple(() =>
                    {
                        Assert.That(entities.GetComponent<DamageableComponent>(part.Id).TotalDamage,
                            Is.EqualTo(FixedPoint2.Zero));
                        Assert.That(part.Component.Enabled, Is.True);
                        Assert.That(part.Component.Body, Is.EqualTo(mob));
                    });
                }

                // This is the event used by the completed surgery removal step. It
                // deliberately bypasses combat severing protection.
                var arm = parts.First(part => part.Component.PartType == BodyPartType.Arm);
                var removal = new AmputateAttemptEvent(arm.Id);
                entities.EventBus.RaiseLocalEvent(arm.Id, ref removal);
                Assert.That(arm.Component.Body, Is.Null);
            }
        });

        await pair.CleanReturnAsync();
    }
}
