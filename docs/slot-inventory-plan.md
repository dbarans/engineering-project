# Slot-Based Hotbar & Backpack — Implementation Plan

## Goal

Add a slot-based **hotbar** and **player backpack** matching the reference screenshot:

- Both have **fixed slots that are always present** (empty slots render empty, never disappear).
- Items are placed via **click-to-pick / click-to-place**:
  - Clicking an item attaches it to the cursor.
  - Clicking a slot places the held item into that slot.
- Items have a **max stack size**.
- A slot shows **one icon + a quantity badge** — one item is visually one icon, never multiple icons.
- Example **Item 1 / Item 2 / Item 3** provided.

## Decisions (confirmed)

- **Keep the existing inventory** (`PlayerInventory` + `InventoryUI`) untouched; it still serves crafting via `IInventory`. The new slot system is **parallel and self-contained**.
- **Backpack size: 4 columns × 5 rows = 20 slots.** Hotbar = 5 slots.
- The existing **hotbar is refactored** (no longer a read-only mirror of a flat item list).
- New `SlotInventory` and the old `PlayerInventory` are **independent** by default (no auto-sync) unless decided otherwise later.

## Current state (for reference)

- `ItemData` (ScriptableObject): `icon`, `itemName`, `value` — no stack limit.
- `PlayerInventory` + `InventoryUI`: dynamic list of `ItemStack`, rendered as crafting-style rows. **Ignored** for the new design but preserved for crafting.
- `HotbarUI` / `HotbarSlotUI`: builds fixed slots but only mirrors distinct inventory items read-only; no click/place, no count display. Slot prefab background `Image` has `RaycastTarget: 0` (can't receive clicks yet).
- Crafting (`CraftingManager`, `RequiredIngredientRow`) depends on `IInventory` — must keep working.

## Target architecture

```
ItemData (+ maxStack)
   │
ItemStack  ── reusable type: { item, count }; IsEmpty / SpaceLeft / CanStackWith
   │
ItemContainer  ── fixed-size ItemStack[], event SlotChanged(i); TryAddItem, Get/Set/Swap
   │                       ▲                              ▲
SlotInventory        HotbarUI (container = 5)      BackpackUI (container = 4×5)
(MonoBehaviour, owns        │                              │
 Hotbar + Backpack    SlotView (1 per slot): icon + count badge + highlight,
 containers)                          IPointerClickHandler
                                            │
                                   HeldItemController (cursor) ── pick / place / merge / swap
```

## Visual contract for a slot

**One slot = one item = one icon + a quantity badge.**

- `SlotView` draws a **single** icon for the slot's `ItemStack`, regardless of count.
- A count `TMP_Text` badge overlays the icon (bottom-right, like the red numbers in the screenshot).
- Badge shown when `count > 1`; hidden when `count == 1` or the slot is empty.
- The number is the stack's `count` (capped by `maxStack`), never a number of separate icons.

## New files

| File | Role |
|---|---|
| `Assets/Scripts/Inventory/ItemContainer.cs` | Plain C# class: fixed `ItemStack[]`, `SlotChanged(i)` event, `TryAddItem`, `Get` / `Set` / `Swap`. |
| `Assets/Scripts/Inventory/SlotInventory.cs` | MonoBehaviour owning a `Hotbar` container (5) + `Backpack` container (4×5 = 20). Source of truth for the new system. |
| `Assets/Scripts/UI/SlotView.cs` | Per-slot view: one icon + count badge (`TMP_Text`, shown only when count > 1) + highlight; `IPointerClickHandler` → routes click to `HeldItemController`. |
| `Assets/Scripts/UI/HeldItemController.cs` | Cursor "held stack": follows the mouse, applies pick / place / merge / swap rules. One per Canvas. |
| `Assets/Scripts/UI/BackpackUI.cs` | Builds 20 `SlotView`s over the Backpack container in a `GridLayoutGroup`; toggled by the Inventory input action. |

## Edited files

- **`ItemData.cs`** — add `public int maxStack = 1;`.
- **`PlayerInventory.cs`** — promote `ItemStack` to a reusable type with `IsEmpty` / `SpaceLeft` / `CanStackWith` (shared by the new container; the old list keeps using it). No behavior change to the old inventory.
- **`HotbarUI.cs` / `HotbarSlotUI.cs`** — refactor: bind `SlotView`s to `SlotInventory.Hotbar` instead of read-only mirroring a flat item list; keep scroll-wheel selection + highlight.

## Interaction rules (`HeldItemController`)

Click model = pick/place via `IPointerClickHandler` (not drag) — matches the requirement and needs no new input actions.

- Empty hand + slot has item → **pick up** whole stack.
- Holding + empty slot → **place**.
- Holding + same item → **merge** up to `maxStack`, remainder stays on cursor.
- Holding + different item → **swap**.
- *(Optional later: right-click to pick/place a single unit — stack splitting.)*

## Example items (`Assets/Items/`, new `.asset`s)

| Item | Sprite | maxStack |
|---|---|---|
| **Item 1** — Sword | `Art/GDS/Sprites/Items/sword.png` | 1 (non-stackable) |
| **Item 2** — Mana Potion | `Art/GDS/Sprites/Items/mana.png` | 16 |
| **Item 3** — Wood | `Art/GDS/Sprites/Items/wood.png` | 99 |

## Unity-editor wiring checklist (manual — can't be done headlessly)

1. `HotbarSlot.prefab`: set background `Image` → `RaycastTarget = ✓`; add a count `TMP_Text` child (bottom-right); attach `SlotView`.
2. Build a Backpack panel under the Canvas with a `GridLayoutGroup` (4 columns) + the slot prefab; attach `BackpackUI`.
3. Add a `HeldItem` `Image` at the top of the Canvas hierarchy, `RaycastTarget = ✗`; attach `HeldItemController`.
4. Add `SlotInventory` to the Player; assign references in `HotbarUI` / `BackpackUI` / `HeldItemController`.
5. Confirm the Canvas has a `GraphicRaycaster` and the scene has an `EventSystem` (required for UI clicks).

## Open question (resolve at implementation time)

Whether the new `SlotInventory` and the old `PlayerInventory` should auto-sync (e.g., picked-up world items land in the new backpack) or stay fully independent. Default: **independent** — implemented as independent.

---

## Implementation status (code + automated wiring)

All scripts and example assets are committed. A one-click editor tool now performs the
scene/prefab wiring:

> **Run it:** in Unity, **Tools ▸ Slot Inventory ▸ Build UI & Wire Scene** (with the scene
> containing your Canvas + Player open). It is idempotent — re-running reuses what exists.

The tool ([`Assets/Scripts/Editor/SlotInventorySetup.cs`](../Assets/Scripts/Editor/SlotInventorySetup.cs)):

- creates `Assets/Prefabs/UI/InventoryItem.prefab` — a standalone **item entity** (icon +
  red count `TMP_Text` badge). It is a persistent GameObject that is **re-parented**
  slot → cursor → slot as it moves (not recreated); the cursor adopts a slot's entity on
  pick-up, and a slot only spawns a fresh entity when items arrive in the data externally;
- creates `Assets/Prefabs/UI/InventorySlot.prefab` — a **slot frame** (clickable background
  `Image` + `SelectionHighlight` + `SlotView`) that hosts an item entity when occupied;
- **bakes the hotbar + backpack slot instances into the scene at edit time** (as prefab
  instances), so the slots exist before Play instead of being generated at runtime;
- adds a `Backpack` panel (4×5 `GridLayoutGroup`) with `BackpackUI`, hidden until toggled;
- adds a `HeldItem` cursor (`Image` + `HeldItemController`, `RaycastTarget` off) on its own
  high-sorting canvas so the dragged item always draws above the HUD and is never clipped;
- adds `SlotInventory` to the player and wires `HotbarUI` / `BackpackUI` / `HeldItemController`;
- ensures an `EventSystem` (new Input System UI module) and a `GraphicRaycaster`;
- seeds the example items via `SlotInventoryDebugFill` so content is visible immediately.

The backpack opens/closes on **Tab** (configurable via `BackpackUI.toggleKey`) — handled
inside `BackpackUI` itself, so no input-asset changes are needed. After running the tool the
feature works end-to-end; nothing else is required.

The manual reference below documents the same wiring if you prefer to do it by hand.

### Scripts (with GUIDs for prefab wiring)

| Script | GUID |
|---|---|
| `Assets/Scripts/Inventory/ItemContainer.cs` | `acf076dfce4098742ab7a91cc6e1470d` |
| `Assets/Scripts/Inventory/SlotInventory.cs` | `c67c5cd8f8cc12444a3decfe01a3a663` |
| `Assets/Scripts/UI/SlotView.cs` | `753fd2362457ee34c9f27c5def98bcdf` |
| `Assets/Scripts/UI/HeldItemController.cs` | `49ce59647913dba429f886d692ffad1b` |
| `Assets/Scripts/UI/BackpackUI.cs` | `e2c8ef36e10fa314f93de9f84aabffa7` |

`SlotView` is the **single universal slot component** used by both the hotbar and the
backpack. `HotbarSlotUI.cs` is now superseded by `SlotView` and no longer referenced by
`HotbarUI`; it is left in place only so the existing `HotbarSlot.prefab` doesn't show a
missing-script until step 1 below is done — delete it once the prefab uses `SlotView`.

### Example items (`Assets/Items/`)

- `Item 1 - Sword.asset` — `sword.png`, maxStack 1
- `Item 2 - Mana Potion.asset` — `mana.png`, maxStack 16
- `Item 3 - Wood.asset` — `wood.png`, maxStack 99

(`itemName` is set to "Item 1/2/3" so they don't collide by-name with the existing
`Mana Potion.asset` under `ItemStack.IsSameItem`.)

### Editor wiring checklist (refined)

0. **Item prefab** (`InventoryItem.prefab`): an `Icon` `Image` + a count `TMP_Text` badge,
   both `RaycastTarget = ✗`; attach `InventoryItem` and assign `iconImage` / `countLabel`.
1. **Slot prefab** (`InventorySlot.prefab`, reused for the backpack): a background `Image`
   with `RaycastTarget = ✓` + a `SelectionHighlight` `Image`; attach `SlotView` and assign
   `itemPrefab` (→ `InventoryItem.prefab`), `itemAnchor` (the slot root), `selectionHighlight`.
   The slot frame holds **no** icon/count itself — those belong to the item entity.
2. **Backpack panel**: a `panelRoot` (start inactive) containing a `GridLayoutGroup`
   (4 columns) `slotsContainer`. Attach `BackpackUI`; assign `panelRoot`, `slotsContainer`,
   `slotPrefab`, `slotInventory`, `heldItem`.
3. **HeldItem**: an empty `RectTransform` at the top of the Canvas hierarchy. Attach
   `HeldItemController`; assign `followTarget` (its own RectTransform) and `itemPrefab`
   (→ `InventoryItem.prefab`). It gets its own high-sorting `Canvas` automatically.
4. **Player**: add `SlotInventory`. Re-wire `HotbarUI` (now exposes `slotPrefab`,
   `slotsContainer`, `slotInventory`, `heldItem` — the old `slotCount`/`inventory` fields
   are gone) and `BackpackUI` to reference it.
5. **Backpack toggle**: handled by `BackpackUI` itself on the **Tab** key
   (`toggleKey` field) — no input action needed. Change the key in the inspector if desired.
6. Confirm the Canvas has a `GraphicRaycaster` and the scene has an `EventSystem`.
