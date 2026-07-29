// <copyright file="MuMiamiAncientDropsUpdatePlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.Updates.MuMiami;

using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Mu Miami: adds an ancient item drop group and attaches it to Kalima 1-7, Aida and Icarus
/// only — pilgrimage loot, so geography means something.
/// </summary>
/// <remarks>
/// Stock Season 6 has no ancient drop group on any field map at all;
/// <c>SpecialItemType.Ancient</c> appears in exactly one seeded place, the Chaos Castle
/// rewards. The generator supports it regardless: <c>DefaultDropGenerator.GenerateItemDrop</c>
/// routes <see cref="SpecialItemType.Ancient"/> to <c>GenerateRandomAncient</c>, which picks
/// from the items that carry an <c>AncientOption</c> set group. The group therefore needs no
/// <see cref="DropItemGroup.PossibleItems"/>.
///
/// <para>
/// RESTART REQUIRED. The ancient item pool is derived in <c>DefaultDropGenerator</c>'s
/// constructor from <c>ItemDefinition.DropsFromMonsters</c>, and the generator is built once
/// per <c>GameServer</c>. The group row appears live, but no ancient will actually drop until
/// the stack is restarted once after applying this.
/// </para>
///
/// <para>
/// "Only these maps" is enforced by attachment, not by a filter: the group is added to nine
/// <c>GameMapDefinition.DropItemGroups</c> collections and to no monster and no character.
/// <c>scripts/verify-balance.sh</c> asserts the count is exactly nine and lists them.
/// </para>
/// </remarks>
[PlugIn]
[Display(Name = PlugInName, Description = PlugInDescription)]
[Guid("F86DDA12-E0C9-478F-8453-4DC2E7056730")]
public class MuMiamiAncientDropsUpdatePlugIn : UpdatePlugInBase
{
    /// <summary>
    /// The plug in name.
    /// </summary>
    internal const string PlugInName = "Mu Miami: ancient drops in Kalima, Aida and Icarus";

    /// <summary>
    /// The plug in description.
    /// </summary>
    internal const string PlugInDescription = "Mu Miami balance: adds a 0.2 % ancient item drop group to Kalima 1-7, Aida and Icarus, and nowhere else. Requires one stack restart to take effect (the ancient item pool is captured in the drop generator's constructor).";

    /// <summary>
    /// The Mu Miami ancient drop chance. Low on purpose: with excellent items at 3 %, ancient
    /// sets and the Chaos Machine gamble are the only scarcity left in the game.
    /// </summary>
    internal const double AncientChance = 0.002;

    /// <summary>
    /// The GUID seed for the group. Chosen well clear of upstream's single-number drop group
    /// seeds (1-4) and of its map-scoped two-number seeds.
    /// </summary>
    private const short AncientGroupId = 900;

    /// <summary>
    /// The maps the ancient group is attached to, by <c>GameMapDefinition.Number</c>:
    /// Kalima 1-6 (24-29), Icarus (10), Aida (33), Kalima 7 (36).
    /// </summary>
    private static readonly short[] AncientMapNumbers = [10, 24, 25, 26, 27, 28, 29, 33, 36];

    /// <inheritdoc />
    public override string Name => PlugInName;

    /// <inheritdoc />
    public override string Description => PlugInDescription;

    /// <inheritdoc />
    public override UpdateVersion Version => (UpdateVersion)MuMiamiUpdateVersions.AncientDrops;

    /// <inheritdoc />
    public override string DataInitializationKey => VersionSeasonSix.DataInitialization.Id;

    /// <inheritdoc />
    public override bool IsMandatory => false;

    /// <inheritdoc />
    public override DateTime CreatedAt => new(2026, 07, 28, 12, 10, 0, DateTimeKind.Utc);

    /// <inheritdoc />
#pragma warning disable CS1998
    protected override async ValueTask ApplyAsync(IContext context, GameConfiguration gameConfiguration)
#pragma warning restore CS1998
    {
        var id = GuidHelper.CreateGuid<DropItemGroup>(AncientGroupId);
        var group = gameConfiguration.DropItemGroups.FirstOrDefault(g => g.GetId() == id);
        if (group is null)
        {
            group = context.CreateNew<DropItemGroup>();
            group.SetGuid(AncientGroupId);
            gameConfiguration.DropItemGroups.Add(group);
        }

        group.Description = "Mu Miami ancient items (Kalima, Aida, Icarus only)";
        group.ItemType = SpecialItemType.Ancient;
        group.Chance = AncientChance;

        // No level window and no monster: every monster on the nine maps can drop one. The
        // maps are already high-level content, so a window would only add a second place to
        // get the scoping wrong.
        group.MinimumMonsterLevel = null;
        group.MaximumMonsterLevel = null;
        group.Monster = null;

        foreach (var mapNumber in AncientMapNumbers)
        {
            var map = gameConfiguration.Maps.FirstOrDefault(m => m.Number == mapNumber && m.Discriminator == 0)
                      ?? throw new InvalidOperationException(
                          $"Map number {mapNumber} is missing from this configuration. Mu Miami's ancient drop update "
                          + "expects a stock Season 6 Episode 3 database.");

            if (!map.DropItemGroups.Any(g => g.GetId() == id))
            {
                map.DropItemGroups.Add(group);
            }
        }
    }
}
