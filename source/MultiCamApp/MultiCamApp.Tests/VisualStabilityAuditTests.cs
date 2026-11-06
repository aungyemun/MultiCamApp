using MultiCamApp.Verification;
using Xunit;

namespace MultiCamApp.Tests;

/// <summary>Calculations behind the visual stability audit, on synthetic video with known answers.</summary>
public class VisualStabilityAuditTests
{
    private const int Rows = 180;

    private static (double[,] prof, double[] g, double[] t) Synth(int n, double fps, double bandAmp, int k, double driftHz,
        int flashFrame = -1, double flashGain = 1.0)
    {
        var prof = new double[n, Rows]; var g = new double[n]; var t = new double[n];
        for (var i = 0; i < n; i++)
        {
            t[i] = i / fps;
            var gain = i == flashFrame ? flashGain : 1.0;
            double sum = 0;
            for (var y = 0; y < Rows; y++)
            {
                var v = 30000 * gain * (1 + bandAmp * Math.Cos(2 * Math.PI * (k * y / (double)Rows + driftHz * t[i])));
                prof[i, y] = v; sum += v;
            }
            g[i] = sum / Rows;
        }
        return (prof, g, t);
    }

    [Fact]
    public void DriftingBand_StrengthFrequencyAndDirectionRecovered()
    {
        var (p, g, t) = Synth(600, 30, 0.01, 4, 0.5);
        var r = new VisualStabilityResult();
        VisualStabilityAuditService.Analyse(p, g, t, 600, r);
        Assert.InRange(r.BandStrengthPct, 0.95, 1.05);
        Assert.Equal(4, r.BandsPerFrame);
        Assert.InRange(r.BandDriftHz, 0.45, 0.55);
        Assert.Equal("up", r.BandDirection);     // constant phase: y decreases as t increases
        Assert.Equal(0, r.FlashFrames);
    }

    [Fact]
    public void CleanVideo_NoBandsNoFlash()
    {
        var (p, g, t) = Synth(300, 30, 0.0, 4, 0.5);
        var r = new VisualStabilityResult { };
        VisualStabilityAuditService.Analyse(p, g, t, 300, r);
        Assert.True(r.BandStrengthPct < 0.01);
        Assert.Equal(0, r.FlashFrames);
    }

    [Fact]
    public void SingleBrightFrame_CountedAsFlash()
    {
        var (p, g, t) = Synth(300, 30, 0.0, 4, 0.5, flashFrame: 150, flashGain: 1.10);
        var r = new VisualStabilityResult();
        VisualStabilityAuditService.Analyse(p, g, t, 300, r);
        Assert.Equal(1, r.FlashFrames);
        Assert.InRange(r.LumaRangePct, 9.5, 10.5);
    }

    [Theory]
    [InlineData(0.10, "PASS")]
    [InlineData(0.50, "WARNING_BANDS_FAINT")]
    [InlineData(2.10, "WARNING_BANDS_VISIBLE")]
    public void Verdict_UsesVisibilityThresholds(double strength, string verdict)
        => Assert.Equal(verdict, new VisualStabilityResult { Success = true, BandStrengthPct = strength }.Verdict);
}
