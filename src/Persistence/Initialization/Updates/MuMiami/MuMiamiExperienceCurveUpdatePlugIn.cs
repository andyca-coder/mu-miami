// <copyright file="MuMiamiExperienceCurveUpdatePlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.Updates.MuMiami;

using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Mu Miami: replaces the level to required-experience curve with a four-phase piecewise
/// expression, so a solo character reaches level 400 in roughly 35 hours instead of 627.
/// </summary>
/// <remarks>
/// The mechanism is the expression string only. <c>ExperienceRate</c>,
/// <c>MasterExperienceRate</c>, per-server rates and per-map <c>ExpMultiplier</c> all stay at
/// 1.0 — see <c>docs/design/balance-canon.md</c>, "The curve". That keeps the whole curve as
/// one live-editable configuration value with per-band control, and leaves the multipliers
/// free for the brief 003 hot zone.
///
/// <para>
/// The derivation, the simulator output it was calibrated against, and the arithmetic behind
/// every constant below are in <c>docs/design/curve-worksheet.md</c>.
/// </para>
/// </remarks>
[PlugIn]
[Display(Name = PlugInName, Description = PlugInDescription)]
[Guid("C0604EEE-A67E-4E55-A46E-6D9A616AE109")]
public class MuMiamiExperienceCurveUpdatePlugIn : UpdatePlugInBase
{
    /// <summary>
    /// The plug in name.
    /// </summary>
    internal const string PlugInName = "Mu Miami: experience curve";

    /// <summary>
    /// The plug in description.
    /// </summary>
    internal const string PlugInDescription = "Mu Miami balance: four-phase piecewise level curve, ~35 hours to level 400. Ignition 1-150 ~4h, the climb 151-300 ~10h, the grind 301-380 ~13h, the summit 381-400 ~8h.";

    /// <summary>
    /// The Mu Miami level to required-experience curve.
    /// </summary>
    /// <remarks>
    /// Shape: <c>V(level) = (level + 8) * (level - 1)^2</c>, the same cubic the stock curve
    /// uses, with a different multiplier per phase and a constant per branch that makes the
    /// whole thing continuous at the seams. Stock uses a flat multiplier of 10 throughout;
    /// Mu Miami uses 3.2 / 1.17 / 1.64 / 3.04.
    ///
    /// <para>
    /// Branch boundaries are 151 / 301 / 381 rather than 150 / 300 / 380 because the cost of
    /// "level 150 to 151" is <c>F(151) - F(150)</c>, and that step belongs to the Ignition
    /// phase. Off-by-one here would leak one level's cost into the next phase's budget.
    /// </para>
    ///
    /// <para>
    /// Multipliers are written as integer fractions (<c>16 * X / 5</c>, not <c>3.2 * X</c>)
    /// so nothing in the string depends on how a decimal separator is parsed. mXparser is
    /// culture-invariant, but the server also runs with
    /// <c>DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false</c> and this costs nothing.
    /// </para>
    ///
    /// <para>
    /// The seam constants are the exact cumulative values:
    /// <c>F(151) = 3.2 * V(151) = 3.2 * 3577500 = 11448000</c>;
    /// <c>F(301) = F(151) + 1.17 * (V(301) - V(151)) = 39800025</c>;
    /// <c>F(381) = F(301) + 1.64 * (V(381) - V(301)) = 86313049</c>.
    /// Total to level 400: 113,011,569.
    /// </para>
    ///
    /// <para>
    /// This constant is the single source of truth: <c>scripts/simulate-progression.ts</c>
    /// parses it out of this file, so the simulator always scores the string that actually
    /// ships. Do not reformat it into a multi-line concatenation without updating that regex.
    /// </para>
    /// </remarks>
    internal const string ExperienceFormula = "if(level == 0, 0, if(level <= 151, 16 * (level + 8) * (level - 1) * (level - 1) / 5, if(level <= 301, 11448000 + 117 * ((level + 8) * (level - 1) * (level - 1) - 3577500) / 100, if(level <= 381, 39800025 + 41 * ((level + 8) * (level - 1) * (level - 1) - 27810000) / 25, 86313049 + 76 * ((level + 8) * (level - 1) * (level - 1) - 56171600) / 25))))";

    /// <inheritdoc />
    public override string Name => PlugInName;

    /// <inheritdoc />
    public override string Description => PlugInDescription;

    /// <inheritdoc />
    public override UpdateVersion Version => (UpdateVersion)MuMiamiUpdateVersions.ExperienceCurve;

    /// <inheritdoc />
    public override string DataInitializationKey => VersionSeasonSix.DataInitialization.Id;

    /// <inheritdoc />
    public override bool IsMandatory => false;

    /// <inheritdoc />
    public override DateTime CreatedAt => new(2026, 07, 28, 12, 0, 0, DateTimeKind.Utc);

    /// <inheritdoc />
#pragma warning disable CS1998
    protected override async ValueTask ApplyAsync(IContext context, GameConfiguration gameConfiguration)
#pragma warning restore CS1998
    {
        // Idempotent by construction: assigning the same string twice is a no-op, and
        // GameContext.OnGameConfigurationChangeAsync rebuilds the experience table either way.
        gameConfiguration.ExperienceFormula = ExperienceFormula;

        // MasterExperienceFormula is deliberately untouched. Canon calls 400+ a "slow burn,
        // near-vanilla pace", and the master tree is not this brief's fight.
    }
}
