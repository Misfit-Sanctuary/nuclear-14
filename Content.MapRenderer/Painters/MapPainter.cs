using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests;
using Content.Server.GameTicking;
using Content.Server.Maps;
using Robust.Client.GameObjects;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.ContentPack;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Content.MapRenderer.Painters
{
    public sealed class MapPainter
    {
        // #Misfits Change - bypass GameTicker round flow for map rendering to support large maps
        public static async IAsyncEnumerable<RenderedGridImage<Rgba32>> Paint(string map)
        {
            await foreach (var grid in PaintCore(map, null))
                yield return grid;
        }

        // #Misfits Add - render a map directly from its resource path, without a gameMap prototype.
        public static async IAsyncEnumerable<RenderedGridImage<Rgba32>> PaintFromPath(ResPath mapPath)
        {
            await foreach (var grid in PaintCore(null, mapPath))
                yield return grid;
        }

        private static async IAsyncEnumerable<RenderedGridImage<Rgba32>> PaintCore(string? gameMapId, ResPath? rawPath)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            // Use DummyTicker to avoid going through GameTicker.RestartRound(),
            // which can fail for large grid-map files (e.g. Wendover with 183K+ entities).
            await using var pair = await PoolManager.GetServerClient(new PoolSettings
            {
                DummyTicker = true,
                Connected = true,
                Fresh = true,
            });

            var server = pair.Server;
            var client = pair.Client;

            Console.WriteLine($"Loaded client and server in {(int) stopwatch.Elapsed.TotalMilliseconds} ms");

            stopwatch.Restart();

            var cEntityManager = client.ResolveDependency<IClientEntityManager>();
            var cPlayerManager = client.ResolveDependency<Robust.Client.Player.IPlayerManager>();

            await client.WaitPost(() =>
            {
                if (cEntityManager.TryGetComponent(cPlayerManager.LocalEntity, out SpriteComponent? sprite))
                {
                    sprite.Visible = false;
                }
            });

            var sEntityManager = server.ResolveDependency<IServerEntityManager>();
            var sPlayerManager = server.ResolveDependency<IPlayerManager>();

            // Load the map directly via MapLoaderSystem, bypassing GameTicker round flow.
            MapId loadedMapId = default;
            await server.WaitPost(() =>
            {
                var protoManager = server.ResolveDependency<IPrototypeManager>();
                ResPath mapFilePath;

                if (rawPath.HasValue)
                {
                    mapFilePath = rawPath.Value;
                    Console.WriteLine($"Loading map file (raw path): {mapFilePath}");
                }
                else
                {
                    var mapProto = protoManager.Index<GameMapPrototype>(gameMapId!);
                    mapFilePath = mapProto.MapPath;
                    Console.WriteLine($"Loading map file: {mapFilePath}");
                }

                // Pre-check: validate that all referenced entity prototypes exist
                var missingProtos = new List<string>();
                var resMan = server.ResolveDependency<IResourceManager>();
                if (resMan.TryContentFileRead(mapFilePath, out var stream))
                {
                    using var reader = new System.IO.StreamReader(stream);
                    string? line;
                    var protoSet = new HashSet<string>();
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.StartsWith("- proto: "))
                        {
                            var proto = line.Substring("- proto: ".Length).Trim();
                            if (!string.IsNullOrEmpty(proto) && proto != "\"\"" && protoSet.Add(proto))
                            {
                                if (!protoManager.HasIndex<EntityPrototype>(proto))
                                    missingProtos.Add(proto);
                            }
                        }
                    }
                    Console.WriteLine($"Prototype check: {protoSet.Count} unique protos, {missingProtos.Count} missing");
                    foreach (var m in missingProtos)
                    {
                        Console.WriteLine($"  MISSING: {m}");
                    }
                }

                if (missingProtos.Count > 0)
                {
                    Console.WriteLine($"WARNING: {missingProtos.Count} missing prototypes will prevent map loading.");
                    Console.WriteLine("The engine's ValidatePrototypes() check will fail.");
                    throw new Exception($"Map has {missingProtos.Count} missing prototype(s). Fix or remove them from the map file.");
                }

                var mapLoader = sEntityManager.System<MapLoaderSystem>();

                if (!mapLoader.TryLoadGeneric(mapFilePath, out var result))
                {
                    throw new Exception($"TryLoadGeneric failed for: {mapFilePath}");
                }

                Console.WriteLine($"TryLoadGeneric succeeded: {result.Maps.Count} map(s), {result.Grids.Count} grid(s)");

                if (result.Maps.Count == 0)
                {
                    throw new Exception($"No maps found in file: {mapFilePath}");
                }

                var mapEntity = result.Maps.First();
                loadedMapId = sEntityManager.GetComponent<MapComponent>(mapEntity.Owner).MapId;

                // Initialize the map so grid AABBs and tile data are computed.
                var mapManager = sEntityManager.System<SharedMapSystem>();
                mapManager.InitializeMap(loadedMapId);

                Console.WriteLine($"Directly loaded map (MapId {loadedMapId}) in {(int) stopwatch.Elapsed.TotalMilliseconds} ms");
            });

            stopwatch.Restart();

            await pair.RunTicksSync(10);
            await Task.WhenAll(client.WaitIdleAsync(), server.WaitIdleAsync());

            var sMapManager = sEntityManager.System<SharedMapSystem>();

            var tilePainter = new TilePainter(client, server);
            var entityPainter = new GridPainter(client, server);
            Entity<MapGridComponent>[] grids = null!;
            var xformQuery = sEntityManager.GetEntityQuery<TransformComponent>();
            var xformSystem = sEntityManager.System<SharedTransformSystem>();

            await server.WaitPost(() =>
            {
                var playerEntity = sPlayerManager.Sessions.Single().AttachedEntity;

                if (playerEntity.HasValue)
                {
                    sEntityManager.DeleteEntity(playerEntity.Value);
                }

                grids = sMapManager.GetAllGrids(loadedMapId).ToArray();
                var sMapSystem = sEntityManager.System<SharedMapSystem>();

                Console.WriteLine($"Found {grids.Length} grid(s) on map {loadedMapId}");
                foreach (var (uid, g) in grids)
                {
                    Console.WriteLine($"  Grid {uid}: AABB={g.LocalAABB}, TileSize={g.TileSize}, ChunkCount={g.ChunkCount}");

                    // For map-grid entities (Map+MapGrid on same entity), the engine's
                    // RegenerateCollision skips AABB calculation. Compute it from tile data.
                    if (g.LocalAABB.IsEmpty() && g.ChunkCount > 0)
                    {
                        Console.WriteLine($"  Computing AABB for grid {uid} from tile data...");
                        int minX = int.MaxValue, minY = int.MaxValue;
                        int maxX = int.MinValue, maxY = int.MinValue;
                        int tileCount = 0;

                        var enumerator = sMapSystem.GetAllTilesEnumerator(uid, g);
                        while (enumerator.MoveNext(out var tileRef))
                        {
                            if (tileRef.Value.X < minX) minX = tileRef.Value.X;
                            if (tileRef.Value.X > maxX) maxX = tileRef.Value.X;
                            if (tileRef.Value.Y < minY) minY = tileRef.Value.Y;
                            if (tileRef.Value.Y > maxY) maxY = tileRef.Value.Y;
                            tileCount++;
                        }

                        if (tileCount > 0)
                        {
                            var aabb = new Box2(minX, minY, maxX + 1, maxY + 1);
                            // LocalAABB has internal set; use reflection to set it for map-grids
                            typeof(MapGridComponent).GetProperty(nameof(MapGridComponent.LocalAABB))!
                                .SetValue(g, aabb);
                            Console.WriteLine($"  Computed AABB: {aabb} from {tileCount} tiles");
                        }
                    }
                }

                foreach (var (uid, _) in grids)
                {
                    var gridXform = xformQuery.GetComponent(uid);
                    xformSystem.SetWorldRotation(gridXform, Angle.Zero);
                }
            });

            await pair.RunTicksSync(10);
            await Task.WhenAll(client.WaitIdleAsync(), server.WaitIdleAsync());

            foreach (var (uid, grid) in grids)
            {
                // Skip empty grids
                if (grid.LocalAABB.IsEmpty())
                {
                    Console.WriteLine($"Warning: Grid {uid} was empty. Skipping image rendering.");
                    continue;
                }

                var tileXSize = grid.TileSize * TilePainter.TileImageSize;
                var tileYSize = grid.TileSize * TilePainter.TileImageSize;

                var bounds = grid.LocalAABB;

                var left = bounds.Left;
                var right = bounds.Right;
                var top = bounds.Top;
                var bottom = bounds.Bottom;

                var w = (int) Math.Ceiling(right - left) * tileXSize;
                var h = (int) Math.Ceiling(top - bottom) * tileYSize;

                // #Misfits Change - quadrant rendering for large maps
                const long maxSingleImageBytes = 800_000_000L;
                var imageBytes = (long) w * h * 4;
                Image<Rgba32> gridCanvas;

                if (imageBytes <= maxSingleImageBytes)
                {
                    // Standard single-pass rendering for maps that fit in memory.
                    gridCanvas = new Image<Rgba32>(w, h);

                    await server.WaitPost(() =>
                    {
                        tilePainter.Run(gridCanvas, uid, grid);
                        entityPainter.Run(gridCanvas, uid, grid);

                        gridCanvas.Mutate(e => e.Flip(FlipMode.Vertical));
                    });
                }
                else
                {
                    // Quadrant rendering: split grid into 2x2 sections, render each
                    // at full resolution (each under ~636 MB), then downscale and
                    // composite into the final image.
                    const int scaleDivisor = 4; // 32px/tile → 8px/tile
                    var finalW = Math.Max(1, w / scaleDivisor);
                    var finalH = Math.Max(1, h / scaleDivisor);

                    Console.WriteLine($"Image too large ({imageBytes / (1024 * 1024)} MB). " +
                                      $"Using 2x2 quadrant rendering, output {finalW}x{finalH} (1/{scaleDivisor} scale).");

                    var midX = (float) Math.Ceiling((left + right) / 2.0);
                    var midY = (float) Math.Ceiling((bottom + top) / 2.0);

                    var quadrants = new[]
                    {
                        new Box2(left, bottom, midX, midY),  // 0: bottom-left
                        new Box2(midX, bottom, right, midY), // 1: bottom-right
                        new Box2(left, midY, midX, top),     // 2: top-left
                        new Box2(midX, midY, right, top),    // 3: top-right
                    };

                    gridCanvas = new Image<Rgba32>(finalW, finalH);
                    var aabbProp = typeof(MapGridComponent)
                        .GetProperty(nameof(MapGridComponent.LocalAABB))!;

                    for (var qi = 0; qi < 4; qi++)
                    {
                        var qBounds = quadrants[qi];
                        var qW = (int) Math.Ceiling(qBounds.Width) * tileXSize;
                        var qH = (int) Math.Ceiling(qBounds.Height) * tileYSize;

                        if (qW <= 0 || qH <= 0)
                            continue;

                        Console.WriteLine($"  Quadrant {qi + 1}/4: {qW}x{qH} px " +
                                          $"({qW * (long) qH * 4 / (1024 * 1024)} MB)...");

                        // Set grid AABB to quadrant bounds for TilePainter offsets.
                        aabbProp.SetValue(grid, qBounds);

                        // Entity/decal positions were precomputed with the full AABB
                        // origin. Shift them to the quadrant's coordinate space.
                        var entityOffsetX = (int) ((left - qBounds.Left) * tileXSize);
                        var entityOffsetY = (int) ((bottom - qBounds.Bottom) * tileYSize);

                        using var qCanvas = new Image<Rgba32>(qW, qH);
                        await server.WaitPost(() =>
                        {
                            tilePainter.Run(qCanvas, uid, grid);
                            entityPainter.Run(qCanvas, uid, grid, entityOffsetX, entityOffsetY);
                        });

                        // Flip, downscale, and composite into the final image.
                        qCanvas.Mutate(e => e.Flip(FlipMode.Vertical));

                        var scaledW = Math.Max(1, qW / scaleDivisor);
                        var scaledH = Math.Max(1, qH / scaleDivisor);
                        qCanvas.Mutate(e => e.Resize(scaledW, scaledH));

                        // Position in final image (y=0 at top, matching visual orientation).
                        var fx = (int) ((qBounds.Left - left) * tileXSize) / scaleDivisor;
                        var fy = (int) ((top - qBounds.Top) * tileYSize) / scaleDivisor;

                        gridCanvas.Mutate(e => e.DrawImage(qCanvas, new Point(fx, fy), 1));
                        Console.WriteLine($"  Quadrant {qi + 1} composited at ({fx}, {fy}), {scaledW}x{scaledH}");
                    }

                    // Restore the full AABB.
                    aabbProp.SetValue(grid, bounds);
                }

                var renderedImage = new RenderedGridImage<Rgba32>(gridCanvas)
                {
                    GridUid = uid,
                    Offset = xformSystem.GetWorldPosition(uid),
                };

                yield return renderedImage;
            }

            // We don't care if it fails as we have already saved the images.
            try
            {
                await pair.CleanReturnAsync();
            }
            catch
            {
                // ignored
            }
        }
    }
}
