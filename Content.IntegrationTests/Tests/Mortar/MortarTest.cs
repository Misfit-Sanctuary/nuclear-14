using System.Linq;
using System.Numerics;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Server.Light.Components;
using Content.Server.Light.EntitySystems;
using Content.Server.Power.Components;
using Content.Shared._Misfits.Fire;
using Content.Shared._RMC14.Mortar;
using Content.Shared.Chemistry.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Emp;
using Content.Shared.Light.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Radiation.Components;
using Content.Shared.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Spawners;

namespace Content.IntegrationTests.Tests.Mortar;

[TestFixture]
public sealed class MortarTest
{
    /// Deploys a mortar and sends a target from the client interface, then checks the server stored it.

    [Test]
    public async Task SetTargetThroughBui()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var cEntMan = client.EntMan;
        var map = await pair.CreateTestMap();

        var serverSession = server.PlayerMan.GetSessionById(client.Session!.UserId);

        EntityUid mortar = default;
        await server.WaitPost(() =>
        {
            var player = entMan.SpawnEntity("MobHuman", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(serverSession, player);
            mortar = entMan.SpawnEntity("RMCMortarKit", map.GridCoords);
        });

        // Let the client learn about the mortar.
        await pair.RunTicksSync(10);

        // Deploying is normally a four second do-after started by using the kit in hand.
        await server.WaitPost(() =>
        {
            var player = serverSession.AttachedEntity!.Value;
            var use = new UseInHandEvent(player);
            entMan.EventBus.RaiseLocalEvent(mortar, use);
        });

        await pair.RunSeconds(6);

        var deployed = false;
        var deployedVisual = false;
        await server.WaitPost(() =>
        {
            deployed = entMan.GetComponent<MortarComponent>(mortar).Deployed;

            var appearance = server.System<SharedAppearanceSystem>();
            deployedVisual = appearance.TryGetData<MortarVisuals>(mortar, MortarVisualLayers.State, out var state) &&
                             state == MortarVisuals.Deployed;
        });

        Assert.That(deployed, Is.True, "The mortar kit did not deploy.");
        Assert.That(deployedVisual, Is.True, "The mortar sprite did not switch to its deployed state.");

        var registeredInterface = false;
        await server.WaitPost(() =>
            registeredInterface = server.System<SharedUserInterfaceSystem>()
                .TryGetInterfaceData(mortar, MortarUiKey.Key, out _));

        Assert.That(registeredInterface, Is.True, "The mortar prototype did not register a MortarUiKey interface.");

        // Open the interface the way the game does: the client asks and predicts it opening.
        await client.WaitPost(() =>
        {
            var clientMortar = pair.ToClientUid(mortar);
            client.System<SharedUserInterfaceSystem>().OpenUi(clientMortar, MortarUiKey.Key, predicted: true);
        });

        await pair.RunTicksSync(10);

        var target = new Vector2i(20, 20);
        var sent = false;

        await client.WaitPost(() =>
        {
            var clientMortar = pair.ToClientUid(mortar);
            if (!cEntMan.TryGetComponent<UserInterfaceComponent>(clientMortar, out var ui) ||
                !ui!.ClientOpenInterfaces.TryGetValue(MortarUiKey.Key, out var bui))
            {
                return;
            }

            bui.SendMessage(new MortarTargetBuiMsg(target));
            sent = true;
        });

        Assert.That(sent, Is.True, "The client never opened a mortar window.");

        await pair.RunSeconds(4);

        var stored = Vector2i.Zero;
        var hasTarget = false;
        await server.WaitPost(() =>
        {
            var mortarComp = entMan.GetComponent<MortarComponent>(mortar);
            stored = mortarComp.Target;
            hasTarget = mortarComp.HasTarget;
        });

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(hasTarget, Is.True, "The server never registered a target.");
                Assert.That(stored, Is.EqualTo(target), "The stored target does not match what the client sent.");
            });

            // A roof over the target must stop the aim. The test map is a single tile.
            var roofedTarget = Vector2i.Zero;
            await server.WaitPost(() =>
            {
                var roofMap = server.System<RoofSystem>();
                entMan.EnsureComponent<RoofComponent>(map.Grid.Owner);
                roofMap.SetRoof((map.Grid.Owner, map.Grid.Comp, null), roofedTarget, true);
            });

            var roofedSent = false;
            await client.WaitPost(() =>
            {
                var clientMortar = pair.ToClientUid(mortar);
                if (cEntMan.TryGetComponent<UserInterfaceComponent>(clientMortar, out var ui) &&
                    ui!.ClientOpenInterfaces.TryGetValue(MortarUiKey.Key, out var bui))
                {
                    bui.SendMessage(new MortarTargetBuiMsg(roofedTarget));
                    roofedSent = true;
                }
            });

            Assert.That(roofedSent, Is.True, "The mortar window closed before the roofed target could be sent.");

            await pair.RunSeconds(4);

            var roofedStored = Vector2i.Zero;
            var roofedHasTarget = false;
            await server.WaitPost(() =>
            {
                var mortarComp = entMan.GetComponent<MortarComponent>(mortar);
                roofedStored = mortarComp.Target;
                roofedHasTarget = mortarComp.HasTarget;
            });

            Assert.Multiple(() =>
            {
                Assert.That(roofedHasTarget, Is.True, "Refusing the roofed aim also cleared the previous target.");
                Assert.That(roofedStored, Is.EqualTo(target),
                    "The mortar accepted a target under a roof, so the shell would have come down inside.");
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    /// Fires the impact event and checks a utility shell leaves its payload behind.

    [Test]
    public async Task UtilityShellsLeaveTheirPayloadOnTheImpactTile()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings());
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        var mapSystem = server.System<SharedMapSystem>();
        var xform = server.System<SharedTransformSystem>();

        var impactPosition = Vector2.Zero;
        var roofedTile = Vector2i.Zero;

        // Map relative impact point, coming down on a roof, which must still take the payload.
        await server.WaitPost(() =>
        {
            var mapUid = mapSystem.GetMap(map.MapId);
            impactPosition = xform.ToMapCoordinates(map.GridCoords).Position;

            entMan.EnsureComponent<RoofComponent>(map.Grid.Owner);
            server.System<RoofSystem>().SetRoof((map.Grid.Owner, map.Grid.Comp, null), roofedTile, true);

            LandShell(entMan, "RMCMortarShellSmoke", new EntityCoordinates(mapUid, impactPosition));
        });

        await pair.RunTicksSync(3);

        var roofedSmoke = 0;

        await server.WaitPost(() =>
        {
            var smokeQuery = entMan.EntityQueryEnumerator<SmokeComponent>();
            while (smokeQuery.MoveNext(out _, out var smoke))
            {
                if (smoke.Duration == 45f && smoke.SpreadAmount == 35)
                    roofedSmoke++;
            }
        });

        // Roof off, and a second round lands on the same tile to check the open-ground path.
        await server.WaitPost(() =>
        {
            var mapUid = mapSystem.GetMap(map.MapId);
            var impact = new EntityCoordinates(mapUid, impactPosition);

            server.System<RoofSystem>().SetRoof((map.Grid.Owner, map.Grid.Comp, null), roofedTile, false);

            LandShell(entMan, "RMCMortarShellSmoke", impact);
            LandShell(entMan, "RMCMortarShellIllumination", impact);
        });

        await pair.RunTicksSync(3);

        var smokeClouds = 0;
        var smokeDistance = float.MaxValue;
        var litFlares = 0;
        var flareAnchored = false;
        var flarePickupable = false;
        var flareLifetime = 0f;
        var flareBurnTime = 0f;

        await server.WaitPost(() =>
        {
            var smokeQuery = entMan.EntityQueryEnumerator<SmokeComponent>();
            while (smokeQuery.MoveNext(out var uid, out var smoke))
            {
                if (smoke.Duration != 45f || smoke.SpreadAmount != 35)
                    continue;

                smokeClouds++;
                smokeDistance = (xform.GetWorldPosition(uid) - impactPosition).Length();
            }

            var lightQuery = entMan.EntityQueryEnumerator<ExpendableLightComponent>();
            while (lightQuery.MoveNext(out var uid, out var light))
            {
                var meta = entMan.GetComponent<MetaDataComponent>(uid);
                if (meta.EntityPrototype?.ID != "RMCMortarIlluminationFlare")
                    continue;

                if (light.Activated)
                    litFlares++;

                flareAnchored = entMan.GetComponent<TransformComponent>(uid).Anchored;
                flarePickupable = entMan.HasComponent<ItemComponent>(uid);

                // The flare has to take itself off the map once it has burned out.
                if (entMan.TryGetComponent<TimedDespawnComponent>(uid, out var despawn))
                    flareLifetime = despawn.Lifetime;

                flareBurnTime = light.GlowDuration + light.FadeOutDuration;
            }
        });

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(smokeClouds, Is.EqualTo(2), "A smoke shell left no cloud behind, or left the wrong number of them.");
                Assert.That(smokeDistance, Is.LessThan(1.5f), "The smoke cloud did not land on the impact tile.");
                Assert.That(roofedSmoke, Is.EqualTo(1),
                    "A shell that came down on a roof did not deliver its payload underneath it.");
                Assert.That(litFlares, Is.EqualTo(1), "The illumination shell did not leave a burning flare.");
                Assert.That(flareAnchored, Is.True, "The illumination flare is not anchored, so it can drift or be moved.");
                Assert.That(flarePickupable, Is.False, "The illumination flare is an item and can be picked up.");
                Assert.That(flareLifetime, Is.GreaterThanOrEqualTo(flareBurnTime),
                    "The illumination flare despawns before it has finished burning.");
                Assert.That(flareLifetime, Is.LessThan(flareBurnTime + 30f),
                    "The illumination flare outlives its burn by a long time instead of cleaning itself up.");
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    /// The illumination camera moves a player's view there and back, and is lost safely.

    [Test]
    public async Task IlluminationShellCameraIsViewableFromTheMortar()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var cEntMan = client.EntMan;
        var map = await pair.CreateTestMap();

        var serverSession = server.PlayerMan.GetSessionById(client.Session!.UserId);

        EntityUid player = default;
        EntityUid mortar = default;
        await server.WaitPost(() =>
        {
            player = entMan.SpawnEntity("MobHuman", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(serverSession, player);
            mortar = entMan.SpawnEntity("RMCMortarKit", map.GridCoords);
        });

        await pair.RunTicksSync(10);

        // Bring the round down the way the flight code does, firing mortar recorded.
        var camera = default(EntityUid);
        var cameraFound = false;
        var cameraAnchored = false;
        var cameraLifetime = 0f;
        var linkedToMortar = false;

        await server.WaitPost(() =>
        {
            var mapUid = server.System<SharedMapSystem>().GetMap(map.MapId);
            var impact = new EntityCoordinates(mapUid,
                server.System<SharedTransformSystem>().ToMapCoordinates(map.GridCoords).Position);

            var shell = entMan.SpawnEntity("RMCMortarShellIllumination", impact);

            // Recorded from the tube it fired out of, so the camera knows where to report.
            var ev = new MortarShellLandEvent(impact, mortar);
            entMan.EventBus.RaiseLocalEvent(shell, ref ev);

            var cameras = entMan.EntityQueryEnumerator<MortarImpactCameraComponent>();
            while (cameras.MoveNext(out var uid, out _))
            {
                camera = uid;
                cameraFound = true;
                cameraAnchored = entMan.GetComponent<TransformComponent>(uid).Anchored;

                if (entMan.TryGetComponent<TimedDespawnComponent>(uid, out var despawn))
                    cameraLifetime = despawn.Lifetime;
            }

            linkedToMortar = cameraFound && entMan.GetComponent<MortarComponent>(mortar).ImpactCamera == camera;
        });

        Assert.Multiple(() =>
        {
            Assert.That(cameraFound, Is.True, "The illumination round left no impact camera behind.");
            Assert.That(linkedToMortar, Is.True, "The mortar was not linked to the camera its own round dropped.");
            Assert.That(cameraAnchored, Is.True, "The impact camera is not anchored, so it can drift off the impact tile.");
            Assert.That(cameraLifetime, Is.GreaterThan(0f), "The impact camera never despawns.");
        });

        await client.WaitPost(() =>
        {
            var clientMortar = pair.ToClientUid(mortar);
            client.System<SharedUserInterfaceSystem>().OpenUi(clientMortar, MortarUiKey.Key, predicted: true);
        });

        await pair.RunTicksSync(10);

        // Pressing the button in the open mortar window, once to watch and once to stop.
        async Task PressCameraButton()
        {
            await client.WaitPost(() =>
            {
                var clientMortar = pair.ToClientUid(mortar);
                if (cEntMan.TryGetComponent<UserInterfaceComponent>(clientMortar, out var ui) &&
                    ui!.ClientOpenInterfaces.TryGetValue(MortarUiKey.Key, out var bui))
                {
                    bui.SendMessage(new MortarCameraBuiMsg());
                }
            });

            await pair.RunTicksSync(5);
        }

        EntityUid? lookingAt = null;
        await PressCameraButton();

        await server.WaitPost(() => lookingAt = entMan.GetComponent<EyeComponent>(player).Target);
        Assert.That(lookingAt, Is.EqualTo(camera),
            "The mortar interface did not move the player's view to the impact site.");

        await PressCameraButton();

        await server.WaitPost(() => lookingAt = entMan.GetComponent<EyeComponent>(player).Target);
        Assert.That(lookingAt, Is.Null, "The interface did not give the view back to the player.");

        // Watching is temporary: it ends on its own, so nobody is left holding a feed.
        await PressCameraButton();

        await server.WaitPost(() => lookingAt = entMan.GetComponent<EyeComponent>(player).Target);
        Assert.That(lookingAt, Is.EqualTo(camera), "The player never got the feed back for the expiry check.");

        await pair.RunSeconds(6);

        await server.WaitPost(() => lookingAt = entMan.GetComponent<EyeComponent>(player).Target);
        Assert.That(lookingAt, Is.Null, "The camera view did not end on its own after five seconds.");

        // Stepping away drops the feed, so nobody walks around blind.
        await PressCameraButton();

        var watchingBeforeMove = false;
        await server.WaitPost(() =>
        {
            watchingBeforeMove = entMan.GetComponent<EyeComponent>(player).Target == camera;

            var xform = entMan.GetComponent<TransformComponent>(player);
            server.System<SharedTransformSystem>()
                .SetLocalPositionNoLerp(player, xform.LocalPosition + new Vector2(1f, 0f), xform);
        });

        Assert.That(watchingBeforeMove, Is.True, "The view was already gone before the player had moved.");

        await pair.RunTicksSync(5);

        await server.WaitPost(() => lookingAt = entMan.GetComponent<EyeComponent>(player).Target);
        Assert.That(lookingAt, Is.Null, "Moving did not hand the view back to the player.");

        await server.WaitPost(() =>
        {
            var xform = entMan.GetComponent<TransformComponent>(player);
            server.System<SharedTransformSystem>()
                .SetLocalPositionNoLerp(player, xform.LocalPosition - new Vector2(1f, 0f), xform);
        });

        await pair.RunTicksSync(5);

        // Closing the window closes the feed with it, since the window is the only way back out.
        await PressCameraButton();

        var closedWindow = false;
        await client.WaitPost(() =>
        {
            var clientMortar = pair.ToClientUid(mortar);
            if (cEntMan.TryGetComponent<UserInterfaceComponent>(clientMortar, out var ui) &&
                ui!.ClientOpenInterfaces.TryGetValue(MortarUiKey.Key, out var bui))
            {
                bui.Close();
                closedWindow = true;
            }
        });

        Assert.That(closedWindow, Is.True, "The mortar window was not open to close.");

        await pair.RunTicksSync(5);

        await server.WaitPost(() => lookingAt = entMan.GetComponent<EyeComponent>(player).Target);
        Assert.That(lookingAt, Is.Null, "Closing the mortar window left the player watching the feed.");

        await client.WaitPost(() =>
        {
            var clientMortar = pair.ToClientUid(mortar);
            client.System<SharedUserInterfaceSystem>().OpenUi(clientMortar, MortarUiKey.Key, predicted: true);
        });

        await pair.RunTicksSync(10);

        // Watch again, then take the camera away underneath them.
        await PressCameraButton();

        var stillLinked = false;
        await server.WaitPost(() => entMan.QueueDeleteEntity(camera));
        await pair.RunTicksSync(10);

        await server.WaitPost(() =>
        {
            lookingAt = entMan.GetComponent<EyeComponent>(player).Target;
            stillLinked = entMan.GetComponent<MortarComponent>(mortar).ImpactCamera != null;
        });

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(lookingAt, Is.Null, "The view stayed pointed at a camera that no longer exists.");
                Assert.That(stillLinked, Is.False, "The mortar still offers a camera it can no longer reach.");
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    /// The plasma, phosphorus and pulse rounds each deliver their own payload.

    [Test]
    public async Task SpecialtyShellsDeliverTheirPayloads()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings());
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        var mapSystem = server.System<SharedMapSystem>();
        var impact = new EntityCoordinates(mapSystem.GetMap(map.MapId), Vector2.Zero);

        var plasmaFire = false;

        await server.WaitPost(() => LandShell(entMan, "RMCMortarShellPlasma", impact));
        await pair.RunTicksSync(3);

        await server.WaitPost(() =>
        {
            var fires = entMan.EntityQueryEnumerator<TileFireComponent>();
            while (fires.MoveNext(out var uid, out _))
            {
                if (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "MisfitsTileFirePlasma")
                    plasmaFire = true;
            }
        });

        var phosphorusFire = false;
        var causticSmoke = false;

        // The phosphorus round's own fire replaces the plasma fire already there.
        await server.WaitPost(() => LandShell(entMan, "RMCMortarShellPhosphorus", impact));
        await pair.RunTicksSync(3);

        await server.WaitPost(() =>
        {
            var fires = entMan.EntityQueryEnumerator<TileFireComponent>();
            while (fires.MoveNext(out var uid, out _))
            {
                if (entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "MisfitsTileFirePhosphorus")
                    phosphorusFire = true;
            }

            var clouds = entMan.EntityQueryEnumerator<SmokeComponent>();
            while (clouds.MoveNext(out var uid, out var smoke))
            {
                if (smoke.Solution is not { } solutionEnt)
                    continue;

                var solution = entMan.GetComponent<SolutionComponent>(solutionEnt.Owner).Solution;
                causticSmoke = solution.Contents.Any(quantity =>
                    quantity.Reagent.Prototype == "MisfitsWhitePhosphorusSmoke");
            }
        });

        EntityUid cell = default;
        await server.WaitPost(() => cell = entMan.SpawnEntity("PowerCellSmall", impact));
        await pair.RunTicksSync(3);

        await server.WaitPost(() => LandShell(entMan, "RMCMortarShellEMP", impact));
        await pair.RunTicksSync(3);

        var cellDisabled = false;
        var cellCharge = -1f;

        await server.WaitPost(() =>
        {
            cellDisabled = entMan.HasComponent<EmpDisabledComponent>(cell);
            cellCharge = entMan.GetComponent<BatteryComponent>(cell).CurrentCharge;
        });

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(plasmaFire, Is.True, "The plasma shell did not leave any plasma fire behind.");
                Assert.That(phosphorusFire, Is.True, "The phosphorus shell did not leave any fire behind.");
                Assert.That(causticSmoke, Is.True, "The phosphorus shell's smoke carries no white phosphorus reagent.");
                Assert.That(cellDisabled, Is.True, "The pulse shell left the power cell working.");
                Assert.That(cellCharge, Is.EqualTo(0f), "The pulse shell did not drain the power cell.");
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    /// The chemical round mixes two beakers into a single puff on the impact tile.

    [Test]
    public async Task ChemicalShellMixesItsBeakersOnImpact()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings());
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        var mapSystem = server.System<SharedMapSystem>();
        var xform = server.System<SharedTransformSystem>();
        var solution = server.System<SolutionContainerSystem>();
        var itemSlots = server.System<ItemSlotsSystem>();

        var impact = default(EntityCoordinates);
        var beakersLoaded = false;
        EntityUid shell = default;

        await server.WaitPost(() =>
        {
            impact = new EntityCoordinates(mapSystem.GetMap(map.MapId), xform.ToMapCoordinates(map.GridCoords).Position);
            shell = entMan.SpawnEntity("RMCMortarShellChemical", impact);

            var beakerA = entMan.SpawnEntity("Beaker", impact);
            var beakerB = entMan.SpawnEntity("Beaker", impact);

            beakersLoaded = FillBeaker(entMan, solution, beakerA, "Water", 15) &&
                            FillBeaker(entMan, solution, beakerB, "Sugar", 15) &&
                            itemSlots.TryInsert(shell, MortarChemicalShellComponent.BeakerSlotAId, beakerA, null) &&
                            itemSlots.TryInsert(shell, MortarChemicalShellComponent.BeakerSlotBId, beakerB, null);

            var ev = new MortarShellLandEvent(impact);
            entMan.EventBus.RaiseLocalEvent(shell, ref ev);
        });

        await pair.RunTicksSync(3);

        var mixedReagents = 0;
        var cloudVolume = FixedPoint2.Zero;

        await server.WaitPost(() =>
        {
            var clouds = entMan.EntityQueryEnumerator<SmokeComponent>();
            while (clouds.MoveNext(out _, out var smoke))
            {
                if (smoke.Solution is not { } solutionEnt)
                    continue;

                var cloud = entMan.GetComponent<SolutionComponent>(solutionEnt.Owner).Solution;
                if (!cloud.Contents.Any(quantity => quantity.Reagent.Prototype == "Water") ||
                    !cloud.Contents.Any(quantity => quantity.Reagent.Prototype == "Sugar"))
                {
                    continue;
                }

                mixedReagents++;
                cloudVolume = cloud.Volume;
            }
        });

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(beakersLoaded, Is.True,
                    "The chemical shell did not take two filled beakers, so there was nothing to mix.");
                Assert.That(mixedReagents, Is.EqualTo(1),
                    "The chemical shell did not release both beakers as a single puff.");
                Assert.That(cloudVolume, Is.EqualTo(FixedPoint2.New(30)),
                    "The cloud does not carry the full contents of both beakers.");
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    /// The kinetic round hits one tile and leaves its casing behind to be fired again.

    [Test]
    public async Task KineticShellStrikesOneTileAndSurvives()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings());
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        var mapSystem = server.System<SharedMapSystem>();
        var xform = server.System<SharedTransformSystem>();

        var origin = xform.ToMapCoordinates(map.GridCoords).Position;
        var impact = new EntityCoordinates(mapSystem.GetMap(map.MapId), origin);
        var beside = new EntityCoordinates(mapSystem.GetMap(map.MapId), origin + new Vector2(1, 0));

        EntityUid shell = default;
        EntityUid hit = default;
        EntityUid missed = default;

        await server.WaitPost(() =>
        {
            hit = entMan.SpawnEntity("MobHuman", impact);
            missed = entMan.SpawnEntity("MobHuman", beside);
            shell = entMan.SpawnEntity("RMCMortarShellKinetic", impact);

            var active = entMan.EnsureComponent<ActiveMortarShellComponent>(shell);

#pragma warning disable RA0002
            active.Coordinates = impact;
#pragma warning restore RA0002
        });

        await pair.RunTicksSync(3);

        var hitDamage = FixedPoint2.Zero;
        var missedDamage = FixedPoint2.Zero;
        var casingLeft = false;
        var casingActive = true;

        await server.WaitPost(() =>
        {
            hitDamage = entMan.GetComponent<DamageableComponent>(hit).TotalDamage;
            missedDamage = entMan.GetComponent<DamageableComponent>(missed).TotalDamage;
            casingLeft = !entMan.Deleted(shell);
            casingActive = entMan.HasComponent<ActiveMortarShellComponent>(shell);
        });

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(hitDamage, Is.GreaterThan(FixedPoint2.Zero),
                    "The kinetic round did not hit whatever was standing on the impact tile.");
                Assert.That(missedDamage, Is.EqualTo(FixedPoint2.Zero),
                    "The kinetic round hit a tile next to the one it struck.");
                Assert.That(casingLeft, Is.True, "The reusable casing was destroyed on impact.");
                Assert.That(casingActive, Is.False,
                    "The casing was still marked as in flight, so it could never be loaded again.");
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    /// The uranium round splashes the impact area with radiation that burns out on its own.

    [Test]
    public async Task UraniumShellSplashesTemporaryRadiation()
    {
        var pair = await PoolManager.GetServerClient(new PoolSettings());
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        var mapSystem = server.System<SharedMapSystem>();
        var xform = server.System<SharedTransformSystem>();

        var impact = new EntityCoordinates(mapSystem.GetMap(map.MapId), xform.ToMapCoordinates(map.GridCoords).Position);

        var splatters = 0;
        var radiating = 0;
        var lifetime = 0f;

        await server.WaitPost(() => LandShell(entMan, "RMCMortarShellUranium", impact));
        await pair.RunTicksSync(1);

        await server.WaitPost(() =>
        {
            var query = entMan.EntityQueryEnumerator<RadiationSourceComponent, TimedDespawnComponent>();
            while (query.MoveNext(out _, out var source, out var despawn))
            {
                splatters++;
                if (source.Intensity > 0)
                    radiating++;

                lifetime = despawn.Lifetime;
            }
        });

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(splatters, Is.EqualTo(14),
                    "The uranium round did not scatter its splatters over the area.");
                Assert.That(radiating, Is.EqualTo(splatters),
                    "A splatter was left behind without radiating.");
                Assert.That(lifetime, Is.EqualTo(45f).Within(0.5f),
                    "The splatters do not burn out on their own.");
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    /// Fills a beaker with a reagent and reports whether it worked.

    private static bool FillBeaker(IEntityManager entMan, SolutionContainerSystem solution, EntityUid beaker, string reagent, int quantity)
    {
        if (!entMan.TryGetComponent(beaker, out FitsInDispenserComponent fits) ||
            !solution.TryGetSolution(beaker, fits.Solution, out var solutionEntity, out _))
        {
            return false;
        }

        return solution.TryAddReagent(solutionEntity.Value, reagent, FixedPoint2.New(quantity));
    }

    /// Brings a shell down on a tile the way the flight code does once its timer runs out.

    private static void LandShell(IEntityManager entMan, string prototype, EntityCoordinates impact)
    {
        var shell = entMan.SpawnEntity(prototype, impact);
        var ev = new MortarShellLandEvent(impact);
        entMan.EventBus.RaiseLocalEvent(shell, ref ev);
    }
}
