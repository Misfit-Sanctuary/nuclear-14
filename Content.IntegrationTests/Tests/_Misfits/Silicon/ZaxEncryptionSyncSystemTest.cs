using Content.Server._Misfits.Silicon;
using Content.Server.Radio.Components;
using Content.Shared.Radio.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Misfits.Silicon;

[TestFixture]
[TestOf(typeof(ZaxEncryptionSyncSystem))]
public sealed class ZaxEncryptionSyncSystemTest
{
    [Test]
    public async Task InstalledKeyChannelsPropagateAcrossZaxNetwork()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var containers = entities.System<SharedContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var core = entities.SpawnEntity("PlayerZaxAiEmpty", map.GridCoords);
            var firstUnit = entities.SpawnEntity("N14MobZaxPlayerMrHandy", map.GridCoords);
            var secondUnit = entities.SpawnEntity("N14MobZaxPlayerMrGutsy", map.GridCoords);

            var coreKeys = entities.GetComponent<EncryptionKeyHolderComponent>(core);
            var firstKeys = entities.GetComponent<EncryptionKeyHolderComponent>(firstUnit);
            var secondKeys = entities.GetComponent<EncryptionKeyHolderComponent>(secondUnit);

            Assert.That(coreKeys.Channels, Is.EquivalentTo(new[] { "ZAXBinary", "VaultCommon", "WastelandGlobal" }));
            Assert.That(entities.GetComponent<IntrinsicRadioTransmitterComponent>(core).Channels, Is.EquivalentTo(coreKeys.Channels));
            Assert.That(entities.GetComponent<ActiveRadioComponent>(core).Channels, Is.EquivalentTo(coreKeys.Channels));
            Assert.That(firstKeys.Channels, Is.EquivalentTo(coreKeys.Channels));
            Assert.That(secondKeys.Channels, Is.EquivalentTo(coreKeys.Channels));

            var ncrKey = entities.SpawnEntity("EncryptionKeyNCR", map.GridCoords);
            Assert.That(containers.Insert(ncrKey, firstKeys.KeyContainer), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(coreKeys.Channels, Does.Contain("NCR"));
                Assert.That(firstKeys.Channels, Does.Contain("NCR"));
                Assert.That(secondKeys.Channels, Does.Contain("NCR"));
                Assert.That(entities.GetComponent<ActiveRadioComponent>(core).Channels, Does.Contain("NCR"));
                Assert.That(entities.GetComponent<ActiveRadioComponent>(secondUnit).Channels, Does.Contain("NCR"));
            });
        });

        await pair.CleanReturnAsync();
    }
}
