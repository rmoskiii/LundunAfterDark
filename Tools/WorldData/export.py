"""Export the validated world as LundunWorld.json (Unity JsonUtility-friendly: no nested arrays)."""
import json, math
import validate
from world import *
from graph import route

R = validate.run(verbose=False)
assert not R['problems'], R['problems']
nodes = R['nodes']

CLASSES = {
    'ring':     dict(width=22, lanesPerDirection=2, speedLimitMps=22.4, speedLimitMph=50, pavement=0.0, verge=3.0, centralReservation=2.0, markings='dual carriageway, solid edge, dashed lane'),
    'arterial': dict(width=15, lanesPerDirection=2, speedLimitMps=13.4, speedLimitMph=30, pavement=4.0, verge=0.0, centralReservation=0.0, markings='white centre line, dashed lanes, double yellows'),
    'street':   dict(width=10, lanesPerDirection=1, speedLimitMps=8.9, speedLimitMph=20, pavement=3.0, verge=0.0, centralReservation=0.0, markings='white dashed centre, double yellows'),
    'service':  dict(width=7,  lanesPerDirection=1, speedLimitMps=6.7, speedLimitMph=15, pavement=1.5, verge=0.0, centralReservation=0.0, markings='none'),
    'bridge':   dict(width=15, lanesPerDirection=1, speedLimitMps=13.4, speedLimitMph=30, pavement=3.5, verge=0.0, centralReservation=0.0, markings='white centre line; parapets 1.1 m'),
}

# Junction IDs in reading order (north to south, west to east)
order = sorted(range(len(nodes)), key=lambda i: (-round(nodes[i][1]), round(nodes[i][0])))
jid = {i: f'J{k+1:02d}' for k, i in enumerate(order)}
road_ids = {}
bridge_names = ['Westbridge', 'Rye Bridge', 'Quay Bridge', 'Ring West Crossing', 'Ring East Crossing']
k = 0
for n, c, pts in ROADS:
    if c == 'bridge': road_ids[n] = f'B{bridge_names.index(n)+1}'
    else: k += 1; road_ids[n] = f'R{k:02d}'

def chain(p, pts):
    best = (1e18, 0.0); acc = 0.0
    for a, b in zip(pts, pts[1:]):
        d, q, t = validate.seg_closest(p, a, b)
        if d < best[0] - 1e-9: best = (d, acc + t * math.dist(a, b))
        acc += math.dist(a, b)
    return best[1]
roads_out = []
for n, c, pts in ROADS:
    pts = [tuple(map(float, p)) for p in pts]
    L = sum(math.dist(a, b) for a, b in zip(pts, pts[1:]))
    ids = R['road_nodes'][n]
    roads_out.append(dict(id=road_ids[n], name=n, roadClass=c, width=WIDTH_OVERRIDE.get(n, ROAD_WIDTH[c]), lengthM=round(L, 2),
                          points=[dict(x=p[0], z=p[1]) for p in pts],
                          junctions=[jid[i] for i in ids],
                          junctionChainage=[round(chain(nodes[i], pts), 2) for i in ids]))
junctions_out = []
for i in order:
    rs = [road_ids[n] for n, ids in R['road_nodes'].items() if i in ids]
    legs = []
    for n, ids in R['road_nodes'].items():
        if i not in ids: continue
        pts = [tuple(map(float, p)) for p in dict((a, b) for a, _, b in ROADS)[n]]
        c = chain(nodes[i], pts); L = sum(math.dist(a, b) for a, b in zip(pts, pts[1:]))
        if c > 0.1: legs.append(dict(road=road_ids[n], toward='start'))
        if c < L - 0.1: legs.append(dict(road=road_ids[n], toward='end'))
    assert len(legs) == R['arms'][i]
    junctions_out.append(dict(id=jid[i], x=round(nodes[i][0], 2), z=round(nodes[i][1], 2), arms=len(legs), roads=rs, legs=legs))
def measure_bridge(bp):
    half, wall = RIVER_WIDTH / 2, 4.0
    s = []; total = sum(math.dist(a, b) for a, b in zip(bp, bp[1:])); n = int(total / 0.05)
    a, b = bp[0], bp[-1]
    s = [(a[0] + (b[0] - a[0]) * k / n, a[1] + (b[1] - a[1]) * k / n) for k in range(n + 1)]
    rd = lambda p: validate.poly_closest(p, [tuple(map(float, q)) for q in RIVER])[0]
    wet = [rd(p) < half for p in s]; wal = [rd(p) < half + wall for p in s]
    step = total / n
    first_wet = wet.index(True); last_wet = len(wet) - 1 - wet[::-1].index(True)
    first_wal = wal.index(True); last_wal = len(wal) - 1 - wal[::-1].index(True)
    # crossing angle: bridge direction vs river direction at the middle of the water
    mid = s[(first_wet + last_wet) // 2]; _, q, t = validate.poly_closest(mid, [tuple(map(float, q)) for q in RIVER])
    k = min(int(t), len(RIVER) - 2); rv = (RIVER[k + 1][0] - RIVER[k][0], RIVER[k + 1][1] - RIVER[k][1]); bv = (b[0] - a[0], b[1] - a[1])
    ang = math.degrees(math.acos(abs(rv[0] * bv[0] + rv[1] * bv[1]) / (math.hypot(*rv) * math.hypot(*bv))))
    return dict(waterSpanM=round(sum(wet) * step, 1), wallToWallM=round(sum(wal) * step, 1),
                approachFirstM=round(first_wal * step, 1), approachLastM=round((len(s) - 1 - last_wal) * step, 1),
                waterEntry=dict(x=round(s[first_wet][0], 1), z=round(s[first_wet][1], 1)), waterExit=dict(x=round(s[last_wet][0], 1), z=round(s[last_wet][1], 1)),
                crossingAngleDeg=round(ang, 1))
bridges_out = []
for b in sorted(R['bridges'], key=lambda b: bridge_names.index(b['name'])):
    n_end, s_end = (b['start'], b['end']) if b['start'][1] > b['end'][1] else (b['end'], b['start'])
    nj = min(nodes, key=lambda q: math.dist(q, n_end)); sj = min(nodes, key=lambda q: math.dist(q, s_end))
    m = measure_bridge([tuple(map(float, p)) for p in dict((a, c) for a, _, c in ROADS)[b['name']]])
    first_is_north = tuple(map(float, dict((a, c) for a, _, c in ROADS)[b['name']][0])) == tuple(n_end)
    bridges_out.append(dict(id=road_ids[b['name']], roadId=road_ids[b['name']], name=b['name'], northEnd=dict(x=n_end[0], z=n_end[1]), southEnd=dict(x=s_end[0], z=s_end[1]),
                            northApproachM=m['approachFirstM'] if first_is_north else m['approachLastM'],
                            southApproachM=m['approachLastM'] if first_is_north else m['approachFirstM'],
                            wallToWallM=m['wallToWallM'], waterEntry=m['waterEntry'], waterExit=m['waterExit'], crossingAngleDeg=m['crossingAngleDeg'],
                            northJunction=jid[nodes.index(nj)], southJunction=jid[nodes.index(sj)],
                            lengthM=round(b['length'], 2), waterSpanM=m['waterSpanM'], width=WIDTH_OVERRIDE.get(b['name'], 15),
                            deckY=0.0, deckThickness=1.2, parapetHeight=1.1, northRoad=b['start_road'] if b['start'] == n_end else b['end_road'],
                            southRoad=b['end_road'] if b['start'] == n_end else b['start_road']))

POI_EXTRA = {  # yaw: Unity degrees, 0 = facing north (+Z), 90 = east, clockwise from above
    'Safehouse': dict(id='POI_SAFEHOUSE', yaw=180), 'M1 The Pickup (Gilmore courtyard)': dict(id='M1_ONFOOT', yaw=0),
    'M2 Kestrel showroom': dict(id='M2', yaw=180), 'M3 Warehouse 9': dict(id='M3', yaw=180), 'M4 Tower Plaza meet': dict(id='M4', yaw=0),
    'Police station': dict(id='POI_POLICE', yaw=90), 'Hospital': dict(id='POI_HOSPITAL', yaw=180),
    'Knightsbury Circus': dict(id='LM_CIRCUS', yaw=0), 'Quay Towers': dict(id='LM_QUAY_TOWERS', yaw=0)}
pois_out = [dict(id=POI_EXTRA[n]['id'], name=n, kind=k, x=float(p[0]), z=float(p[1]), yaw=POI_EXTRA[n]['yaw']) for n, p, k in POIS]

SPAWNS = [
    dict(id='SP_PLAYER_START', x=905.0, z=197.0, yaw=180, note='Pavement outside the safehouse door; confirm against the prefab pavement (test G4)'),
    dict(id='SP_KESTREL', x=934.0, z=183.6, yaw=270, note='Parked up on the south pavement, Peckam High Street (east of the old street, out of the traffic lane)'),
    dict(id='SP_BUSTED', x=780.5, z=250.0, yaw=90, note='Police station steps, Rye Lane west pavement'),
    dict(id='SP_WASTED', x=240.0, z=741.0, yaw=180, note='Hospital entrance, North Embankment north pavement'),
    dict(id='SP_M1_PARK', x=300.0, z=476.0, yaw=270, note='Estate car park drop-off (drive target for M1)'),
] + [dict(id=f'SP_POLICE_{k+1}', x=x, z=z, yaw=y, note='Police car spawn on the ring; builder offsets to the left-hand lane')
     for k, (x, z, y) in enumerate([(300, 40, 90), (1300, 40, 270), (200, 1160, 90), (1300, 1160, 270),
                                    (40, 420, 0), (40, 1020, 180), (1560, 300, 0), (1560, 1020, 180)])]

DEFINITIONS = {
    'roadEntries': 'Every drivable link in roads[], ordinary roads (R##) AND bridge roads (B#). Bridges are roads with roadClass "bridge".',
    'bridges': 'bridges[] adds measurements for the bridge roads; each entry points at its road via roadId. It does not add roads.',
    'junction': 'A point where a road starts or ends, or where 2+ roads meet. Junction x/z lie on every listed road centreline (±0.1 m).',
    'legs': 'One leg = one direction you can drive away from the junction along one road. toward = which end of that road you head for ("start" = its first point).',
    'arms': 'Number of legs. A road passing through gives 2, a road ending here gives 1. Allowed 2–4; 2 only where one road ends exactly where another begins.',
    'junctionChainage': 'Distance in metres along the road from its first point to each listed junction, same order as junctions[].',
    'lengthM': 'Length of the road centreline polyline, in metres. For a bridge road this is the full deck length including approaches.',
    'waterSpanM': 'Length of the bridge centreline over water: between the two bank lines (35 m either side of the river centreline).',
    'wallToWallM': 'Length of the bridge centreline over water + embankment walls (39 m either side of the river centreline).',
    'approachM': 'northApproachM / southApproachM: bridge deck over land at each end. northApproachM + wallToWallM + southApproachM = lengthM.',
    'crossingAngleDeg': 'Angle between bridge and river where they cross; 90 = perpendicular.',
    'district': 'Area label and building zone. May contain roads; must not overlap water or another district.',
    'park': 'Green space. Must not overlap any road, pavement or water.',
    'validation': 'Counts computed by the exporter from the arrays; validate_world.py recomputes them and fails on any mismatch.',
}
out = dict(
    schemaVersion='1.1', units='metres', generated='2026-10-12', definitions=DEFINITIONS,
    widthOverrides={road_ids[n]: w for n, w in WIDTH_OVERRIDE.items()},
    axes=dict(x='west→east', z='south→north', y='up', origin='south-west corner of the world', yaw='degrees, 0 = +Z north, 90 = +X east, clockwise from above'),
    heights=dict(ground=0.0, roadSurface=0.02, pavementTop=0.15, waterSurface=-2.0, riverBed=-4.0, embankmentWallTop=0.9, bridgeDeck=0.0),
    worldBounds=dict(groundMinX=-300, groundMaxX=1900, groundMinZ=-300, groundMaxZ=1500, wallMinX=-120, wallMaxX=1720, wallMinZ=-120, wallMaxZ=1320),
    camera=dict(farClip=450, fogStart=120, fogEnd=400),
    roadClasses=[dict(name=k, **v) for k, v in CLASSES.items()],
    river=dict(width=RIVER_WIDTH, wallThickness=4.0, centreline=[dict(x=float(p[0]), z=float(p[1])) for p in RIVER]),
    roads=roads_out, bridges=bridges_out, junctions=junctions_out,
    districts=[dict(id=f'D_{n.split()[0][:6].upper()}{"_PEN" if "PENINSULA" in n else ""}', name=n, polygon=[dict(x=float(p[0]), z=float(p[1])) for p in d['poly']])
               for n, d in DISTRICTS.items()],
    parks=[dict(name=n, polygon=[dict(x=float(p[0]), z=float(p[1])) for p in pts]) for n, pts in PARKS],
    pois=pois_out, spawns=SPAWNS,
    existingStreet=dict(prefab='PeckamStreet', centreX=EXISTING_STREET['centre'][0], centreZ=EXISTING_STREET['centre'][1],
                        sizeX=EXISTING_STREET['size'][0], sizeZ=EXISTING_STREET['size'][1], replacesRoad='Peckam High Street',
                        replacesFromX=EXISTING_STREET['road_from_x'], replacesToX=EXISTING_STREET['road_to_x']),
)
import validate as V
for sp in SPAWNS:
    dj = min(math.dist((sp['x'], sp['z']), (j['x'], j['z'])) for j in junctions_out)
    dr = min(V.poly_closest((sp['x'], sp['z']), [tuple(map(float, p)) for p in pts])[0] for n, c, pts in ROADS)
    if sp['id'].startswith('SP_POLICE'): assert dj >= 40 and dr < 1, (sp['id'], dj, dr)
    else: assert dr < 12, (sp['id'], dr)
# Validation summary + measured routes (for the acceptance tests)
routes = []
for label, a, b in [('Safehouse → M1 car park', POIS[0][1], (300, 476)), ('Safehouse → M2 showroom', POIS[0][1], POIS[2][1]),
                    ('Safehouse → M4 Tower Plaza', POIS[0][1], POIS[4][1]), ('Safehouse → M3 Warehouse 9', POIS[0][1], POIS[3][1]),
                    ('M1 car park → M3 Warehouse 9', (300, 476), POIS[3][1])]:
    L, names, _ = route(a, b); routes.append(dict(label=label, metres=round(L), seconds_at_15mps=round(L / 15), via=names))
import collections
n_b = sum(1 for r in roads_out if r['roadClass'] == 'bridge')
out['validation'] = dict(roadEntries=len(roads_out), ordinaryRoads=len(roads_out) - n_b, bridgeRoads=n_b, bridgeDetails=len(bridges_out),
                         junctions=len(junctions_out), junctionArms={f'{k}-arm': v for k, v in sorted(collections.Counter(j['arms'] for j in junctions_out).items())},
                         connectedPieces=1, referenceRoutes=routes)
# Independent check before writing: the file must pass validate_world.py or it is not written
import validate_world
rep = validate_world.validate(json.loads(json.dumps(out)))
failed = [(c, f) for c, _, f, _ in rep.rows if f]
if failed:
    rep.print(); raise SystemExit('NOT WRITTEN: validate_world.py failed')
json.dump(out, open('LundunWorld.json', 'w'), indent=1)
print('validate_world.py:', f'{len(rep.rows)}/{len(rep.rows)} rules pass')
print('roads', len(roads_out), 'bridges', len(bridges_out), 'junctions', len(junctions_out))
for r in routes: print(f"  {r['label']:32s} {r['metres']:5d} m ~{r['seconds_at_15mps']:3d}s  {' → '.join(r['via'])}")
for b in bridges_out: print(' ', b['id'], b['name'], b['northEnd'], b['southEnd'], b['northJunction'], b['southJunction'], b['northRoad'], '↔', b['southRoad'])
