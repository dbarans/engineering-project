using NUnit.Framework;

/// <summary>
/// The metrics are what make "this parameter change improved the dungeons" a checkable
/// claim, so they have to agree with the layout they describe.
/// </summary>
public class DungeonMetricsTests
{
    private static LayoutParams SmallParams()
    {
        var parameters = LayoutParams.Default;
        parameters.MapWidth = 64;
        parameters.MapHeight = 48;
        parameters.TargetRoomCount = 8;
        parameters.MinRoomCount = 6;
        parameters.MinRoomSize = 5;
        parameters.MaxRoomSize = 9;
        return parameters;
    }

    [Test]
    public void MetricsMatchTheLayoutTheyDescribe()
    {
        DungeonLayout layout = new RoomCorridorGenerator().Generate("metrics", SmallParams());
        DungeonMetrics metrics = DungeonMetrics.Measure(layout);

        Assert.AreEqual(layout.Rooms.Count, metrics.Rooms);
        Assert.AreEqual(layout.Links.Count, metrics.Corridors);
        Assert.AreEqual(layout.CountWalkable(), metrics.WalkableCells);
        Assert.AreEqual(layout.Width * layout.Height, metrics.TotalCells);
    }

    [Test]
    public void ValidLayoutsLeaveNoRoomUnreachable()
    {
        DungeonMetrics metrics = DungeonMetrics.Measure(
            new RoomCorridorGenerator().Generate("reachable", SmallParams()));

        Assert.AreEqual(0, metrics.UnreachableRooms);
        Assert.Greater(metrics.MaxDepth, 0, "every room sits at the entrance");
    }

    [Test]
    public void LoopCountIsZeroForATree()
    {
        var parameters = SmallParams();
        parameters.ExtraLoopChance = 0f;

        DungeonMetrics metrics = DungeonMetrics.Measure(
            new RoomCorridorGenerator().Generate("tree", parameters));

        Assert.AreEqual(0, metrics.Loops);
        Assert.AreEqual(0f, metrics.LoopRatio);
    }

    [Test]
    public void LoopCountRisesWithLoopChance()
    {
        var tree = SmallParams();
        tree.ExtraLoopChance = 0f;
        var looped = SmallParams();
        looped.ExtraLoopChance = 1f;

        int treeLoops = DungeonMetrics.Measure(
            new RoomCorridorGenerator().Generate("compare", tree)).Loops;
        int loopedLoops = DungeonMetrics.Measure(
            new RoomCorridorGenerator().Generate("compare", looped)).Loops;

        Assert.Greater(loopedLoops, treeLoops);
    }

    [Test]
    public void OpenRatioIsAFraction()
    {
        DungeonMetrics metrics = DungeonMetrics.Measure(
            new RoomCorridorGenerator().Generate("ratio", SmallParams()));

        Assert.Greater(metrics.OpenRatio, 0f);
        Assert.Less(metrics.OpenRatio, 1f, "the whole map is open — the border should be solid");
    }

    [Test]
    public void MeasuringNullIsSafe()
    {
        DungeonMetrics metrics = DungeonMetrics.Measure(null);

        Assert.AreEqual(0, metrics.Rooms);
        Assert.AreEqual(0f, metrics.OpenRatio);
    }
}
