"""Lundun world layout, in metres. x = west->east (0..1600), z = south->north (0..1200).
This is the single source of truth for Visual A, the GPS mock-up and (later) the Unity greybox."""

WORLD_W, WORLD_H = 1600, 1200

# River Tamesis centreline (loops south round the Canary Quay peninsula, like the Isle of Dogs)
RIVER = [(-20, 640), (200, 670), (450, 655), (700, 630), (900, 600), (990, 540), (1030, 450),
         (1080, 360), (1170, 300), (1290, 300), (1380, 360), (1420, 470), (1450, 570),
         (1520, 630), (1620, 650)]
RIVER_WIDTH = 70

# Districts: name, polygon, tint
DISTRICTS = {
    "KNIGHTSBURY":    dict(poly=[(120, 735), (930, 700), (930, 1130), (120, 1130)], tint=(176, 186, 206),
                           label=(150, 1110), sub="West End · towers, stone, luxury"),
    "CANARY QUAY":    dict(poly=[(960, 700), (1520, 700), (1520, 1000), (960, 1000)], tint=(190, 180, 205),
                           label=(1180, 985), sub="Docklands · warehouses"),
    "CANARY QUAY PENINSULA": dict(poly=[(1101.5, 406.0), (1170, 360), (1290, 360), (1355.0, 411.5), (1390, 560),
                           (1420, 640), (1060, 640), (1065.0, 472.0)], tint=(170, 165, 200), label=None, sub=None),
    "GILMORE ESTATE": dict(poly=[(130, 120), (600, 120), (600, 560), (130, 560)], tint=(200, 178, 168),
                           label=(150, 545), sub="Tower blocks · courtyards"),
    "PECKAM":         dict(poly=[(650, 100), (1060, 100), (1060, 300), (977.0, 468.5), (650, 520)], tint=(212, 192, 168),
                           label=(670, 510), sub="High street · safehouse"),
}

PARKS = [
    ("Hydemoor Park", [(60, 935), (288, 935), (288, 1144), (60, 1144)]),
    ("Gilmore Green", [(200, 195), (330, 195), (330, 285), (200, 285)]),
    ("Quay Gardens",  [(1240, 430), (1310, 432), (1335, 520), (1250, 525)]),
    ("Rye Common",    [(1000, 110), (1050, 110), (1050, 175), (1000, 175)]),
]

# Roads: (name, class, polyline). Classes: ring (dual carriageway, later motorway), arterial, street, service
ROADS = [
    # Outer ring (Phase 1: dual carriageway; later upgraded to the M52)
    # The ring is two pieces joined by the two ring bridges, so no ordinary road ever crosses the river
    ("Lundun Ring North", "ring", [(1560, 700), (1560, 1160), (40, 1160), (40, 720)]),
    ("Lundun Ring South", "ring", [(40, 560), (40, 40), (1560, 40), (1560, 560)]),

    # --- North bank ---
    ("Oxbridge Street", "arterial", [(40, 920), (1560, 920)]),                 # Oxford St nod, crosses whole north
    ("Regent Row", "arterial", [(560, 1160), (560, 730)]),                     # N-S through the West End
    ("North Embankment", "arterial", [(40, 720), (300, 735), (560, 730), (820, 715), (950, 700),
                                      (1150, 700), (1560, 700)]),
    ("Brompton Road", "street", [(300, 1160), (300, 735)]),
    ("Mayfield Street", "street", [(820, 1160), (820, 715)]),
    ("Knightsbury Mews", "street", [(300, 1030), (820, 1030)]),
    ("Sloane Lane", "street", [(300, 820), (820, 820)]),
    ("Commercial Way", "arterial", [(950, 700), (950, 1160)]),
    ("Dock Street", "street", [(1150, 920), (1150, 700)]),
    ("Wharf Road", "street", [(1350, 920), (1350, 700)]),
    ("Warehouse Lane", "service", [(950, 810), (1560, 810)]),

    # --- Canary Quay peninsula (reached from the north bank) ---
    ("Westferry Road", "arterial", [(1150, 700), (1100, 600), (1100, 470), (1170, 400), (1290, 395)]),
    ("Quay Loop", "street", [(1290, 395), (1350, 450), (1375, 560), (1300, 640), (1250, 700)]),
    ("Tower Plaza", "street", [(1100, 540), (1370.5, 540)]),

    # --- Bridges (the ONLY river crossings) ---
    ("Westbridge", "bridge", [(430, 732.5), (430, 560)]),
    ("Rye Bridge", "bridge", [(820, 715), (790, 528.9)]),
    ("Quay Bridge", "bridge", [(1230, 397.5), (1230, 230)]),
    ("Ring West Crossing", "bridge", [(40, 720), (40, 560)]),       # part of the ring
    ("Ring East Crossing", "bridge", [(1560, 700), (1560, 560)]),

    # --- South bank ---
    ("South Embankment", "arterial", [(40, 560), (430, 560), (700, 545), (790, 528.9), (840, 520), (960, 470),
                                      (1000, 380), (1060, 260), (1230, 230), (1380, 240), (1470, 320), (1500, 450),
                                      (1560, 560)]),
    ("Old Kent Lane", "arterial", [(40, 300), (600, 300), (1060, 260)]),
    ("Rye Lane", "arterial", [(790, 528.9), (790, 40)]),                          # Peckam's spine, to the ring
    ("Peckam High Street", "street", [(700, 190), (980, 190)]),                # your existing street lives here
    ("Bellenden Road", "street", [(700, 291.3), (700, 40)]),
    ("Choumert Grove", "street", [(980, 267.0), (980, 40)]),
    ("Gilmore Road", "street", [(430, 560), (430, 40)]),
    ("Estate Service Road", "service", [(180, 300), (180, 470), (430, 470)]),
    ("Thurlow Street", "street", [(40, 180), (430, 180)]),
]

# Points of interest: name, (x, z), kind
POIS = [
    ("Safehouse", (905, 205), "safehouse"),
    ("M1 The Pickup (Gilmore courtyard)", (330, 380), "mission"),
    ("M2 Kestrel showroom", (690, 860), "mission"),
    ("M3 Warehouse 9", (1260, 780), "mission"),
    ("M4 Tower Plaza meet", (1220, 560), "mission"),
    ("Police station", (745, 250), "police"),
    ("Hospital", (240, 760), "hospital"),
    ("Knightsbury Circus", (560, 920), "landmark"),
    ("Quay Towers", (1200, 470), "landmark"),
]

ROAD_WIDTH = {"ring": 22, "arterial": 15, "street": 10, "service": 7, "bridge": 15}
DEAD_ENDS_OK = set()

# Existing Prototype street (prefab) footprint: it replaces the High Street between these x values
EXISTING_STREET = dict(centre=(860, 190), size=(112, 64), road_from_x=804, road_to_x=916)
# Per-road width overrides (ring bridges match the dual carriageway)
WIDTH_OVERRIDE = {"Ring West Crossing": 22, "Ring East Crossing": 22}
