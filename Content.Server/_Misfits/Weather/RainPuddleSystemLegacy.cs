



// // #Misfits Change Add: Server-side rain puddle spawning system.
// // Spawns water puddles on open, weather-affected tiles near connected players
// // ONLY while the "Rain" weather prototype is in the Running state and has SpawnPuddles = true.
// // Both guards must pass — the explicit Rain proto check prevents any other weather from
// // accidentally triggering puddle spawning even if SpawnPuddles is inadvertently set.
// //
// // Design goals:
// //   1. Player-proximity culling  — tiles outside every player's viewport are never processed.
// //   2. Periodic, not per-tick    — an accumulator fires the pass at most once per PuddleInterval seconds.
// //   3. No spreading              — PuddleMaxVolume is kept below PuddleComponent.OverflowVolume (20 u)
// //                                  so the FluidSpreader is never meaningfully activated.
// //   4. Blood washing             — rain removes non-evaporating reagents (blood) from existing puddles
// //                                  each pass, simulating rain washing blood away.
// //   5. Soak into ground          — Water has evaporates:true, so rain puddles dry via EvaporationComponent
// //                                  after rain stops with no extra code required.
// using System.Numerics;
// using Content.Server.Fluids.EntitySystems;
// using Content.Shared.Chemistry.Components;
// using Content.Shared.Chemistry.EntitySystems;
// using Content.Shared.FixedPoint;
// using Content.Shared.StepTrigger.Components;
// using Content.Shared.Fluids;
// using Content.Shared.Fluids.Components;
// using Content.Shared.Light.Components;
// using Content.Shared.Slippery;
// using Content.Shared.Maps;
// using Content.Shared.Weather;
// using Robust.Shared.Map;
// using Robust.Shared.Map.Components;
// using Robust.Shared.Maths;
// using Robust.Shared.Player;
// using Robust.Shared.Prototypes;
// using Robust.Shared.Random;
//
// namespace Content.Server._Misfits.Weather;
//
// /// <summary>
// /// Periodically spawns/grows water puddles on open tiles near players while
// /// the <c>Rain</c> weather prototype is active and fully running.
// /// Explicitly gated to the "Rain" proto ID: puddle spawning will never fire
// /// for any other weather type, regardless of its <see cref="WeatherPrototype.SpawnPuddles"/> flag.
// ///
// /// Performance profile (default settings, 10 players, 500 outdoor tiles in range):
// ///   - One pass every 30 s → ~17 tile checks/s total, negligible CPU.
// ///   - Puddles stay below overflow threshold → spreader self-aborts immediately, ~0 extra cost.
// /// </summary>
// public sealed class RainPuddleSystem : EntitySystem
// {
//     [Dependency] private readonly PuddleSystem _puddle = default!;
//     [Dependency] private readonly SharedMapSystem _map = default!;
//     [Dependency] private readonly SharedSolutionContainerSystem _solutionContainerSystem = default!;
//     [Dependency] private readonly SharedWeatherSystem _weatherSystem = default!;
//     [Dependency] private readonly SharedTransformSystem _transform = default!;
//     [Dependency] private readonly IPrototypeManager _proto = default!;
//     [Dependency] private readonly IRobustRandom _random = default!;
//
//     /// <summary>
//     /// The only weather prototype that is permitted to spawn puddles.
//     /// Changing the rain proto ID? Update this constant to match.
//     /// </summary>
//     private static readonly ProtoId<WeatherPrototype> RainProtoId = "Rain";
//
//     /// <summary>
//     /// Weather prototype IDs that must never trigger puddle spawning regardless of any flag.
//     /// "Default" and "Null" are sentinel/fallback values used internally and should never
//     /// be treated as real weather states.
//     /// </summary>
//     private static readonly HashSet<string> _excludedProtos = new(StringComparer.OrdinalIgnoreCase)
//     {
//         "Default",
//         "Null",
//     };
//
//     /// <summary>
//     /// Per (mapUid, protoId) elapsed-time accumulator driving the interval timer.
//     /// Entries are created lazily and accumulate until they exceed <see cref="WeatherPrototype.PuddleInterval"/>.
//     /// </summary>
//     private readonly Dictionary<(EntityUid, string), float> _accumulators = new();
//
//     public override void Update(float frameTime)
//     {
//         // #Misfits Tweak: Rain puddle spawning and blood washing disabled.
//         // We do not need rain to wash blood or to spawn water puddles; the per-player
//         // viewport tile scan was also a medium CPU cost during rain. Commenting out rather
//         // than deleting to preserve the original logic for reference.
//         //
//         // var weatherQuery = EntityQueryEnumerator<WeatherComponent>();
//         // while (weatherQuery.MoveNext(out var mapUid, out var weatherComp))
//         // {
//         //     foreach (var (protoId, weatherData) in weatherComp.Weather)
//         //     {
//         //         if (_excludedProtos.Contains(protoId.Id))
//         //             continue;
//         //         if (protoId != RainProtoId)
//         //             continue;
//         //         if (weatherData.State != WeatherState.Running)
//         //             continue;
//         //         if (!_proto.TryIndex<WeatherPrototype>(protoId, out var proto) || !proto.SpawnPuddles)
//         //             continue;
//         //
//         //         var key = (mapUid, protoId.Id);
//         //         _accumulators.TryGetValue(key, out var accum);
//         //         accum += frameTime;
//         //
//         //         if (accum < proto.PuddleInterval)
//         //         {
//         //             _accumulators[key] = accum;
//         //             continue;
//         //         }
//         //
//         //         _accumulators[key] = accum - proto.PuddleInterval;
//         //         SpawnRainPuddles(mapUid, proto);
//         //     }
//         // }
//     }
//
//     /// <summary>
//     /// Iterates all attached players on <paramref name="mapUid"/>, collects the weather-affected
//     /// tiles within <see cref="WeatherPrototype.PuddleViewportRadius"/> of each player (deduplicating
//     /// overlapping viewports), then for each tile:
//     /// <list type="bullet">
//     ///   <item>Washes non-evaporating reagents (blood) from existing puddles if <see cref="WeatherPrototype.WashBloodPuddles"/> is enabled.</item>
//     ///   <item>Deposits rain water up to <see cref="WeatherPrototype.PuddleMaxVolume"/>.</item>
//     /// </list>
//     /// After rain ends, rain-water puddles evaporate naturally via <see cref="EvaporationComponent"/>
//     /// because <c>Water.evaporates = true</c> — the "soaks into the ground" effect requires no extra code.
//     /// </summary>
//     private void SpawnRainPuddles(EntityUid mapUid, WeatherPrototype proto)
//     {
//         var puddleQuery = GetEntityQuery<PuddleComponent>();
//
//         // Deduplication set: we never process the same tile twice in one pass,
//         // even when multiple players' view circles overlap.
//         var visited = new HashSet<(EntityUid gridUid, Vector2i indices)>();
//
//         var playerQuery = EntityQueryEnumerator<ActorComponent, TransformComponent>();
//         while (playerQuery.MoveNext(out _, out _, out var xform))
//         {
//             // Only affect players that are on this map and standing on a grid
//             if (xform.MapUid != mapUid || xform.GridUid == null)
//                 continue;
//
//             var gridUid = xform.GridUid.Value;
//             if (!TryComp<MapGridComponent>(gridUid, out var grid))
//                 continue;
//
//             // RoofComponent is per-grid; retrieve once per player but it's cheap
//             TryComp<RoofComponent>(gridUid, out var roofComp);
//
//             // Build a world-space bounding box centred on the player matching the configured radius
//             var worldPos = _transform.GetWorldPosition(xform);
//             var diameter = proto.PuddleViewportRadius * 2f;
//             var viewBox = Box2.CenteredAround(worldPos, new Vector2(diameter, diameter));
//
//             foreach (var tileRef in _map.GetTilesIntersecting(gridUid, grid, viewBox))
//             {
//                 // Skip tiles already processed this pass (overlapping viewports or duplicate grids)
//                 if (!visited.Add((gridUid, tileRef.GridIndices)))
//                     continue;
//
//                 // Must be an open, rain-exposed tile: no roof, correct tile flags, no BlockWeather entity
//                 if (!_weatherSystem.CanWeatherAffect(gridUid, grid, tileRef, roofComp))
//                     continue;
//
//                 // Random per-tile chance keeps distribution looking organic rather than uniform
//                 if (!_random.Prob(proto.PuddleChance))
//                     continue;
//
//                 // Find any existing puddle anchored on this tile.
//                 // We reuse the result for both blood-washing and the volume cap check.
//                 EntityUid existingUid = EntityUid.Invalid;
//                 PuddleComponent? existingComp = null;
//                 var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tileRef.GridIndices);
//                 while (anchored.MoveNext(out var ent))
//                 {
//                     if (!puddleQuery.TryGetComponent(ent, out var pc))
//                         continue;
//                     existingUid = ent.Value;
//                     existingComp = pc;
//                     break;
//                 }
//
//                 // === Blood washing ===
//                 // Rain removes non-evaporating reagents (blood) from existing puddles.
//                 // Blood has evaporates:false so it never dries on its own outdoors;
//                 // this simulates rain literally washing it away.
//                 // SplitSolutionWithout removes only reagents NOT in EvaporationReagents (i.e. blood);
//                 // the split solution is discarded and UpdateChemicals is called internally.
//                 //
//                 // #Misfits Fix: Track whether non-evaporating reagents (blood) were present so we
//                 // can skip the rain-water addition step below. Previously, rain always added water
//                 // even after washing, causing puddle volume to INCREASE each pass
//                 // (3u water added > 2u blood removed). Now: if blood still exists after washing,
//                 // we only wash — no extra water is deposited on that tile this pass.
//                 bool hadNonEvapReagents = false;
//                 if (proto.WashBloodPuddles && existingUid.IsValid() && existingComp != null)
//                 {
//                     if (_solutionContainerSystem.ResolveSolution(existingUid, existingComp.SolutionName,
//                         ref existingComp.Solution, out var puddleSol))
//                     {
//                         // Volume of non-evaporating reagents = total minus evaporating portion
//                         var evapVol = puddleSol.GetTotalPrototypeQuantity(SharedPuddleSystem.EvaporationReagents);
//                         var nonEvapVol = puddleSol.Volume - evapVol;
//
//                         if (nonEvapVol > FixedPoint2.Zero)
//                         {
//                             hadNonEvapReagents = true;
//                             _solutionContainerSystem.SplitSolutionWithout(
//                                 existingComp.Solution!.Value,
//                                 FixedPoint2.Min(nonEvapVol, FixedPoint2.New(proto.PuddleWashAmount)),
//                                 SharedPuddleSystem.EvaporationReagents);
//                         }
//                     }
//                 }
//
//                 // === Rain water addition ===
//                 // Skip adding water to tiles where blood is being washed this pass.
//                 // Adding water on top of blood washing caused puddle volumes to GROW each interval
//                 // because PuddleAmountPerInterval > PuddleWashAmount.
//                 // Once blood is fully removed, the tile is treated as a clean tile next pass
//                 // and rain water can accumulate normally.
//                 if (hadNonEvapReagents)
//                     continue;
//
//                 // CurrentVolume re-reads the solution so it reflects the post-wash state above.
//                 // PuddleMaxVolume must stay below PuddleComponent.OverflowVolume (default 20 u)
//                 // so the fluid spreader never triggers and puddles won't creep indoors.
//                 // After rain stops, Water evaporates via EvaporationComponent — the "soaks into
//                 // the ground" effect — because Water.evaporates = true requires no extra code.
//                 var currentVol = existingUid.IsValid() && existingComp != null
//                     ? _puddle.CurrentVolume(existingUid, existingComp)
//                     : FixedPoint2.Zero;
//
//                 if (currentVol >= FixedPoint2.New(proto.PuddleMaxVolume))
//                     continue;
//
//                 // Silently deposit a small amount of the configured reagent.
//                 // No spill sound — rain should be ambient, not a constant splat chorus.
//                 var coords = _map.GridTileToLocal(gridUid, grid, tileRef.GridIndices);
//                 var rainSolution = new Solution(proto.PuddleReagent, FixedPoint2.New(proto.PuddleAmountPerInterval));
//                 _puddle.TrySpillAt(coords, rainSolution, out var spawnedPuddleUid, sound: false);
//
//                 // #Misfits Fix: Rain water should not cause slipping.
//                 // If TrySpillAt created a BRAND NEW puddle (no pre-existing puddle was on this
//                 // tile — existingUid was invalid), strip the SlipperyComponent and StepTrigger
//                 // that the base "Puddle" prototype adds. Pre-existing puddles (blood, etc.) are
//                 // untouched, so they stay slippery as expected.
//                 if (!existingUid.IsValid() && spawnedPuddleUid.IsValid())
//                 {
//                     RemComp<SlipperyComponent>(spawnedPuddleUid);
//                     RemComp<StepTriggerComponent>(spawnedPuddleUid);
//                 }
//             }
//         }
//     }
// }
