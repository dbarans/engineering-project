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

    /// <summary>
    /// A tree has no way round anywhere: every corridor is the only way through, so no room
    /// has a second route to it.
    /// </summary>
    [Test]
    public void NoRoomHasAWayRoundInATree()
    {
        var parameters = SmallParams();
        parameters.ExtraLoopChance = 0f;

        DungeonMetrics metrics = DungeonMetrics.Measure(new RoomCorridorGenerator().Generate("tree-routes", parameters));

        Assert.AreEqual(0, metrics.RoomsWithTwoRoutes);
        Assert.AreEqual(0f, metrics.TwoRouteRatio);
    }

    /// <summary>
    /// The number the loop settings are tuned against: with both of them wide open, most of
    /// the map has to be reachable by more than one route. Averaged over seeds, because how
    /// many loops a single map can carry depends on where its rooms landed.
    /// </summary>
    [Test]
    public void MostRoomsHaveAWayRoundWhenLoopsAreWideOpen()
    {
        var parameters = SmallParams();
        parameters.ExtraLoopChance = 1f;
        parameters.LoopCandidateFraction = 1f;

        float total = 0f;
        const int seeds = 20;
        for (int i = 0; i < seeds; i++)
            total += DungeonMetrics.Measure(new RoomCorridorGenerator().Generate($"open-routes{i}", parameters)).TwoRouteRatio;

        Assert.Greater(total / seeds, 0.8f,
            "loops wide open still left most of the map on a single route");
    }

    /// <summary>
    /// Rooms built with one way in on purpose are held out of the ratio, or asking for more
    /// treasure rooms would make the map score worse for doing what it was told. Every
    /// sampled closet counts, including the spares demoted back to ordinary rooms: they are
    /// held out of the loop edges too, so no loop setting can ever give them a way round.
    /// </summary>
    [Test]
    public void LockedRoomsAreNotCountedAgainstTheRatio()
    {
        var parameters = SmallParams();
        parameters.ExtraLoopChance = 1f;
        parameters.LoopCandidateFraction = 1f;

        DungeonLayout layout = new RoomCorridorGenerator().Generate("locked-routes", parameters);
        DungeonMetrics metrics = DungeonMetrics.Measure(layout);

        int closets = 0;
        foreach (var room in layout.Rooms)
            if (room.IsTreasurePlot) closets++;

        Assert.AreEqual(closets, metrics.SingleRouteByDesign);
        Assert.LessOrEqual(metrics.RoomsWithTwoRoutes, metrics.Rooms - closets);
    }
}
