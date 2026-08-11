# GU-0057 — Loot chest generation

Implementation plan. Written in English to match `GENERATION_NOTES.md` and `ENEMY_NOTES.md`.
Scope: the procedural dungeon generator should place **chests stocked with loot**, deterministically
from the dungeon seed, surviving a save/load round trip.

## What already exists (do not rebuild any of this)

| Piece | Where | State |
|---|---|---|
| Chest storage | `Chest/ChestInventory.cs` | 4×4 `ItemContainer`, created lazily; `startingItems` is a serialized `List<StartingStack>` applied on first access |
| Chest interaction | `Chest/ChestInteractable.cs` | `E` to open, static "which chest is open", already tolerates being spawned into a generated scene |
| Chest persistence | `Saving/ChestSaveable.cs` | GU-0053; captures/restores the container by entity guid. `Restore` overwrites **every** slot, so an emptied chest stays empty even though generation refills it |
| Prefab | `Assets/Prefabs/World/Chest.prefab` | Has `SaveableEntity` + `ChestSaveable`, layer 11 = `ObstaclePathOnly` |
| Registry id | `Assets/Resources/PrefabRegistry.asset` | **`world.chest` is already registered** — no registry edit needed |
| Placement helpers | `Generation/DungeonPopulator.cs` | `FreeCells`, `TryTakeAnchor`, `FootprintCells`, `BlocksPathfinding`, `SlotGuid` |
| Weighted loot tables | `Generation/RoomContentSettings.cs` | `ItemChoice` + `PickItem` |

So the feature is: **a new spawn pass in `DungeonPopulator` + a stocking API on `ChestInventory` + new
fields on `RoomContentSettings`.** Nothing else in the generator changes.

---

## Design decisions (fixed — do not re-litigate)

| # | Decision | Rationale |
|---|---|---|
| C1 | Chests are stocked through **`startingItems`**, never by writing into `Container` directly | `ChestInventory`'s own doc comment says so, and it is load-bearing: `Container` is runtime-only state, so contents poured into it vanish the moment the Dungeon scene is baked (generate in edit mode → save scene). `startingItems` is serialized and survives. |
| C2 | Chest contents are rolled from a **seed-derived stream**, like every other spawn | A chest that rerolls its contents on every load would let the player farm it by reloading, and would break the "seed + diffs" save model (D6 in `GENERATION_NOTES.md`). |
| C3 | Chest spawn reuses `TryTakeAnchor(..., blocking: true, ...)` | The prefab sits on `ObstaclePathOnly`, so it really does block `PathfindingGrid`. That path already keeps blocking objects off chokepoints and their neighbours, and biases them against a wall — which is also where a chest belongs visually. |
| C4 | Guid via the existing `SlotGuid(seed, roomIndex, slot)` scheme (D7) | Regeneration must reproduce the same chest identity, or `ChestSaveable` cannot match a saved chest to the rebuilt one. **Chests must therefore be spawned at a stable point in the per-room slot order** — see the ordering note in step 3. |
| C5 | The **Treasure** room gets a guaranteed chest with its own richer table | Room kinds already exist and `RoomKind.Treasure` currently only scatters floor loot. A chest is what makes it read as a reward rather than as a room with more litter. |
| C6 | Start room never gets a chest | Consistent with it never getting enemies. |
| C7 | An **authored template room** (`ApplyTemplate` returned true) gets no random chests | Same rule already applied to random loot and props: the design is not buried under scatter. A template that wants a chest places one via a `Prefab` marker with id `world.chest` (it will be unstocked — see "Not in scope"). |

---

## Step 1 — `Chest/ChestInventory.cs`: a stocking API

Add a public method; keep `startingItems` private.

```csharp
/// <summary>
/// Replaces what this chest starts stocked with. Called by the dungeon populator.
/// Writes the serialized starting list rather than <see cref="Container"/>, because the
/// container is runtime-only state and would not survive the Dungeon scene being baked.
/// </summary>
public void SetStartingItems(IEnumerable<(ItemData item, int count)> stacks)
```

Requirements:
- Clears `startingItems`, then adds one `StartingStack` per entry, skipping null items and counts ≤ 0.
- If `_container` **has already been created**, re-apply: clear every slot, then `ApplyStartingItems()`.
  (Ordinarily the populator stocks before anyone opens the chest, so `_container` is still null — but
  a second Generate in edit mode over a live scene can hit an already-created container, and a chest
  that silently keeps the previous run's contents is a confusing bug to chase.)
- In the editor and outside play mode, mark the component dirty so the value is written into the
  scene when the baked dungeon is saved:
  ```csharp
  #if UNITY_EDITOR
  if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
  #endif
  ```
  This is not optional — without it, the Dungeon scene bakes chests that look stocked in memory and
  come back empty after a domain reload.
- Add `public int SlotCount => columns * rows;` so the populator can cap the number of stacks it
  tries to put in (a 4×4 chest cannot hold 20 distinct stacks and the surplus would be dropped
  silently by `TryAddItem`).

## Step 2 — `Generation/RoomContentSettings.cs`: the tables

New section, placed after `[Header("Treasure room")]`:

```csharp
[Header("Chests")]
[Tooltip("Id from the PrefabRegistry. Leave empty to generate no chests at all.")]
public string chestPrefabId = "world.chest";

[Tooltip("Chance an ordinary room contains a chest. Rooms at depth 0 (Start) never do.")]
[Range(0f, 1f)] public float chestChancePerRoom = 0.25f;

[Tooltip("Added to the chance per hop from the start room, so the far end of the run is " +
         "where the rewards are. Clamped to 1.")]
[Range(0f, 0.5f)] public float chestChancePerDepth = 0.05f;

[Min(0)] public int maxChestsPerRoom = 1;

[Tooltip("What an ordinary chest is stocked with, by weight.")]
public List<ItemChoice> chestLoot = new List<ItemChoice>();
[Min(0)] public int minChestStacks = 1;
[Min(1)] public int maxChestStacks = 3;

[Tooltip("Chests guaranteed in the Treasure room, on top of the loose treasure loot.")]
[Min(0)] public int treasureChests = 1;

[Tooltip("The Treasure room's chest table. Falls back to chestLoot when left empty.")]
public List<ItemChoice> treasureChestLoot = new List<ItemChoice>();
[Min(0)] public int minTreasureChestStacks = 2;
[Min(1)] public int maxTreasureChestStacks = 4;
```

Plus:
- `public float ChestChanceFor(int depth) => Mathf.Clamp01(chestChancePerRoom + depth * chestChancePerDepth);`
- Extend `OnValidate` the same way the existing pairs are clamped:
  `maxChestStacks`/`minChestStacks`, `maxTreasureChestStacks`/`minTreasureChestStacks`, and
  `ClampCounts` over `chestLoot` and `treasureChestLoot`.

## Step 3 — `Generation/DungeonPopulator.cs`: the spawn pass

New method, in the "rooms" region next to `SpawnCampFixtures`:

```csharp
private void SpawnChests(DungeonLayout layout, Room room, List<Vector2Int> free,
    DeterministicRandom random, int count, List<RoomContentSettings.ItemChoice> table,
    int minStacks, int maxStacks, ref int slot)
```

Body:
1. Bail when `string.IsNullOrEmpty(content.chestPrefabId)` or `count <= 0`.
2. Per chest:
   - `GameObject prefab = _prefabs.Resolve(content.chestPrefabId);`
     `bool blocking = BlocksPathfinding(prefab);` (it is — but read it rather than assume, the same
     way `SpawnProps` does).
   - `if (!TryTakeAnchor(layout, free, blocking, random, out Vector2Int cell)) return;`
   - `GameObject chest = _prefabs.Spawn(content.chestPrefabId, builder.CellCenter(cell), _contentRoot, SlotGuid(layout.Seed, room.Index, slot++));`
   - `if (chest == null) continue;`
   - `StockChest(chest, table, minStacks, maxStacks, random.Derive($"chest{slot}"));`

And the stocking helper:

```csharp
private void StockChest(GameObject chest, List<RoomContentSettings.ItemChoice> table,
    int minStacks, int maxStacks, DeterministicRandom random)
```
- `var inventory = chest.GetComponent<ChestInventory>();` — warn and return if missing (a registry id
  pointed at something that is not a chest is worth saying out loud, not silently ignoring).
- Roll `int stacks = Mathf.Min(random.RangeInclusive(minStacks, maxStacks), inventory.SlotCount);`
- For each stack: `content.PickItem(table, random)`, resolve through
  `ItemDatabase.Instance.Resolve(choice.itemId)`. On a null item, log the same warning `Drop` does
  (naming the id and pointing at *Tools ▸ Save System ▸ Rebuild Item Database*) and skip that stack.
- Collect into a `List<(ItemData, int)>` with `random.RangeInclusive(choice.minCount, choice.maxCount)`,
  then one call to `inventory.SetStartingItems(...)`.
- Note in the doc comment **why this does not go through `WorldItemPickup` like `Drop` does**: floor
  loot is a world drop captured by `WorldItemsSaveable` by position, chest contents are entity state
  captured by `ChestSaveable` by guid. Two different persistence paths, on purpose.

### Wiring into `SpawnRoomContent` — and the ordering constraint

This is the part to get right, because of C4. The per-room `slot` counter feeds `SlotGuid`, so
**inserting a new pass in the middle of the existing order renumbers every spawn after it**, and every
existing save's guids stop matching. There are no shipped saves to protect here, but the same applies
within this change: pick one order and do not shuffle it afterwards.

Add chests **after** enemies and **before** props, in the `Treasure` and `default` cases:

```csharp
case RoomKind.Treasure:
    if (!authored)
        SpawnItems(content.treasureLoot, content.treasureLootCount, free, roomRandom);
    SpawnEnemies(layout, room, free, roomRandom, ref slot);
    if (!authored)
        SpawnChests(layout, room, free, roomRandom.Derive("chests"), content.treasureChests,
            content.treasureChestLoot.Count > 0 ? content.treasureChestLoot : content.chestLoot,
            content.minTreasureChestStacks, content.maxTreasureChestStacks, ref slot);
    break;

default:
    SpawnEnemies(layout, room, free, roomRandom, ref slot);
    if (!authored)
    {
        SpawnItems(...);                       // unchanged
        var chestRandom = roomRandom.Derive("chests");
        int chests = 0;
        for (int i = 0; i < content.maxChestsPerRoom; i++)
            if (chestRandom.Chance(content.ChestChanceFor(room.DepthFromStart))) chests++;
        SpawnChests(layout, room, free, chestRandom, chests, content.chestLoot,
            content.minChestStacks, content.maxChestStacks, ref slot);
    }
    break;
```

`Start` and `Camp` are untouched (C6; the camp is a safe room, and a reward there undercuts the
reason to go deeper).

Deriving a dedicated `"chests"` stream matters for the same reason the per-room streams do: retuning
the chest tables must not shift the props' and enemies' rolls in every other room.

## Step 4 — the settings asset

`RoomContentSettings.asset` (find it under `Assets/` — same folder as `DungeonGenerationSettings.asset`)
must be filled in, or the new fields run on their C# defaults with an **empty** `chestLoot` and every
chest generates empty. Fill `chestLoot` and `treasureChestLoot` with ids that actually exist in
`Assets/Resources/ItemDatabase.asset` — read that file for the id list rather than guessing.
Reasonable starting point (a guess, not a design — tune in the editor):
- `chestLoot`: ammo, bandage/heal, lamp fuel, crafting material — weights roughly even.
- `treasureChestLoot`: the same plus a weapon, and higher counts.

## Step 5 — docs

Append a short **Stage 9 — Loot chests** section to `GENERATION_NOTES.md`: what was added, the C1
(`startingItems`, not `Container`) and C4 (slot-order/guid) decisions, and the in-editor checklist
below. Keep it in the existing voice — what was decided and why, not a changelog.

---

## Not in scope (say so, do not silently do it)

- **Locked chests / keys.** `GU-0045` added key-opened doors; a locked chest is a separate change.
- **Stocking template-marker chests.** A `Prefab` marker with id `world.chest` will spawn an unstocked
  chest. Making markers carry a loot table is a `DungeonSpawnMarker` change and belongs with the
  template work, not here.
- **Mimics / trapped chests.** Out of scope.
- **Chest art and open/closed sprite states.** Prefab-side, unchanged here.

## Risks

| Risk | Mitigation |
|---|---|
| Chest stocked into `Container` instead of `startingItems` → empty after baking the scene | C1; the only write path is `SetStartingItems` |
| Baked chest contents not persisted → empty after domain reload | `EditorUtility.SetDirty` in step 1 |
| Chest parked on a chokepoint walls off part of the dungeon | C3 — `TryTakeAnchor` with `blocking: true` already excludes chokepoints and their neighbours |
| Re-generating after a save produces different guids → saved chest contents lost | C4; keep the spawn order fixed once chosen |
| More stacks rolled than the chest has slots → silent loss | `Mathf.Min(..., inventory.SlotCount)` in step 3 |
| Item id not in `ItemDatabase` → silently empty chest | Warn per skipped stack, same message shape as `Drop` |

## Verification

**Compile-level / logic-level (do this first, it is what can be done outside the editor):**
- The whole project compiles clean.
- Two builds from the same seed produce identical chest positions and identical contents — this is the
  claim C2 rests on and is worth asserting, not eyeballing.

**In-editor checklist (requires Unity, the same shape as the other stages'):**
1. `Tools ▸ Dungeon ▸ Setup Scene Tilemaps` if the scene has not been set up in this branch.
2. Generate a dungeon from the `DungeonBuilder` context menu. Chests appear — against walls, not in
   the middle of rooms, none in the start room, at least one in the treasure room.
3. Enter Play mode, walk to a chest, press `E`: the panel opens and the chest **has items in it**.
   An empty panel means either `chestLoot` is empty on the asset or stocking went to `Container`.
4. Take an item, save at the typewriter, quit, load: the chest comes back **still missing that item**,
   and every other chest still has its own contents (this is the GU-0053 independence guarantee —
   verify it rather than assume it).
5. Generate twice with the same seed: identical chests in identical places with identical contents.
6. Confirm no chest blocks a corridor or a doorway, and that an enemy still paths through every room.
