"""Geometric validator for the Lundun road network (no rasters).
Checks: every road end lands on another road (or is a declared dead end), crossings become junctions,
bridges start and end on land and connect to roads on both banks, non-bridge roads keep clear of the water,
and the whole network is one connected piece."""
import math, sys, importlib
import world; importlib.reload(world)
from world import *

TOL = 1.0            # metres: an end this close to another road counts as connected
NEAR = 25.0          # metres: an end this close but not connected is reported as a likely mistake
WALL = 4.0           # embankment wall thickness beyond the water edge
MIN_CLEAR = 6.0      # metres of land required between road edge and embankment wall

def seg_closest(p, a, b):
    ax, az = a; bx, bz = b; px, pz = p
    dx, dz = bx - ax, bz - az; L2 = dx * dx + dz * dz
    t = 0 if L2 == 0 else max(0, min(1, ((px - ax) * dx + (pz - az) * dz) / L2))
    q = (ax + t * dx, az + t * dz); return math.dist(p, q), q, t

def poly_closest(p, pts):
    best = (1e9, None, None)
    for i, (a, b) in enumerate(zip(pts, pts[1:])):
        d, q, t = seg_closest(p, a, b)
        if d < best[0]: best = (d, q, i + t)
    return best

def seg_intersect(a, b, c, d):
    def cross(o, p, q): return (p[0] - o[0]) * (q[1] - o[1]) - (p[1] - o[1]) * (q[0] - o[0])
    d1, d2, d3, d4 = cross(c, d, a), cross(c, d, b), cross(a, b, c), cross(a, b, d)
    if ((d1 > 0) != (d2 > 0)) and ((d3 > 0) != (d4 > 0)) and d1 * d2 < 0 and d3 * d4 < 0:
        t = d1 / (d1 - d2); return (a[0] + t * (b[0] - a[0]), a[1] + t * (b[1] - a[1]))
    return None

def river_dist(p):  # distance from a point to the river centreline
    return poly_closest(p, RIVER)[0]

def sample(pts, step=2.0):
    for a, b in zip(pts, pts[1:]):
        n = max(1, int(math.dist(a, b) / step))
        for k in range(n + 1):
            t = k / n; yield (a[0] + t * (b[0] - a[0]), a[1] + t * (b[1] - a[1]))

def run(verbose=True):
    problems, notes = [], []
    roads = [(n, c, [tuple(map(float, p)) for p in pts]) for n, c, pts in ROADS]
    water_half = RIVER_WIDTH / 2

    # ---------- 1. every road end must sit on another road ----------
    nodes = []          # (x, z) junction points
    def add_node(p):
        for i, q in enumerate(nodes):
            if math.dist(p, q) < 3.0: return i
        nodes.append(p); return len(nodes) - 1
    ends = {}
    for n, c, pts in roads:
        for end, p in (('start', pts[0]), ('end', pts[-1])):
            best = (1e9, None, None)
            for m, c2, pts2 in roads:
                if m == n: continue
                d, q, _ = poly_closest(p, pts2)
                if d < best[0]: best = (d, q, m)
            ends[(n, end)] = best
            if best[0] <= TOL: add_node(p)
            elif n in DEAD_ENDS_OK: notes.append(f'{n} {end} is a declared dead end')
            elif best[0] <= NEAR: problems.append(f'{n} {end} {p} misses {best[2]} by {best[0]:.1f} m (snap to {tuple(round(v,1) for v in best[1])})')
            else: problems.append(f'{n} {end} {p} is a dead end ({best[0]:.0f} m from the nearest road)')

    # ---------- 2. crossings become junctions (and must not happen on bridges over water) ----------
    crossings = []
    for i, (n, c, pts) in enumerate(roads):
        for m, c2, pts2 in roads[i + 1:]:
            for a, b in zip(pts, pts[1:]):
                for cc, dd in zip(pts2, pts2[1:]):
                    x = seg_intersect(a, b, cc, dd)
                    if x:
                        crossings.append((n, m, x)); add_node(x)
                        if river_dist(x) < water_half + WALL: problems.append(f'{n} crosses {m} over the river at {x}')

    # ---------- 3. water rules ----------
    bridges = []
    for n, c, pts in roads:
        w = ROAD_WIDTH[c] / 2
        min_clear = min(river_dist(p) for p in sample(pts)) - water_half - WALL - w
        if c == 'bridge':
            s_land = river_dist(pts[0]) - water_half - WALL; e_land = river_dist(pts[-1]) - water_half - WALL
            over = [p for p in sample(pts, 1.0) if river_dist(p) < water_half]
            span = len(over) * 1.0
            ok = s_land > 0 and e_land > 0 and span > 40
            bridges.append(dict(name=n, start=pts[0], end=pts[-1], length=math.dist(pts[0], pts[-1]), span=span,
                                start_land=s_land, end_land=e_land,
                                start_road=ends[(n, 'start')][2], end_road=ends[(n, 'end')][2]))
            if not ok: problems.append(f'Bridge {n}: start on land {s_land:.1f} m, end on land {e_land:.1f} m, span {span:.0f} m')
        else:
            if min_clear < 0: problems.append(f'{n} enters the river/embankment by {-min_clear:.1f} m')
            elif min_clear < MIN_CLEAR: problems.append(f'{n} is only {min_clear:.1f} m from the embankment wall (want ≥ {MIN_CLEAR} m)')

    # ---------- 4. connectivity on the road graph ----------
    parent = list(range(len(nodes)))
    def find(i):
        while parent[i] != i: parent[i] = parent[parent[i]]; i = parent[i]
        return i
    road_nodes = {}
    for n, c, pts in roads:
        on = []
        for i, q in enumerate(nodes):
            d, _, pos = poly_closest(q, pts)
            if d <= 3.0: on.append((pos, i))
        on.sort(); road_nodes[n] = [i for _, i in on]
        for (_, a), (_, b) in zip(on, on[1:]): parent[find(a)] = find(b)
        if len(on) < 2: problems.append(f'{n} touches fewer than 2 junctions')
    comps = {find(i) for i in range(len(nodes))}
    if len(comps) != 1: problems.append(f'Network splits into {len(comps)} separate pieces')
    degree = {i: 0 for i in range(len(nodes))}
    for n, ids in road_nodes.items():
        for a, b in zip(ids, ids[1:]): degree[a] += 1; degree[b] += 1

    # ---------- 4b. junction spacing and near-misses ----------
    for n, ids in road_nodes.items():
        c = [cc for nn, cc, _ in roads if nn == n][0]
        for a, b in zip(ids, ids[1:]):
            gap = math.dist(nodes[a], nodes[b])
            if gap < 25: problems.append(f'{n}: junctions only {gap:.0f} m apart at {nodes[a]} / {nodes[b]}')
    for i, (n, c, pts) in enumerate(roads):
        for m, c2, pts2 in roads[i + 1:]:
            shared = set(road_nodes[n]) & set(road_nodes[m])
            if shared: continue
            dmin = min(poly_closest(p, pts2)[0] for p in sample(pts, 2))
            need = ROAD_WIDTH[c] / 2 + ROAD_WIDTH[c2] / 2 + 12
            if dmin < need: problems.append(f'{n} and {m} pass within {dmin:.0f} m without joining (want ≥ {need:.0f} m)')
    # junction sizes
    arms = {}
    for n, ids in road_nodes.items():
        for k, i in enumerate(ids):
            arms[i] = arms.get(i, 0) + (1 if k in (0, len(ids) - 1) else 2)
    for i, a in arms.items():
        if a > 4: problems.append(f'Junction at {nodes[i]} has {a} arms (max 4 for a readable junction)')

    # ---------- 4c. parks and the existing street must be free of roads ----------
    def inside(p, poly):
        x, z = p; c = False
        for (x1, z1), (x2, z2) in zip(poly, poly[1:] + poly[:1]):
            if (z1 > z) != (z2 > z) and x < (x2 - x1) * (z - z1) / (z2 - z1) + x1: c = not c
        return c
    def road_clear(p, extra, skip=()):
        return min(poly_closest(p, pts)[0] - (WIDTH_OVERRIDE.get(n, ROAD_WIDTH[c]) / 2 + extra)
                   for n, c, pts in roads if n not in skip)
    for name, poly in PARKS:
        xs = [q[0] for q in poly]; zs = [q[1] for q in poly]
        bad = [(x, z) for x in range(int(min(xs)), int(max(xs)), 5) for z in range(int(min(zs)), int(max(zs)), 5)
               if inside((x, z), poly) and road_clear((x, z), 3.0) < 0]
        if bad: problems.append(f'Park {name} overlaps a road/pavement at {len(bad)} sample points, e.g. {bad[0]}')
        if any(river_dist((x, z)) < RIVER_WIDTH / 2 + WALL for x in range(int(min(xs)), int(max(xs)), 5) for z in range(int(min(zs)), int(max(zs)), 5) if inside((x, z), poly)):
            problems.append(f'Park {name} is in the river')
    es = EXISTING_STREET; cx, cz = es['centre']; hw, hd = es['size'][0] / 2, es['size'][1] / 2
    rect = [(cx - hw, cz - hd), (cx + hw, cz - hd), (cx + hw, cz + hd), (cx - hw, cz + hd)]
    for n, c, pts in roads:
        if n == 'Peckam High Street': continue
        if any(inside(p, rect) for p in sample(pts, 1)) or any(seg_intersect(a, b, r1, r2) for a, b in zip(pts, pts[1:]) for r1, r2 in zip(rect, rect[1:] + rect[:1])):
            problems.append(f'{n} runs through the existing street footprint')
    for name, p, kind in POIS:
        if kind != 'landmark' and road_clear(p, 0) < 0: problems.append(f'POI {name} sits on a road surface')

    # ---------- 5. POIs must be near a road ----------
    for name, p, kind in POIS:
        d = min(poly_closest(p, pts)[0] for n, c, pts in roads)
        if d > 90: problems.append(f'POI {name} is {d:.0f} m from any road')

    if verbose:
        print(f'roads {len(roads)} · junction nodes {len(nodes)} · crossings {len(crossings)} · bridges {len(bridges)} · pieces {len(comps)}')
        for b in bridges:
            print(f"  {b['name']:20s} {b['start']} → {b['end']}  len {b['length']:.0f} m, over water {b['span']:.0f} m, "
                  f"land at ends {b['start_land']:.0f}/{b['end_land']:.0f} m, joins {b['start_road']} ↔ {b['end_road']}")
        print('PROBLEMS:' if problems else 'NO PROBLEMS'); [print('  ✗', p) for p in problems]
        [print('  ·', n) for n in notes]
    return dict(arms=arms, problems=problems, nodes=nodes, road_nodes=road_nodes, bridges=bridges, crossings=crossings, degree=degree, ends=ends)

if __name__ == '__main__':
    r = run()
    sys.exit(1 if r['problems'] else 0)
