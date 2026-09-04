using Optimus.Core.Diagnostics;
using Optimus.Core.Optimization;
using Xunit;

namespace Optimus.Core.Tests.Optimization;

/// <summary>
/// Locks the estimator against the ONE end-to-end measurement that exists.
///
/// <para>
/// On 2026-07-25 the product ran for the first time on a real file (<c>kaneki.cdr</c>, Davi's machine)
/// and <b>promised 24,8% while delivering 14,3%</b>. That is the exact failure mode the product is built
/// to prevent (P1): over-promising is a lie, under-promising is only a disappointment. These tests exist
/// so nobody can quietly restore the optimistic model.
/// </para>
/// </summary>
public class EstimatorCalibrationTests
{
    // The file as measured. Composition read from the container, geometry share from the payload
    // analyzer (~73% of the file — NOT the 97,4% the ZIP "vector" entry reports, which also contains
    // the duplicated style JSON).
    private const long TotalBytes = 7_959_265;
    private const double GeometrySharePercent = 73.0;
    private const double RealReductionPercent = 14.3;

    private static CdrComposition KanekiComposition() =>
        CdrComposition.FromBytes(TotalBytes, new[]
        {
            (CdrComponent.Vector, (long)(TotalBytes * 0.974)),
            (CdrComponent.Preview, (long)(TotalBytes * 0.023)),
            (CdrComponent.EmbeddedFonts, (long)(TotalBytes * 0.002)),
            (CdrComponent.Metadata, (long)(TotalBytes * 0.001)),
        });

    private static SavingsEstimate EstimateForBalanced()
    {
        var settings = new OptimizationSettings
        {
            Color = ColorFidelity.High,
            Image = ImageQuality.Untouched,
            Weight = DrawingWeight.Balanced,
            Space = ColorSpaceTarget.Keep,
            RemovePreview = true,
            RemoveEmptyLayers = true,
            RemoveCompatibilityData = true,
            Reserialize = true,
        };

        return SavingsEstimator.Estimate(KanekiComposition(), settings, GeometrySharePercent);
    }

    /// <summary>
    /// THE regression: the estimate must no longer be nearly double the truth. A 24,8% promise against a
    /// 14,3% result is what made the operator distrust the number.
    /// </summary>
    [Fact]
    public void The_balanced_estimate_no_longer_promises_double_the_real_result()
    {
        double estimate = EstimateForBalanced().TotalPercent;

        Assert.True(estimate < 20.0,
            $"estimativa {estimate:0.#}% ainda muito acima do real medido ({RealReductionPercent}%)");
    }

    /// <summary>
    /// And it must not swing to useless pessimism: an estimate far below the truth trains the operator to
    /// ignore it, which is the same failure with the opposite sign.
    /// </summary>
    [Fact]
    public void The_balanced_estimate_is_not_uselessly_pessimistic()
    {
        double estimate = EstimateForBalanced().TotalPercent;

        Assert.True(estimate > 7.0,
            $"estimativa {estimate:0.#}% baixa demais para ser útil (real: {RealReductionPercent}%)");
    }

    /// <summary>
    /// Where it should land: at or just under the measured result. Under-promising is the side the
    /// product chooses deliberately.
    /// </summary>
    [Fact]
    public void The_balanced_estimate_lands_at_or_below_the_measured_result()
    {
        double estimate = EstimateForBalanced().TotalPercent;

        Assert.True(estimate <= RealReductionPercent + 1.5,
            $"estimativa {estimate:0.#}% acima do real medido ({RealReductionPercent}%) — "
          + "prometer a mais é exatamente o defeito que este teste existe para impedir");
    }

    /// <summary>
    /// The tight band. Pinned so the calibration cannot drift silently: the model is
    /// <c>total × geometryShare × nodeReduction × DeflateRecovery</c> plus the preview, and on this file
    /// that lands within a couple of points of the 14,3% actually measured.
    /// </summary>
    [Fact]
    public void The_balanced_estimate_lands_within_two_points_of_the_measurement()
    {
        double estimate = EstimateForBalanced().TotalPercent;

        Assert.True(System.Math.Abs(estimate - RealReductionPercent) <= 2.0,
            $"estimativa {estimate:0.0}% contra {RealReductionPercent}% medido — "
          + "a calibração saiu da faixa");
    }

    /// <summary>A more aggressive setting must still promise more than a conservative one.</summary>
    [Fact]
    public void More_aggressive_settings_still_promise_more()
    {
        CdrComposition c = KanekiComposition();

        double preserve = SavingsEstimator.Estimate(
            c, new OptimizationSettings { Weight = DrawingWeight.PreserveDetail }, GeometrySharePercent)
            .TotalPercent;

        double balanced = SavingsEstimator.Estimate(
            c, new OptimizationSettings { Weight = DrawingWeight.Balanced }, GeometrySharePercent)
            .TotalPercent;

        double max = SavingsEstimator.Estimate(
            c, new OptimizationSettings { Weight = DrawingWeight.MaxFluidity }, GeometrySharePercent)
            .TotalPercent;

        Assert.True(preserve < balanced, "detalhado deveria prometer menos que equilibrado");
        Assert.True(balanced < max, "equilibrado deveria prometer menos que máximo");
    }

    /// <summary>
    /// "Não mexer" must promise nothing from the drawing lever — the Safe preset's whole point is that it
    /// changes no art.
    /// </summary>
    [Fact]
    public void Leaving_the_drawing_alone_promises_nothing_from_it()
    {
        SavingsEstimate estimate = SavingsEstimator.Estimate(
            KanekiComposition(),
            new OptimizationSettings { Weight = DrawingWeight.Untouched },
            GeometrySharePercent);

        foreach (SavingItem item in estimate.Items)
            if (item.Label.Contains("desenho") || item.Label.Contains("Simplifica"))
            {
                Assert.Equal(0, item.Bytes);
                Assert.False(item.Lossy, "sem simplificação não há perda a declarar");
            }
    }

    /// <summary>The estimate can never exceed the file itself.</summary>
    [Fact]
    public void The_estimate_never_exceeds_the_file()
    {
        SavingsEstimate estimate = SavingsEstimator.Estimate(
            KanekiComposition(),
            new OptimizationSettings { Weight = DrawingWeight.MaxFluidity, RemovePreview = true },
            GeometrySharePercent);

        Assert.True(estimate.TotalBytes <= TotalBytes);
        Assert.True(estimate.TotalPercent <= 100);
    }
}
