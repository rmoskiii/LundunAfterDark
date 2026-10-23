# E1: the map expansions' streets, squares and districts (Brixtun so far) -> Assets/World/LundunExpansion.json (+ a C# copy for the harness)
# Run from the project folder:  python3 Tools/WorldData/expansion_layout.py Assets/World/LundunExpansion.json /tmp/TestExpansion.cs
# (the second file is a C# copy of the same data for testing the generator outside Unity)
import json, math, sys, os
OUT_JSON = sys.argv[1]; OUT_CS = sys.argv[2] if len(sys.argv) > 2 else os.devnull
BASE = json.load(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', 'Assets', 'World', 'LundunWorld.json')))
base_roads = {r['id']: r for r in BASE['roads']}
base_juncs = {j['id']: j for j in BASE['junctions']}

roads = [  # id, name, class, width, points
 ('XR01', 'Brixtun Road', 'arterial', 18, [(430,40),(430,-80),(428,-185),(426.5,-245),(425,-305),(420,-500),(415,-700)]),
 ('XR02', 'Atlantic Road', 'street', 10, [(530,40),(532,-40),(533,-80),(534,-120),(535,-175),(535,-312)]),
 ('XR03', 'Electric Parade', 'street', 10, [(428,-185),(455,-200),(490,-203),(518,-192),(535,-175)]),
 ('XR04', 'Coldharbor Lane', 'arterial', 15, [(425,-305),(535,-312),(700,-330),(840,-350),(982,-378)]),
 ('XR05', 'Acre Lane', 'arterial', 15, [(425,-305),(258,-318),(120,-326),(40,-330)]),
 ('XR06', 'Stockwell Road', 'street', 10, [(260,40),(259,-80),(258.4,-240),(258,-318),(257,-500),(256,-700)]),
 ('XR07', 'Loughborough Road', 'street', 10, [(700,40),(700,-130),(700,-330),(700,-500),(700,-695)]),
 ('XR08', 'Denmore Hill', 'street', 10, [(980,40),(981,-140),(982,-378),(985,-690)]),
 ('XR09', 'Brixtun Hill', 'street', 10, [(40,-700),(256,-700),(415,-700),(700,-695),(985,-690)]),
 ('XR10', 'Effram Road', 'street', 10, [(40,-330),(40,-700)]),
 ('XR11', 'Somerleyton Road', 'street', 10, [(534,-120),(700,-130),(840,-135),(981,-140)]),
 ('XR12', 'Herne Lane', 'street', 10, [(840,-135),(840,-350)]),
 ('XR13', 'Saltoun Road', 'street', 10, [(258.4,-240),(426.5,-245)]),
 ('XR14', 'Tulse Lane', 'street', 10, [(257,-500),(420,-500),(700,-500)]),
]
# junctions: id, x, z  (ids J55/J56/J58 = joins onto the Ring's existing junctions; XJ_R02_* = new joins on the Ring)
juncs = [('J55',430,40),('J56',700,40),('J58',980,40),('XJR02A',530,40),('XJR02S',260,40),
 ('XJ01',428,-185),('XJ02',535,-175),('XJ03',425,-305),('XJ04',535,-312),('XJ05',700,-330),('XJ06',982,-378),
 ('XJ07',258,-318),('XJ08',40,-330),('XJ09',40,-700),('XJ10',256,-700),('XJ11',415,-700),('XJ12',700,-695),('XJ13',985,-690),
 ('XJ14',534,-120),('XJ15',700,-130),('XJ16',840,-135),('XJ17',981,-140),('XJ18',840,-350),('XJ19',258.4,-240),('XJ20',426.5,-245),
 ('XJ21',257,-500),('XJ22',420,-500),('XJ23',700,-500)]

def chain(pts):
    c=[0.0]
    for i in range(1,len(pts)): c.append(c[-1]+math.dist(pts[i-1],pts[i]))
    return c

R=[]; road_at={}
for rid,name,cls,w,pts in roads:
    ch=chain(pts); js=[]; jc=[]
    for jid,x,z in juncs:
        for i,p in enumerate(pts):
            if abs(p[0]-x)<0.01 and abs(p[1]-z)<0.01: js.append(jid); jc.append(round(ch[i],3))
    order=sorted(range(len(js)),key=lambda k:jc[k]); js=[js[k] for k in order]; jc=[jc[k] for k in order]
    R.append({'id':rid,'name':name,'roadClass':cls,'width':float(w),'lengthM':round(ch[-1],3),'points':[{'x':float(x),'z':float(z)} for x,z in pts],'junctions':js,'junctionChainage':jc})
    road_at[rid]=(pts,ch,js,jc)

J=[]
for jid,x,z in juncs:
    legs=[]; rs=[]
    for rid,(pts,ch,js,jc) in road_at.items():
        if jid in js:
            c=jc[js.index(jid)]; rs.append(rid)
            if c>0.01: legs.append({'road':rid,'toward':'start'})
            if c<ch[-1]-0.01: legs.append({'road':rid,'toward':'end'})
    if jid.startswith('XJR02'):   # a new join on the Ring: the Ring passes through it
        rs.insert(0,'R02'); legs=[{'road':'R02','toward':'start'},{'road':'R02','toward':'end'}]+legs
    J.append({'id':jid,'x':float(x),'z':float(z),'arms':len(legs) + (len(base_juncs[jid]['legs']) if jid in base_juncs else 0),'roads':rs,'legs':legs})

P=lambda pts:[{'x':float(x),'z':float(z)} for x,z in pts]
districts=[
 {'id':'D_BRIXTUN','name':'BRIXTUN','polygon':P([(130,18),(1060,18),(1060,-400),(712,-400),(712,-760),(272,-760),(272,-332),(130,-337)])},
 {'id':'D_BRIXTUN_EST','name':'ANGEL FIELDS ESTATE','polygon':P([(-100,18),(110,18),(110,-337),(272,-332),(272,-760),(-100,-760)])},
]
parks=[
 {'id':'P_WINDWARD','name':'Windward Square','polygon':P([(440,-236),(500,-236),(500,-292),(440,-292)])},
 {'id':'P_BROCKMERE','name':'Brockmere Park','polygon':P([(430,-716),(690,-712),(690,-905),(430,-905)])},
 # E3: the market square in front of Brixtun station, between the viaduct, Brixtun Road and Atlantic Road (last, so nothing else moves)
 {'id':'P_MARKET','name':'Brixtun Market','polygon':P([(443.5,-74.9),(524.0,-74.9),(524.0,-58.0),(443.5,-58.0)])},
]
pois=[]
x={'schemaVersion':'1.1','worldBounds':{'groundMinX':-300.0,'groundMaxX':1900.0,'groundMinZ':-1130.0,'groundMaxZ':2080.0,'wallMinX':-120.0,'wallMaxX':1720.0,'wallMinZ':-950.0,'wallMaxZ':1900.0},
   'roadClasses':[], 'roads':R,'junctions':J,'districts':districts,'parks':parks,'pois':pois}
json.dump(x,open(OUT_JSON,'w'),indent=4)

# C# copy for the test harness (no JsonUtility there)
def pts_cs(p): return 'new LwPoint[]{'+','.join('new LwPoint{x=%sf,z=%sf}'%(q['x'],q['z']) for q in p)+'}'
cs=['public static class TestExpansion { public static LundunExpansionData Make() { var X=new LundunExpansionData();',
 'X.worldBounds=new LwBounds{groundMinX=-300,groundMaxX=1900,groundMinZ=-1130,groundMaxZ=2080,wallMinX=-120,wallMaxX=1720,wallMinZ=-950,wallMaxZ=1900};',
 'X.roads=new LwRoad[]{'+','.join('new LwRoad{id="%s",name="%s",roadClass="%s",width=%sf,lengthM=%sf,points=%s,junctions=new string[]{%s},junctionChainage=new float[]{%s}}'%(r['id'],r['name'],r['roadClass'],r['width'],r['lengthM'],pts_cs(r['points']),','.join('"%s"'%j for j in r['junctions']),','.join('%sf'%c for c in r['junctionChainage'])) for r in R)+'};',
 'X.junctions=new LwJunction[]{'+','.join('new LwJunction{id="%s",x=%sf,z=%sf,arms=%d,roads=new string[]{%s},legs=new LwLeg[]{%s}}'%(j['id'],j['x'],j['z'],j['arms'],','.join('"%s"'%r for r in j['roads']),','.join('new LwLeg{road="%s",toward="%s"}'%(l['road'],l['toward']) for l in j['legs'])) for j in J)+'};',
 'X.districts=new LwPoly[]{'+','.join('new LwPoly{id="%s",name="%s",polygon=%s}'%(d['id'],d['name'],pts_cs(d['polygon'])) for d in districts)+'};',
 'X.parks=new LwPoly[]{'+','.join('new LwPoly{id="%s",name="%s",polygon=%s}'%(d['id'],d['name'],pts_cs(d['polygon'])) for d in parks)+'};',
 'X.pois=new LwPoi[0]; X.roadClasses=new LwRoadClass[0]; return X; } }']
open(OUT_CS,'w').write('\n'.join(cs))
print(len(R),'roads',len(J),'junctions')
for j in J: print(j['id'],j['arms'],[l['road']+':'+l['toward'] for l in j['legs']])
