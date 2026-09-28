// SPDX-License-Identifier: AGPL-3.0-or-later


using System.IO;
using System.Threading.Tasks;
using Content.Shared.Construction;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Database;
using Robust.Shared.Map;

namespace Content.Server.Construction
{
    public sealed partial class ConstructionSystem
    {
        public async Task<bool> TryStartStructureConstruction(EntityUid user, string prototype, EntityCoordinates coordinates, Angle angle)
        {
            if (!_prototypeManager.TryIndex(prototype, out ConstructionPrototype? constructionPrototype))
            {
                Log.Error($"Tried to start unattended construction of invalid recipe '{prototype}'!");
                return false;
            }

            if (!_prototypeManager.TryIndex(constructionPrototype.Graph, out ConstructionGraphPrototype? constructionGraph))
            {
                Log.Error($"Invalid construction graph '{constructionPrototype.Graph}' in recipe '{prototype}'!");
                return false;
            }

            if (_whitelistSystem.IsWhitelistFail(constructionPrototype.EntityWhitelist, user))
                return false;

            var startNode = constructionGraph.Nodes[constructionPrototype.StartNode];
            var targetNode = constructionGraph.Nodes[constructionPrototype.TargetNode];
            var pathFind = constructionGraph.Path(startNode.Name, targetNode.Name);

            foreach (var condition in constructionPrototype.Conditions)
            {
                if (!condition.Condition(user, coordinates, angle.GetCardinalDir()))
                    return false;
            }

            if (pathFind == null)
            {
                throw new InvalidDataException(
                    $"Can't find path from starting node to target node in construction! Recipe: {prototype}");
            }

            var edge = startNode.GetEdge(pathFind[0].Name);

            if (edge == null)
            {
                throw new InvalidDataException(
                    $"Can't find edge from starting node to the next node in pathfinding! Recipe: {prototype}");
            }

            foreach (var step in edge.Steps)
            {
                switch (step)
                {
                    case Content.Shared.Construction.Steps.ToolConstructionGraphStep _:
                        throw new InvalidDataException("Invalid first step for construction recipe!");
                }
            }

            if (await Construct(user, $"structure_construction_{user}", constructionGraph, edge, targetNode) is not { Valid: true } structure)
                return false;

            var xform = Transform(structure);
            var wasAnchored = xform.Anchored;
            xform.Anchored = false;
            xform.Coordinates = coordinates;
            xform.LocalRotation = constructionPrototype.CanRotate ? angle : Angle.Zero;
            xform.Anchored = wasAnchored;

            RaiseLocalEvent(structure, new ConstructionCompletedEvent(structure, user));
            _adminLogger.Add(LogType.Construction, LogImpact.Low, $"{ToPrettyString(user):actor} unattended-built {prototype} into {ToPrettyString(structure)} at {Transform(structure).Coordinates}");

            return true;
        }
    }
}
