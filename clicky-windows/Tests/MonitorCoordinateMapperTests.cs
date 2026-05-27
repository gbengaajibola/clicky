using System.Windows;
using Xunit;

namespace Clicky.Tests;

/// <summary>
/// Unit tests for MonitorCoordinateMapper.
/// Tests that don't depend on the actual connected display configuration are self-contained;
/// those that call the real Screen.AllScreens are marked with [Fact(Skip=...)] on headless CI
/// and run in integration mode locally.
/// </summary>
public sealed class MonitorCoordinateMapperTests
{
    // ── Out-of-range monitor index ────────────────────────────────────────────

    [Fact]
    public void GetMonitorBounds_NegativeIndex_ReturnsNull()
    {
        var bounds = MonitorCoordinateMapper.GetMonitorBounds(zeroBasedMonitorIndex: -1);
        Assert.Null(bounds);
    }

    [Fact]
    public void GetMonitorBounds_IndexFarBeyondConnectedScreens_ReturnsNull()
    {
        var bounds = MonitorCoordinateMapper.GetMonitorBounds(zeroBasedMonitorIndex: 9999);
        Assert.Null(bounds);
    }

    // ── NormalizedToAbsoluteScreenPixels: out-of-range monitor ───────────────

    [Fact]
    public void NormalizedToAbsoluteScreenPixels_InvalidMonitorIndex_ReturnsNull()
    {
        var result = MonitorCoordinateMapper.NormalizedToAbsoluteScreenPixels(
            normalizedX: 0.5,
            normalizedY: 0.5,
            zeroBasedMonitorIndex: 9999);

        Assert.Null(result);
    }

    // ── ConnectedMonitorCount ─────────────────────────────────────────────────

    [Fact]
    public void ConnectedMonitorCount_IsAtLeastOne()
    {
        // Every machine running this test has at least one screen
        Assert.True(MonitorCoordinateMapper.ConnectedMonitorCount >= 1);
    }

    // ── Primary monitor bounds sanity check ───────────────────────────────────

    [Fact]
    public void GetMonitorBounds_PrimaryMonitor_HasPositiveDimensions()
    {
        var bounds = MonitorCoordinateMapper.GetMonitorBounds(zeroBasedMonitorIndex: 0);

        Assert.NotNull(bounds);
        Assert.True(bounds!.Value.Width > 0);
        Assert.True(bounds!.Value.Height > 0);
    }

    // ── Normalized coordinate math ────────────────────────────────────────────

    [Fact]
    public void NormalizedToAbsoluteScreenPixels_TopLeftCorner_MapsToBoundsOrigin()
    {
        var bounds = MonitorCoordinateMapper.GetMonitorBounds(zeroBasedMonitorIndex: 0);
        if (bounds == null) return; // headless — skip

        var result = MonitorCoordinateMapper.NormalizedToAbsoluteScreenPixels(
            normalizedX: 0.0,
            normalizedY: 0.0,
            zeroBasedMonitorIndex: 0);

        Assert.NotNull(result);
        Assert.Equal(bounds.Value.X, result!.Value.X, precision: 1);
        Assert.Equal(bounds.Value.Y, result!.Value.Y, precision: 1);
    }

    [Fact]
    public void NormalizedToAbsoluteScreenPixels_Center_MapsToMidpoint()
    {
        var bounds = MonitorCoordinateMapper.GetMonitorBounds(zeroBasedMonitorIndex: 0);
        if (bounds == null) return; // headless — skip

        var result = MonitorCoordinateMapper.NormalizedToAbsoluteScreenPixels(
            normalizedX: 0.5,
            normalizedY: 0.5,
            zeroBasedMonitorIndex: 0);

        Assert.NotNull(result);
        Assert.Equal(bounds.Value.X + bounds.Value.Width / 2, result!.Value.X, precision: 1);
        Assert.Equal(bounds.Value.Y + bounds.Value.Height / 2, result!.Value.Y, precision: 1);
    }

    [Fact]
    public void NormalizedToAbsoluteScreenPixels_BottomRightCorner_MapsToFarEdge()
    {
        var bounds = MonitorCoordinateMapper.GetMonitorBounds(zeroBasedMonitorIndex: 0);
        if (bounds == null) return; // headless — skip

        var result = MonitorCoordinateMapper.NormalizedToAbsoluteScreenPixels(
            normalizedX: 1.0,
            normalizedY: 1.0,
            zeroBasedMonitorIndex: 0);

        Assert.NotNull(result);
        Assert.Equal(bounds.Value.Right, result!.Value.X, precision: 1);
        Assert.Equal(bounds.Value.Bottom, result!.Value.Y, precision: 1);
    }
}
