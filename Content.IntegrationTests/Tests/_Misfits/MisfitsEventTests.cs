// #Misfits Add - the picker's state has to reach the client, and taking it has to give a body.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._Misfits.Events;
using Content.Server._Misfits.Ghoul;
using Content.Server._Misfits.Ghost.Roles;
using Content.Server.GameTicking;
using Content.Server.EUI;
using Content.Server.Ghost.Roles;
using Content.Server.Kitchen.Components;
using Content.Server.NPC.Components;
using Content.Server.Language;
using Content.Server.Paper;
using Content.Server.Speech.Components;
using Content.Server.Station.Systems;
using Content.Shared._Misfits.Events;
using Content.Shared._Misfits.Ghost.Roles;
using Content.Shared.Actions;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Dataset;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Ghost;
using Content.Shared.Language;
using Content.Shared.Inventory;
using Content.Shared.Mind.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Roles;
using Content.Shared.Storage;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
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

    [Test]
    public async Task LawEventBriefsBothRolesOnTheSameCrime()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var localisation = server.ResolveDependency<ILocalizationManager>();
        var testMap = await pair.CreateTestMap();

        EntityUid rule = default;

        await server.WaitPost(() =>
        {
            var law = prototypes.Index<EntityPrototype>("MisfitsLaw");
            Assert.That(law.TryGetComponent<MisfitsEventComponent>(out var events), Is.True);
            Assert.That(events!.Briefing, Is.EqualTo(new ProtoId<LocalizedDatasetPrototype>("MisfitsNcrCrimes")),
                "the law event does not roll a crime");

            var crimes = prototypes.Index<LocalizedDatasetPrototype>("MisfitsNcrCrimes");
            Assert.That(crimes.Values, Has.Count.EqualTo(12), "the NCR crime dataset lost entries");
            foreach (var crime in crimes.Values)
                Assert.That(localisation.GetString(crime), Is.Not.Empty, $"{crime} has no text");

            entMan.SpawnEntity("MisfitsEventAnchor", testMap.GridCoords);
            rule = entMan.System<MisfitsEventSystem>().RunEvent("MisfitsLaw")!.Value;
        });

        string? briefing = null;

        await server.WaitPost(() =>
        {
            briefing = entMan.GetComponent<MisfitsEventComponent>(rule).BriefingText;
        });

        Assert.That(briefing, Is.Not.Null.And.Not.Empty, "the event rolled no crime for its roles");

        await server.WaitPost(() =>
        {
            // Every spawner the event places, the criminal now and the ranger later, carries the one rolled crime.
            var pickers = 0;
            var query = entMan.EntityQueryEnumerator<MisfitsGhostRoleCharacterPickerComponent>();
            while (query.MoveNext(out _, out var picker))
            {
                Assert.That(picker.Briefing, Is.EqualTo(briefing), "a role spawner was not told the crime");
                pickers++;
            }

            Assert.That(pickers, Is.GreaterThan(0), "the law event placed no role spawner");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EventRolesOnlyOfferPeopleSpecies()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        string[] roles =
        [
            "MisfitsSpawnPointGhostRaider",
            "MisfitsSpawnPointGhostLegionSlave",
            "MisfitsSpawnPointGhostLegionHunter",
            "MisfitsSpawnPointGhostCriminal",
            "MisfitsSpawnPointGhostRanger",
            "MisfitsSpawnPointGhostTrader",
            "MisfitsSpawnPointGhostCaravanGuard",
        ];

        await server.WaitPost(() =>
        {
            foreach (var id in roles)
            {
                var proto = prototypes.Index<EntityPrototype>(id);

                Assert.That(proto.TryGetComponent<MisfitsGhostRoleCharacterPickerComponent>(out var picker), Is.True,
                    $"{id} has no character picker");
                Assert.That(picker!.AllowedSpecies, Is.Not.Empty,
                    $"{id} would randomise any race, robots included");

                foreach (var species in picker.AllowedSpecies)
                {
                    Assert.That(species.Id.StartsWith("Robot") || species.Id is "IPC" or "C27", Is.False,
                        $"{id} can be taken as {species.Id}");
                }
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EventRoleGearEquipsWhatItLists()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var testMap = await pair.CreateTestMap();

        string[] gearIds =
        [
            "MisfitsRaiderGear",
            "MisfitsLegionSlaveGear",
            "MisfitsLegionHunterGear",
            "MisfitsCriminalGear",
            "MisfitsRangerGear",
            "MisfitsTraderGear",
            "MisfitsCaravanGuardGear",
        ];

        await server.WaitPost(() =>
        {
            var spawning = entMan.System<StationSpawningSystem>();
            var inventory = entMan.System<InventorySystem>();

            foreach (var id in gearIds)
            {
                var gear = prototypes.Index<StartingGearPrototype>(id);
                var mob = entMan.SpawnEntity("MobHuman", testMap.GridCoords);
                spawning.EquipStartingGear(mob, gear, raiseEvent: false);

                // A misspelled slot name is silently skipped, so make sure every listed slot actually got filled.
                foreach (var slot in gear.Equipment.Keys)
                {
                    Assert.That(inventory.TryGetSlotEntity(mob, slot, out _), Is.True,
                        $"{id} lists slot '{slot}', but nothing was equipped there");
                }

                entMan.DeleteEntity(mob);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryTeamInAnEventSharesOneFrequency()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            entMan.SpawnEntity("MisfitsEventAnchorTown", testMap.GridCoords);
            Assert.That(entMan.System<MisfitsEventSystem>().RunEvent("MisfitsRaiders"), Is.Not.Null);

            var raiders = FrequenciesOf(entMan, "MisfitsSpawnPointGhostRaider");
            Assert.That(raiders, Is.Not.Empty, "the raiders never spawned");
            Assert.That(raiders.Distinct().Count(), Is.EqualTo(1),
                "the raiders were handed different frequencies");
            Assert.That(raiders[0], Is.InRange(1000, 3000),
                "the rolled frequency is outside what a handheld radio tunes");

            Assert.That(entMan.System<MisfitsEventSystem>().RunEvent("MisfitsCaravan"), Is.Not.Null);

            var caravan = FrequenciesOf(entMan, "MisfitsSpawnPointGhostTrader");
            caravan.AddRange(FrequenciesOf(entMan, "MisfitsSpawnPointGhostCaravanGuard"));

            Assert.That(caravan, Has.Count.EqualTo(3), "the caravan did not spawn a trader and two guards");
            Assert.That(caravan.Distinct().Count(), Is.EqualTo(1),
                "the caravan crew was handed different frequencies");
        });

        await pair.CleanReturnAsync();
    }

    // Frequencies of every spawned picker with this prototype.
    private static List<int> FrequenciesOf(IEntityManager entMan, string protoId)
    {
        var frequencies = new List<int>();

        var query = entMan.EntityQueryEnumerator<MisfitsGhostRoleCharacterPickerComponent>();
        while (query.MoveNext(out var uid, out var picker))
        {
            if (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == protoId)
                frequencies.Add(picker.Frequency);
        }

        return frequencies;
    }

    [Test]
    public async Task TakenRoleGetsItsTeamFrequencyOnANote()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var players = server.ResolveDependency<IPlayerManager>();
        var testMap = await pair.CreateTestMap();

        var frequency = 0;
        EntityUid spawner = default;

        await server.WaitPost(() =>
        {
            entMan.SpawnEntity("MisfitsEventAnchorTown", testMap.GridCoords);
            entMan.System<MisfitsEventSystem>().RunEvent("MisfitsRaiders");

            var query = entMan.EntityQueryEnumerator<MisfitsGhostRoleCharacterPickerComponent>();
            while (query.MoveNext(out var uid, out var picker))
            {
                if (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID != "MisfitsSpawnPointGhostRaider")
                    continue;

                spawner = uid;
                frequency = picker.Frequency;
                break;
            }
        });

        Assert.That(frequency, Is.GreaterThan(0), "the raider spawner had no team frequency");

        await server.WaitPost(() =>
        {
            var session = players.Sessions.Single();
            players.SetAttachedEntity(session, entMan.SpawnEntity("AdminObserver", testMap.GridCoords));

            Assert.That(entMan.System<MisfitsGhostRoleCharacterPickerSystem>().TrySpawn(session, spawner, -1),
                Is.True, "the role could not be taken");
        });

        await server.WaitPost(() =>
        {
            var body = players.Sessions.Single().AttachedEntity!.Value;
            var inventory = entMan.System<InventorySystem>();

            Assert.That(inventory.TryGetSlotEntity(body, "back", out var back), Is.True,
                "the raider spawned without a backpack to carry the note");

            var carried = entMan.GetComponent<StorageComponent>(back!.Value);

            var note = carried.Container.ContainedEntities.FirstOrDefault(uid =>
                entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "Paper");
            Assert.That(note, Is.Not.EqualTo(default(EntityUid)), "the role spawned without its frequency note");
            Assert.That(entMan.GetComponent<PaperComponent>(note).PaperContent, Does.Contain(frequency.ToString()),
                "the note does not carry the team frequency");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LobbySessionsCanOpenTheGhostRoleList()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.ResolveDependency<IEntityManager>();
        var players = server.ResolveDependency<IPlayerManager>();

        await server.WaitPost(() =>
        {
            var session = players.Sessions.Single();

            // A player still sitting in the lobby: connected, but with no body and no ghost.
            players.SetAttachedEntity(session, null);
            entMan.System<GhostRoleSystem>().OpenEui(session);
        });

        await pair.RunTicksSync(10);

        var opened = false;
        await client.WaitPost(() =>
        {
            var euiManager = client.ResolveDependency<Content.Client.Eui.EuiManager>();
            var openUisField = typeof(Content.Client.Eui.EuiManager)
                .GetField("_openUis", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var openUis = (System.Collections.IDictionary) openUisField.GetValue(euiManager)!;

            foreach (var value in openUis.Values)
            {
                var eui = value!.GetType()
                    .GetField("Eui", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!
                    .GetValue(value);

                if (eui is Content.Client.UserInterface.Systems.Ghost.Controls.Roles.GhostRolesEui)
                    opened = true;
            }
        });

        Assert.That(opened, Is.True, "a lobby player did not get the ghost role list");
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ALobbyPlayerCanTakeAnEventRole()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var players = server.ResolveDependency<IPlayerManager>();
        var testMap = await pair.CreateTestMap();

        EntityUid spawner = default;

        await server.WaitPost(() =>
        {
            entMan.SpawnEntity("MisfitsEventAnchorTown", testMap.GridCoords);
            entMan.System<MisfitsEventSystem>().RunEvent("MisfitsRaiders");

            var query = entMan.EntityQueryEnumerator<MisfitsGhostRoleCharacterPickerComponent>();
            while (query.MoveNext(out var uid, out _))
            {
                if (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID != "MisfitsSpawnPointGhostRaider")
                    continue;

                spawner = uid;
                break;
            }
        });

        await server.WaitPost(() =>
        {
            var session = players.Sessions.Single();

            // A player sitting in the lobby: connected, not a ghost, no body at all.
            players.SetAttachedEntity(session, null);

            Assert.That(entMan.System<MisfitsGhostRoleCharacterPickerSystem>().TrySpawn(session, spawner, -1),
                Is.True, "a lobby player could not take the event role");

            var body = session.AttachedEntity;
            Assert.That(body, Is.Not.Null, "the lobby player was left without a body");
            Assert.That(entMan.HasComponent<GhostComponent>(body!.Value), Is.False,
                "the lobby player got a ghost instead of the role's body");
            Assert.That(entMan.HasComponent<MindContainerComponent>(body.Value), Is.True,
                "the lobby player's new body has no mind");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AnEventCanPlaceAPreMappedStructure()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await pair.CreateTestMap();

        var before = 0;
        var after = 0;
        var placed = 0;

        await server.WaitPost(() =>
        {
            before = CountGridsOnMap(entMan, testMap.MapId);
            placed = entMan.System<MisfitsEventSystem>().PlaceStructures(
                new List<MisfitsEventStructure> { new() { Path = new ResPath("/Maps/Test/empty.yml") } },
                testMap.MapCoords);
            after = CountGridsOnMap(entMan, testMap.MapId);
        });

        Assert.Multiple(() =>
        {
            Assert.That(placed, Is.EqualTo(1), "the structure was not placed");
            Assert.That(after, Is.GreaterThan(before), "the structure's grid never joined the map");
        });

        await pair.CleanReturnAsync();
    }

    // Grids sitting on one map.
    private static int CountGridsOnMap(IEntityManager entMan, MapId mapId)
    {
        var count = 0;

        var query = entMan.EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapID == mapId)
                count++;
        }

        return count;
    }

    [Test]
    public async Task DragonEventSpawnsAPackAndAnAlpha()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            // A generic anchor accepts the wilderness-only dragon event.
            entMan.SpawnEntity("MisfitsEventAnchor", testMap.GridCoords);

            Assert.That(entMan.System<MisfitsEventSystem>().RunEvent("MisfitsDragon"), Is.Not.Null);
        });

        await server.WaitPost(() =>
        {
            Assert.Multiple(() =>
            {
                // The alpha shapes the nest itself, so it must not arrive with the event.
                Assert.That(CountPrototype(entMan, "MisfitsGeckoNest"), Is.Zero, "the event still spawns a nest");
                Assert.That(CountPrototype(entMan, "MisfitsMobGeckoFireAlpha"), Is.EqualTo(1),
                    "the alpha gecko did not arrive - unique entries collided");
                Assert.That(CountPrototype(entMan, "MisfitsSpawnPointGeckoFire"), Is.EqualTo(4),
                    "the four sentient pack geckos did not arrive");
                Assert.That(CountPrototype(entMan, "MisfitsMobGeckoFirePack"), Is.Zero,
                    "the event still spawns plain geckos instead of ghost role geckos");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CaravanTakesATownAnchor()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            // The caravan only accepts town anchors.
            entMan.SpawnEntity("MisfitsEventAnchorTown", testMap.GridCoords);

            Assert.That(entMan.System<MisfitsEventSystem>().RunEvent("MisfitsCaravan"), Is.Not.Null);
        });

        await server.WaitPost(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(CountPrototype(entMan, "MisfitsTrademachineCaravan"), Is.EqualTo(1),
                    "the caravan wagon did not arrive");
                Assert.That(CountPrototype(entMan, "MisfitsSpawnPointGhostTrader"), Is.EqualTo(1));
                Assert.That(CountPrototype(entMan, "MisfitsSpawnPointGhostCaravanGuard"), Is.EqualTo(2));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CaravanWagonOnlyRollsWithItsKey()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await pair.CreateTestMap();

        await server.WaitPost(() =>
        {
            var slots = entMan.System<ItemSlotsSystem>();
            var wagon = entMan.SpawnEntity("MisfitsTrademachineCaravan", testMap.GridCoords);
            var key = entMan.SpawnEntity("MisfitsCaravanKey", testMap.GridCoords);
            var xform = entMan.GetComponent<TransformComponent>(wagon);
            var physics = entMan.GetComponent<PhysicsComponent>(wagon);

            Assert.Multiple(() =>
            {
                Assert.That(xform.Anchored, Is.True, "the wagon did not arrive anchored");
                Assert.That(physics.BodyType, Is.EqualTo(BodyType.Static), "an anchored wagon should be static");
            });

            Assert.That(slots.TryInsert(wagon, "key_slot", key, null), Is.True, "the key would not go into the slot");

            Assert.Multiple(() =>
            {
                Assert.That(xform.Anchored, Is.False, "the wagon stays anchored with its key in the lock");
                Assert.That(physics.BodyType, Is.EqualTo(BodyType.Dynamic),
                    "a static wagon cannot be dragged, key or no key");
            });

            Assert.That(slots.TryEject(wagon, "key_slot", null, out _), Is.True, "the key could not be taken back out");

            Assert.Multiple(() =>
            {
                Assert.That(xform.Anchored, Is.True, "the wagon should anchor again once its key is out");
                Assert.That(physics.BodyType, Is.EqualTo(BodyType.Static));
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GeckosHaveHandsAndTheirNestGrowsRoles()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var testMap = await pair.CreateTestMap();

        EntityUid pack = default;
        EntityUid alpha = default;
        EntityUid nest = default;

        await server.WaitPost(() =>
        {
            pack = entMan.SpawnEntity("MisfitsMobGeckoFirePack", testMap.GridCoords);
            alpha = entMan.SpawnEntity("MisfitsMobGeckoFireAlpha", testMap.GridCoords);
            nest = entMan.SpawnEntity("MisfitsGeckoNest", testMap.GridCoords);
        });

        // Hands and the granted action arrive when the mobs finish spawning.
        await server.WaitRunTicks(1);

        await server.WaitPost(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<HandsComponent>(pack).Hands, Has.Count.EqualTo(2),
                    "a pack gecko should have two hands");
                Assert.That(entMan.GetComponent<HandsComponent>(alpha).Hands, Has.Count.EqualTo(2),
                    "the alpha should have the pack gecko's hands");
                Assert.That(entMan.HasComponent<MisfitsNamePromptComponent>(alpha), Is.True,
                    "the alpha should ask its player for a name");
                Assert.That(entMan.HasComponent<ActionGrantComponent>(alpha), Is.True,
                    "the alpha should be granted the make nest action");
                Assert.That(entMan.HasComponent<SharpComponent>(pack), Is.True,
                    "a gecko's teeth should let it butcher without a knife");
                Assert.That(entMan.GetComponent<ReplacementAccentComponent>(pack).ReplacementChance, Is.Zero,
                    "a gecko's words should not be replaced with growls");

                var languages = entMan.GetComponent<LanguageKnowledgeComponent>(pack).SpokenLanguages;
                Assert.Multiple(() =>
                {
                    Assert.That(languages, Does.Contain(new ProtoId<LanguagePrototype>("English")),
                        "a gecko should speak English");
                    Assert.That(languages, Does.Contain(new ProtoId<LanguagePrototype>("N14Gecko")),
                        "a gecko should speak its own tongue");
                });
            });

            var inventory = entMan.System<InventorySystem>();
            foreach (var slot in new[] { "pocket1", "pocket2", "belt", "back", "head" })
            {
                Assert.That(inventory.TryGetSlot(alpha, slot, out _), Is.True,
                    $"the sentient gecko has no {slot} slot");
            }

            var hat = entMan.SpawnEntity("N14ClothingHeadHatBeanie", testMap.GridCoords);
            Assert.That(inventory.CanEquip(alpha, hat, "head", out var reason), Is.True,
                $"a hat will not go on the gecko's head slot: {reason}");
            entMan.DeleteEntity(hat);

            Feed("N14FoodMeatGeckoFire");
            Assert.That(CountPrototype(entMan, "MisfitsSpawnPointGeckoFire"), Is.Zero,
                "one offering should not raise a gecko yet");

            Feed("N14FoodMeatGeckoFire");
            Assert.That(CountPrototype(entMan, "MisfitsSpawnPointGeckoFire"), Is.EqualTo(1),
                "two offerings should raise one gecko role");
        });

        await server.WaitPost(() =>
        {
            var nests = CountPrototype(entMan, "MisfitsGeckoNest");

            // Actions are broadcast, which is how the componentless handler hears them.
            entMan.EventBus.RaiseLocalEvent(alpha, new MisfitsMakeNestActionEvent { Performer = alpha }, true);

            Assert.That(CountPrototype(entMan, "MisfitsGeckoNest"), Is.EqualTo(nests + 1),
                "the make nest action did not leave a nest behind");
        });

        await pair.CleanReturnAsync();

        void Feed(string food)
        {
            var coords = entMan.GetComponent<TransformComponent>(nest).Coordinates;
            var meal = entMan.SpawnEntity(food, coords);
            entMan.EventBus.RaiseLocalEvent(nest, new InteractUsingEvent(pack, meal, nest, coords));
        }
    }

    [Test]
    public async Task SentientGhoulsJoinTheHordeAndRallyIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var testMap = await pair.CreateTestMap();

        EntityUid ghoul = default;
        EntityUid prey = default;
        EntityUid caller = default;

        await server.WaitPost(() =>
        {
            var spawner = entMan.SpawnEntity("MisfitsSpawnPointGhostGhoul", testMap.GridCoords);
            var picker = entMan.GetComponent<MisfitsGhostRoleCharacterPickerComponent>(spawner);

            Assert.That(prototypes.HasIndex<EntityPrototype>("ActionGhoulRally"), Is.True,
                "the sentient ghouls have no rally action to be given");

            // The picker hands its components to every body it spawns, so a stand-in gets what a ghoul would.
            caller = entMan.SpawnEntity("MisfitsEventAnchor", testMap.GridCoords);
            entMan.AddComponents(caller, picker.AddComponents);

            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<MisfitsGhoulRallyComponent>(caller), Is.True,
                    "a sentient ghoul should be given the rally ability");
                Assert.That(entMan.System<NpcFactionSystem>().ContainsFaction(caller, "Feral"), Is.True,
                    "the horde would attack a sentient ghoul that is not in its faction");
            });

            // A horde ghoul, something it is hostile to, then the shout.
            ghoul = entMan.SpawnEntity("N14MobGhoulFeral", testMap.GridCoords);
            prey = entMan.SpawnEntity("N14MobGecko", testMap.GridCoords);
            entMan.EventBus.RaiseLocalEvent(caller, new MisfitsGhoulRallyActionEvent { Performer = caller }, true);
        });

        await server.WaitPost(() =>
        {
            Assert.That(entMan.TryGetComponent(ghoul, out RecruitedFollowerComponent? follower), Is.True,
                "the rallied ghoul did not join the caller");
            Assert.That(follower!.Commander, Is.EqualTo(caller),
                "the rallied ghoul follows someone other than the ghoul that called");
            Assert.That(entMan.TryGetComponent(ghoul, out FactionExceptionComponent? exception), Is.True,
                "the rallied ghoul was not sent at anything");
            Assert.That(exception!.Hostiles, Does.Contain(prey),
                "the rallied ghoul ignored the creature standing next to it");
        });

        await pair.CleanReturnAsync();
    }

    private static int CountPrototype(IEntityManager entMan, string id)
    {
        var count = 0;
        var query = entMan.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out _, out var meta))
        {
            if (meta.EntityPrototype?.ID == id)
                count++;
        }

        return count;
    }

    private sealed class ClientPickerHolder
    {
        public Content.Client._Misfits.Ghost.Roles.MisfitsGhostRoleCharacterPickerEui? Picker;
    }
}
