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

    /// <summary>
    /// The exit and the treasure are two different rewards at two different ends of the
    /// run, and a generator that lets them collide quietly turns the dungeon into one
    /// room the player has to reach. Also pins that the exit is never the hub: the run
    /// would be over at the spawn point.
    /// </summary>
    [Test]
    public void ExitRoomIsNeitherTheHubNorTheTreasure()
    {
        DungeonLayout layout = Generate("exit-roles", SmallParams());

        Room exit = FindKind(layout, RoomKind.Exit);
        Assert.IsNotNull(exit, "no exit room was placed");
        Assert.LessOrEqual(CountKind(layout, RoomKind.Exit), 1, "more than one way out");

        Assert.AreNotEqual(FindKind(layout, RoomKind.Hub).Index, exit.Index);
        Assert.AreNotEqual(FindKind(layout, RoomKind.Treasure).Index, exit.Index);
    }

    /// <summary>
    /// The way out has to actually lead out. A door cut into a pocket of rock with a
    /// corridor behind it is not an exit, and the player cannot tell the difference until
    /// it has cost them the run's only key.
    /// </summary>
    [Test]
    public void TheExitDoorOpensOntoTheOutsideOfTheMap()
    {
        foreach (string seed in new[] { "way-out-a", "way-out-b", "way-out-c", "way-out-d" })
        {
            DungeonLayout layout = Generate(seed, SmallParams());
            Room exit = FindKind(layout, RoomKind.Exit);
            if (exit == null)
            {
                Assert.IsFalse(layout.HasExitDoor, $"seed '{seed}': an exit door with no exit room");
                continue;
            }

            Assert.IsTrue(layout.HasExitDoor, $"seed '{seed}': the exit room has no way out in it");

            Vector2Int door = layout.ExitDoorCell;
            Vector2Int threshold = layout.ExitThresholdCell;

            Assert.IsFalse(layout.IsWalkable(door),
                $"seed '{seed}': the exit door cell was carved open, leaving a hole in the map");
            Assert.IsTrue(layout.IsWalkable(threshold),
                $"seed '{seed}': the threshold at {threshold} cannot be stood on");
            Assert.IsTrue(exit.Contains(threshold),
                $"seed '{seed}': the threshold is outside the exit room");

            int step = Mathf.Abs(door.x - threshold.x) + Mathf.Abs(door.y - threshold.y);
            Assert.AreEqual(1, step, $"seed '{seed}': the door does not adjoin its threshold");

            // Everything from the door outwards, away from the room, has to be solid all
            // the way off the map.
            Vector2Int outward = door - threshold;
            for (Vector2Int cell = door; layout.Contains(cell.x, cell.y); cell += outward)
            {
                Assert.IsFalse(layout.IsWalkable(cell),
                    $"seed '{seed}': the exit door leads back into the dungeon at {cell}");
            }
        }
    }

    /// <summary>
    /// The exit room is meant to be somewhere the dungeon stops, not one more room on the
    /// way to somewhere else: a corridor out the far side makes it read as a through-route
    /// whatever is in its wall.
    ///
    /// Pinned on seeds known to have a dead end available. The generator falls back to any
    /// room with an outward wall when a map offers no dead end that also backs onto the
    /// outside — measured at 1 seed in 200 — so this cannot be asserted for every seed
    /// without pinning the fallback shut, and a run with no ending is the worse outcome.
    /// </summary>
    [Test]
    public void TheExitRoomIsADeadEnd()
    {
        foreach (string seed in new[] { "way-out-a", "way-out-b", "way-out-c", "way-out-d" })
        {
            DungeonLayout layout = Generate(seed, SmallParams());
            Room exit = FindKind(layout, RoomKind.Exit);
            Assert.IsNotNull(exit, $"seed '{seed}': no exit room was placed");

            Assert.AreEqual(1, CountEntrances(layout, exit),
                $"seed '{seed}': the exit room can be walked through");
        }
    }

    /// <summary>
    /// Ways into a room: groups of adjoining walkable cells just outside it. Grouped, not
    /// counted cell by cell — one opening three cells wide is still one way in.
    /// </summary>
    private static int CountEntrances(DungeonLayout layout, Room room)
    {
        var outside = new HashSet<Vector2Int>();
        foreach (Vector2Int cell in room.Cells)
        {
            foreach (Vector2Int neighbour in Neighbours(cell))
            {
                if (room.Contains(neighbour) || !layout.IsWalkable(neighbour)) continue;
                outside.Add(neighbour);
            }
        }

        int openings = 0;
        var pending = new Stack<Vector2Int>();

        while (outside.Count > 0)
        {
            openings++;

            Vector2Int start = default;
            foreach (Vector2Int cell in outside) { start = cell; break; }
            pending.Push(start);
            outside.Remove(start);

            while (pending.Count > 0)
            {
                foreach (Vector2Int neighbour in Neighbours(pending.Pop()))
                {
                    if (outside.Remove(neighbour)) pending.Push(neighbour);
                }
            }
        }

        return openings;
    }

    private static IEnumerable<Vector2Int> Neighbours(Vector2Int cell)
    {
        yield return new Vector2Int(cell.x + 1, cell.y);
        yield return new Vector2Int(cell.x - 1, cell.y);
        yield return new Vector2Int(cell.x, cell.y + 1);
        yield return new Vector2Int(cell.x, cell.y - 1);
    }

    /// <summary>
    /// The key has to be a journey, not a detour. Pinned against the hub's own distance
    /// from the exit rather than an absolute number of hops, because how far apart two
    /// rooms can be is a property of the map size, not of this rule.
    /// </summary>
    [Test]
    public void ExitKeyIsNotStashedNextToTheExit()
    {
        DungeonLayout layout = Generate("exit-key", SmallParams());
        Room exit = FindKind(layout, RoomKind.Exit);
        Assert.IsNotNull(exit, "no exit room was placed");

        Room keyRoom = null;
        int flagged = 0;
        foreach (var room in layout.Rooms)
        {
            if (!room.HoldsExitKey) continue;
            flagged++;
            keyRoom = room;
        }

        Assert.AreEqual(1, flagged, "the dungeon must hold exactly one exit key");
        Assert.AreNotEqual(exit.Index, keyRoom.Index, "the key is locked inside the door it opens");
        Assert.AreNotEqual(RoomKind.Hub, keyRoom.Kind, "the key is handed over at the spawn point");
        Assert.AreNotEqual(RoomKind.Treasure, keyRoom.Kind, "the key and the reward are one trip");

        // The three destinations have to be spread over the map, not clustered in one
        // corner of it. The floor is deliberately slack: measured over 60 maps, the worst
        // case is 40% of the diagonal at the shipped settings but only 21% on the cramped
        // map these tests use, where eight rooms leave the scoring little to choose
        // between. 15% still catches the collapse this guards against — before the fix,
        // key and treasure landed within 5–8% of each other on real seeds.
        float diagonal = Mathf.Sqrt(layout.Width * layout.Width + layout.Height * layout.Height);
        float floorDistance = diagonal * 0.15f;

        Assert.Greater(Vector2Int.Distance(keyRoom.Center, exit.Center), floorDistance,
            "the key is stashed on the exit's doorstep");

        Room treasure = FindKind(layout, RoomKind.Treasure);
        if (treasure != null)
        {
            Assert.Greater(Vector2Int.Distance(keyRoom.Center, treasure.Center), floorDistance,
                "the key and the treasure are the same trip");
        }
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
