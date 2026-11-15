// #Misfits Add - the picker's state has to reach the client, and taking it has to give a body.
using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._Misfits.Events;
using Content.Server._Misfits.Ghost.Roles;
using Content.Server.GameTicking;
using Content.Server.EUI;
using Content.Shared._Misfits.Ghost.Roles;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.IntegrationTests.Tests._Misfits;

[TestFixture]
[TestOf(typeof(MisfitsGhostRoleCharacterPickerEui))]
public sealed class MisfitsEventTests
{
    [Test]
    public async Task StateReachesTheClientWithOptions()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            Dirty = true,
        });

        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var players = server.ResolveDependency<IPlayerManager>();
        var euiManager = server.ResolveDependency<EuiManager>();
        var serializer = server.ResolveDependency<IRobustSerializer>();

        var testMap = await pair.CreateTestMap();
        MisfitsGhostRoleCharacterEuiState state = default!;
        MisfitsGhostRoleCharacterPickerEui serverPicker = default!;

        await server.WaitPost(() =>
        {
            var spawner = entMan.SpawnEntity("MisfitsSpawnPointGhostGhoul", testMap.GridCoords);
            var pickerSystem = entMan.System<MisfitsGhostRoleCharacterPickerSystem>();

            // Opening the EUI sets the session the state is built for, as the ghost role menu does.
            serverPicker = new MisfitsGhostRoleCharacterPickerEui(pickerSystem, spawner, "sentient ghoul");
            euiManager.OpenEui(serverPicker, players.Sessions.Single());

            state = (MisfitsGhostRoleCharacterEuiState) serverPicker.GetNewState();
        });

        Assert.That(state.Options, Is.Not.Null, "the picker built no option list");
        Assert.That(state.Options.Length, Is.GreaterThan(0), "the picker offered nobody");
        Assert.That(state.Options.Any(option => option.Index == -1), Is.True,
            "the randomly generated character option is missing");

        MisfitsGhostRoleCharacterEuiState received = default!;

        await server.WaitPost(() =>
        {
            using var stream = new MemoryStream();
            serializer.Serialize(stream, state);
            stream.Position = 0;

            received = (MisfitsGhostRoleCharacterEuiState) serializer.Deserialize(stream);
        });

        Assert.Multiple(() =>
        {
            Assert.That(received.RoleName, Is.EqualTo(state.RoleName), "the role name was lost");
            Assert.That(received.Options, Is.Not.Null, "the option list was lost");
            Assert.That(received.Options.Length, Is.EqualTo(state.Options.Length), "options were lost");
            Assert.That(received.Options[0].Name, Is.EqualTo(state.Options[0].Name), "option names were lost");
            Assert.That(received.Options[0].Index, Is.EqualTo(state.Options[0].Index), "option indices were lost");
        });

        // The state also has to reach the client's picker, not just survive a local round trip.
        var client = pair.Client;
        var clientPicker = new ClientPickerHolder();

        await pair.RunTicksSync(5);

        await client.WaitPost(() =>
        {
            var euiManager = client.ResolveDependency<Content.Client.Eui.EuiManager>();
            var openUisField = typeof(Content.Client.Eui.EuiManager)
                .GetField("_openUis", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var openUis = (System.Collections.IDictionary) openUisField.GetValue(euiManager)!;

            foreach (var value in openUis.Values)
            {
                var eui = (Content.Client.Eui.BaseEui) value!.GetType()
                    .GetField("Eui", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!
                    .GetValue(value)!;

                if (eui is Content.Client._Misfits.Ghost.Roles.MisfitsGhostRoleCharacterPickerEui picker)
                    clientPicker.Picker = picker;
            }
        });

        Assert.That(clientPicker.Picker, Is.Not.Null, "the client never opened the character picker");
        Assert.That(clientPicker.Picker!.OptionCount, Is.GreaterThan(0),
            "the client's dropdown had no characters in it");

        // OptionButton only raises OnItemSelected, so the picker must listen or clicks do nothing.
        var hasSelectionHandler = false;

        await client.WaitPost(() =>
        {
            var optionsField = typeof(Content.Client._Misfits.Ghost.Roles.MisfitsGhostRoleCharacterPickerEui)
                .GetField("_options", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var optionButton = (Robust.Client.UserInterface.Controls.OptionButton) optionsField
                .GetValue(clientPicker.Picker)!;

            hasSelectionHandler = typeof(Robust.Client.UserInterface.Controls.OptionButton)
                .GetField("OnItemSelected", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(optionButton) is Delegate;
        });

        Assert.That(hasSelectionHandler, Is.True,
            "the dropdown ignores clicks, so no entry can be selected");

        // Taking the first (randomly generated) entry has to give the player a body.
        await server.WaitPost(() =>
        {
            var ghost = entMan.SpawnEntity("AdminObserver", testMap.GridCoords);
            players.SetAttachedEntity(players.Sessions.Single(), ghost);
        });

        await server.WaitPost(() =>
        {
            // What the client's Take button sends once an entry is selected.
            serverPicker.HandleMessage(new MisfitsGhostRoleCharacterChosenMessage(-1));
        });

        await pair.RunTicksSync(5);

        var tookRole = false;
        await server.WaitPost(() =>
        {
            var attached = players.Sessions.Single().AttachedEntity;
            tookRole = attached is { Valid: true } && !entMan.HasComponent<Content.Shared.Ghost.GhostComponent>(attached.Value);
        });

        Assert.That(tookRole, Is.True, "taking the role did not give the player a body");

        // The gear in the spawner's YAML has to reach the body the role hands out.
        await server.WaitPost(() =>
        {
            var body = players.Sessions.Single().AttachedEntity!.Value;
            var inventory = entMan.System<Content.Shared.Inventory.InventorySystem>();

            Assert.That(inventory.TryGetSlotEntity(body, "jumpsuit", out var worn), Is.True,
                "the ghost role body spawned without its starting gear");
            Assert.That(entMan.GetComponent<MetaDataComponent>(worn!.Value).EntityPrototype?.ID,
                Is.EqualTo("N14ClothingUniformJumpsuitRags"), "the body wore the wrong gear");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DisabledEventsLeaveTheRollPool()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitPost(() =>
        {
            var events = entMan.System<MisfitsEventSystem>();
            var horde = prototypes.Index<EntityPrototype>("MisfitsGhoulHorde");

            Assert.That(events.AvailableEvents(2, TimeSpan.FromMinutes(30)).ContainsKey(horde), Is.True,
                "the ghoul horde was not rollable before being switched off");

            events.SetEventDisabled(horde.ID, true);
            Assert.That(events.IsEventDisabled(horde.ID), Is.True);
            Assert.That(events.AvailableEvents(2, TimeSpan.FromMinutes(30)).ContainsKey(horde), Is.False,
                "a disabled event stayed in the roll pool");

            events.SetEventDisabled(horde.ID, false);
            Assert.That(events.AvailableEvents(2, TimeSpan.FromMinutes(30)).ContainsKey(horde), Is.True,
                "re-enabling the event did not put it back in the roll pool");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EventsSpawnAtAMappedAnchor()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await pair.CreateTestMap();

        EntityUid rule = default;
        Vector2 anchorPosition = default;

        await server.WaitPost(() =>
        {
            var anchor = entMan.SpawnEntity("MisfitsEventAnchor", testMap.GridCoords);
            anchorPosition = entMan.GetComponent<TransformComponent>(anchor).WorldPosition;

            rule = entMan.System<MisfitsEventSystem>().RunEvent("MisfitsGhoulHorde")!.Value;
        });

        await server.WaitPost(() =>
        {
            var component = entMan.GetComponent<MisfitsEventComponent>(rule);

            Assert.Multiple(() =>
            {
                Assert.That(component.LastSpawnLocation, Is.EqualTo("event anchor").IgnoreCase);
                Assert.That(component.SpawnedAt, Is.Not.Null, "the event did not spawn");
                Assert.That((component.SpawnedAt!.Value.Position - anchorPosition).Length(), Is.LessThan(25f),
                    "the event spawned away from the mapped anchor");
                Assert.That(component.LastSpawnedCount, Is.GreaterThan(0));
            });
        });

        await pair.CleanReturnAsync();
    }

    private sealed class ClientPickerHolder
    {
        public Content.Client._Misfits.Ghost.Roles.MisfitsGhostRoleCharacterPickerEui? Picker;
    }
}
