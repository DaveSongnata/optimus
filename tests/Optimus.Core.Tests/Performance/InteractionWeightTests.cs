using Optimus.Core.Performance;
using Xunit;

namespace Optimus.Core.Tests.Performance;

/// <summary>
/// The fluidity ruler. It answers the client's REAL complaint — "the PC stalls when I drag inside
/// Corel" — which is a different problem from file size and has different causes (O8).
///
/// <para>
/// It is a MODEL, and the tests pin down the properties that must hold for it to be honest: it is
/// monotonic in every input, a live effect costs more than a plain shape (Corel recomputes effects
/// on every redraw), and it never pretends to be a millisecond figure. The real timing comes from
/// <c>Window.Refresh</c> measured before and after, in Phase 4.
/// </para>
/// </summary>
public class InteractionWeightTests
{
    [Fact]
    public void Empty_document_weighs_nothing()
    {
        Assert.Equal(0, InteractionWeight.Compute(new InteractionInputs()));
    }

    [Fact]
    public void More_nodes_weigh_more()
    {
        double light = InteractionWeight.Compute(new InteractionInputs { Nodes = 1_000 });
        double heavy = InteractionWeight.Compute(new InteractionInputs { Nodes = 400_000 });
        Assert.True(heavy > light);
    }

    [Fact]
    public void More_objects_weigh_more()
    {
        double a = InteractionWeight.Compute(new InteractionInputs { Objects = 100 });
        double b = InteractionWeight.Compute(new InteractionInputs { Objects = 17_000 });
        Assert.True(b > a);
    }

    /// <summary>
    /// A live effect is regenerated on every frame of a drag, so it must outweigh an ordinary shape
    /// by a wide margin — otherwise the score would tell the operator to chase nodes when three
    /// contour groups are the actual cause.
    /// </summary>
    [Fact]
    public void One_live_effect_outweighs_one_plain_object_by_far()
    {
        double effect = InteractionWeight.Compute(new InteractionInputs { LiveEffects = 1 });
        double plain = InteractionWeight.Compute(new InteractionInputs { Objects = 1 });
        Assert.True(effect > plain * 50, $"effect={effect} plain={plain}");
    }

    [Fact]
    public void Deeper_nesting_weighs_more()
    {
        double shallow = InteractionWeight.Compute(new InteractionInputs { Objects = 100, NestedObjects = 0 });
        double deep = InteractionWeight.Compute(new InteractionInputs { Objects = 100, NestedObjects = 100 });
        Assert.True(deep > shallow);
    }

    [Fact]
    public void Transparencies_weigh_more()
    {
        double none = InteractionWeight.Compute(new InteractionInputs { Objects = 10 });
        double some = InteractionWeight.Compute(new InteractionInputs { Objects = 10, Transparencies = 10 });
        Assert.True(some > none);
    }

    /// <summary>The real client document, so the band the score lands in is a documented fact.</summary>
    [Fact]
    public void Real_client_document_scores_as_heavy()
    {
        var kaneki = new InteractionInputs
        {
            Nodes = 421_057,
            Objects = 16_899,
            NestedObjects = 16_893,
            LiveEffects = 3,
        };

        double score = InteractionWeight.Compute(kaneki);
        Assert.Equal(InteractionBand.Heavy, InteractionWeight.Classify(score));
    }

    [Fact]
    public void A_trivial_document_scores_as_light()
    {
        var simple = new InteractionInputs { Nodes = 400, Objects = 20 };
        Assert.Equal(InteractionBand.Light, InteractionWeight.Classify(InteractionWeight.Compute(simple)));
    }

    /// <summary>
    /// Bands must be ordered and cover the whole range — a score can never fall outside them, or the
    /// UI would have nothing to say.
    /// </summary>
    [Theory]
    [InlineData(0, InteractionBand.Light)]
    [InlineData(-1, InteractionBand.Light)]
    [InlineData(double.MaxValue, InteractionBand.Heavy)]
    public void Classification_covers_the_whole_range(double score, InteractionBand expected)
    {
        Assert.Equal(expected, InteractionWeight.Classify(score));
    }

    /// <summary>
    /// The dominant contributor drives the advice. On a document with a handful of heavy effects the
    /// answer must be "flatten the effects", not "reduce nodes".
    /// </summary>
    [Fact]
    public void Reports_the_dominant_contributor()
    {
        var nodeBound = new InteractionInputs { Nodes = 400_000, Objects = 100 };
        Assert.Equal(InteractionFactor.Nodes, InteractionWeight.Dominant(nodeBound));

        var effectBound = new InteractionInputs { Nodes = 500, Objects = 50, LiveEffects = 400 };
        Assert.Equal(InteractionFactor.LiveEffects, InteractionWeight.Dominant(effectBound));

        var objectBound = new InteractionInputs { Nodes = 100, Objects = 50_000 };
        Assert.Equal(InteractionFactor.Objects, InteractionWeight.Dominant(objectBound));
    }
}
