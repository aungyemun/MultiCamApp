using MultiCamApp.Capture;
using MultiCamApp.Recording;
using Xunit;

namespace MultiCamApp.Tests;

public sealed class MultiCameraRecordingCoordinatorTests
{
    [Theory]
    [InlineData(1, new[] { "a", null, null, null }, 1)]
    [InlineData(2, new[] { "a", "b", null, null }, 2)]
    [InlineData(3, new[] { "a", "b", "c", null }, 3)]
    [InlineData(4, new[] { "a", "b", "c", "d" }, 4)]
    public void GetLayoutSlots_ReturnsAllSelectedSlotsUpToLayout(
        int layoutCount, string?[] deviceIds, int expectedCount)
    {
        var pipelines = new[]
        {
            new CameraSlotPipeline(0),
            new CameraSlotPipeline(1),
            new CameraSlotPipeline(2),
            new CameraSlotPipeline(3)
        };

        var slots = MultiCameraRecordingCoordinator.GetLayoutSlots(
            pipelines, layoutCount, i => deviceIds[i]);

        Assert.Equal(expectedCount, slots.Count);
    }

    [Fact]
    public void GetLayoutSlots_SparseSelection_SkipsEmptySlots()
    {
        var pipelines = new[]
        {
            new CameraSlotPipeline(0),
            new CameraSlotPipeline(1),
            new CameraSlotPipeline(2),
            new CameraSlotPipeline(3)
        };
        string?[] ids = ["a", null, "c", null];

        var slots = MultiCameraRecordingCoordinator.GetLayoutSlots(pipelines, 3, i => ids[i]);

        Assert.Equal(2, slots.Count);
        Assert.Equal("cam1", slots[0].SlotName);
        Assert.Equal("cam3", slots[1].SlotName);
    }

    [Fact]
    public void GetLayoutSlots_ThreeCameraLayout_IncludesCam3()
    {
        var pipelines = new[]
        {
            new CameraSlotPipeline(0),
            new CameraSlotPipeline(1),
            new CameraSlotPipeline(2),
            new CameraSlotPipeline(3)
        };
        string?[] ids = ["dev1", "dev2", "dev3", null];

        var slots = MultiCameraRecordingCoordinator.GetLayoutSlots(pipelines, 3, i => ids[i]);

        Assert.Equal(3, slots.Count);
        Assert.Equal("cam3", slots[2].SlotName);
    }
}
