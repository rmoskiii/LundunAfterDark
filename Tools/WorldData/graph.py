"""Junction graph built from the validated world, plus exact-point routing (the Phase 2 GPS in miniature)."""
import heapq, math
import validate
from world import *

R = validate.run(verbose=False)
NODES = R['nodes']
ROADS_F = [(n, c, [tuple(map(float, p)) for p in pts]) for n, c, pts in ROADS]

def _arc(pts):
    acc = [0.0]
    for a, b in zip(pts, pts[1:]): acc.append(acc[-1] + math.dist(a, b))
    return acc

def _pos_on(p, pts, A):
    d, q, t = validate.poly_closest(p, pts); k = min(int(t), len(pts) - 2); f = t - k
    return A[k] + math.dist(pts[k], pts[k + 1]) * f, q, d

# road -> sorted [(arc position, node id)]
ROAD_POS = {}
ADJ = {}
for n, c, pts in ROADS_F:
    A = _arc(pts)
    pos = sorted((_pos_on(NODES[i], pts, A)[0], i) for i in R['road_nodes'][n])
    ROAD_POS[n] = (pts, A, pos)
    for (la, a), (lb, b) in zip(pos, pos[1:]):
        ADJ.setdefault(a, []).append((b, lb - la, n)); ADJ.setdefault(b, []).append((a, lb - la, n))

def _snap(p):
    best = None
    for n, (pts, A, pos) in ROAD_POS.items():
        L, q, d = _pos_on(p, pts, A)
        if best is None or d < best[0]: best = (d, n, L, q)
    return best

def route(a, b):
    """Shortest road route between two world points. Returns (metres, [road names], polyline)."""
    _, na, La, qa = _snap(a); _, nb, Lb, qb = _snap(b)
    S, T = 'S', 'T'; adj = {k: list(v) for k, v in ADJ.items()}
    for tag, n, L in ((S, na, La), (T, nb, Lb)):
        pts, A, pos = ROAD_POS[n]
        before = [x for x in pos if x[0] <= L]; after = [x for x in pos if x[0] > L]
        for (Lx, i) in ([before[-1]] if before else []) + ([after[0]] if after else []):
            adj.setdefault(tag, []).append((i, abs(L - Lx), n)); adj.setdefault(i, []).append((tag, abs(L - Lx), n))
    if na == nb:  # same road: direct link too
        adj[S].append((T, abs(La - Lb), na))
    import itertools; tick = itertools.count()
    dist = {S: 0}; prev = {}; pq = [(0, next(tick), S)]
    while pq:
        d, _, u = heapq.heappop(pq)
        if u == T: break
        if d > dist[u]: continue
        for v, w, n in adj.get(u, []):
            if d + w < dist.get(v, 1e9): dist[v] = d + w; prev[v] = (u, n); heapq.heappush(pq, (d + w, next(tick), v))
    seq = []; u = T
    while u != S: seq.append(prev[u]); u = prev[u][0]
    seq.reverse()
    names = []
    for _, n in seq:
        if not names or names[-1] != n: names.append(n)
    pts = [qa] + [NODES[u] for u, _ in seq[1:]] + [qb]
    return dist[T], names, pts

if __name__ == '__main__':
    home = POIS[0][1]
    for name, p, k in POIS[1:5]:
        L, names, _ = route(home, p); print(f'{name:36s} {L:5.0f} m ~{L/15:3.0f}s  {" → ".join(names)}')
    L, names, _ = route(POIS[1][1], POIS[3][1]); print(f'{"Estate → Warehouse 9":36s} {L:5.0f} m ~{L/15:3.0f}s  {" → ".join(names)}')
