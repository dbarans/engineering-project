# Loot & crafting budget

How much crafting material one generated map has to contain, where it comes from, and what the
four drop-source rules mean in numbers. Written in English to match `GENERATION_NOTES.md` and
`ENEMY_NOTES.md`. Every figure below is an **expected value over one dungeon**, recomputable from
the constants named in each row — retune a constant, redo the row.

## The rules this is measured against

| # | Rule | Source |
|---|---|---|
| R1 | Work out exactly how much crafting material a map needs | design guideline |
| R2 | Chest drops must be varied | design guideline |
| R3 | Barrels drop **only** simple crafting materials | design guideline |
| R4 | Crates drop simple materials and possibly ammo — **never more ammo than is needed to clear the map's enemies** | design guideline |
| R5 | Enemies drop **only** simple crafting materials | design guideline |

"Simple crafting material" = an item with no recipe of its own: **Scrap, Wood, Rags, Alcohol,
Gunpowder**. Everything else (Bullet, Shell, Plank, Ink, Bandage, Torch, Door Key) is a *product*
and by R3/R5 has no business dropping from a barrel or a corpse.

## Recipes (`Assets/Items/Recipes`)

| Product | Cost |
|---|---|
| Bullet | 1 Scrap + 1 Gunpowder |
| Shell | 2 Scrap + 2 Gunpowder |
| Plank | 5 Wood |
| Ink | 3 Scrap + 1 Alcohol + 2 Wood |
| Bandage | 2 Rags + 1 Alcohol |
| Door Key | 3 Scrap |
| Torch | 1 Wood + 1 Alcohol |

## What one map holds

From `DungeonGenerationSettings.asset` and `RoomContentSettings.asset`:

| Quantity | Value | Where it comes from |
|---|---|---|
| Rooms | 30 (1 hub + 3 treasure + 1 exit + 25 ordinary) | `targetRoomCount`, `treasureRoomCount` |
| Mean carved cells per room | ~170 | sides uniform in 7..20, minus shaping and interior solids |
| Props | ~190 | `propsPerHundredFloorCells = 4` over 28 propped rooms |
| **Barrels** | **~93** | barrel weight 0.49 of 1.00 in the prop table |
| **Enemies** | **~47** | `EnemyCountFor`: 0.9 + 0.64·(depth−1), capped at 2/room, + up to 2 corridor ambushes |
| **Ordinary chests** | **~11** | `0.25 + 0.05·depth`, max 1 per room |
| Treasure chests | 3 | `treasureChests = 1` in each of 3 treasure rooms |
| Hub chests | 2, empty | player storage |

Enemy HP pool = 47 × 100 = **4 700 HP**. That number is the yardstick R4 refers to.

## What the player has to spend material on

| Sink | Unit cost | Per run | Notes |
|---|---|---|---|
| Kill with pistol | 4 Bullet = 4 Scrap + 4 Gunpowder | — | 100 HP / 25 dmg |
| Kill with shotgun | 2–3 Shell = 4–6 Scrap + 4–6 Gunpowder | — | 5 pellets x 10 dmg, point blank |
| Kill with axe | 3 charged hits = 3 durability = **1 Scrap** | — | repair restores 3 durability per Scrap |
| Treasure doors | 3 Scrap each | 9 Scrap | 3 treasure rooms, `treasureKeyItemId` = Door Key |
| Saving | 1 Ink each | 6 saves -> 18 Scrap + 6 Alcohol + 12 Wood | `SaveCost`; ink is non-stackable |
| Light | 1 Wood + 1 Alcohol | 3 torches | torches never burn out — a one-off cost |
| Healing | 2 Rags + 1 Alcohol | 8 bandages -> 16 Rags + 8 Alcohol | **see the bandage bug below** |
| Barricading a door | 2 Plank = 10 Wood | 2 stages -> 20 Wood | by far the most expensive thing in the game |

**Melee is ~4x cheaper per kill than a gun and costs no Gunpowder at all.** That is the economy's
spine: the axe is the sustainable answer, guns are the panic button. Gunpowder is the only material
with no use but ammo, which makes it the single lever that decides how much of a map can be cleared
by force.

### Reference profile (what "enough" means)

25 melee kills, ~15 shot kills, 6 saves, 3 torches, 8 bandages, 3 keys, 2 barricade stages:

| Material | Demand |
|---|---|
| Scrap | ~87 (9 keys + 18 ink + 25 repairs + ~35 ammo) |
| Wood | ~35 |
| Rags | ~16 |
| Alcohol | ~17 |
| Gunpowder | whatever the map gives; all of it becomes ammo |

A map should carry **~1.3–1.4x the demand**: loot is scattered, some of it sits behind enemies the
player walks around, and a chest can roll three stacks of the same thing.

---

## The current tables fail R3, R4 and R5

Measured from `Barrel.prefab`, `SkullGuyEnemy.prefab`, `BlindListenerEnemy.prefab` and
`RoomContentSettings.asset` as of this note:

| Material | Expected on map | Comment |
|---|---|---|
| Bullet | **174** | ready ammo, no crafting needed |
| Shell | 24 | |
| Scrap | 192 | |
| Gunpowder | **5.5** | |
| Wood | 11 | |
| Rags | 5.7 | |
| Alcohol | **2.4** | |
| Ink | 10 | |
| Bandage | 2.5 | |

Consequences, in order of severity:

1. **R4 is broken by a wide margin.** 174 bullets + 24 shells = 5 535 damage against a 4 700 HP
   enemy pool — the map hands out **118 %** of the ammo needed to shoot everything on it, before a
   single item is crafted. The rule caps this at 100 %; a stealth game wants far less.
2. **R3 and R5 are broken directly.** Barrels drop Bullet (20 %, 1–3) and enemies drop Bullet
   (100 %, 1–3) — a *product*, from the two sources that are supposed to yield raw material only.
3. **The crafting loop is dead.** Gunpowder averages 5.5 per map against 192 Scrap, so at most
   ~6 bullets can ever be crafted. Ammo crafting exists on paper only; the player shoots what they
   find. Alcohol at 2.4 does the same to Ink, Bandage and Torch — every recipe needing it is gated
   behind a material the map barely contains.
4. **Scrap is worthless.** 192 against a demand of ~87, and it is the only material barrels and
   corpses drop in quantity, so the flood cannot be avoided.

## The tables now in the assets

Applied to `Barrel.prefab`, both enemy prefabs and `RoomContentSettings.asset`.
Targets: R3/R5 satisfied by construction, Gunpowder made the ammo throttle, and total ammo
(found + craftable) held at **~40 % of the enemy HP pool** — well under R4's ceiling, so most of a
map has to be walked past rather than shot through.

### Barrels — `Barrel.prefab -> dropsOnDestroy` (~93 barrels)

| Item | Chance | Qty |
|---|---|---|
| Scrap | 40 % | 1–3 |
| Wood | 30 % | 1–2 |
| Rags | 12 % | 1–2 |
| Alcohol | 10 % | 1 |

Bullet removed (R3). Barrels are the bulk-material source: cheap to break, no ammo, no products.

### Enemies — `possibleRandomDrops` on both enemy prefabs (~47 enemies)

| Item | Chance | Qty |
|---|---|---|
| Scrap | 70 % | 1–3 |
| Gunpowder | 30 % | 1–2 |
| Rags | 25 % | 1–2 |
| Alcohol | 20 % | 1 |

Bullet removed (R5). **Watch the picker**: `EnemyBase.GenerateLoot` draws `min(2, list.Count)`
random entries and only then rolls each one's chance, so with four entries every line fires at
**half** its stated chance. The numbers above already account for that; adding a fifth entry
silently cuts everything to 40 %.

### Ordinary chests — `chestLoot` (~11 chests, 1–3 stacks each)

Simple materials plus a modest ammo trickle, per R4.

| Item | Weight | Qty |
|---|---|---|
| Gunpowder | 1.4 | 2–5 |
| Scrap | 1.2 | 2–5 |
| Wood | 0.9 | 2–4 |
| Bullet | 0.9 | 2–5 |
| Alcohol | 0.8 | 1–3 |
| Rags | 0.6 | 2–3 |
| Shell | 0.4 | 1–3 |

### Treasure chests — `treasureChestLoot` (3 chests, 2–4 stacks each)

Varied, per R2: the only place finished products show up, which is what a spent key buys.

| Item | Weight | Qty |
|---|---|---|
| Bandage | 1.0 | 1–2 |
| Ink | 1.0 | 1 |
| Bullet | 1.0 | 6–10 |
| Gunpowder | 1.0 | 4–8 |
| Torch | 0.8 | 1 |
| Plank | 0.8 | 2–4 |
| Shell | 0.8 | 3–6 |
| Alcohol | 0.8 | 2–3 |

### Resulting budget

| Material | Supply | Demand | Ratio |
|---|---|---|---|
| Scrap | 122 | 87 | 1.40 |
| Wood | 51 | 35 | 1.46 |
| Rags | 31 | 16 | 1.92 |
| Alcohol | 22 | 17 | 1.30 |
| Gunpowder | 35 | — | throttle |

Ammo: 21 Bullet + 7 Shell found (889 dmg, 19 % of the HP pool) plus 35 craftable bullets
(879 dmg, 19 %) = **38 % of the enemy HP pool**. The player can fight roughly 18 of 47 enemies and
has to avoid the rest. Moving *that one percentage* is the difference between a shooter and a
stealth game, and Gunpowder is the dial: one Gunpowder on the map is exactly one bullet, i.e.
25 damage.

Rags run rich (1.92) because bandages are their only sink. If a rag-fuelled item (a molotov, a
bandaged trap) never arrives, drop the barrel rate to 8 %.

---

## Bugs and blockers found while measuring

| Thing | Where | Why it matters |
|---|---|---|
| **Bandage heals 0** | `Assets/Items/Item 16 - Bandage.asset`, `healAmount: 0` | Its recipe's only purpose does nothing, so the Rags + Alcohol sink does not exist in play. Set it before trusting any healing figure here. |
| Floor loot is switched off | `RoomContentSettings.asset`: `minLootPerRoom`/`maxLootPerRoom` = 0, `treasureLootCount` = 0, `guaranteedItemDrops` = 0 | The `loot`, `treasureLoot` and guaranteed-ink settings are dead config. All supply currently comes from barrels, corpses and chests — either wire the floor tables in or delete them, but do not tune them expecting an effect. |
| Debug loadout ships enabled | `Player.prefab -> SlotInventoryDebugFill`, `m_Enabled: 1` | The player starts with axe + pistol + shotgun + 8 Bullet + 3 Shell + 16 Wood + 6 Scrap + 5 Gunpowder. Every balance figure above assumes this is off. |
| Repair button has no scrap item wired | `Canvas.prefab -> WeaponRepairButton.scrapItem: {fileID: 0}` | It resolves at runtime, but the authored value is empty — worth pinning down, since axe repair is the cheapest kill cost in the game. |

## "Chests" vs "crates" — settled

The guideline separates chests (varied) from crates (simple materials + limited ammo). The project
has exactly one container prefab, `world.chest`, and the pair is read as **ordinary room chests =
crates** and **treasure-room chests = chests**. That is what the tables above implement, and it
keeps the whole rule set inside the existing generator: no new prefab, no `PrefabRegistry` id, no
extra spawn pass. Should a distinct crate prop ever be wanted as a separate object, all three of
those become necessary.
