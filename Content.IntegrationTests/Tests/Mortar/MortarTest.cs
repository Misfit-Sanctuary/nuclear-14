using Content.Shared._RMC14.Mortar;
using Content.Shared.Interaction.Events;
using Content.Shared.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.IoC;
using Robust.Shared.Reflection;

namespace Content.IntegrationTests.Tests.Mortar;

[TestFixture]
public sealed class MortarTest
{
    private const string BuiTypeName = "MortarBui";

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
        var diagnostics = "none";

        await client.WaitPost(() =>
        {
            var playerMan = client.ResolveDependency<Robust.Client.Player.IPlayerManager>();
            var refMan = client.ResolveDependency<IReflectionManager>();
            var clientMortar = pair.ToClientUid(mortar);
            var hasUi = cEntMan.TryGetComponent<UserInterfaceComponent>(clientMortar, out var ui);

            var open = "";

            if (hasUi)
                open = string.Join(",", ui!.ClientOpenInterfaces.Keys);

            var type = refMan.LooseGetType(BuiTypeName);
            var typeResolves = type != null;
            var createError = "ok";

            if (type != null)
            {
                try
                {
                    var factory = client.ResolveDependency<IDynamicTypeFactory>();
                    factory.CreateInstance(type, [clientMortar, MortarUiKey.Key], inject: false);
                }
                catch (Exception e)
                {
                    createError = e.GetType().Name + ": " + e.Message;
                }
            }

            diagnostics =
                $"localEntity={playerMan.LocalEntity}, hasUiComponent={hasUi}, {BuiTypeName}Resolves={typeResolves}, create={createError}, open=[{open}]";

            if (!hasUi || !ui!.ClientOpenInterfaces.TryGetValue(MortarUiKey.Key, out var bui))
                return;

            bui.SendMessage(new MortarTargetBuiMsg(target));
            sent = true;
        });

        Assert.That(sent, Is.True, $"The client never opened a mortar window. ({diagnostics})");

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
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }
}
