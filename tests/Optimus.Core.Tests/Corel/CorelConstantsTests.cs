using Optimus.Core.Corel;
using Xunit;

namespace Optimus.Core.Tests.Corel;

/// <summary>
/// Guards the VGCore constants against the historic "guessed enum" bug.
///
/// Every value asserted here was read from the real typelib dump at
/// <c>docs/vgcore-tlb-dump.txt</c> (VGCore 25.2). If one of these tests fails, the constant was
/// changed WITHOUT checking the dump — re-read the dump, do not "fix" the test.
/// </summary>
public class CorelConstantsTests
{
    /// <summary>
    /// THE regression test for the 10x bug. `cdrMillimeter = 3`; `4` is `cdrCentimeter`.
    /// Setting the document unit to 4 makes every measurement come back 10x off, which silently
    /// turned Optimus v1.0's "0.03 mm" tolerance into 0.3 mm. Dump: `ENUM cdrUnit`.
    /// </summary>
    [Fact]
    public void CdrMillimeter_is_3_not_4()
    {
        Assert.Equal(3, CorelConstants.CdrMillimeter);
        Assert.Equal(4, CorelConstants.CdrCentimeter);
        Assert.NotEqual(CorelConstants.CdrMillimeter, CorelConstants.CdrCentimeter);
    }

    /// <summary>Shape types used to classify the document. Dump: `ENUM cdrShapeType`.</summary>
    [Theory]
    [InlineData(3, nameof(CorelConstants.CdrCurveShape))]
    [InlineData(5, nameof(CorelConstants.CdrBitmapShape))]
    [InlineData(6, nameof(CorelConstants.CdrTextShape))]
    [InlineData(7, nameof(CorelConstants.CdrGroupShape))]
    public void Shape_type_values_match_the_typelib(int expected, string name)
    {
        int actual = name switch
        {
            nameof(CorelConstants.CdrCurveShape) => CorelConstants.CdrCurveShape,
            nameof(CorelConstants.CdrBitmapShape) => CorelConstants.CdrBitmapShape,
            nameof(CorelConstants.CdrTextShape) => CorelConstants.CdrTextShape,
            nameof(CorelConstants.CdrGroupShape) => CorelConstants.CdrGroupShape,
            _ => -1,
        };
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// `cdrCurveShape` is 3, NOT 5 — a comment in Optimus v1.0 claimed 5 (which is a bitmap).
    /// Keeping this explicit because the two are easy to swap and the failure is silent.
    /// </summary>
    [Fact]
    public void CurveShape_is_not_confused_with_BitmapShape()
    {
        Assert.Equal(3, CorelConstants.CdrCurveShape);
        Assert.Equal(5, CorelConstants.CdrBitmapShape);
    }

    /// <summary>Save-time levers used by the lossless phase. Dump: `ENUM cdrThumbnailSize`.</summary>
    [Fact]
    public void CdrNoThumbnail_is_0()
    {
        Assert.Equal(0, CorelConstants.CdrNoThumbnail);
    }
}
