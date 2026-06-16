using MTGProxyBuilder.Core.Models;

namespace MTGProxyBuilder.Tests.Models;

public class CalibrationTransformTests
{
    private const float MmToPt = 72f / 25.4f;

    [Fact]
    public void ZeroOffsets_ProduceIdentityTransform()
    {
        var profile = new PrinterProfile();
        var transform = CalibrationTransform.Compute(profile, 189f, 264f);

        Assert.Equal(0f, transform.TranslateXPt);
        Assert.Equal(0f, transform.TranslateYPt);
        Assert.Equal(0f, transform.RotationDegrees);
    }

    [Fact]
    public void UniformOffset_ProducesTranslationOnly()
    {
        var profile = new PrinterProfile
        {
            OffsetTLXMm = 1f, OffsetTLYMm = 0.5f,
            OffsetTRXMm = 1f, OffsetTRYMm = 0.5f,
            OffsetBLXMm = 1f, OffsetBLYMm = 0.5f,
            OffsetBRXMm = 1f, OffsetBRYMm = 0.5f,
        };

        var transform = CalibrationTransform.Compute(profile, 189f, 264f);

        Assert.Equal(1f * MmToPt, transform.TranslateXPt, 0.001f);
        Assert.Equal(0.5f * MmToPt, transform.TranslateYPt, 0.001f);
        Assert.Equal(0f, transform.RotationDegrees, 0.0001f);
    }

    [Fact]
    public void AsymmetricOffset_ProducesNonZeroRotation()
    {
        // Right side shifted down relative to left side -> positive rotation
        var profile = new PrinterProfile
        {
            OffsetTLXMm = 0f, OffsetTLYMm = 0f,
            OffsetTRXMm = 0f, OffsetTRYMm = 1f,
            OffsetBLXMm = 0f, OffsetBLYMm = 0f,
            OffsetBRXMm = 0f, OffsetBRYMm = 1f,
        };

        var transform = CalibrationTransform.Compute(profile, 189f, 264f);

        Assert.NotEqual(0f, transform.RotationDegrees);
    }

    [Fact]
    public void HasCorrection_FalseForIdentity()
    {
        var transform = new CalibrationTransform(0f, 0f, 0f);
        Assert.False(transform.HasCorrection);
    }

    [Fact]
    public void HasCorrection_TrueForTranslation()
    {
        var transform = new CalibrationTransform(1f, 0f, 0f);
        Assert.True(transform.HasCorrection);
    }

    [Fact]
    public void HasCorrection_TrueForRotation()
    {
        var transform = new CalibrationTransform(0f, 0f, 0.01f);
        Assert.True(transform.HasCorrection);
    }

    [Fact]
    public void LegacyMigration_CopiesValuesToAllCorners()
    {
        var profile = new PrinterProfile
        {
            OffsetXMm = 1.5f,
            OffsetYMm = -0.75f,
        };

        profile.MigrateLegacyOffsets();

        Assert.Equal(1.5f, profile.OffsetTLXMm);
        Assert.Equal(-0.75f, profile.OffsetTLYMm);
        Assert.Equal(1.5f, profile.OffsetTRXMm);
        Assert.Equal(-0.75f, profile.OffsetTRYMm);
        Assert.Equal(1.5f, profile.OffsetBLXMm);
        Assert.Equal(-0.75f, profile.OffsetBLYMm);
        Assert.Equal(1.5f, profile.OffsetBRXMm);
        Assert.Equal(-0.75f, profile.OffsetBRYMm);
    }

    [Fact]
    public void LegacyMigration_DoesNotOverwriteExistingCorners()
    {
        var profile = new PrinterProfile
        {
            OffsetXMm = 1.5f,
            OffsetYMm = -0.75f,
            OffsetTLXMm = 0.5f, // At least one corner is non-zero
        };

        profile.MigrateLegacyOffsets();

        // Corner values should remain unchanged
        Assert.Equal(0.5f, profile.OffsetTLXMm);
        Assert.Equal(0f, profile.OffsetTLYMm);
        Assert.Equal(0f, profile.OffsetTRXMm);
    }
}
