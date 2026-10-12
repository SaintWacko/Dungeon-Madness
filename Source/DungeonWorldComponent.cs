using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace DungeonMadness
{
    /// <summary>
    /// Every dungeon in the world, as plain data: a surface tile id and a level. Dungeons are deliberately not
    /// WorldObjects: at the default spacing a world holds thousands of them, and the world object holder ticks,
    /// draws and raycasts each one. A real world object is only made for the dungeon a caravan is using.
    /// </summary>
    public class DungeonWorldComponent : WorldComponent
    {
        // Bumped when generation changes in a way old saves should not keep
        private const int CurrentGenVersion = 1;

        private int genVersion;
        // The max level this world was generated with: the settings slider may have moved since
        private int generatedMaxLevel;
        // Parallel arrays, sorted by tile id
        private int[] tiles = Array.Empty<int>();
        private ushort[] levels = Array.Empty<ushort>();
        private Dictionary<int, int> indexByTile = new Dictionary<int, int>();

        // Pawns who have entered each dungeon, by surface tile id, as pawn load ids. Load ids rather than Pawn
        // references so a pawn who has died and been discarded does not leave a dangling reference.
        private Dictionary<int, EnteredSet> enteredBy = new Dictionary<int, EnteredSet>();

        // Scribe cannot nest a collection directly inside a dictionary value
        private class EnteredSet : IExposable
        {
            public HashSet<string> ids = new HashSet<string>();
            public void ExposeData()
            {
                Scribe_Collections.Look(ref ids, "ids", LookMode.Value);
                if (ids == null) ids = new HashSet<string>();
            }
        }

        // Map parent count the layer was last drawn against: a settlement or site on a dungeon tile hides its icon,
        // and an abandoned one shows it again. Caravans are not map parents, so they never trigger a rebuild.
        private int drawnMapParentCount = -1;

        public DungeonWorldComponent(World world) : base(world) { }

        public static DungeonWorldComponent Instance => Find.World?.GetComponent<DungeonWorldComponent>();

        public int Count => tiles.Length;
        public int GetTile(int index) => tiles[index];
        public int GetLevel(int index) => levels[index];
        public int MaxLevel => generatedMaxLevel;

        public bool HasEntered(int tileId, Pawn pawn) =>
            enteredBy.TryGetValue(tileId, out var set) && set.ids.Contains(pawn.GetUniqueLoadID());

        public void MarkEntered(int tileId, Pawn pawn)
        {
            if (!enteredBy.TryGetValue(tileId, out var set)) enteredBy[tileId] = set = new EnteredSet();
            set.ids.Add(pawn.GetUniqueLoadID());
        }

        public bool TryGetLevel(PlanetTile tile, out int level)
        {
            level = 0;
            if (!tile.Valid || tile.Layer != world.grid.Surface) return false;
            if (!indexByTile.TryGetValue(tile.tileId, out int i)) return false;
            level = levels[i];
            return true;
        }

        /// <summary>Whether the dungeon on this tile can be used: something else (a settlement, a site, a player
        /// colony) built on the tile after generation takes it over.</summary>
        public bool IsBlocked(PlanetTile tile)
        {
            foreach (var mp in world.worldObjects.MapParents)
                if (mp.Tile == tile && !(mp is DungeonSite)) return true;
            return false;
        }

        /// <summary>Surface tile ids that hold a map parent, for checking many dungeons at once.</summary>
        public HashSet<int> BlockedSurfaceTiles()
        {
            var set = new HashSet<int>();
            var surface = world.grid.Surface;
            foreach (var mp in world.worldObjects.MapParents)
                if (mp.Tile.Valid && mp.Tile.Layer == surface && !(mp is DungeonSite)) set.Add(mp.Tile.tileId);
            return set;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref genVersion, "genVersion");
            Scribe_Values.Look(ref generatedMaxLevel, "generatedMaxLevel");

            byte[] tileBytes = null, levelBytes = null;
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                tileBytes = new byte[tiles.Length * sizeof(int)];
                Buffer.BlockCopy(tiles, 0, tileBytes, 0, tileBytes.Length);
                levelBytes = new byte[levels.Length * sizeof(ushort)];
                Buffer.BlockCopy(levels, 0, levelBytes, 0, levelBytes.Length);
            }
            Scribe_Collections.Look(ref enteredBy, "enteredBy", LookMode.Value, LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && enteredBy == null) enteredBy = new Dictionary<int, EnteredSet>();
            DataExposeUtility.LookByteArray(ref tileBytes, "tiles");
            DataExposeUtility.LookByteArray(ref levelBytes, "levels");
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                tiles = new int[(tileBytes?.Length ?? 0) / sizeof(int)];
                if (tiles.Length > 0) Buffer.BlockCopy(tileBytes, 0, tiles, 0, tiles.Length * sizeof(int));
                levels = new ushort[(levelBytes?.Length ?? 0) / sizeof(ushort)];
                if (levels.Length > 0) Buffer.BlockCopy(levelBytes, 0, levels, 0, levels.Length * sizeof(ushort));
                if (levels.Length != tiles.Length)
                {
                    Log.Error($"[Dungeon Madness] Saved dungeon data is inconsistent ({tiles.Length} tiles, {levels.Length} levels); regenerating.");
                    genVersion = 0;
                }
            }
        }

        public override void FinalizeInit(bool fromLoad)
        {
            base.FinalizeInit(fromLoad);
            // New worlds, and saves from before the mod was added
            if (genVersion != CurrentGenVersion) Generate();
            else RebuildIndex();
            if (generatedMaxLevel <= 0) generatedMaxLevel = levels.Length > 0 ? levels.Max(l => (int)l) : DungeonsMod.Settings.maxLevel;
        }

        public override void WorldComponentUpdate()
        {
            int count = world.worldObjects.MapParents.Count;
            if (count != drawnMapParentCount)
            {
                drawnMapParentCount = count;
                SetLayerDirty();
            }
        }

        public static void SetLayerDirty()
        {
            var w = Find.World;
            if (w?.renderer == null || w.grid?.Surface == null) return;
            w.renderer.SetDirty<WorldDrawLayer_Dungeons>(w.grid.Surface);
        }

        private void RebuildIndex()
        {
            indexByTile = new Dictionary<int, int>(tiles.Length);
            for (int i = 0; i < tiles.Length; i++) indexByTile[tiles[i]] = i;
        }

        public static bool VehicleFrameworkActive => ModsConfig.IsActive("SmashPhil.VehicleFramework");

        /// <summary>Places dungeons over the surface layer and assigns their levels. Replaces any existing ones.</summary>
        public void Generate()
        {
            var settings = DungeonsMod.Settings;
            var layer = world.grid.Surface;
            int n = layer.TilesCount;
            int seed = Gen.HashCombineInt(world.info.Seed, 0x7D0A6E5);
            bool allowWater = settings.oceanDungeons && VehicleFrameworkActive;

            // Poisson-disk placement: visit tiles in random order and take each eligible tile that no earlier
            // dungeon is within `spacing` tiles of. The random order keeps it from lining up into rows.
            var rng = new System.Random(seed);
            int[] order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            for (int i = n - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            var blocked = new bool[n];
            var visited = new int[n]; // stamp per placed dungeon, so the BFS never clears an array
            var queue = new Queue<int>();
            var depth = new Dictionary<int, int>();
            var neighbors = new List<PlanetTile>(8);
            var chosen = new List<int>();
            // Built once: AnyWorldObjectAt walks every world object per call
            var occupied = new HashSet<int>();
            foreach (var wo in world.worldObjects.AllWorldObjects)
                if (wo.Tile.Valid && wo.Tile.Layer == layer) occupied.Add(wo.Tile.tileId);
            int radius = Mathf.Max(1, settings.spacing - 1);
            int stamp = 0;

            foreach (int t in order)
            {
                if (blocked[t] || occupied.Contains(t) || !Eligible(layer, t, allowWater)) continue;
                chosen.Add(t);
                stamp++;
                blocked[t] = true;
                visited[t] = stamp;
                queue.Clear();
                depth.Clear();
                queue.Enqueue(t);
                depth[t] = 0;
                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    int d = depth[cur];
                    if (d >= radius) continue;
                    layer.GetTileNeighbors(cur, neighbors);
                    foreach (var nb in neighbors)
                    {
                        int id = nb.tileId;
                        if (visited[id] == stamp) continue;
                        visited[id] = stamp;
                        blocked[id] = true;
                        depth[id] = d + 1;
                        queue.Enqueue(id);
                    }
                }
            }

            chosen.Sort();
            tiles = chosen.ToArray();
            // Entry records are keyed by tile and belong to the old layout
            enteredBy.Clear();
            levels = AssignLevels(layer, tiles, seed, settings);
            genVersion = CurrentGenVersion;
            generatedMaxLevel = settings.maxLevel;
            RebuildIndex();
            SetLayerDirty();
            Log.Message($"[Dungeon Madness] Placed {tiles.Length} dungeons over {n} tiles (spacing {settings.spacing}, max level {settings.maxLevel}).");
        }

        private bool Eligible(PlanetLayer layer, int t, bool allowWater)
        {
            var tile = layer[t];
            var pt = new PlanetTile(t, layer);
            if (tile.WaterCovered)
            {
                if (!allowWater) return false;
            }
            else if (world.Impassable(pt)) return false;
            return true;
        }

        // Smooth noise over the globe gives connected valleys and peaks. Ranking the values turns them into an even
        // spread before the skew, so every level gets dungeons whatever the noise's own distribution looks like.
        private static ushort[] AssignLevels(PlanetLayer layer, int[] tiles, int seed, DungeonSettings settings)
        {
            var noise = new Perlin(settings.roughness, 2.0, 0.5, 3, seed, QualityMode.Medium);
            int count = tiles.Length;
            var values = new double[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 c = layer.GetTileCenter(tiles[i]).normalized;
                values[i] = noise.GetValue(c.x, c.y, c.z);
            }
            int[] byValue = Enumerable.Range(0, count).OrderBy(i => values[i]).ToArray();

            var result = new ushort[count];
            int max = settings.maxLevel;
            for (int rank = 0; rank < count; rank++)
            {
                float p = (rank + 0.5f) / count;
                int level = 1 + Mathf.FloorToInt(Mathf.Pow(p, settings.levelSkew) * max);
                result[byValue[rank]] = (ushort)Mathf.Clamp(level, 1, max);
            }
            return result;
        }
    }
}
