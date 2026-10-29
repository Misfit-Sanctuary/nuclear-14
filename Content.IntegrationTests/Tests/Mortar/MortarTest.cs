using System.Numerics;
using Content.Server.Light.Components;
using Content.Server.Light.EntitySystems;
using Content.Shared._RMC14.Mortar;
using Content.Shared.Chemistry.Components;
using Content.Shared.Light.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
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
    /// <summary>
    ///     Deploys a mortar and sends a target from the client interface, then checks the server stored it.
    /// </summary>
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

            // A roof over the target has to stop the aim being accepted at all. The test map is a single
            // tile, so the roof goes on the only tile there is.
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

    /// <summary>
    ///     Fires the impact event the way the flight code does, with the map-relative coordinates it stores,
    ///     and checks that a utility shell actually leaves its payload on the grid it landed on.
    /// </summary>
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

        // The mortar stores the impact point relative to the map entity, not a grid, so the test uses the
        // same shape of coordinates. This round comes down on a roof and must not reach the floor under it.
        await server.WaitPost(() =>
        {
            var mapUid = mapSystem.GetMap(map.MapId);
            impactPosition = xform.ToMapCoordinates(map.GridCoords).Position;

            entMan.EnsureComponent<RoofComponent>(map.Grid.Owner);
            server.System<RoofSystem>().SetRoof((map.Grid.Owner, map.Grid.Comp, null), roofedTile, true);

            LandShell(entMan, "RMCMortarShellSmoke", new EntityCoordinates(mapUid, impactPosition));
        });

        await pair.RunTicksSync(3);

        var smokeUnderRoof = 0;

        await server.WaitPost(() =>
        {
            var smokeQuery = entMan.EntityQueryEnumerator<SmokeComponent>();
            while (smokeQuery.MoveNext(out _, out var smoke))
            {
                if (smoke.Duration == 45f && smoke.SpreadAmount == 35)
                    smokeUnderRoof++;
            }
        });

        // Roof comes off again, so the same tile can also show that a round that is allowed to land does
        // deliver its payload.
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
                Assert.That(smokeClouds, Is.EqualTo(1), "The smoke shell left no cloud behind, or left more than one.");
                Assert.That(smokeDistance, Is.LessThan(1.5f), "The smoke cloud did not land on the impact tile.");
                Assert.That(smokeUnderRoof, Is.EqualTo(0),
                    "A shell that came down on a roof still delivered its payload underneath it.");
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

    /// <summary>
    ///     Brings a shell down on a tile the way the flight code does once its timer runs out.
    /// </summary>
    private static void LandShell(IEntityManager entMan, string prototype, EntityCoordinates impact)
    {
        var shell = entMan.SpawnEntity(prototype, impact);
        var ev = new MortarShellLandEvent(impact);
        entMan.EventBus.RaiseLocalEvent(shell, ref ev);
    }
}
