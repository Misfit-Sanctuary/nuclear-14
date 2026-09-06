using System.Linq;
using Content.Server._Misfits.Expeditions.Generation;
using Content.Shared._Misfits.Expeditions;
using NUnit.Framework;

namespace Content.Tests.Server._Misfits.Expeditions;

[TestFixture]
public sealed class UndergroundExpeditionPlanBuilderTest
{
    [TestCase(UndergroundTheme.Vault, 7, 12, RoomType.VaultVault)]
    [TestCase(UndergroundTheme.Sewer, 6, 10, RoomType.SewerNest)]
    [TestCase(UndergroundTheme.Metro, 8, 14, RoomType.MetroCommand)]
    public void ThemePlansAreValidAcrossSeeds(
        UndergroundTheme theme,
        int minimumRooms,
        int maximumRooms,
        RoomType objectiveType)
    {
        var profile = UndergroundThemeProfiles.GetProfile(theme);
        for (var seed = 0; seed < 100; seed++)
        {
            var parameters = new UndergroundGenParams
            {
                Seed = seed,
                Theme = theme,
                MinRooms = minimumRooms,
                MaxRooms = maximumRooms,
                HubCount = 4,
            };
            var plan = UndergroundExpeditionPlanBuilder.Build(
                parameters,
                profile,
                parameters.EnvironmentalStates,
                ExpeditionSeedStreams.Create(seed, "topology"));

            var validation = UndergroundExpeditionPlanBuilder.Validate(plan);
            Assert.That(validation.Errors, Is.Empty, $"seed {seed}: {string.Join("; ", validation.Errors)}");
            Assert.That(plan.Rooms.Count(room => room.RoomType != RoomType.FactionHub),
                Is.InRange(minimumRooms, maximumRooms));
            Assert.That(plan.Rooms.Single(room => room.Id == plan.ObjectiveRoomId).RoomType, Is.EqualTo(objectiveType));

            // A config with no faction groups represents one shared player party.
            Assert.That(plan.Rooms.Count(room => room.RoomType == RoomType.FactionHub), Is.EqualTo(1));
        }
    }

    [Test]
    public void NonVaultThemesDoNotInheritUniversalCentralRoom()
    {
        foreach (var theme in new[] { UndergroundTheme.Sewer, UndergroundTheme.Metro })
        {
            var parameters = new UndergroundGenParams { Seed = 42, Theme = theme };
            var plan = UndergroundExpeditionPlanBuilder.Build(
                parameters,
                UndergroundThemeProfiles.GetProfile(theme),
                parameters.EnvironmentalStates,
                ExpeditionSeedStreams.Create(parameters.Seed, "topology"));

            Assert.That(plan.Rooms.Any(room => room.RoomType == RoomType.Central), Is.False);
        }
    }

    [Test]
    public void VaultUsesSecurityCheckpointAndReactorServiceChain()
    {
        var parameters = new UndergroundGenParams
        {
            Seed = 101,
            Theme = UndergroundTheme.Vault,
            MinRooms = 7,
            MaxRooms = 12,
        };
        var plan = UndergroundExpeditionPlanBuilder.Build(
            parameters,
            UndergroundThemeProfiles.GetProfile(parameters.Theme),
            parameters.EnvironmentalStates,
            ExpeditionSeedStreams.Create(parameters.Seed, "topology"));

        Assert.That(plan.Rooms.Single(room => room.Id == "security").RoomType,
            Is.EqualTo(RoomType.VaultSecurity));
        Assert.That(plan.Rooms.Single(room => room.Id == "maintenance").RoomType,
            Is.EqualTo(RoomType.VaultMaintenance));
        Assert.That(plan.Connections.Any(edge => edge.From == "entry-0" && edge.To == "security"), Is.True);
        Assert.That(plan.Connections.Any(edge => edge.From == "security" && edge.To == "atrium"), Is.True);
        Assert.That(plan.Connections.Any(edge => edge.From == "maintenance" && edge.To == "reactor"), Is.True);
    }

    [TestCase(UndergroundTheme.Vault, 14, 20)]
    [TestCase(UndergroundTheme.Sewer, 14, 22)]
    [TestCase(UndergroundTheme.Metro, 15, 24)]
    public void EnlargedExpeditionPlansCanFillConfiguredRange(
        UndergroundTheme theme,
        int minimumRooms,
        int maximumRooms)
    {
        var profile = UndergroundThemeProfiles.GetProfile(theme);
        for (var seed = 0; seed < 100; seed++)
        {
            var parameters = new UndergroundGenParams
            {
                Seed = seed,
                Theme = theme,
                GridWidth = 128,
                GridHeight = 128,
                MinRooms = minimumRooms,
                MaxRooms = maximumRooms,
                HubCount = 1,
            };
            var plan = UndergroundExpeditionPlanBuilder.Build(
                parameters,
                profile,
                parameters.EnvironmentalStates,
                ExpeditionSeedStreams.Create(seed, "topology"));

            Assert.That(UndergroundExpeditionPlanBuilder.Validate(plan).Errors, Is.Empty);
            Assert.That(plan.Rooms.Count(room => room.RoomType != RoomType.FactionHub),
                Is.InRange(minimumRooms, maximumRooms));
        }
    }

    [Test]
    public void FactionGroupsCapTheNumberOfHubs()
    {
        var parameters = new UndergroundGenParams
        {
            Seed = 7,
            Theme = UndergroundTheme.Vault,
            HubCount = 4,
            FactionSpawnGroups = new()
            {
                new N14FactionSpawnGroup(),
                new N14FactionSpawnGroup(),
                new N14FactionSpawnGroup(),
            },
        };
        var plan = UndergroundExpeditionPlanBuilder.Build(
            parameters,
            UndergroundThemeProfiles.GetProfile(parameters.Theme),
            parameters.EnvironmentalStates,
            ExpeditionSeedStreams.Create(parameters.Seed, "topology"));

        Assert.That(plan.Rooms.Count(room => room.RoomType == RoomType.FactionHub), Is.EqualTo(3));
        Assert.That(plan.Rooms.Where(room => room.RoomType == RoomType.FactionHub).Select(room => room.FactionIndex),
            Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public void NamedSeedStreamsAndPlansAreDeterministic()
    {
        const int seed = 8675309;
        var parameters = new UndergroundGenParams
        {
            Seed = seed,
            Theme = UndergroundTheme.Metro,
            MinRooms = 8,
            MaxRooms = 14,
        };
        var profile = UndergroundThemeProfiles.GetProfile(parameters.Theme);

        var first = UndergroundExpeditionPlanBuilder.Build(
            parameters, profile, parameters.EnvironmentalStates, ExpeditionSeedStreams.Create(seed, "topology"));
        var second = UndergroundExpeditionPlanBuilder.Build(
            parameters, profile, parameters.EnvironmentalStates, ExpeditionSeedStreams.Create(seed, "topology"));
        var firstGeometry = ExpeditionSeedStreams.Create(seed, "geometry");
        var secondGeometry = ExpeditionSeedStreams.Create(seed, "geometry");

        Assert.That(Signature(second), Is.EqualTo(Signature(first)));
        Assert.That(
            Enumerable.Range(0, 8).Select(_ => firstGeometry.Next()).ToArray(),
            Is.EqualTo(Enumerable.Range(0, 8).Select(_ => secondGeometry.Next()).ToArray()));
        Assert.That(ExpeditionSeedStreams.Create(seed, "geometry").Next(),
            Is.Not.EqualTo(ExpeditionSeedStreams.Create(seed, "entities").Next()));
    }

    private static string Signature(ExpeditionGenerationPlan plan)
    {
        var rooms = string.Join("|", plan.Rooms.Select(room =>
            $"{room.Id}:{room.RoomType}:{room.ZoneRole}:{room.Required}:{room.IsObjective}"));
        var edges = string.Join("|", plan.Connections.Select(edge => $"{edge.From}>{edge.To}:{edge.Required}"));
        return $"{plan.Identity.SiteType};{plan.Identity.FailureCause};{rooms};{edges}";
    }
}
