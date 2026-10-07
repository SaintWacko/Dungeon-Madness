using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ThisUsedToBeAboutDungeons
{
    /// <summary>What blocks the passage between two chambers. Every connection has exactly one, so a party deals
    /// with one room at a time: the cells beyond stay fogged until the divider is opened or removed.</summary>
    public enum DividerType
    {
        Door,
        Rubble,
    }

    /// <summary>One room of a dungeon, as saved on the site. Phase 4 reads these to decide where creatures go.</summary>
    public class DungeonChamber : IExposable
    {
        public int id;
        public int minX, minZ, width, height;
        /// <summary>A ragged cave pocket rather than a built room.</summary>
        public bool cave;
        /// <summary>Chambers between this one and the entrance, by the shortest route.</summary>
        public int depth;
        public List<int> links = new List<int>();

        public CellRect Rect => new CellRect(minX, minZ, width, height);
        public IntVec3 Center => Rect.CenterCell;

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref minX, "minX");
            Scribe_Values.Look(ref minZ, "minZ");
            Scribe_Values.Look(ref width, "width");
            Scribe_Values.Look(ref height, "height");
            Scribe_Values.Look(ref cave, "cave");
            Scribe_Values.Look(ref depth, "depth");
            Scribe_Collections.Look(ref links, "links", LookMode.Value);
            if (links == null) links = new List<int>();
        }
    }

    public class DungeonDivider : IExposable
    {
        public IntVec3 cell;
        public DividerType type;
        public int a, b;

        public void ExposeData()
        {
            Scribe_Values.Look(ref cell, "cell");
            Scribe_Values.Look(ref type, "type");
            Scribe_Values.Look(ref a, "a");
            Scribe_Values.Look(ref b, "b");
        }
    }

    /// <summary>The part of a generated layout that is kept on the DungeonSite for the rest of the party's visit.</summary>
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

    /// <summary>A generated layout plus the per-cell grids needed to build the map. The grids are not saved.</summary>
    public class DungeonLayoutResult
    {
        public int size;
        public DungeonLayoutData data;
        /// <summary>Chamber id owning each cell (z * size + x), or -1.</summary>
        public int[] chamberAt;
        /// <summary>True for passage cells between chambers.</summary>
        public bool[] corridor;

        public int Index(IntVec3 c) => c.z * size + c.x;
        public bool IsOpen(IntVec3 c) => chamberAt[Index(c)] >= 0 || corridor[Index(c)];
    }

    /// <summary>
    /// Lays out a dungeon as a graph of chambers joined by one-cell-wide passages, each passage holding a divider.
    ///
    /// Chambers are placed by rejection sampling with at least Gap cells of solid wall between any two, so the only
    /// way from one to another is a passage. A passage is an L-shaped path between two chamber centres; it is
    /// refused when it would touch another chamber, another passage, or the exit's reserved area, so no passage
    /// can be bypassed. A spanning tree is built first (shortest feasible links), then a few extra links add loops.
    /// Chambers that no passage could reach are dropped.
    /// </summary>
    public class DungeonLayoutGenerator
    {
        public const int Gap = 3;
        public const int Edge = 3;
        const int Unowned = -1;
        const int Reserved = -2;

        readonly int n;
        readonly int[] chamberAt;
        readonly int[] corridorAt;
        readonly List<DungeonChamber> chambers = new List<DungeonChamber>();
        readonly List<Edge2> edges = new List<Edge2>();
        readonly float caveFraction;

        struct Edge2
        {
            public int a, b;
            public List<IntVec3> cells;
            public int dividerIndex;
        }

        DungeonLayoutGenerator(int size, float caveFraction)
        {
            n = size;
            this.caveFraction = caveFraction;
            chamberAt = new int[n * n];
            corridorAt = new int[n * n];
            for (int i = 0; i < chamberAt.Length; i++) { chamberAt[i] = Unowned; corridorAt[i] = -1; }
        }

        public static DungeonLayoutResult Generate(int size, float caveFraction)
        {
            return new DungeonLayoutGenerator(size, caveFraction).Build();
        }

        int Idx(IntVec3 c) => c.z * n + c.x;
        bool InBounds(IntVec3 c) => c.x >= 0 && c.z >= 0 && c.x < n && c.z < n;

        DungeonLayoutResult Build()
        {
            float sizeT = Mathf.InverseLerp(DungeonEntry.MinMapSize, DungeonEntry.MaxMapSize, n);
            int target = Mathf.Clamp(Mathf.RoundToInt(n * n / 650f), 5, 40);
            PlaceChambers(target, Mathf.RoundToInt(Mathf.Lerp(11f, 16f, sizeT)));
            ReserveExit();
            Connect();
            return Compile();
        }

        // ---- Chambers

        void PlaceChambers(int target, int maxDim)
        {
            int failures = 0;
            while (chambers.Count < target && failures < 400)
            {
                int w = Rand.RangeInclusive(7, maxDim), h = Rand.RangeInclusive(7, maxDim);
                int xMax = n - Edge - w, zMax = n - 4 - h; // room above the first chamber for the exit stairway
                if (xMax < Edge || zMax < Edge) { failures++; continue; }
                var rect = new CellRect(Rand.RangeInclusive(Edge, xMax), Rand.RangeInclusive(Edge, zMax), w, h);
                bool clear = true;
                for (int i = 0; i < chambers.Count && clear; i++)
                    if (chambers[i].Rect.ExpandedBy(Gap).Overlaps(rect)) clear = false;
                if (!clear) { failures++; continue; }

                // The first chamber holds the exit and is always a built room
                bool cave = chambers.Count > 0 && Rand.Chance(caveFraction);
                var chamber = new DungeonChamber
                {
                    id = chambers.Count, minX = rect.minX, minZ = rect.minZ, width = w, height = h, cave = cave,
                };
                chambers.Add(chamber);
                Stamp(chamber);
            }
        }

        void Stamp(DungeonChamber ch)
        {
            CellRect rect = ch.Rect;
            if (!ch.cave)
            {
                foreach (var c in rect) chamberAt[Idx(c)] = ch.id;
                return;
            }
            // A ragged blob: the radius wobbles with angle, so the shape is star-shaped around its centre
            float p1 = Rand.Range(0f, Mathf.PI * 2f), p2 = Rand.Range(0f, Mathf.PI * 2f);
            float cx = rect.minX + (rect.Width - 1) / 2f, cz = rect.minZ + (rect.Height - 1) / 2f;
            float rx = Mathf.Max(1f, (rect.Width - 1) / 2f), rz = Mathf.Max(1f, (rect.Height - 1) / 2f);
            foreach (var c in rect)
            {
                float dx = (c.x - cx) / rx, dz = (c.z - cz) / rz;
                float limit = 0.88f + 0.10f * Mathf.Sin(3f * Mathf.Atan2(dz, dx) + p1) + 0.07f * Mathf.Sin(5f * Mathf.Atan2(dz, dx) + p2);
                if (Mathf.Sqrt(dx * dx + dz * dz) <= limit) chamberAt[Idx(c)] = ch.id;
            }
            chamberAt[Idx(ch.Center)] = ch.id;
        }

        /// <summary>The stairway sits in the wall just above the first chamber's north edge; keep passages away.</summary>
        void ReserveExit()
        {
            if (chambers.Count == 0) return;
            IntVec3 exit = ExitCellFor(chambers[0]);
            var zone = new CellRect(exit.x - 2, exit.z, 5, 2);
            foreach (var c in zone)
                if (InBounds(c) && chamberAt[Idx(c)] == Unowned) chamberAt[Idx(c)] = Reserved;
        }

        public static IntVec3 ExitCellFor(DungeonChamber first)
        {
            return new IntVec3(first.Center.x, 0, first.minZ + first.height);
        }

        // ---- Passages

        void Connect()
        {
            int count = chambers.Count;
            var parent = new int[count];
            for (int i = 0; i < count; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }

            var pairs = new List<KeyValuePair<float, int>>();
            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                    pairs.Add(new KeyValuePair<float, int>((chambers[i].Center - chambers[j].Center).LengthHorizontalSquared, i * count + j));
            pairs.Sort((p, q) => p.Key.CompareTo(q.Key));

            // Spanning tree: shortest feasible links that join two separate groups
            foreach (var p in pairs)
            {
                int a = p.Value / count, b = p.Value % count;
                if (Find(a) == Find(b)) continue;
                if (!TryLink(a, b)) continue;
                parent[Find(a)] = Find(b);
            }

            // A few extra links between near neighbours make loops
            int loops = Mathf.Max(1, count / 4);
            foreach (var p in pairs)
            {
                if (loops <= 0) break;
                int a = p.Value / count, b = p.Value % count;
                if (Find(a) != Find(0) || Find(b) != Find(0)) continue;
                if (chambers[a].links.Contains(b)) continue;
                if (p.Key > 40f * 40f || !Rand.Chance(0.35f)) continue;
                if (TryLink(a, b)) loops--;
            }

            // Drop chambers no passage reached
            int root = Find(0);
            var keep = new List<DungeonChamber>();
            var remap = new int[count];
            for (int i = 0; i < count; i++)
            {
                remap[i] = -1;
                if (Find(i) == root) { remap[i] = keep.Count; keep.Add(chambers[i]); }
                else foreach (var c in chambers[i].Rect) if (chamberAt[Idx(c)] == chambers[i].id) chamberAt[Idx(c)] = Unowned;
            }
            for (int i = 0; i < chamberAt.Length; i++) if (chamberAt[i] >= 0) chamberAt[i] = remap[chamberAt[i]];
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                e.a = remap[e.a]; e.b = remap[e.b];
                edges[i] = e;
            }
            chambers.Clear();
            for (int i = 0; i < keep.Count; i++)
            {
                keep[i].id = i;
                keep[i].links = new List<int>();
                chambers.Add(keep[i]);
            }
            foreach (var e in edges)
            {
                chambers[e.a].links.Add(e.b);
                chambers[e.b].links.Add(e.a);
            }
        }

        bool TryLink(int a, int b)
        {
            if (!TryRoute(chambers[a], chambers[b], out var cells, out int divider)) return false;
            int edgeId = edges.Count;
            foreach (var c in cells) corridorAt[Idx(c)] = edgeId;
            edges.Add(new Edge2 { a = a, b = b, cells = cells, dividerIndex = divider });
            chambers[a].links.Add(b);
            chambers[b].links.Add(a);
            return true;
        }

        bool TryRoute(DungeonChamber ca, DungeonChamber cb, out List<IntVec3> cells, out int divider)
        {
            cells = null;
            divider = -1;
            bool horizontalFirst = Rand.Bool;
            for (int attempt = 0; attempt < 2; attempt++, horizontalFirst = !horizontalFirst)
            {
                var path = PathBetween(ca.Center, cb.Center, horizontalFirst);
                var outside = new List<IntVec3>();
                int phase = 0; // 0 inside A, 1 in the passage, 2 inside B
                bool ok = true;
                foreach (var c in path)
                {
                    int owner = chamberAt[Idx(c)];
                    if (owner == ca.id) { if (phase != 0) { ok = false; break; } }
                    else if (owner == cb.id) phase = 2;
                    else if (owner == Unowned)
                    {
                        if (phase == 2) { ok = false; break; }
                        phase = 1;
                        outside.Add(c);
                    }
                    else { ok = false; break; }
                }
                if (!ok || phase != 2 || outside.Count < 3) continue;
                if (!PassageIsClear(outside, ca.id, cb.id)) continue;
                int d = PickDividerIndex(outside);
                if (d < 0) continue;
                cells = outside;
                divider = d;
                return true;
            }
            return false;
        }

        static List<IntVec3> PathBetween(IntVec3 a, IntVec3 b, bool horizontalFirst)
        {
            var path = new List<IntVec3> { a };
            int x = a.x, z = a.z;
            void Horizontal() { int s = b.x >= x ? 1 : -1; while (x != b.x) { x += s; path.Add(new IntVec3(x, 0, z)); } }
            void Vertical() { int s = b.z >= z ? 1 : -1; while (z != b.z) { z += s; path.Add(new IntVec3(x, 0, z)); } }
            if (horizontalFirst) { Horizontal(); Vertical(); } else { Vertical(); Horizontal(); }
            return path;
        }

        bool PassageIsClear(List<IntVec3> cells, int a, int b)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                IntVec3 c = cells[i];
                if (c.x < 2 || c.z < 2 || c.x > n - 3 || c.z > n - 3) return false;
                bool middle = i >= 1 && i <= cells.Count - 2;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        var nb = new IntVec3(c.x + dx, 0, c.z + dz);
                        int idx = Idx(nb);
                        int owner = chamberAt[idx];
                        if (owner == Reserved) return false;
                        if (owner >= 0 && owner != a && owner != b) return false;
                        // A passage that runs along a room's edge would open onto it more than once
                        if (owner >= 0 && middle) return false;
                        if (corridorAt[idx] != -1) return false;
                    }
            }
            return true;
        }

        /// <summary>A cell near the middle of the passage whose neighbours along it are in a straight line, so the
        /// divider has solid wall on both sides.</summary>
        static int PickDividerIndex(List<IntVec3> cells)
        {
            var good = new List<int>();
            for (int i = 1; i <= cells.Count - 2; i++)
            {
                IntVec3 d = cells[i + 1] - cells[i - 1];
                if ((Mathf.Abs(d.x) == 2 && d.z == 0) || (Mathf.Abs(d.z) == 2 && d.x == 0)) good.Add(i);
            }
            if (good.Count == 0) return -1;
            float mid = (cells.Count - 1) / 2f;
            float best = float.MaxValue;
            foreach (int i in good) best = Mathf.Min(best, Mathf.Abs(i - mid));
            var near = good.FindAll(i => Mathf.Abs(i - mid) <= best + 1f);
            return near.RandomElement();
        }

        // ---- Result

        DungeonLayoutResult Compile()
        {
            var data = new DungeonLayoutData();
            data.chambers.AddRange(chambers);

            // Depth: chambers between each one and the entrance
            foreach (var c in chambers) c.depth = -1;
            var queue = new Queue<int>();
            if (chambers.Count > 0) { chambers[0].depth = 0; queue.Enqueue(0); }
            while (queue.Count > 0)
            {
                var cur = chambers[queue.Dequeue()];
                foreach (int l in cur.links)
                    if (chambers[l].depth < 0) { chambers[l].depth = cur.depth + 1; queue.Enqueue(l); }
            }

            var corridor = new bool[n * n];
            foreach (var e in edges)
            {
                foreach (var c in e.cells) corridor[Idx(c)] = true;
                data.dividers.Add(new DungeonDivider
                {
                    cell = e.cells[e.dividerIndex],
                    type = Rand.Chance(0.55f) ? DividerType.Door : DividerType.Rubble,
                    a = e.a,
                    b = e.b,
                });
            }
            if (chambers.Count > 0) data.exitCell = ExitCellFor(chambers[0]);

            // Reserved markers served only to keep passages away; they are wall like everything else
            for (int i = 0; i < chamberAt.Length; i++) if (chamberAt[i] == Reserved) chamberAt[i] = Unowned;
            return new DungeonLayoutResult { size = n, data = data, chamberAt = chamberAt, corridor = corridor };
        }
    }
}
