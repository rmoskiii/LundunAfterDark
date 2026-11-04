// Pure C#: S3, the story's shops. LAGOS FOODS (Uncle Femi's corner shop on Rye Lane) becomes a real walk-in shop, and
// the city keeps exactly one LAGOS FOODS and one FADEZ BARBERS (Peckam's shop signs are picked at random, so there were
// several of each). Nothing else moves:
//   • the parade it's in stays in the building list (so every random number after it is drawn exactly as before); the
//     building kit draws it as three pieces instead of one, and the middle piece (the shop's unit) without its ground
//     floor, which the dressing fills with the shop's own front and inside (LundunShopLayout says where things go)
//   • the shop-front roll is unchanged: a duplicate keeps its roll and just shows another name from the list (chosen by
//     where it is, not by a random number); the shop's own unit is rolled as before and built nowhere
// The game (LundunStoryShop) puts the shutters down or up: shut in Free Roam, open in Story when a mission or the
// story says so. The NavMesh (Lundun → 3) bakes the floor inside, so people can walk in.
using System;
using System.Collections.Generic;

namespace LundunGreybox
{
    public static partial class Generator
    {
        static readonly V2 LagosAnchor = new V2(776, 393), FadezAnchor = new V2(805, 339);
        const string LagosName = "LAGOS FOODS", FadezName = "FADEZ BARBERS";
        static BuildingInfo lagosBld;
        static int lagosUnit = -1;
        static V2 lagosN, lagosT, lagosO;                   // outward normal, along the front (+x), the unit's front middle
        static double lagosUW, lagosD, lagosFaceW;          // unit width, building depth, the whole front's width
        static bool lagosKitDone;

        /// The face a building's shop fronts go on (the rule DressShopFronts and the kit both use). False if none.
        static bool ShopFaceOf(BuildingInfo b, out V2 n, out V2 t, out V2 face, out double width, out double depth)
        {
            V2 f = b.dir.Norm, l = f.Left;
            V2[] normals = { l, l * -1, f, f * -1 };
            double[] halfDepth = { b.dep / 2, b.dep / 2, b.len / 2, b.len / 2 };
            double[] faceLen = { b.len, b.len, b.dep, b.dep };
            int bestFace = -1; double bestGap = 6;
            for (int i = 0; i < 4; i++)
            {
                V2 fc = b.c + normals[i] * halfDepth[i];
                foreach (RoadInfo ri in roads.Values)
                {
                    if (ri.pave < 3 || ri.cls == "bridge") continue;
                    double gap = ri.line.DistanceTo(fc) - ri.w / 2 - ri.pave;
                    if (gap < bestGap && V2.Dot(normals[i], ClosestOn(ri, fc) - fc) > 0) { bestGap = gap; bestFace = i; }
                }
            }
            n = t = face = new V2(0, 0); width = depth = 0;
            if (bestFace < 0) return false;
            n = normals[bestFace]; t = n.Left;
            face = b.c + n * halfDepth[bestFace];
            width = faceLen[bestFace]; depth = halfDepth[bestFace] * 2;
            return true;
        }

        /// BuildBuildings, before the kit: which building and which of its shop units is LAGOS FOODS.
        static void FindLagos(GreyboxResult R)
        {
            lagosBld = null; lagosUnit = -1; lagosKitDone = false;
            if (ExpansionMode) return;
            foreach (BuildingInfo b in R.buildings)
            {
                if (b.district != "D_PECKAM" || V2.Dist(b.c, LagosAnchor) > Math.Max(b.len, b.dep)) continue;
                V2 n, t, face; double width, depth;
                if (!ShopFaceOf(b, out n, out t, out face, out width, out depth)) continue;
                int units = Math.Max(1, (int)Math.Floor((width - 1.0) / 7.0));
                double unitW = (width - 1.0) / units;
                for (int u = 0; u < units; u++)
                {
                    V2 uc = face + t * (-width / 2 + 0.5 + unitW * (u + 0.5));
                    if (V2.Dist(uc, LagosAnchor) > 4) continue;
                    lagosBld = b; lagosUnit = u; lagosN = n; lagosT = t; lagosO = uc;
                    lagosUW = unitW; lagosD = depth; lagosFaceW = width;
                    R.log.Add(string.Format("  LAGOS FOODS: unit {0} of a {1:0} m parade on Rye Lane", u + 1, width));
                    return;
                }
            }
            R.log.Add("  !! LAGOS FOODS: its parade wasn't found on Rye Lane (the shop stays a shop front)");
        }

        // ================================================================== the building kit's part (BuildKit)
        /// The parade as three pieces: either side as usual, the shop's unit with nothing below the first floor.
        /// False (draw it as usual) if the kit sees the building differently.
        static bool LagosKit(Dictionary<string, MeshData> chunks, string ck, KitPlan p, BuildingInfo b)
        {
            if (b != lagosBld || (p.type != KitType.Parade && p.type != KitType.Pub) || V2.Dot(p.n, lagosN) < 0.99) { lagosBld = null; return false; }
            double s0 = -lagosFaceW / 2 + 0.5 + lagosUW * lagosUnit, s1 = s0 + lagosUW;
            double a = -p.W / 2, z = p.W / 2;
            if (s0 - a < 2.0) s0 = a;                          // a sliver either side goes with the shop's piece
            if (z - s1 < 2.0) s1 = z;
            MeshData A = ChunkMesh(chunks, KitMatA + ck, KitMatA), B = ChunkMesh(chunks, KitMatB + ck, KitMatB);
            if (s0 > a) KitPiece(p, b, a, s0, A, B, 0);
            if (z > s1) KitPiece(p, b, s1, z, A, B, 0);
            KitPiece(p, b, s0, s1, A, B, LundunShopLayout.GroundFloorTop);
            lagosKitDone = true;
            return true;
        }

        static void KitPiece(KitPlan p, BuildingInfo b, double sa, double sb, MeshData A, MeshData B, double cutBelow)
        {
            double mid = (sa + sb) / 2;
            KitPlan q = new KitPlan { type = KitType.Parade, face = p.face, road = p.road, gap = p.gap, nearJunction = false, mixed = p.mixed,
                                      c = p.c + p.r * mid, n = p.n, r = p.r, W = sb - sa, D = p.D };
            Random kr = new Random((int)(KHash((int)Math.Round(q.c.x * 10) + 7, (int)Math.Round(q.c.z * 10) - 3) & 0x7FFFFFFF));
            if (cutBelow <= 0)
            {
                new Kit { A = A, B = B, p = q, rnd = kr, h = b.h }.Parade(false);
            }
            else
            {
                MeshData ta = new MeshData("Cut", "Buildings", KitMatA, true), tb = new MeshData("Cut", "Buildings", KitMatB, true);
                new Kit { A = ta, B = tb, p = q, rnd = kr, h = b.h }.Parade(false);
                ClipAbove(ta, cutBelow, A);
                ClipAbove(tb, cutBelow, B);
            }
            if (p.eave <= 0) p.eave = q.eave;
        }

        /// Copies src's triangles into dst, keeping only what's above y (cut triangles are trimmed, their UVs too).
        static void ClipAbove(MeshData src, double y, MeshData dst)
        {
            List<P3> poly = new List<P3>(), outp = new List<P3>();
            for (int i = 0; i + 2 < src.tri.Count; i += 3)
            {
                poly.Clear();
                for (int k = 0; k < 3; k++)
                {
                    int v = src.tri[i + k];
                    poly.Add(new P3(src.pos[v * 3], src.pos[v * 3 + 1], src.pos[v * 3 + 2], src.uv[v * 2], src.uv[v * 2 + 1]));
                }
                double ux = poly[1].x - poly[0].x, uy = poly[1].y - poly[0].y, uz = poly[1].z - poly[0].z;
                double vx = poly[2].x - poly[0].x, vy = poly[2].y - poly[0].y, vz = poly[2].z - poly[0].z;
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                outp.Clear();
                for (int k = 0; k < 3; k++)
                {
                    P3 cur = poly[k], nxt = poly[(k + 1) % 3];
                    bool cIn = cur.y >= y - 1e-6, nIn = nxt.y >= y - 1e-6;
                    if (cIn) outp.Add(cur);
                    if (cIn != nIn)
                    {
                        double t = (y - cur.y) / (nxt.y - cur.y);
                        outp.Add(new P3(cur.x + (nxt.x - cur.x) * t, y, cur.z + (nxt.z - cur.z) * t, cur.u + (nxt.u - cur.u) * t, cur.v + (nxt.v - cur.v) * t));
                    }
                }
                for (int k = 1; k + 1 < outp.Count; k++) dst.Tri(outp[0], outp[k], outp[k + 1], nx, ny, nz);
            }
        }

        // ================================================================== the shop-front roll (DressShopFronts)
        /// The name a rolled shop front shows: LAGOS FOODS only on its own unit, FADEZ BARBERS only on the one by the
        /// anchor; any other roll of those two shows another name from the list, picked by place (no random numbers).
        static string[] StoryShopName(string[][] list, string[] rolled, V2 uc, BuildingInfo b, int u)
        {
            if (ExpansionMode || list != PeckamShops) return rolled;
            if (b == lagosBld && u == lagosUnit) return ShopEntry(list, LagosName) ?? rolled;
            bool fadezHere = V2.Dist(uc, FadezAnchor) < 4;
            if (fadezHere) return ShopEntry(list, FadezName) ?? rolled;
            if (rolled[0] != LagosName && rolled[0] != FadezName) return rolled;
            int k = (int)(KHash((int)Math.Round(uc.x * 10), (int)Math.Round(uc.z * 10) + 11) % (uint)list.Length);
            for (int i = 0; i < list.Length; i++)
            {
                string[] s = list[(k + i) % list.Length];
                if (s[0] != LagosName && s[0] != FadezName) return s;
            }
            return rolled;
        }

        static string[] ShopEntry(string[][] list, string name) { foreach (string[] s in list) if (s[0] == name) return s; return null; }

        /// True for LAGOS FOODS' own unit (its front is built by DressLagos instead).
        static bool IsLagosUnit(BuildingInfo b, int u) { return lagosKitDone && b == lagosBld && u == lagosUnit; }

        /// The barber's on Rye Lane: a place for the story (where Tayo waits).
        static void FadezPlace(V2 uc, V2 n)
        {
            DressingResult d = keepD ?? D;
            foreach (PlaceInfo p in d.places) if (p.id == "FADEZ_BARBERS") return;
            V2 door = uc + n * 1.6;
            d.places.Add(new PlaceInfo { id = "FADEZ_BARBERS", kind = "shop", name = FadezName, doorX = door.x, doorZ = door.z, doorYaw = Yaw(n) });
        }

        // ================================================================== the shop itself (DressPlaces)
        static V2 LP(double x, double z) { return lagosO + lagosT * x - lagosN * z; }

        static void LBox(double x0, double x1, double z0, double z1, double y0, double y1, string sw, MeshData m = null)
        {
            SBox(m ?? mSolid, LP((x0 + x1) / 2, (z0 + z1) / 2), lagosT, Math.Abs(x1 - x0) / 2, Math.Abs(z1 - z0) / 2, y0, y1, A[sw]);
        }

        static void LCol(string name, double x0, double x1, double z0, double z1, double y0, double y1)
        {
            Collider(name, LP((x0 + x1) / 2, (z0 + z1) / 2), lagosT, Math.Abs(x1 - x0), Math.Abs(z1 - z0), y0, y1);
        }

        /// A picture across x0..x1 at depth z. toFront = facing the street side, otherwise facing the back.
        static void LFront(double x0, double x1, double z, double y0, double y1, string tile, bool glow, bool toFront = true)
        {
            VQuad(glow ? mGlow : mSolid, LP(x0, z), LP(x1, z), y0, y1, A[tile], toFront ? lagosN : lagosN * -1);
        }

        /// A picture along z0..z1 at x, facing +x (right) or −x.
        static void LSide(double x, double z0, double z1, double y0, double y1, string tile, bool glow, bool right)
        {
            VQuad(glow ? mGlow : mSolid, LP(x, z0), LP(x, z1), y0, y1, A[tile], right ? lagosT : lagosT * -1);
        }

        static void LFlat(double x0, double x1, double z0, double z1, double y, string tile, bool down)
        {
            UVRect r = A[tile];
            mSolid.Quad(new P3(LP(x0, z0), y, r.u0, r.v0), new P3(LP(x1, z0), y, r.u1, r.v0), new P3(LP(x1, z1), y, r.u1, r.v1), new P3(LP(x0, z1), y, r.u0, r.v1),
                        0, down ? -1 : 1, 0);
        }

        static void DressLagos()
        {
            if (!lagosKitDone || lagosBld == null) return;
            double W = lagosUW, D2 = lagosD, hw = W / 2;
            double fy = LundunShopLayout.FloorY, cy = LundunShopLayout.CeilingY, gf = LundunShopLayout.GroundFloorTop;
            double dx = LundunShopLayout.DoorX(W), dh = LundunShopLayout.DoorHalf, dTop = LundunShopLayout.DoorTop;
            double wx0 = LundunShopLayout.WindowX0(W), wx1 = LundunShopLayout.WindowX1(W);
            double wy0 = LundunShopLayout.WindowY0, wy1 = LundunShopLayout.WindowY1;
            double part = LundunShopLayout.Partition, back = D2 - 0.25;

            // keep the street furniture off the pavement in front
            for (double x = -hw; x <= hw; x += 2) Claim(LP(x, -1.5), 1.6);

            // ---- the front: fascia with the name, green pilasters, the window (open: you see in), the doorway
            LBox(-hw, hw, -0.16, 0.0, 2.95, gf, "charcoal");
            VQuad(mGlow, LP(-hw + 0.2, -0.18), LP(hw - 0.2, -0.18), 3.02, 3.86, A["shop_" + LagosName], lagosN);
            LBox(-hw, -hw + 0.3, -0.2, 0.15, 0, 2.95, "lf_green");
            LBox(hw - 0.3, hw, -0.2, 0.15, 0, 2.95, "lf_green");
            LBox(wx1, dx - dh, -0.15, 0.1, 0, 2.95, "lf_green");                       // between window and door
            LBox(wx0, wx1, -0.12, 0.1, 0, wy0, "lf_green");                            // stall riser
            LBox(wx0, wx1, -0.1, 0.06, wy1, 2.95, "charcoal");                         // window head
            foreach (double mx in new[] { wx0 + (wx1 - wx0) / 3, wx0 + (wx1 - wx0) * 2 / 3 })
                LBox(mx - 0.035, mx + 0.035, -0.06, 0.02, wy0, wy1, "charcoal");      // mullions
            LBox(dx - dh, dx + dh, -0.1, 0.06, dTop, 2.95, "charcoal");                // over the door
            LBox(dx + dh, hw - 0.3, -0.15, 0.1, 0, 2.95, "lf_green");
            LBox(dx - dh - 0.05, dx + dh + 0.05, -0.02, 0.02, fy, fy + 0.02, "charcoal");   // threshold
            LCol("ShopWindow", wx0, wx1, -0.1, 0.1, 0, 2.95);
            LCol("ShopFront", -hw, wx0, -0.2, 0.15, 0, 3.0);
            LCol("ShopFront", wx1, dx - dh, -0.2, 0.15, 0, 3.0);
            LCol("ShopFront", dx + dh, hw, -0.2, 0.15, 0, 3.0);
            LCol("ShopFront", -hw, hw, -0.2, 0.15, dTop, 3.0);

            // ---- shell: floor, ceiling, linings, the back wall
            LCol("ShopFloor", -hw, hw, 0, D2, -0.4, fy);
            for (double x = -hw; x < hw - 0.01; x += 1.8)
                for (double z = 0; z < D2 - 0.01; z += 1.8)
                    LFlat(x, Math.Min(x + 1.8, hw), z, Math.Min(z + 1.8, D2), fy + 0.005, "lf_floor", false);
            LFlat(-hw, hw, 0.02, D2, cy, "lf_ceiling", true);
            LCol("ShopCeiling", -hw, hw, 0.2, D2, cy, cy + 0.3);                    // the camera stops here (not the floor above)
            LBox(-hw, hw, 0.02, 0.2, cy, gf - 0.05, "charcoal");                       // behind the fascia, from inside
            foreach (int s in new[] { -1, 1 })
            {
                LSide(s * (hw - 0.04), 0.15, D2 - 0.1, fy, cy, "lf_wall", false, s < 0);
                LCol("ShopWall", s * hw - 0.15, s * hw + 0.15, 0.15, D2, 0, cy);
            }
            LBox(-hw, hw, back, D2, 0, gf, "lf_backwall");
            LFront(-hw + 0.05, hw - 0.05, back - 0.01, fy, cy, "lf_wall", false);
            LBox(-0.6, 0.6, D2, D2 + 0.04, fy, 2.2, "charcoal");                       // back door (outside)
            LCol("ShopBack", -hw, hw, back, D2, 0, gf);
            SBox(mGlow, LP(-hw * 0.45, 1.5) , lagosT, 0.08, 1.4, cy - 0.06, cy - 0.01, A["lf_lamp"]);   // strip lights
            SBox(mGlow, LP(hw * 0.25, 1.5), lagosT, 0.08, 1.4, cy - 0.06, cy - 0.01, A["lf_lamp"]);
            SBox(mGlow, LP(-hw * 0.45, 5.0), lagosT, 0.08, 1.4, cy - 0.06, cy - 0.01, A["lf_lamp"]);
            SBox(mGlow, LP(hw * 0.25, 5.0), lagosT, 0.08, 1.4, cy - 0.06, cy - 0.01, A["lf_lamp"]);
            SBox(mGlow, LP(0, (part + back) / 2), lagosT, 0.5, 0.08, cy - 0.06, cy - 0.01, A["lf_lamp"]);

            // ---- the wall shelves down the left, floor to head height, stocked
            double sx0 = -hw + 0.06, sx1 = -hw + 0.5;
            LBox(sx0, sx1, 1.0, 6.4, fy, fy + 0.08, "lf_shelf");
            LBox(sx0, sx0 + 0.05, 1.0, 6.4, fy, 2.3, "lf_shelf");
            LBox(sx0, sx1, 1.0, 1.05, fy, 2.3, "lf_shelf"); LBox(sx0, sx1, 6.35, 6.4, fy, 2.3, "lf_shelf");
            string[] goods = { "lf_goods1", "lf_goods2", "lf_goods3", "lf_goods2", "lf_goods1" };
            int gi = 0;
            for (double y = 0.35; y < 2.3; y += 0.45)
            {
                LBox(sx0, sx1, 1.05, 6.35, y - 0.03, y, "lf_shelf");
                for (double z = 1.05; z < 6.3; z += 1.32)
                    LSide(sx1 - 0.04, z, Math.Min(z + 1.32, 6.35), y, y + 0.4, goods[(gi++) % goods.Length], false, true);
            }
            LCol("ShopShelf", sx0, sx1, 1.0, 6.4, 0, 2.3);
            LSide(-hw + 0.03, 1.1, 2.4, 2.45, 3.05, "lf_poster1", false, true);
            LSide(-hw + 0.03, 3.6, 4.9, 2.45, 3.05, "lf_poster2", false, true);

            // ---- the middle gondola, stocked both sides, a promo end facing the door
            double gx = -hw + 2.25, gz0 = 1.8, gz1 = 5.7;
            LBox(gx - 0.4, gx + 0.4, gz0, gz1, fy, fy + 0.1, "lf_shelf");
            LBox(gx - 0.03, gx + 0.03, gz0, gz1, fy, 1.65, "lf_shelf");
            for (double y = 0.4; y < 1.6; y += 0.42)
            {
                LBox(gx - 0.4, gx + 0.4, gz0, gz1, y - 0.03, y, "lf_shelf");
                for (double z = gz0; z < gz1 - 0.01; z += 1.3)
                {
                    LSide(gx - 0.36, z, Math.Min(z + 1.3, gz1), y, y + 0.38, goods[(gi++) % goods.Length], false, false);
                    LSide(gx + 0.36, z, Math.Min(z + 1.3, gz1), y, y + 0.38, goods[(gi++) % goods.Length], false, true);
                }
            }
            LBox(gx - 0.42, gx + 0.42, gz0 - 0.04, gz0, fy, 1.65, "lf_green");
            LFront(gx - 0.4, gx + 0.4, gz0 - 0.05, 0.4, 1.5, "lf_goods3", false);
            LCol("ShopGondola", gx - 0.42, gx + 0.42, gz0 - 0.05, gz1, 0, 1.65);
            // rice sacks under the window, inside
            for (int k = 0; k < 3; k++)
            {
                double x = wx0 + 0.4 + k * 0.75;
                if (x > wx1 - 0.4) break;
                LBox(x - 0.32, x + 0.32, 0.35, 0.85, fy, fy + 0.3 + (k % 2) * 0.28, "lf_sack");
            }
            LCol("ShopSacks", wx0 + 0.05, Math.Min(wx1, wx0 + 2.3), 0.3, 0.9, 0, 0.75);

            // ---- the counter by the door, the till, the shelves behind, the name inside
            double cx0 = LundunShopLayout.CounterX0(W), cx1 = LundunShopLayout.CounterX1(W);
            double cz0 = LundunShopLayout.CounterZ0, cz1 = LundunShopLayout.CounterZ1, ct = LundunShopLayout.CounterTop;
            LBox(cx0, cx1, cz0, cz1, fy, ct, "lf_counter");
            LBox(cx0 - 0.05, cx1 + 0.05, cz0 - 0.05, cz1 + 0.05, ct, ct + 0.05, "lf_top");
            LSide(cx0 - 0.01, cz0 + 0.1, cz1 - 0.1, fy + 0.1, ct - 0.1, "lf_goods1", false, false);      // sweets at the front
            LBox(cx0 + 0.15, cx0 + 0.45, 3.0, 3.4, ct + 0.05, ct + 0.2, "charcoal");                     // the till
            LBox(cx0 + 0.25, cx0 + 0.3, 3.05, 3.35, ct + 0.2, ct + 0.45, "charcoal");
            LSide(cx0 + 0.24, 3.07, 3.33, ct + 0.22, ct + 0.43, "lf_screen", true, false);
            LCol("ShopCounter", cx0, cx1, cz0, cz1, 0, ct + 0.05);
            double bx = hw - 0.3;
            LBox(bx, hw - 0.06, cz0, cz1, fy, 2.3, "lf_shelf");
            for (double y = 0.9; y < 2.2; y += 0.42)
                for (double z = cz0; z < cz1 - 0.01; z += 1.2)
                    LSide(bx - 0.01, z, Math.Min(z + 1.2, cz1), y, y + 0.38, "lf_cigs", false, false);
            LCol("ShopBackShelf", bx, hw, cz0, cz1, 0, 2.3);
            LSide(hw - 0.07, cz0 + 0.2, cz1 - 0.2, 2.45, 2.95, "shop_" + LagosName, true, false);

            // ---- the drinks fridge on the back wall, the partition and the back room
            double fx0 = -hw + 0.6, fx1 = -hw + 2.9;
            LBox(fx0, fx1, part - 0.7, part, fy, 2.15, "charcoal");
            for (int k = 0; k < 2; k++)
            {
                double a = fx0 + 0.08 + k * (fx1 - fx0 - 0.16) / 2, b = a + (fx1 - fx0 - 0.16) / 2 - 0.04;
                LFront(a, b, part - 0.71, fy + 0.15, 2.05, "lf_fridge", true);
            }
            LCol("ShopFridge", fx0, fx1, part - 0.7, part, 0, 2.15);
            double px0 = 0.5, px1 = 1.6;                                                // the doorway through
            LBox(-hw, px0, part, part + 0.15, fy, cy, "lf_wall");
            LBox(px1, hw, part, part + 0.15, fy, cy, "lf_wall");
            LBox(px0, px1, part, part + 0.15, 2.2, cy, "lf_wall");
            LFront(px0 + 0.1, px1 - 0.1, part - 0.01, 2.25, 2.6, "lf_staff", false);
            LCol("ShopPartition", -hw, px0, part, part + 0.15, 0, cy);
            LCol("ShopPartition", px1, hw, part, part + 0.15, 0, cy);
            // back room: boxes and sacks, a desk and a chair, the safe
            LBox(hw - 1.3, hw - 0.15, part + 0.4, part + 1.3, fy, fy + 0.6, "lf_box");
            LBox(hw - 1.2, hw - 0.35, part + 0.5, part + 1.2, fy + 0.6, fy + 1.1, "lf_box");
            LBox(hw - 1.3, hw - 0.15, part + 1.5, part + 2.4, fy, fy + 0.55, "lf_box");
            LCol("ShopBoxes", hw - 1.3, hw - 0.15, part + 0.4, part + 2.4, 0, 1.25);
            LBox(-hw + 0.2, -hw + 1.2, part + 0.5, part + 1.5, fy, fy + 0.45, "lf_sack");
            LBox(-hw + 0.25, -hw + 1.1, part + 0.6, part + 1.4, fy + 0.45, fy + 0.8, "lf_sack");
            LCol("ShopSacks", -hw + 0.2, -hw + 1.2, part + 0.5, part + 1.5, 0, 0.95);
            double dz0 = back - 0.85, dz1 = back - 0.1;
            LBox(-hw + 1.6, -hw + 3.1, dz0, dz1, fy + 0.72, fy + 0.77, "lf_counter");     // desk
            foreach (double lx in new[] { -hw + 1.65, -hw + 3.05 }) LBox(lx - 0.03, lx + 0.03, dz0 + 0.05, dz1 - 0.05, fy, fy + 0.72, "charcoal");
            LBox(-hw + 2.1, -hw + 2.6, dz0 - 0.55, dz0 - 0.1, fy + 0.4, fy + 0.45, "charcoal");   // chair
            LCol("ShopDesk", -hw + 1.6, -hw + 3.1, dz0, dz1, 0, 0.8);
            LBox(hw - 0.85, hw - 0.15, back - 0.75, back - 0.05, fy, fy + 1.0, "lf_safe");            // the safe
            LCol("ShopSafe", hw - 0.85, hw - 0.15, back - 0.75, back - 0.05, 0, 1.1);

            // the place (where the story finds it): outside the door, facing the street; car.x/z = unit width, depth
            V2 outside = LP(dx, -1.2);
            D.places.Add(new PlaceInfo { id = "LAGOS_FOODS", kind = "shop", name = LagosName, doorX = outside.x, doorZ = outside.z, doorYaw = Yaw(lagosN),
                                         hasCar = false, carX = W, carZ = D2, carYaw = 0 });
            D.Count("story shops");
        }

        // ================================================================== its pictures (the city's atlas, appended)
        static void LagosAtlas(DressAtlas a)
        {
            a.Swatch("lf_green", Col.Hex("1f6e2e"));
            a.Swatch("lf_wall", Col.Hex("efe4cc"));
            a.Swatch("lf_shelf", Col.Hex("e4e4de"));
            a.Swatch("lf_counter", Col.Hex("6b4527"));
            a.Swatch("lf_top", Col.Hex("d8d2c4"));
            a.Swatch("lf_box", Col.Hex("b08a5a"));
            a.Swatch("lf_sack", Col.Hex("d9c9a3"));
            a.Swatch("lf_safe", Col.Hex("3c4044"));
            a.Swatch("lf_ceiling", Col.Hex("f2efe8"));
            a.Swatch("lf_lamp", Col.Hex("fff4dc"));
            a.Swatch("lf_backwall", Col.Hex("7e4636"));
            Random r = new Random(5150);
            int x, y;
            if (a.Alloc("lf_floor", 32, 32, out x, out y))                      // two-tone vinyl squares
                for (int j = 0; j < 2; j++) for (int i = 0; i < 2; i++)
                        a.Fill(x + i * 16, y + j * 16, 16, 16, (i + j) % 2 == 0 ? Col.Hex("d9d2c2") : Col.Hex("a59c8a"));
            // shelves of goods: tins and packets, bottles (palm oil, drinks), sacks and big bags
            Col[] pal = { Col.Hex("c8102e"), Col.Hex("f2c200"), Col.Hex("1f7a3a"), Col.Hex("e86a1a"), Col.Hex("1d4fb8"), Col.Hex("f4f1ea"), Col.Hex("7a3b2c"), Col.Hex("8a1f6b") };
            for (int g = 1; g <= 3; g++)
            {
                if (!a.Alloc("lf_goods" + g, 128, 60, out x, out y)) continue;
                a.FillV(x, y, 128, 60, Col.Hex("2a2a2a"), Col.Hex("4a4a4a"));
                int px = x + 2;
                while (px < x + 124)
                {
                    int w = g == 1 ? 8 + r.Next(8) : g == 2 ? 5 + r.Next(4) : 18 + r.Next(10);
                    int h = g == 1 ? 26 + r.Next(26) : g == 2 ? 40 + r.Next(16) : 30 + r.Next(22);
                    w = Math.Min(w, x + 126 - px);
                    Col c = pal[r.Next(pal.Length)];
                    a.Fill(px, y + 2, w, h, c);
                    a.Fill(px, y + 2 + h / 3, w, Math.Max(3, h / 5), c.Lerp(Col.Hex("ffffff"), 0.55));      // label
                    if (g == 2) a.Fill(px + w / 3, y + 2 + h, Math.Max(2, w / 3), 4, Col.Hex("222222"));      // cap
                    if (g == 3) a.Fill(px, y + 2 + h - 4, w, 4, c.Scale(0.7));                               // the bag's fold
                    px += w + 1 + r.Next(2);
                }
            }
            if (a.Alloc("lf_fridge", 48, 60, out x, out y))
            {
                a.FillV(x, y, 48, 60, Col.Hex("b8d8e8"), Col.Hex("e8f4fa"));
                for (int row = 0; row < 3; row++)
                    for (int i = 0; i < 7; i++)
                        a.Fill(x + 3 + i * 6, y + 4 + row * 18, 4, 13, pal[r.Next(pal.Length)]);
                a.Frame(x, y, 48, 60, 2, Col.Hex("2c2f32"));
            }
            if (a.Alloc("lf_cigs", 64, 60, out x, out y))                        // top-up cards and the bits behind the counter
            {
                a.Fill(x, y, 64, 60, Col.Hex("303030"));
                for (int row = 0; row < 5; row++)
                    for (int i = 0; i < 8; i++)
                        a.Fill(x + 1 + i * 8, y + 2 + row * 12, 7, 10, pal[r.Next(pal.Length)].Lerp(Col.Hex("ffffff"), r.NextDouble() * 0.3));
            }
            a.Sign("lf_poster1", 128, 60, "PLANTAIN 3 FOR £1", Col.Hex("f2c200"), Col.Hex("1f6e2e"), Col.Hex("1f6e2e"), 0.55);
            a.Sign("lf_poster2", 128, 60, "FRESH YAM IN STORE", Col.Hex("1f6e2e"), Col.Hex("ffffff"), Col.Hex("f2c200"), 0.55);
            a.Sign("lf_staff", 96, 30, "PRIVATE", Col.Hex("3a3d40"), Col.Hex("ffffff"), new Col(0, 0, 0, 0), 0.5);
            if (a.Alloc("lf_screen", 32, 24, out x, out y)) { a.FillV(x, y, 32, 24, Col.Hex("0d3b2a"), Col.Hex("2fbf71")); a.Fill(x + 4, y + 14, 20, 3, Col.Hex("d9ffe9")); }
        }
    }
}
