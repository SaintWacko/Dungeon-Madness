using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DungeonMadness
{
    public enum RoomShape
    {
        /// <summary>A natural cavern: wobbly edge, sometimes rock pillars.</summary>
        Cave,
        Rect,
        /// <summary>A rectangle with rows of columns.</summary>
        Hall,
        Round,
        L,
        Cross,
    }

    /// <summary>What a built room was used for. Ruins are dressed with steles, sarcophagi and urns; the rest are
    /// furnished rooms whose walls carry power. A suite is a large furnished room split by inner walls into smaller
    /// rooms of different uses.</summary>
    public enum RoomUse : byte
    {
        None,
        Ruin,
        Suite,
        Barracks,
        DiningRoom,
        Brewery,
        Storage,
        BatteryRoom,
        PrisonCell,
        ThroneRoom,
        Workshop,
        Kitchen,
        Hospital,
        Library,
    }

    public enum CellFeature : byte
    {
        None,
        Door,
        Plug,
    }

    /// <summary>One chamber of a dungeon, as saved on the site. Phase 4 reads these to place creatures.</summary>
    public class DungeonChamber : IExposable
    {
        public int id;
        /// <summary>Bounding box of the chamber's reachable floor.</summary>
        public int minX, minZ, width, height;
        public IntVec3 center;
        public float radius;
        public RoomShape shape;
        /// <summary>The ceiling has fallen in over part of it: daylight, weather and surface plants.</summary>
        public bool skylight;
        /// <summary>Chambers between this one and the entrance, by link count.</summary>
        public int depth;
        public RoomUse use;
        public List<int> links = new List<int>();

        public bool Furnished => use >= RoomUse.Suite;

        public bool Cave => shape == RoomShape.Cave;
        public CellRect Rect => new CellRect(minX, minZ, width, height);

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref minX, "minX");
            Scribe_Values.Look(ref minZ, "minZ");
            Scribe_Values.Look(ref width, "width");
            Scribe_Values.Look(ref height, "height");
            Scribe_Values.Look(ref center, "center");
            Scribe_Values.Look(ref radius, "radius");
            Scribe_Values.Look(ref shape, "shape");
            Scribe_Values.Look(ref skylight, "skylight");
            Scribe_Values.Look(ref depth, "depth");
            Scribe_Values.Look(ref use, "use");
            Scribe_Collections.Look(ref links, "links", LookMode.Value);
            if (links == null) links = new List<int>();
        }
    }

    /// <summary>What stands between two chambers: a door in a built room's wall, or a plug (collapsed rocks, ice,
    /// a biome plant) in a one-cell squeeze between two caverns.</summary>
    public class DungeonDivider : IExposable
    {
        public List<IntVec3> cells = new List<IntVec3>();
        public ThingDef def;
        // Only read while the map is dressed, so not saved
        public ThingDef filth;
        public bool door;
        public int a, b;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref cells, "cells", LookMode.Value);
            Scribe_Defs.Look(ref def, "def");
            Scribe_Values.Look(ref door, "door");
            Scribe_Values.Look(ref a, "a");
            Scribe_Values.Look(ref b, "b");
            if (cells == null) cells = new List<IntVec3>();
        }
    }

    /// <summary>The part of a generated layout kept on the DungeonSite for the rest of the party's visit.</summary>
    public class DungeonLayoutData : IExposable
    {
        public List<DungeonChamber> chambers = new List<DungeonChamber>();
        public List<DungeonDivider> dividers = new List<DungeonDivider>();
        public IntVec3 exitCell;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref chambers, "chambers", LookMode.Deep);
            Scribe_Collections.Look(ref dividers, "dividers", LookMode.Deep);
            Scribe_Values.Look(ref exitCell, "exitCell");
            if (chambers == null) chambers = new List<DungeonChamber>();
            if (dividers == null) dividers = new List<DungeonDivider>();
        }
    }

    /// <summary>A generated layout plus the per-cell grids the gen step builds the map from. Grids are not saved.</summary>
    public class DungeonLayoutResult
    {
        public int size;
        public DungeonLayoutData data;
        public bool[] open;
        /// <summary>Built-room chamber id per cell, or -1.</summary>
        public int[] built;
        /// <summary>Chamber id per cell (natural or built), or -1 for tunnels and rock.</summary>
        public int[] owner;
        public CellFeature[] feature;
        public ThingDef[] plug;

        public int Index(IntVec3 c) => c.z * size + c.x;
    }

    /// <summary>Smoothed value noise in 0..1, drawn from Rand so it follows the layout seed.</summary>
    public class ValueNoise
    {
        const int S = 97;
        readonly float[] g = new float[S * S];
        readonly float scale;

        public ValueNoise(float scale)
        {
            this.scale = Mathf.Max(0.5f, scale);
            for (int i = 0; i < g.Length; i++) g[i] = Rand.Value;
        }

        float V(int x, int z) => g[((z % S + S) % S) * S + ((x % S + S) % S)];

        public float At(int x, int z)
        {
            float fx = x / scale, fz = z / scale;
            int x0 = Mathf.FloorToInt(fx), z0 = Mathf.FloorToInt(fz);
            float tx = fx - x0, tz = fz - z0;
            tx = tx * tx * (3f - 2f * tx);
            tz = tz * tz * (3f - 2f * tz);
            float a = Mathf.Lerp(V(x0, z0), V(x0 + 1, z0), tx);
            float b = Mathf.Lerp(V(x0, z0 + 1), V(x0 + 1, z0 + 1), tx);
            return Mathf.Lerp(a, b, tz);
        }
    }

    /// <summary>
    /// Lays out a dungeon as a cave system. Mostly natural caverns (wobbly blobs, the odd rock pillar) packed close
    /// together, with a few built rooms of set shapes. Links between chambers are wandering tunnels found by A* over
    /// a noisy cost field; they prefer to run through space that is already open, so they reuse caverns they pass
    /// and the map reads as one system rather than rooms on hallways.
    ///
    /// Dividers suit what they divide: a built room is entered through a door in its wall, and a link between two
    /// caverns may narrow to a one-cell squeeze sealed by locked rock with a plug in it. Built rooms keep an exact
    /// one-cell wall that no tunnel or smoothing pass can breach, so their doors cannot be bypassed.
    ///
    /// Ported from the approved prototype in .circinus/mockups/dungeon-layout.html; keep the two in step.
    /// </summary>
    public class DungeonLayoutGenerator
    {
        class Proto
        {
            public int id, x, z, hw, hh;
            public float r;
            public bool built;
            // Stamped as a plain rectangle, so it can be furnished
            public bool furnishable;
            public RoomShape shape = RoomShape.Cave;
            public List<int> border = new List<int>();
        }

        static readonly int[] DX = { 1, -1, 0, 0 };
        static readonly int[] DZ = { 0, 0, 1, -1 };

        readonly int n;
        readonly float s;
        readonly DungeonThemeDef theme;
        readonly BiomeDef biome;
        readonly float builtChance;
        readonly bool needFurnishable;
        readonly bool[] open, keep, locked, exitMask;
        readonly int[] owner, built;
        readonly CellFeature[] feature;
        readonly ThingDef[] plug;
        readonly List<Proto> ch = new List<Proto>();
        readonly List<DungeonDivider> dividers = new List<DungeonDivider>();
        readonly List<KeyValuePair<int, int>> carved = new List<KeyValuePair<int, int>>();
        int exitX, exitZ;

        // A* scratch, reused across links
        readonly float[] gScore;
        readonly int[] cameFrom, visitStamp;
        int stamp;

        DungeonLayoutGenerator(int size, DungeonThemeDef theme, BiomeDef biome, float builtChance, bool needFurnishable)
        {
            n = size;
            s = size / 100f;
            this.theme = theme;
            this.biome = biome;
            this.builtChance = builtChance;
            this.needFurnishable = needFurnishable;
            int len = n * n;
            open = new bool[len]; keep = new bool[len]; locked = new bool[len]; exitMask = new bool[len];
            owner = new int[len]; built = new int[len];
            feature = new CellFeature[len]; plug = new ThingDef[len];
            gScore = new float[len]; cameFrom = new int[len]; visitStamp = new int[len];
            for (int i = 0; i < len; i++) { owner[i] = -1; built[i] = -1; }
        }

        /// <summary>needFurnishable: at least one room other than the entrance is a built rectangle that can be
        /// furnished, whatever the built chance (an influence paid for entad furniture).</summary>
        public static DungeonLayoutResult Generate(int size, DungeonThemeDef theme, BiomeDef biome, float builtChance, bool needFurnishable = false)
        {
            return new DungeonLayoutGenerator(size, theme, biome, builtChance, needFurnishable).Build();
        }

        int I(int x, int z) => z * n + x;
        int X(int k) => k % n;
        int Z(int k) => k / n;
        bool InMap(int x, int z) => x >= 2 && z >= 2 && x <= n - 3 && z <= n - 3;

        DungeonLayoutResult Build()
        {
            PlaceChambers();
            foreach (var c in ch) if (c.built) StampBuilt(c);
            PlaceExit();
            var blobNoise = new ValueNoise(7f * s + 3f);
            var pillarNoise = new ValueNoise(3.5f);
            foreach (var c in ch) if (!c.built) StampCave(c, blobNoise, pillarNoise);
            ConnectChambers(new ValueNoise(6f));
            Smooth();
            Smooth();
            return Compile();
        }

        // ---- Chambers

        void PlaceChambers()
        {
            float r0 = Mathf.Max(6f, Rand.Range(8f, 10f) * s);
            ch.Add(new Proto
            {
                id = 0,
                x = Mathf.RoundToInt(n / 2f + Rand.Range(-10f, 10f) * s),
                z = Mathf.RoundToInt(n - 1 - (r0 + 6f)),
                r = r0,
                built = Rand.Chance(builtChance),
            });
            int fails = 0;
            while (fails < 900 && ch.Count < 80)
            {
                float r = Mathf.Max(5f, (Rand.Chance(0.2f) ? Rand.Range(12f, 18f) : Rand.Range(6f, 11f)) * s);
                bool b = Rand.Chance(builtChance);
                if (b) r = Mathf.Min(r, Mathf.Max(6f, 10f * s));
                int x = Rand.Range(0, n), z = Rand.Range(0, n);
                if (Fits(x, z, r)) ch.Add(new Proto { id = ch.Count, x = x, z = z, r = r, built = b });
                else fails++;
            }
            if (needFurnishable && ch.Count > 1)
            {
                var pick = ch.Where(c => c.id != 0 && c.built).RandomElementWithFallback() ?? ch.Where(c => c.id != 0).RandomElement();
                pick.built = true;
                pick.furnishable = true;
                pick.r = Mathf.Min(pick.r, Mathf.Max(6f, 10f * s));
            }
        }

        bool Fits(int x, int z, float r)
        {
            if (x - r < 4 || z - r < 4 || x + r > n - 5 || z + r > n - 5) return false;
            foreach (var c in ch)
                if (Mathf.Sqrt((c.x - x) * (c.x - x) + (c.z - z) * (c.z - z)) < c.r + r + Rand.Range(3f, 7f)) return false;
            return true;
        }

        void StampBuilt(Proto c)
        {
            c.hw = Mathf.Max(3, Mathf.RoundToInt(c.r * Rand.Range(0.7f, 1f)));
            c.hh = Mathf.Max(3, Mathf.RoundToInt(c.r * Rand.Range(0.7f, 1f)));
            if (c.id == 0) c.shape = RoomShape.Hall;
            else if (c.furnishable) c.shape = RoomShape.Rect;
            else if (theme.cryptRooms) c.shape = Pick((RoomShape.Hall, 2f), (RoomShape.Rect, 2f), (RoomShape.Cross, 1f));
            else c.shape = Pick((RoomShape.Rect, 2f), (RoomShape.Hall, 1.5f), (RoomShape.Round, 1f), (RoomShape.L, 1.5f), (RoomShape.Cross, 1f));
            int fx = Rand.Bool ? 1 : -1, fz = Rand.Bool ? 1 : -1;
            var mine = new List<int>();
            for (int dz = -c.hh; dz <= c.hh; dz++)
                for (int dx = -c.hw; dx <= c.hw; dx++)
                {
                    bool inside;
                    switch (c.shape)
                    {
                        case RoomShape.Round: inside = (float)(dx * dx) / (c.hw * c.hw) + (float)(dz * dz) / (c.hh * c.hh) <= 1f; break;
                        case RoomShape.L: inside = !(dx * fx > 1 && dz * fz > 1); break;
                        case RoomShape.Cross: inside = Mathf.Abs(dx) <= c.hw / 2.5f || Mathf.Abs(dz) <= c.hh / 2.5f; break;
                        default: inside = true; break;
                    }
                    int x = c.x + dx, z = c.z + dz;
                    if (!inside || !InMap(x, z)) continue;
                    int k = I(x, z);
                    if (built[k] >= 0 || locked[k]) continue; // another room got here first
                    built[k] = c.id; owner[k] = c.id; open[k] = true; keep[k] = true;
                    mine.Add(k);
                }
            // A locked one-cell wall all round, diagonals included
            foreach (int k in mine)
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nk = I(X(k) + dx, Z(k) + dz);
                        if (built[nk] < 0 && !locked[nk]) { locked[nk] = true; c.border.Add(nk); }
                    }
        }

        static RoomShape Pick(params (RoomShape shape, float w)[] options)
        {
            float total = 0f;
            foreach (var o in options) total += o.w;
            float r = Rand.Value * total;
            foreach (var o in options) { r -= o.w; if (r <= 0f) return o.shape; }
            return options[0].shape;
        }

        /// <summary>A 3-cell stairway in the first chamber's north wall. A natural first chamber gets a short
        /// 5-wide approach carved up to it, so the stairs sit at the end of a cut rather than on a ragged edge.</summary>
        void PlaceExit()
        {
            var c0 = ch[0];
            exitX = c0.x;
            if (c0.built) exitZ = c0.z + c0.hh + 1;
            else
            {
                exitZ = c0.z + Mathf.RoundToInt(c0.r * 0.75f);
                for (int z = c0.z; z < exitZ; z++)
                    for (int x = exitX - 2; x <= exitX + 2; x++) { int k = I(x, z); open[k] = true; keep[k] = true; owner[k] = 0; }
                for (int z = exitZ; z <= exitZ + 1; z++)
                    for (int x = exitX - 2; x <= exitX + 2; x++) locked[I(x, z)] = true;
            }
            for (int x = exitX - 1; x <= exitX + 1; x++) { exitMask[I(x, exitZ)] = true; locked[I(x, exitZ)] = true; }
        }

        void StampCave(Proto c, ValueNoise blob, ValueNoise pillars)
        {
            float p1 = Rand.Range(0f, Mathf.PI * 2f), p2 = Rand.Range(0f, Mathf.PI * 2f);
            int r0 = Mathf.CeilToInt(c.r * 1.45f);
            for (int dz = -r0; dz <= r0; dz++)
                for (int dx = -r0; dx <= r0; dx++)
                {
                    int x = c.x + dx, z = c.z + dz;
                    if (!InMap(x, z)) continue;
                    if (c.id == 0 && z >= exitZ) continue;
                    int k = I(x, z);
                    if (locked[k] || built[k] >= 0) continue;
                    float a = Mathf.Atan2(dz, dx);
                    float d = Mathf.Sqrt(dx * dx + dz * dz) / c.r;
                    float lim = 0.85f + 0.12f * Mathf.Sin(3f * a + p1) + 0.08f * Mathf.Sin(5f * a + p2) + (blob.At(x, z) - 0.5f) * 0.55f;
                    if (d >= lim) continue;
                    if (d < lim - 0.3f && c.r > 8f && pillars.At(x, z) > 0.8f) continue; // a rock pillar
                    open[k] = true;
                    if (owner[k] < 0) owner[k] = c.id;
                }
        }

        // ---- Links

        float Gap(int i, int j)
        {
            float dx = ch[i].x - ch[j].x, dz = ch[i].z - ch[j].z;
            return Mathf.Sqrt(dx * dx + dz * dz) - ch[i].r - ch[j].r;
        }

        /// <summary>Spanning tree by gap distance, carving as it goes: a pair joins two groups only once its tunnel
        /// is actually carved, so a failed door or path falls through to the next-nearest pair rather than
        /// stranding a whole branch. Then a few loops between near neighbours.</summary>
        void ConnectChambers(ValueNoise cost)
        {
            int count = ch.Count;
            var pairs = new List<KeyValuePair<float, int>>();
            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                    pairs.Add(new KeyValuePair<float, int>(Gap(i, j), i * count + j));
            pairs.Sort((p, q) => p.Key.CompareTo(q.Key));

            var parent = new int[count];
            for (int i = 0; i < count; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }

            var used = new HashSet<int>();
            foreach (var p in pairs)
            {
                int a = p.Value / count, b = p.Value % count;
                if (Find(a) == Find(b)) continue;
                if (!Carve(a, b, cost)) continue;
                parent[Find(a)] = Find(b);
                carved.Add(new KeyValuePair<int, int>(a, b));
                used.Add(p.Value);
            }
            // Loops between near neighbours
            foreach (var p in pairs)
            {
                if (p.Key >= 9f * s) break;
                if (used.Contains(p.Value) || !Rand.Chance(0.3f)) continue;
                if (Carve(p.Value / count, p.Value % count, cost)) carved.Add(new KeyValuePair<int, int>(p.Value / count, p.Value % count));
            }
        }

        /// <summary>A door cell in a built room's wall facing (tx, tz): a straight stretch of wall with the room on
        /// one side and free rock on the other, at least four cells from any other door of the room.</summary>
        bool TryDoor(Proto c, int tx, int tz, out int doorCell, out int outX, out int outZ)
        {
            doorCell = -1; outX = outZ = 0;
            float best = float.MaxValue;
            foreach (int k in c.border)
            {
                if (exitMask[k] || feature[k] != CellFeature.None) continue;
                int x = X(k), z = Z(k);
                int inDx = 0, inDz = 0, inward = 0;
                for (int d = 0; d < 4; d++)
                    if (built[I(x + DX[d], z + DZ[d])] == c.id) { inDx = DX[d]; inDz = DZ[d]; inward++; }
                if (inward != 1) continue;
                int ox = x - inDx, oz = z - inDz;
                if (!InMap(ox, oz)) continue;
                int ok = I(ox, oz);
                if (built[ok] >= 0 || locked[ok]) continue;
                if (!locked[I(x + inDz, z + inDx)] || !locked[I(x - inDz, z - inDx)]) continue;
                bool crowded = false;
                foreach (int b in c.border)
                    if (feature[b] == CellFeature.Door && Mathf.Abs(X(b) - x) + Mathf.Abs(Z(b) - z) < 4) { crowded = true; break; }
                if (crowded) continue;
                float dist = (tx - x) * (tx - x) + (tz - z) * (tz - z);
                if (dist < best) { best = dist; doorCell = k; outX = ox; outZ = oz; }
            }
            return doorCell >= 0;
        }

        bool Carve(int i, int j, ValueNoise cost)
        {
            Proto A = ch[i], B = ch[j];
            int ax = A.x, az = A.z, bx = B.x, bz = B.z;
            int doorA = -1, doorB = -1;
            if (A.built)
            {
                if (!TryDoor(A, B.x, B.z, out doorA, out ax, out az)) return false;
                feature[doorA] = CellFeature.Door; // claim it now so B's door search sees it
            }
            if (B.built)
            {
                if (!TryDoor(B, ax, az, out doorB, out bx, out bz)) { if (doorA >= 0) feature[doorA] = CellFeature.None; return false; }
                feature[doorB] = CellFeature.Door;
            }
            var path = AStar(ax, az, bx, bz, cost);
            if (path == null)
            {
                if (doorA >= 0) feature[doorA] = CellFeature.None;
                if (doorB >= 0) feature[doorB] = CellFeature.None;
                return false;
            }
            foreach (int d in new[] { doorA, doorB })
            {
                if (d < 0) continue;
                open[d] = true; keep[d] = true;
                dividers.Add(new DungeonDivider { cells = { new IntVec3(X(d), 0, Z(d)) }, door = true, a = i, b = j });
            }

            // A cavern-to-cavern link may narrow to a squeeze: five cells one wide, walled by locked rock, with a
            // plug in the middle. Links out of the entrance chamber never do, so the first room always opens up.
            int pinch = -1;
            if (!A.built && !B.built && i != 0 && j != 0 && Rand.Chance(theme.pinchChance))
            {
                float mid = (path.Count - 1) / 2f, bestD = float.MaxValue;
                for (int p = 3; p < path.Count - 3; p++)
                {
                    int px = X(path[p]), pz = Z(path[p]);
                    bool ok = true;
                    for (int q = p - 2; q <= p + 2 && ok; q++) if (open[path[q]]) ok = false;
                    foreach (int q in new[] { p - 3, p + 3 })
                    {
                        float ddx = X(path[q]) - px, ddz = Z(path[q]) - pz;
                        if (ddx * ddx + ddz * ddz <= 2.6f * 2.6f) ok = false;
                    }
                    // The locked ring must not cut this tunnel anywhere else (a path that bends back into it)
                    for (int q = 0; q < path.Count && ok; q++)
                    {
                        if (q >= p - 2 && q <= p + 2) continue;
                        float ddx = X(path[q]) - px, ddz = Z(path[q]) - pz;
                        if (ddx * ddx + ddz * ddz <= 2.6f * 2.6f) ok = false;
                    }
                    for (int dz = -3; dz <= 3 && ok; dz++)
                        for (int dx = -3; dx <= 3 && ok; dx++)
                        {
                            if (!InMap(px + dx, pz + dz)) { ok = false; break; }
                            int k = I(px + dx, pz + dz);
                            if (open[k] || locked[k]) ok = false;
                        }
                    if (ok && Mathf.Abs(p - mid) < bestD) { bestD = Mathf.Abs(p - mid); pinch = p; }
                }
            }
            if (pinch >= 0)
            {
                int px = X(path[pinch]), pz = Z(path[pinch]);
                for (int dz = -3; dz <= 3; dz++)
                    for (int dx = -3; dx <= 3; dx++)
                        if (dx * dx + dz * dz <= 2.6f * 2.6f) locked[I(px + dx, pz + dz)] = true;
                for (int q = pinch - 2; q <= pinch + 2; q++) { int k = path[q]; locked[k] = false; open[k] = true; keep[k] = true; }
                int length = Rand.RangeInclusive(1, 3);
                int from = pinch - (length == 3 ? 1 : 0);
                ThingDef def = theme.RandomDivider(biome, out ThingDef filth);
                var divider = new DungeonDivider { def = def, filth = filth, a = i, b = j };
                for (int q = from; q < from + length; q++)
                {
                    feature[path[q]] = CellFeature.Plug;
                    plug[path[q]] = def;
                    divider.cells.Add(new IntVec3(X(path[q]), 0, Z(path[q])));
                }
                dividers.Add(divider);
            }

            float w = Rand.Range(0.9f, 1.7f);
            int rad = Mathf.CeilToInt(w);
            for (int p = 0; p < path.Count; p++)
            {
                if (pinch >= 0 && p >= pinch - 2 && p <= pinch + 2) continue;
                int x = X(path[p]), z = Z(path[p]);
                for (int dz = -rad; dz <= rad; dz++)
                    for (int dx = -rad; dx <= rad; dx++)
                    {
                        if (dx * dx + dz * dz > w * w) continue;
                        int cx = x + dx, cz = z + dz;
                        if (!InMap(cx, cz)) continue;
                        int k = I(cx, cz);
                        if (locked[k] || built[k] >= 0) continue;
                        open[k] = true;
                    }
            }
            return true;
        }

        /// <summary>4-connected A* that is cheap through open space and wanders through solid rock on a noise
        /// field. Built rooms and locked cells are impassable except as the target.</summary>
        List<int> AStar(int sx, int sz, int tx, int tz, ValueNoise cost)
        {
            stamp++;
            int start = I(sx, sz), target = I(tx, tz);
            var heap = new MinHeap();
            gScore[start] = 0f; cameFrom[start] = -1; visitStamp[start] = stamp;
            heap.Push(start, 0f);
            bool found = false;
            while (heap.Count > 0)
            {
                int k = heap.Pop(out float f);
                if (k == target) { found = true; break; }
                if (f - Heuristic(k, tx, tz) > gScore[k] + 0.001f) continue; // stale entry
                int x = X(k), z = Z(k);
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + DX[d], nz = z + DZ[d];
                    if (!InMap(nx, nz)) continue;
                    int nk = I(nx, nz);
                    if (nk != target && (locked[nk] || built[nk] >= 0)) continue;
                    float step = open[nk] ? 0.5f : 1.5f + cost.At(nx, nz) * 7f;
                    float ng = gScore[k] + step;
                    if (visitStamp[nk] == stamp && ng >= gScore[nk]) continue;
                    visitStamp[nk] = stamp; gScore[nk] = ng; cameFrom[nk] = k;
                    heap.Push(nk, ng + Heuristic(nk, tx, tz));
                }
            }
            if (!found) return null;
            var path = new List<int>();
            for (int k = target; k != -1; k = cameFrom[k]) path.Add(k);
            path.Reverse();
            return path;
        }

        float Heuristic(int k, int tx, int tz) => Mathf.Abs(X(k) - tx) + Mathf.Abs(Z(k) - tz);

        class MinHeap
        {
            readonly List<int> keys = new List<int>();
            readonly List<float> pri = new List<float>();
            public int Count => keys.Count;

            public void Push(int k, float p)
            {
                keys.Add(k); pri.Add(p);
                int i = keys.Count - 1;
                while (i > 0)
                {
                    int up = (i - 1) >> 1;
                    if (pri[up] <= pri[i]) break;
                    Swap(i, up); i = up;
                }
            }

            public int Pop(out float p)
            {
                int top = keys[0]; p = pri[0];
                int last = keys.Count - 1;
                keys[0] = keys[last]; pri[0] = pri[last];
                keys.RemoveAt(last); pri.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < keys.Count && pri[l] < pri[m]) m = l;
                    if (r < keys.Count && pri[r] < pri[m]) m = r;
                    if (m == i) break;
                    Swap(i, m); i = m;
                }
                return top;
            }

            void Swap(int a, int b)
            {
                int k = keys[a]; keys[a] = keys[b]; keys[b] = k;
                float t = pri[a]; pri[a] = pri[b]; pri[b] = t;
            }
        }

        // ---- Finish

        /// <summary>One cellular-automaton pass over natural rock: fills pockets, rounds corners. Built rooms,
        /// squeezes, the exit and other kept cells are left exactly as they are.</summary>
        void Smooth()
        {
            var next = (bool[])open.Clone();
            for (int z = 2; z <= n - 3; z++)
                for (int x = 2; x <= n - 3; x++)
                {
                    int k = I(x, z);
                    if (keep[k] || locked[k] || built[k] >= 0) continue;
                    int around = 0;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                            if ((dx != 0 || dz != 0) && open[I(x + dx, z + dz)]) around++;
                    if (around >= 5) next[k] = true;
                    else if (around <= 1) next[k] = false;
                }
            System.Array.Copy(next, open, open.Length);
        }

        DungeonLayoutResult Compile()
        {
            // Anything the exit cannot reach is filled back in
            var seen = new bool[n * n];
            var stack = new Stack<int>();
            int startCell = I(exitX, exitZ - 1);
            seen[startCell] = true; stack.Push(startCell);
            while (stack.Count > 0)
            {
                int k = stack.Pop();
                for (int d = 0; d < 4; d++)
                {
                    int nk = I(X(k) + DX[d], Z(k) + DZ[d]);
                    if (!seen[nk] && open[nk]) { seen[nk] = true; stack.Push(nk); }
                }
            }
            for (int k = 0; k < open.Length; k++)
                if (open[k] && !seen[k]) { open[k] = false; feature[k] = CellFeature.None; plug[k] = null; }

            // Chambers with no reachable floor are dropped; bounding boxes are taken from what is left
            int count = ch.Count;
            var minX = new int[count]; var minZ = new int[count]; var maxX = new int[count]; var maxZ = new int[count];
            for (int i = 0; i < count; i++) { minX[i] = minZ[i] = int.MaxValue; maxX[i] = maxZ[i] = int.MinValue; }
            for (int k = 0; k < open.Length; k++)
            {
                int o = owner[k];
                if (o < 0 || !open[k]) continue;
                int x = X(k), z = Z(k);
                if (x < minX[o]) minX[o] = x;
                if (z < minZ[o]) minZ[o] = z;
                if (x > maxX[o]) maxX[o] = x;
                if (z > maxZ[o]) maxZ[o] = z;
            }
            var remap = new int[count];
            var data = new DungeonLayoutData { exitCell = new IntVec3(exitX, 0, exitZ) };
            for (int i = 0; i < count; i++)
            {
                remap[i] = -1;
                if (maxX[i] < minX[i]) continue;
                remap[i] = data.chambers.Count;
                var p = ch[i];
                data.chambers.Add(new DungeonChamber
                {
                    id = data.chambers.Count,
                    minX = minX[i], minZ = minZ[i], width = maxX[i] - minX[i] + 1, height = maxZ[i] - minZ[i] + 1,
                    center = new IntVec3(p.x, 0, p.z), radius = p.r, shape = p.shape,
                });
            }
            for (int k = 0; k < open.Length; k++)
            {
                if (owner[k] >= 0) owner[k] = open[k] ? remap[owner[k]] : -1;
                if (built[k] >= 0) built[k] = remap[built[k]];
            }

            foreach (var d in dividers)
            {
                if (!open[I(d.cells[0].x, d.cells[0].z)]) continue;
                int a = remap[d.a], b = remap[d.b];
                if (a < 0 || b < 0) continue;
                d.a = a; d.b = b;
                data.dividers.Add(d);
            }
            foreach (var l in carved)
            {
                int a = remap[l.Key], b = remap[l.Value];
                if (a < 0 || b < 0 || data.chambers[a].links.Contains(b)) continue;
                data.chambers[a].links.Add(b);
                data.chambers[b].links.Add(a);
            }

            foreach (var c in data.chambers) c.depth = -1;
            var queue = new Queue<int>();
            if (data.chambers.Count > 0 && remap[0] == 0) { data.chambers[0].depth = 0; queue.Enqueue(0); }
            while (queue.Count > 0)
            {
                var cur = data.chambers[queue.Dequeue()];
                foreach (int l in cur.links)
                    if (data.chambers[l].depth < 0) { data.chambers[l].depth = cur.depth + 1; queue.Enqueue(l); }
            }

            return new DungeonLayoutResult { size = n, data = data, open = open, built = built, owner = owner, feature = feature, plug = plug };
        }
    }
}
