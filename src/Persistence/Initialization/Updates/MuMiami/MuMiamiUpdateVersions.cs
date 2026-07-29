// <copyright file="MuMiamiUpdateVersions.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.Updates.MuMiami;

/// <summary>
/// Version numbers for the Mu Miami configuration update plug-ins.
/// </summary>
/// <remarks>
/// FORK DISCIPLINE — read before adding one.
///
/// <para>
/// Upstream tracks update versions in the <see cref="UpdateVersion"/> enum, which is an
/// upstream file. Mu Miami does not edit upstream files, so these values are declared here
/// and cast to <see cref="UpdateVersion"/> at the single point where the interface demands
/// that type. <see cref="UpdatePlugInBase.Key"/> is <c>(int)Version</c> and the persisted
/// <c>ConfigurationUpdate.Version</c> column is an <c>int</c>, so a value outside the enum's
/// declared members round-trips correctly; C# enums are not closed sets.
/// </para>
///
/// <para>
/// The 9000 block is deliberately far above upstream's counter (99 at fork base,
/// <c>AddRenaItem</c>) so a rebase can never collide with it. If upstream ever reaches
/// 9000, move this block, not upstream's.
/// </para>
///
/// <para>
/// Values are permanent once applied to a database: the number is what
/// <see cref="DataUpdateService.DetermineAvailableUpdatesAsync"/> matches against the
/// installed <c>ConfigurationUpdate</c> rows. Never renumber, only append.
/// </para>
/// </remarks>
internal static class MuMiamiUpdateVersions
{
    /// <summary>
    /// The first version number reserved for Mu Miami. Everything at or above this value is
    /// fork-local; the acceptance queries in <c>scripts/verify-balance.sh</c> use it to tell
    /// Mu Miami updates apart from upstream's.
    /// </summary>
    internal const int MuMiamiRangeStart = 9000;

    /// <summary>The piecewise level to required-experience curve (brief 002).</summary>
    internal const int ExperienceCurve = 9001;

    /// <summary>Excellent and jewel drop chances (brief 002).</summary>
    internal const int DropRates = 9002;

    /// <summary>The ancient drop group, scoped to Kalima, Aida and Icarus (brief 002).</summary>
    internal const int AncientDrops = 9003;

    /// <summary>The three named farming clusters (brief 002).</summary>
    internal const int FarmingClusters = 9004;
}
