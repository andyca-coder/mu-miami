// <copyright file="MuMiamiDropRatesUpdatePlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.Updates.MuMiami;

using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Mu Miami: raises the excellent item drop chance to 3 % and the jewel drop chance to 3x
/// stock, leaving the money and random-item groups exactly as upstream seeded them.
/// </summary>
/// <remarks>
/// Both groups are the global defaults created in
/// <c>GameConfigurationInitializerBase.AddItemDropGroups</c> and attached to every map by
/// <c>BaseMapInitializer.InitializeDropItemGroups</c> — the same object instance on every
/// map, so changing <see cref="DropItemGroup.Chance"/> here changes it everywhere at once.
///
/// <para>
/// Stock values, quoted from <c>GameConfigurationInitializerBase.cs</c>: excellent 0.0001,
/// jewels 0.001. Mu Miami: excellent 0.03, jewels 0.003 (exactly 3.0x).
/// </para>
///
/// <para>
/// RESTART REQUIRED for the excellent change to be fully visible.
/// <see cref="DropItemGroup.Chance"/> itself is re-read per kill, but
/// <c>GameConfiguration.ExcellentItemDropLevelDelta</c> and the droppable-item list are
/// captured in <c>DefaultDropGenerator</c>'s constructor. This plug-in does not change those,
/// so the chance takes effect live; the restart in the brief 002 verification flow is there
/// for the ancient group, which does depend on constructor-captured state.
/// </para>
///
/// <para>
/// Budget: this adds 0.0299 + 0.002 to every monster context. The measured worst context and
/// the arithmetic are in <c>docs/design/drop-worksheet.md</c>; re-measure with
/// <c>scripts/verify-balance.sh</c> after any drop-group change.
/// </para>
/// </remarks>
[PlugIn]
[Display(Name = PlugInName, Description = PlugInDescription)]
[Guid("1AD00771-66FC-4C81-9042-279FD6AC460A")]
public class MuMiamiDropRatesUpdatePlugIn : UpdatePlugInBase
{
    /// <summary>
    /// The plug in name.
    /// </summary>
    internal const string PlugInName = "Mu Miami: excellent and jewel drop rates";

    /// <summary>
    /// The plug in description.
    /// </summary>
    internal const string PlugInDescription = "Mu Miami balance: excellent items 0.0001 -> 0.03 (3 %), jewels 0.001 -> 0.003 (3x stock). Money and random-item groups untouched. Chaos Machine odds untouched.";

    /// <summary>
    /// The Mu Miami chance for the global excellent item drop group.
    /// </summary>
    internal const double ExcellentChance = 0.03;

    /// <summary>
    /// The Mu Miami chance for the global jewel drop group: exactly 3x the stock 0.001.
    /// </summary>
    internal const double JewelChance = 0.003;

    /// <summary>
    /// The GUID seed of the global excellent drop group, <c>SetGuid(3)</c> in
    /// <c>GameConfigurationInitializerBase.AddItemDropGroups</c>.
    /// </summary>
    private const short ExcellentGroupId = 3;

    /// <summary>
    /// The GUID seed of the global jewel drop group, <c>SetGuid(4)</c> in
    /// <c>GameConfigurationInitializerBase.AddItemDropGroups</c>. Also the id
    /// <c>InitializerBase.AddItemToJewelItemDrop</c> looks up, so every jewel added by any
    /// version initializer lands in this one group.
    /// </summary>
    private const short JewelGroupId = 4;

    /// <inheritdoc />
    public override string Name => PlugInName;

    /// <inheritdoc />
    public override string Description => PlugInDescription;

    /// <inheritdoc />
    public override UpdateVersion Version => (UpdateVersion)MuMiamiUpdateVersions.DropRates;

    /// <inheritdoc />
    public override string DataInitializationKey => VersionSeasonSix.DataInitialization.Id;

    /// <inheritdoc />
    public override bool IsMandatory => false;

    /// <inheritdoc />
    public override DateTime CreatedAt => new(2026, 07, 28, 12, 5, 0, DateTimeKind.Utc);

    /// <inheritdoc />
#pragma warning disable CS1998
    protected override async ValueTask ApplyAsync(IContext context, GameConfiguration gameConfiguration)
#pragma warning restore CS1998
    {
        // Refuse cleanly rather than silently doing half the job: if the seeded groups are
        // not where they should be, the database is not the stock Season 6 shape this update
        // was written against, and guessing which group to edit would be worse than stopping.
        SetChance(gameConfiguration, ExcellentGroupId, ExcellentChance, "excellent items");
        SetChance(gameConfiguration, JewelGroupId, JewelChance, "jewels");
    }

    private static void SetChance(GameConfiguration gameConfiguration, short groupId, double chance, string what)
    {
        var id = GuidHelper.CreateGuid<DropItemGroup>(groupId);
        var group = gameConfiguration.DropItemGroups.FirstOrDefault(g => g.GetId() == id)
                    ?? throw new InvalidOperationException(
                        $"The seeded drop item group for {what} (guid seed {groupId}) is missing from this configuration. "
                        + "Mu Miami's drop-rate update expects a stock Season 6 Episode 3 database.");

        // Idempotent: re-applying writes the same value.
        group.Chance = chance;
    }
}
