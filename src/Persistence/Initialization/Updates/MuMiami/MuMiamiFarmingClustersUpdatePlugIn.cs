// <copyright file="MuMiamiFarmingClustersUpdatePlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.Updates.MuMiami;

using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Mu Miami: adds three dense spawn clusters at fixed, learnable coordinates — the classic
/// "everyone knows the spot" tradition.
/// </summary>
/// <remarks>
/// One cluster per level band, using existing top-of-band monsters (no new monster
/// definitions — those are blocked on the client model-id question and belong to brief 003):
///
/// <list type="bullet">
/// <item>Dungeon, "Brickell After Dark" — Dark Knight and Gorgon, the low band.</item>
/// <item>Tarkan, "Calle Ocho" — Death Beam Knight and Beam Knight, the flagship mid spot.
/// Stock Tarkan has exactly one Death Beam Knight on the entire map; this is the change a
/// player will notice first.</item>
/// <item>Kanturu Ruins, "Wynwood" — the Season 6 Warrior variants, the high band.</item>
/// </list>
///
/// <para>
/// Coordinates are first-pass, chosen from map data rather than by eye: each rectangle sits
/// on cells the stock spawn list already proves are walkable, and each was checked against
/// the map's terrain (<c>GameMapDefinition.TerrainData</c>, walkable and non-safezone) — 95 %
/// walkable in Dungeon, 89 % in Tarkan, 57 % in Kanturu Ruins. Tune them visually later in
/// the admin panel map editor; <c>docs/design/tuning-loop.md</c> describes how to freeze the
/// result back into this file.
/// </para>
///
/// <para>
/// Quantities are capped per the brief at ~2x the densest stock spawn concentration on that
/// map, measured as spawns per 20x20 cell window: Dungeon 18, Tarkan 8, Kanturu Ruins 9.
/// The chosen clusters land at 1.18x, 1.49x and 1.40x of those observed maxima — dense enough
/// to be a destination, well inside the cap, and far below anything that would trouble the
/// map thread. The per-cluster arithmetic is in <c>docs/design/cluster-worksheet.md</c>.
/// </para>
///
/// <para>
/// Live, no restart: <c>MapInitializer</c> registers for new <c>MonsterSpawnArea</c> rows and
/// spawns <c>Quantity</c> instances as soon as the row exists.
/// </para>
/// </remarks>
[PlugIn]
[Display(Name = PlugInName, Description = PlugInDescription)]
[Guid("68985CF5-9CB3-4A7D-BF2A-F3CE3CD025E5")]
public class MuMiamiFarmingClustersUpdatePlugIn : UpdatePlugInBase
{
    /// <summary>
    /// The plug in name.
    /// </summary>
    internal const string PlugInName = "Mu Miami: farming clusters";

    /// <summary>
    /// The plug in description.
    /// </summary>
    internal const string PlugInDescription = "Mu Miami balance: three dense spawn clusters at fixed coordinates - Dungeon (Brickell After Dark), Tarkan (Calle Ocho) and Kanturu Ruins (Wynwood).";

    /// <summary>
    /// The first spawn-area number reserved for Mu Miami clusters. Spawn numbers only have to
    /// be unique within a map (<c>MonsterSpawnArea.SetGuid(mapNumber, number)</c>); the
    /// highest stock number on any of the three maps is 962, so the 9000 block cannot collide,
    /// including after a rebase that adds stock spawns.
    /// </summary>
    private const short SpawnNumberBase = 9000;

    private static readonly ClusterSpawn[] Clusters =
    [

        // Dungeon (map 1) — "Brickell After Dark". x 5-30, y 101-126: 676 cells, 645 of them
        // walkable and outside the safezone. Stock Dark Knight spawns at (14,107) and (19,120)
        // and a Gorgon at (8,123) are inside the box, which is how it was picked.
        new(1, 1, 5, 30, 101, 126, 10, 24),   // Dark Knight, level 48
        new(1, 2, 5, 30, 101, 126, 18, 12),   // Gorgon, level 55

        // Tarkan (map 8) — "Calle Ocho". x 150-175, y 195-225: 806 cells, 722 walkable.
        // Contains the map's single stock Death Beam Knight spawn at (161,225) and a cluster
        // of stock Beam Knights around (157,214) / (161,218) / (167,225).
        new(8, 1, 150, 175, 195, 225, 63, 18),  // Death Beam Knight, level 93
        new(8, 2, 150, 175, 195, 225, 61, 6),   // Beam Knight, level 84

        // Kanturu Ruins (map 37) — "Wynwood". x 120-150, y 90-130: 1271 cells, 736 walkable
        // (the map is heavily built-up, hence the lower ratio). Contains stock spawns of all
        // three Warrior variants.
        new(37, 1, 120, 150, 90, 130, 556, 18), // Genocider Warrior, level 129
        new(37, 2, 120, 150, 90, 130, 555, 12), // Gigantis Warrior, level 128
        new(37, 3, 120, 150, 90, 130, 554, 10), // Kentauros Warrior, level 126
    ];

    /// <inheritdoc />
    public override string Name => PlugInName;

    /// <inheritdoc />
    public override string Description => PlugInDescription;

    /// <inheritdoc />
    public override UpdateVersion Version => (UpdateVersion)MuMiamiUpdateVersions.FarmingClusters;

    /// <inheritdoc />
    public override string DataInitializationKey => VersionSeasonSix.DataInitialization.Id;

    /// <inheritdoc />
    public override bool IsMandatory => false;

    /// <inheritdoc />
    public override DateTime CreatedAt => new(2026, 07, 28, 12, 15, 0, DateTimeKind.Utc);

    /// <inheritdoc />
#pragma warning disable CS1998
    protected override async ValueTask ApplyAsync(IContext context, GameConfiguration gameConfiguration)
#pragma warning restore CS1998
    {
        foreach (var cluster in Clusters)
        {
            var map = gameConfiguration.Maps.FirstOrDefault(m => m.Number == cluster.MapNumber && m.Discriminator == 0)
                      ?? throw new InvalidOperationException(
                          $"Map number {cluster.MapNumber} is missing from this configuration. Mu Miami's farming "
                          + "cluster update expects a stock Season 6 Episode 3 database.");

            var monster = gameConfiguration.Monsters.FirstOrDefault(m => m.Number == cluster.MonsterNumber)
                          ?? throw new InvalidOperationException(
                              $"Monster number {cluster.MonsterNumber} is missing from this configuration. Mu Miami's "
                              + "farming cluster update expects a stock Season 6 Episode 3 database.");

            var spawnNumber = (short)(SpawnNumberBase + cluster.SpawnIndex);
            var id = GuidHelper.CreateGuid<MonsterSpawnArea>(cluster.MapNumber, spawnNumber);

            var area = map.MonsterSpawns.FirstOrDefault(s => s.GetId() == id);
            if (area is null)
            {
                area = context.CreateNew<MonsterSpawnArea>();
                area.SetGuid(cluster.MapNumber, spawnNumber);
                map.MonsterSpawns.Add(area);
            }

            // Re-applying rewrites the same values, so a second visit to /config-updates is a
            // no-op rather than a second cluster on top of the first.
            area.GameMap = map;
            area.MonsterDefinition = monster;
            area.X1 = cluster.X1;
            area.X2 = cluster.X2;
            area.Y1 = cluster.Y1;
            area.Y2 = cluster.Y2;
            area.Quantity = cluster.Quantity;
            area.Direction = Direction.Undefined;
            area.SpawnTrigger = SpawnTrigger.Automatic;
            area.WaveNumber = 0;
        }
    }

    /// <summary>
    /// One spawn area of one cluster.
    /// </summary>
    /// <param name="MapNumber">The <c>GameMapDefinition.Number</c> of the map.</param>
    /// <param name="SpawnIndex">The index within the map's Mu Miami spawn block; combined with
    /// <see cref="SpawnNumberBase"/> it forms the spawn number, which must be unique per map.</param>
    /// <param name="X1">The left edge of the rectangle, inclusive.</param>
    /// <param name="X2">The right edge of the rectangle, inclusive.</param>
    /// <param name="Y1">The top edge of the rectangle, inclusive.</param>
    /// <param name="Y2">The bottom edge of the rectangle, inclusive.</param>
    /// <param name="MonsterNumber">The <c>MonsterDefinition.Number</c> to spawn.</param>
    /// <param name="Quantity">How many instances to materialise inside the rectangle.</param>
    private sealed record ClusterSpawn(
        short MapNumber,
        short SpawnIndex,
        byte X1,
        byte X2,
        byte Y1,
        byte Y2,
        short MonsterNumber,
        short Quantity);
}
