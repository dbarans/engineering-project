using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Structural guarantees the rest of the feature is allowed to assume: layouts are
/// reproducible from their seed, fully connected, and correctly tagged. A regression
/// in any of these breaks either the save system or the playability of a run.
/// </summary>
public class RoomCorridorGeneratorTests
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

    private static DungeonLayout Generate(string seed, LayoutParams parameters)
    {
        return new RoomCorridorGenerator().Generate(seed, parameters);
    }

    [Test]
    public void SameSeedProducesIdenticalLayout()
    {
        var parameters = SmallParams();
        DungeonLayout first = Generate("alpha", parameters);
        DungeonLayout second = Generate("alpha", parameters);

        Assert.AreEqual(first.ContentHash(), second.ContentHash());
        Assert.AreEqual(first.SpawnCell, second.SpawnCell);
        Assert.AreEqual(first.Rooms.Count, second.Rooms.Count);
    }

    [Test]
    public void DifferentSeedsProduceDifferentLayouts()
    {
        var parameters = SmallParams();
        Assert.AreNotEqual(
            Generate("alpha", parameters).ContentHash(),
            Generate("beta", parameters).ContentHash());
    }

    [Test]
    public void EveryWalkableCellIsReachableFromSpawn()
    {
        var parameters = SmallParams();
        DungeonLayout layout = Generate("connectivity", parameters);

        Assert.AreEqual(
            layout.CountWalkable(),
            RoomCorridorGenerator.CountReachable(layout, layout.SpawnCell),
            "the layout contains a pocket the player cannot reach");
    }

    [Test]
    public void SpawnCellIsWalkableAndInsideTheHub()
    {
        DungeonLayout layout = Generate("spawn", SmallParams());

        Assert.IsTrue(layout.IsWalkable(layout.SpawnCell));
        Room room = layout.RoomAt(layout.SpawnCell);
        Assert.IsNotNull(room, "spawn is not inside any room");
        Assert.AreEqual(RoomKind.Hub, room.Kind);
        Assert.AreEqual(room.Center, layout.SpawnCell, "spawn is not the hub's own centre");
    }

    [Test]
    public void MapBorderStaysSolid()
    {
        DungeonLayout layout = Generate("border", SmallParams());

        for (int x = 0; x < layout.Width; x++)
        {
            Assert.IsFalse(layout.IsWalkable(x, 0), $"open cell on the bottom border at x={x}");
            Assert.IsFalse(layout.IsWalkable(x, layout.Height - 1), $"open cell on the top border at x={x}");
        }
        for (int y = 0; y < layout.Height; y++)
        {
            Assert.IsFalse(layout.IsWalkable(0, y), $"open cell on the left border at y={y}");
            Assert.IsFalse(layout.IsWalkable(layout.Width - 1, y), $"open cell on the right border at y={y}");
        }
    }

    [Test]
    public void RoomsNeverOverlapAndRespectSpacing()
    {
        var parameters = SmallParams();
        DungeonLayout layout = Generate("spacing", parameters);
        IReadOnlyList<Room> rooms = layout.Rooms;

        for (int i = 0; i < rooms.Count; i++)
        {
            for (int j = i + 1; j < rooms.Count; j++)
            {
                RectInt a = rooms[i].Bounds;
                RectInt b = rooms[j].Bounds;

                bool separated =
                    a.xMin >= b.xMax + parameters.RoomSpacing ||
                    a.xMax + parameters.RoomSpacing <= b.xMin ||
                    a.yMin >= b.yMax + parameters.RoomSpacing ||
                    a.yMax + parameters.RoomSpacing <= b.yMin;

                Assert.IsTrue(separated, $"rooms {i} and {j} are closer than the required spacing");
            }
        }
    }

    [Test]
    public void RoomCountStaysWithinConfiguredRange()
    {
        var parameters = SmallParams();
        DungeonLayout layout = Generate("count", parameters);

        Assert.GreaterOrEqual(layout.Rooms.Count, parameters.MinRoomCount);
        Assert.LessOrEqual(layout.Rooms.Count, parameters.TargetRoomCount);
    }

    [Test]
    public void ExactlyOneHubAndTreasureRoom()
    {
        DungeonLayout layout = Generate("roles", SmallParams());

        Assert.AreEqual(1, CountKind(layout, RoomKind.Hub));
        Assert.AreEqual(1, CountKind(layout, RoomKind.Treasure));
    }

    [Test]
    public void TreasureRoomIsDeeperThanTheHub()
    {
        DungeonLayout layout = Generate("depth", SmallParams());

        Room treasure = FindKind(layout, RoomKind.Treasure);
        Room hub = FindKind(layout, RoomKind.Hub);

        Assert.AreEqual(0, hub.DepthFromHub);
        Assert.Greater(treasure.DepthFromHub, 0, "the reward room sits at the entrance");
    }

    /// <summary>
    /// The hub is the one fixture of the layout the player is told to rely on: it holds
    /// the run's only save station and crafting table, so "somewhere in the middle" has
    /// to be a guarantee of the generator rather than a property of a lucky seed.
    /// </summary>
    [Test]
    public void HubRoomIsCentredOnTheMap()
    {
        var parameters = SmallParams();
        DungeonLayout layout = Generate("hub", parameters);

        Room hub = FindKind(layout, RoomKind.Hub);
        Assert.IsNotNull(hub, "no hub room was placed");
        Assert.AreEqual(RoomShape.Rectangle, hub.Shape, "the hub must stay legible at a glance");

        var center = new Vector2Int(layout.Width / 2, layout.Height / 2);
        Assert.IsTrue(hub.Contains(center),
            $"the hub at {hub.Bounds} does not cover the map centre {center}");
    }

    [Test]
    public void HubRoomIsNeverTheTreasure()
    {
        DungeonLayout layout = Generate("hub-roles", SmallParams());

        Room hub = FindKind(layout, RoomKind.Hub);
        Assert.AreNotEqual(hub.Index, FindKind(layout, RoomKind.Treasure).Index);
    }

    [Test]
    public void EverySeedProducesExactlyOneHub()
    {
        var parameters = SmallParams();

        for (int i = 0; i < 200; i++)
        {
            string seed = $"hub-{i}";
            Assert.AreEqual(1, CountKind(Generate(seed, parameters), RoomKind.Hub),
                $"seed '{seed}' did not produce exactly one hub room");
        }
    }

    /// <summary>
    /// The hub occupies the middle of the map before any other room is sampled, so it is
    /// the one placement that could break the spacing guarantee if it were mishandled.
    /// Covered by <see cref="RoomsNeverOverlapAndRespectSpacing"/> for the default size;
    /// this pins the same guarantee at the largest size the settings allow.
    /// </summary>
    [Test]
    public void AnOversizedHubIsClampedToTheRoomSizeRange()
    {
        var parameters = SmallParams();
        parameters.HubRoomSize = 999;
        DungeonLayout layout = Generate("huge-hub", parameters);

        Room hub = FindKind(layout, RoomKind.Hub);
        Assert.IsNotNull(hub, "an oversized hub was dropped instead of being clamped");
        Assert.LessOrEqual(hub.Bounds.width, parameters.MaxRoomSize);
        Assert.LessOrEqual(hub.Bounds.height, parameters.MaxRoomSize);

        Assert.AreEqual(layout.CountWalkable(),
            RoomCorridorGenerator.CountReachable(layout, layout.SpawnCell));
    }

    [Test]
    public void DoorwaysAreMarkedOutsideRooms()
    {
        DungeonLayout layout = Generate("doors", SmallParams());

        int doors = 0;
        for (int y = 0; y < layout.Height; y++)
        {
            for (int x = 0; x < layout.Width; x++)
            {
                if (layout[x, y] != CellType.Door) continue;
                doors++;
                Assert.IsNull(layout.RoomAt(new Vector2Int(x, y)),
                    $"door at ({x},{y}) is inside a room instead of in its opening");
            }
        }

        Assert.Greater(doors, 0, "no doorways were marked at all");
    }

    /// <summary>
    /// A doorway must be something the player can walk through, which means open ground on
    /// both sides of it. The case that used to fail was a corridor running straight past a
    /// room's wall: collinear like a real opening, so it was narrowed like one, leaving a
    /// door with the room on one side and untouched bedrock on the other.
    /// </summary>
    [Test]
    public void EveryDoorwayHasOpenGroundOnBothSides()
    {
        var parameters = SmallParams();
        parameters.CorridorWidth = 1;
        parameters.MaxCorridorWidth = 4;

        // Swept rather than run on one seed: the fault appeared on roughly one doorway in
        // fourteen, so a single layout can easily contain none of them.
        for (int seed = 0; seed < 40; seed++)
        {
            DungeonLayout layout = Generate($"doorway{seed}", parameters);

            for (int y = 0; y < layout.Height; y++)
            {
                for (int x = 0; x < layout.Width; x++)
                {
                    if (layout[x, y] != CellType.Door) continue;

                    bool acrossX = layout.IsWalkable(x - 1, y) && layout.IsWalkable(x + 1, y);
                    bool acrossY = layout.IsWalkable(x, y - 1) && layout.IsWalkable(x, y + 1);

                    Assert.IsTrue(acrossX || acrossY,
                        $"seed doorway{seed}: door at ({x},{y}) opens onto solid rock");
                }
            }
        }
    }

    [Test]
    public void LoopsAreCreatedWhenRequested()
    {
        var parameters = SmallParams();
        parameters.ExtraLoopChance = 1f;
        DungeonLayout looped = Generate("loops", parameters);

        // A spanning tree has exactly rooms-1 edges; anything above that is a cycle.
        Assert.Greater(looped.Links.Count, looped.Rooms.Count - 1,
            "maximum loop chance produced a pure tree");
    }

    [Test]
    public void ZeroLoopChanceProducesATree()
    {
        var parameters = SmallParams();
        parameters.ExtraLoopChance = 0f;
        DungeonLayout tree = Generate("tree", parameters);

        Assert.AreEqual(tree.Rooms.Count - 1, tree.Links.Count);
    }

    [Test]
    public void ManySeedsAllPassValidation()
    {
        var parameters = SmallParams();
        var hashes = new HashSet<uint>();

        for (int i = 0; i < 500; i++)
        {
            string seed = $"batch-{i}";
            DungeonLayout layout = Generate(seed, parameters);

            Assert.IsTrue(RoomCorridorGenerator.Validate(layout, parameters, out string failure),
                $"seed '{seed}' produced an invalid layout: {failure}");
            hashes.Add(layout.ContentHash());
        }

        Assert.AreEqual(500, hashes.Count, "different seeds produced duplicate layouts");
    }

    [Test]
    public void ImpossibleSettingsDegradeInsteadOfCrashing()
    {
        // Far more rooms than the map can hold: generation must still return something
        // playable rather than throw or loop forever.
        var parameters = SmallParams();
        parameters.MapWidth = 24;
        parameters.MapHeight = 24;
        parameters.TargetRoomCount = 40;
        parameters.MinRoomCount = 40;
        parameters.MaxGenerationAttempts = 2;

        DungeonLayout layout = Generate("impossible", parameters);

        Assert.IsNotNull(layout);
        Assert.AreEqual(24, layout.Width);
    }

    private static int CountKind(DungeonLayout layout, RoomKind kind)
    {
        int count = 0;
        foreach (var room in layout.Rooms)
            if (room.Kind == kind) count++;
        return count;
    }

    private static Room FindKind(DungeonLayout layout, RoomKind kind)
    {
        foreach (var room in layout.Rooms)
            if (room.Kind == kind) return room;
        return null;
    }
}
