# TODO

Known issues worth their own ticket. Both were spotted while optimizing pathfinding (GU-0062);
neither is a regression from it — they predate that work and are unrelated to performance.

## Grid is never rebuilt after a door opens or breaks

`PathfindingGrid.BuildGrid()` runs only from `Awake()` and `Configure()`, and walkability is
sampled from physics at that moment. A door that is closed while the grid is built leaves its
cells permanently blocked, so an enemy cannot path through a door it just smashed open
(`EnemyDoorAttacker`) — it will keep attacking or walk away instead of following the player
through.

Sketch: give the grid a `RefreshArea(Bounds)` that re-samples only the affected cells and
re-runs the region flood fill, then have doors call it when their collider state changes.
Note that connected-region ids (`PathfindingGrid.GetRegionId`) are derived from walkability, so
any partial refresh must update them too or reachability checks go stale.

Files: `Assets/Scripts/Pathfinding/PathfindingGrid.cs`, `Assets/Scripts/Objects/` (door scripts),
`Assets/Scripts/Enemy/EnemyDoorAttacker.cs`

## `InvestigateNoise` has no timeout

`EnemyBase` leaves `EnemyState.InvestigateNoise` only when the player is detected or the enemy
gets within `investigateArrivalThreshold` of `noiseTargetPosition`. When the noise came from a
spot the enemy cannot stand on or reach (inside a wall, across a sealed region), that distance is
never closed and the enemy stays in the state indefinitely.

Sketch: add a max investigation duration, and fall through to `EndInvestigation()` when it
expires. The same guard would cover `InvestigateLastKnown`.

Files: `Assets/Scripts/Enemy/EnemyBase.cs` (`UpdateStateMachine`, `EndInvestigation`)
