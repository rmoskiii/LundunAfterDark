# Lundun world data contract — schema 1.1

`LundunWorld.json` is the single source of truth for the Lundun map. The greybox roads (Phase 1), the lane graph, GPS and map (Phase 2) are all generated from it. **Never hand-edit the JSON.** Edit `world.py`, then run the tools below. The exporter refuses to write a file that fails validation.

```
python3 validate.py              # checks the hand-authored layout in world.py
python3 export.py                # writes LundunWorld.json (only if validate_world.py passes)
python3 validate_world.py LundunWorld.json      # independent check: reads ONLY the JSON
python3 test_validator.py LundunWorld.json      # proves the validator catches 17 deliberate breaks
```

`validate_world.py` never imports `world.py` or the exporter. That way the data cannot quietly agree with itself.

---

## 1. Conventions

| Item | Rule |
|---|---|
| Units | metres; 1 Unity unit = 1 m |
| Axes | +X east, +Z north, +Y up. Plan points are `{x, z}` objects (no nested arrays, so Unity's `JsonUtility` can read them) |
| Origin | (0, 0) = south-west corner of the 1600 × 1200 m world |
| Yaw | degrees about +Y; 0 = north, 90 = east, clockwise seen from above |
| Traffic | UK, drive on the left |
| IDs | `R##` ordinary road · `B#` bridge road · `J##` junction (numbered north→south, then west→east) · `D_*` district · `SP_*` spawn |

## 2. What each field means

| Field | Meaning |
|---|---|
| `roads[]` | **Every drivable link, including bridges.** A bridge is a road whose `roadClass` is `"bridge"` and whose id is `B#`. |
| `bridges[]` | Extra measurements for bridge roads. Each entry points at its road with `roadId`. **It does not add roads.** |
| `validation.roadEntries` | Number of entries in `roads[]` (= `ordinaryRoads` + `bridgeRoads`). In v1.0 this was the ambiguous `roads: 30`. |
| junction | A point where a road starts or ends, or where 2+ roads meet. Its x/z lie on every listed road's centreline (±0.1 m). |
| `legs[]` | One leg = one direction you can drive **away** from the junction along one road. `toward: "start"` heads for that road's first point, `"end"` for its last. |
| `arms` | `= legs.length`. A road **passing through** gives 2; a road **ending** here gives 1. Allowed 2–4. 2 only where one road ends exactly where another begins (J41: Westferry Road → Quay Loop). |
| `junctions[]` (on a road) | Every junction on that road, **in order** from its first point. The first and last are the road's two ends. |
| `junctionChainage[]` | Metres along the road from its first point to each listed junction, same order. |
| `lengthM` | Length of the centreline polyline. For a bridge road: the full deck including approaches. |
| `waterSpanM` | Length of the bridge centreline **over water**, bank line to bank line (35 m either side of the river centreline). |
| `wallToWallM` | Length over water **plus** the 4 m embankment walls (39 m either side). |
| `northApproachM` / `southApproachM` | Deck length over land at each end. `north + wallToWall + south = lengthM`. |
| `crossingAngleDeg` | Angle between bridge and river where they cross. 90 = perpendicular. |
| district | Area label and building zone. **May** contain roads; **must not** overlap water/walls or another district. |
| park | Green space. Must not overlap any road, pavement or water. |
| `validation` | Counts computed by the exporter from the arrays. The validator recomputes them and fails on any mismatch. Never typed by hand. |

## 3. Validation rules (V1–V16)

| Rule | Checks | Tolerance |
|---|---|---|
| V1 | Required top-level fields present | — |
| V2 | IDs unique and well formed; every road has a known class and ≥ 2 points | — |
| V3 | Every referenced ID exists: road↔junction, leg→road, bridge→road (and that road is class `bridge`), bridge→junctions | — |
| V4 | Membership agrees both ways: if a junction lists a road, that road lists the junction, and vice versa | — |
| V5 | Each junction lies on every road it lists, **and** lists every road it lies on | 0.10 m |
| V6 | `arms` and `legs` equal what the geometry gives; 2–4 arms | exact |
| V7 | Junctions in order along each road; the road's **actual end points** are its first/last junctions (no dead ends); junctions ≥ 25 m apart; declared chainage matches | 0.10 m |
| V8 | `lengthM` matches the polyline; width matches the class or a declared `widthOverrides` | 0.50 m |
| V9 | Bridge ends = road ends = stated junctions; north end really is north; exactly one water crossing; both ends on land; `waterSpanM`, `wallToWallM` and approaches match measurements | 0.50 m |
| V10 | Only bridges enter the river corridor; ordinary road edges keep ≥ 6 m of land from the wall; no two roads cross without a junction | 6 m |
| V11 | One connected network | — |
| V12 | Districts: no water/wall, no district overlaps. Parks: clear of roads, pavements, water (2 m sampling) | 0 m |
| V13 | POIs off the carriageway (landmarks excepted); spawns on/next to roads; police spawns on a carriageway and ≥ 40 m from junctions | 40 m |
| V14 | Only the replaced road (Peckam High Street) passes through the existing street footprint | — |
| V15 | Summary counts equal the arrays and are unambiguous | exact |
| V16 | Field meanings declared in the file (`definitions`, schema ≥ 1.1) | — |

## 4. Audit log

**12 Oct 2026 — external review of v1.0.** Every point was checked against the file:

| # | Point raised | Finding | Action |
|---|---|---|---|
| 1 | Arm counts don't match listed roads (J01: 3 arms, 2 roads) | Consistent at all 58 junctions under "through road = 2, ending road = 1", but **undocumented** | Rule defined; explicit `legs[]` added; V6 checks it |
| 2 | R19 omits J51 | R19 does list J51 (`J38 J46 J51 J57`); 0 two-way mismatches anywhere | V4 now enforces it permanently |
| 3 | Junctions must lie on their roads | Worst case 0.044 m (J37, rounding) | V5 with a 0.10 m tolerance, both directions |
| 4 | Bridge length vs water span undefined | Measured, but not defined, and rounded to whole metres | Defined (bank-to-bank, wall-to-wall, approaches, angle) and checked by V9 |
| 5 | `roads: 30` vs 25 roads | 25 ordinary + 5 bridge roads; `bridges[]` describes those same 5. Consistent but **ambiguous** | Replaced by `roadEntries` / `ordinaryRoads` / `bridgeRoads` / `bridgeDetails`; V15 |

**New defects found by the independent validator** (not raised in the review):

| Defect | Fix in v1.1 |
|---|---|
| Canary Quay peninsula outline entered water/wall by up to 10.8 m (≈ 664 m²) | 3 corners moved by ≤ 13 m |
| Peckam outline entered water/wall by up to 2.2 m (≈ 112 m²) | 1 corner moved 3.4 m |
| Quay Gardens park clipped the pavement of R15 Quay Loop | 1 corner moved 10 m |
| The first validator version missed a shortened road (dead end), found by the mutation tests | V7 now compares real end points |

**Road and junction geometry is identical between v1.0 and v1.1.** Only the three outlines above changed.

## 5. Learning task P1.1: the same validator in C#

Rebuild V1–V16 inside Unity, using the Python validator as the answer key.

1. `Assets/Scripts/World/WorldData.cs`: plain `[Serializable]` classes that mirror the JSON (`WorldData`, `RoadData`, `PointData`, `JunctionData`, `LegData`, `BridgeData` …). Load with `JsonUtility.FromJson<WorldData>(text)`.
2. `Assets/Editor/World/WorldValidator.cs`: menu **Lundun → Validate World Data**. Start with the structural rules **V1–V8**, which need only lists and maths. Then add V9–V16.
3. **Pass:** the Console output matches `expected_output_v1.1.txt` rule for rule (16/16 PASS).
4. **Fail correctly:** each file in `fixtures/` must fail the rule named at the start of its filename (e.g. `V9_bridge_water_span_wrong.json` → V9 fails).

Done = clean file 16/16, and all 17 fixtures fail their named rule.
