using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace DungeonMadness
{
    public enum FloorMode
    {
        /// <summary>Fills the low points of its noise field (pools, sheets).</summary>
        Low,
        /// <summary>Fills the high points (drifts, beds).</summary>
        High,
        /// <summary>A band through the middle (winding strips).</summary>
        Mid,
    }

    /// <summary>A terrain looked up on the dungeon's biome, so a modded biome's own ground shows up in its caves.</summary>
    public enum BiomeTerrain
    {
        None,
        Gravel,
        Mud,
        ShallowWater,
        /// <summary>The biome's most fertile non-water ground.</summary>
        Soil,
    }

    public class FloorPatch
    {
        /// <summary>Used when biomeTerrain is None, or when the biome does not define that terrain.</summary>
        public TerrainDef terrain;
        public BiomeTerrain biomeTerrain = BiomeTerrain.None;
        /// <summary>Roughly the fraction of natural floor this patch covers.</summary>
        public float share = 0.2f;
        public FloorMode mode = FloorMode.Low;

        public TerrainDef Resolve(BiomeDef biome) => DungeonThemeUtility.BiomeTerrainFor(biome, biomeTerrain) ?? terrain;
    }

    public class DividerOption
    {
        public ThingDef thing;
        /// <summary>A cover plant from the dungeon's biome instead of a set thing; collapsed rocks if it has none.</summary>
        public bool biomePlant;
        /// <summary>Scattered on the open cells either side of the plug (sand around a buried ruin's cave-in).</summary>
        public ThingDef filth;
        public float weight = 1f;
    }

    /// <summary>
    /// How a dungeon looks: its floors, what plugs the squeezes between caverns, how much is built, what grows. A
    /// theme is matched to the dungeon's tile by its numbers (temperature, rainfall, swampiness, plant density) so
    /// that modded biomes get a sensible theme without a patch; a theme may also name biomes outright, and that
    /// wins over any numeric match. Rock, wall colour and the biome's own ground and plants come from the tile,
    /// not from the theme.
    /// </summary>
    public class DungeonThemeDef : Def
    {
        // ---- Matching
        public List<BiomeDef> biomes;
        public FloatRange temperature = new FloatRange(-999f, 999f);
        public FloatRange rainfall = new FloatRange(-1f, 99999f);
        public FloatRange swampiness = new FloatRange(-1f, 99f);
        public FloatRange plantDensity = new FloatRange(-1f, 99f);
        /// <summary>Among themes that match, the highest priority wins; ties are broken by commonality.</summary>
        public int priority;
        public float commonality = 1f;
        /// <summary>When above zero, the theme is only ever chosen by this roll, made once per tile before matching.</summary>
        public float rareChance;

        // ---- Layout
        /// <summary>Chance a chamber is a built room. Scaled by the tile's hilliness.</summary>
        public float builtChance = 0.12f;
        /// <summary>Chance a link between two natural chambers narrows to a squeeze plugged by a divider.</summary>
        public float pinchChance = 0.75f;
        /// <summary>Built rooms favour halls and plain rooms filled with sarcophagi.</summary>
        public bool cryptRooms;
        /// <summary>Chance each book in a library is one that can cause a mental break (an Anomaly tome). Zero keeps
        /// them out of this theme's libraries.</summary>
        public float tomeChance;

        // ---- Look
        public List<FloorPatch> floors = new List<FloorPatch>();
        public List<DividerOption> dividers = new List<DividerOption>();
        /// <summary>Patches of this terrain drift into built rooms (sand in buried ruins).</summary>
        public TerrainDef builtFloorDrift;
        /// <summary>Built floors use fine stone tile rather than flagstone.</summary>
        public bool tiledBuiltFloor;
        /// <summary>Share of natural floor given to the biome's own ground patches.</summary>
        public float biomeGroundShare = 0.15f;
        /// <summary>Chance per eligible cell (inside a glow patch) of a cave plant.</summary>
        public float caveFungus = 0.05f;
        /// <summary>Chance per natural floor cell of a rock chunk.</summary>
        public float chunks = 0.012f;
        /// <summary>Chance a large natural chamber has a collapsed ceiling, letting in daylight and the biome's plants.</summary>
        public float skylightChance = 0.15f;
        /// <summary>Chance a built room (other than the entrance) is a furnished room with powered walls rather than a ruin.</summary>
        public float furnishedChance = 0.17f;

        // ---- Creatures
        /// <summary>Chance a pack in a natural chamber or ruin is insects rather than the biome's animals.</summary>
        public float insectChance;
        /// <summary>Chance a pack in a ruin is mechanoids.</summary>
        public float mechChance = 0.2f;

        public bool Matches(Tile tile)
        {
            if (tile == null) return false;
            if (biomes != null && biomes.Count > 0) return biomes.Contains(tile.PrimaryBiome);
            return temperature.Includes(tile.temperature)
                && rainfall.Includes(tile.rainfall)
                && swampiness.Includes(tile.swampiness)
                && plantDensity.Includes(tile.PrimaryBiome?.plantDensity ?? 0f);
        }

        public ThingDef RandomDivider(BiomeDef biome, out ThingDef filth)
        {
            filth = null;
            var option = dividers.Where(d => d.thing != null || d.biomePlant).RandomElementByWeightWithFallback(d => d.weight);
            if (option == null) return ThingDefOf.CollapsedRocks;
            filth = option.filth;
            if (!option.biomePlant) return option.thing;
            var plants = DungeonThemeUtility.CoverPlantsOf(biome);
            return plants.Count > 0 ? plants.RandomElementByWeight(p => biome.CommonalityOfPlant(p)) : ThingDefOf.CollapsedRocks;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var e in base.ConfigErrors()) yield return e;
            foreach (var f in floors)
                if (f.terrain == null && f.biomeTerrain == BiomeTerrain.None) yield return "floor patch with neither terrain nor biomeTerrain";
            foreach (var d in dividers)
                if (d.thing != null && d.biomePlant) yield return $"divider names {d.thing.defName} and biomePlant; the thing is ignored";
        }
    }

    public static class DungeonThemeUtility
    {
        /// <summary>
        /// The theme for a dungeon tile. Seeded by the world and the tile, so a dungeon keeps its theme for every
        /// party even though each party gets a fresh layout.
        /// </summary>
        public static DungeonThemeDef ThemeFor(PlanetTile tile)
        {
            var all = DefDatabase<DungeonThemeDef>.AllDefsListForReading;
            Tile info = tile.Valid ? Find.WorldGrid[tile] : null;
            Rand.PushState(Gen.HashCombineInt(Gen.HashCombineInt(Find.World.info.Seed, tile.tileId ^ 0x5D1E), tile.Layer?.LayerID ?? 0));
            try
            {
                foreach (var t in all.Where(t => t.rareChance > 0f).OrderBy(t => t.defName))
                    if (Rand.Chance(t.rareChance) && t.Matches(info)) return t;

                var named = all.Where(t => t.rareChance <= 0f && !t.biomes.NullOrEmpty() && t.Matches(info)).ToList();
                var pool = named.Count > 0 ? named : all.Where(t => t.rareChance <= 0f && t.Matches(info)).ToList();
                if (pool.Count == 0) return DungeonDefOf.DMAD_Cave;
                int top = pool.Max(t => t.priority);
                return pool.Where(t => t.priority == top).OrderBy(t => t.defName).RandomElementByWeight(t => t.commonality);
            }
            finally { Rand.PopState(); }
        }

        /// <summary>The tile's rock types, primary first. The primary is fixed per tile so a dungeon keeps its stone.</summary>
        public static List<ThingDef> RocksFor(PlanetTile tile)
        {
            var rocks = Find.World.NaturalRockTypesIn(tile).Where(r => r?.building?.naturalTerrain != null).ToList();
            if (rocks.Count == 0)
            {
                var granite = DefDatabase<ThingDef>.GetNamedSilentFail("Granite");
                if (granite != null) rocks.Add(granite);
            }
            if (rocks.Count > 1)
            {
                Rand.PushState(Gen.HashCombineInt(Find.World.info.Seed, tile.tileId ^ 0x0C4E));
                try
                {
                    var primary = rocks.RandomElement();
                    rocks.Remove(primary);
                    rocks.Insert(0, primary);
                }
                finally { Rand.PopState(); }
            }
            return rocks;
        }

        public static float HillinessBuiltFactor(Hilliness h)
        {
            switch (h)
            {
                case Hilliness.Flat: return 1.3f;
                case Hilliness.SmallHills: return 1f;
                case Hilliness.LargeHills: return 0.8f;
                case Hilliness.Mountainous: return 0.6f;
                default: return 0.5f;
            }
        }

        public static TerrainDef BiomeTerrainFor(BiomeDef biome, BiomeTerrain which)
        {
            if (biome == null) return null;
            switch (which)
            {
                case BiomeTerrain.Gravel: return biome.noGravel ? null : biome.gravelTerrain ?? TerrainDefOf.Gravel;
                case BiomeTerrain.Mud: return biome.mudTerrain ?? DefDatabase<TerrainDef>.GetNamedSilentFail("Mud");
                case BiomeTerrain.ShallowWater: return biome.waterShallowTerrain ?? TerrainDefOf.WaterShallow;
                case BiomeTerrain.Soil: return FertileGroundOf(biome);
                default: return null;
            }
        }

        public static TerrainDef FertileGroundOf(BiomeDef biome)
        {
            TerrainDef best = null;
            if (biome?.terrainsByFertility != null)
                foreach (var t in biome.terrainsByFertility)
                    if (t.terrain != null && !t.terrain.IsWater && t.terrain.fertility > 0.05f && (best == null || t.terrain.fertility > best.fertility))
                        best = t.terrain;
            return best;
        }

        /// <summary>The biome's own natural ground (terrain patch makers and fertility bands), minus water and
        /// anything built. This is what gives a modded biome's caves its own floor.</summary>
        public static List<TerrainDef> BiomeGroundOf(BiomeDef biome)
        {
            var list = new List<TerrainDef>();
            if (biome == null) return list;
            void Add(TerrainDef t)
            {
                if (t == null || t.IsWater || !t.natural || list.Contains(t)) return;
                if (t == TerrainDefOf.Gravel || t.passability == Traversability.Impassable) return;
                list.Add(t);
            }
            if (biome.terrainsByFertility != null) foreach (var t in biome.terrainsByFertility) Add(t.terrain);
            if (biome.terrainPatchMakers != null)
                foreach (var pm in biome.terrainPatchMakers)
                    if (pm?.thresholds != null) foreach (var t in pm.thresholds) Add(t.terrain);
            return list;
        }

        static readonly Dictionary<BiomeDef, List<ThingDef>> coverPlants = new Dictionary<BiomeDef, List<ThingDef>>();

        /// <summary>
        /// The biome's wild trees, bushes and cacti that could plug a squeeze. Only plain vanilla-class plants with
        /// no comps, so anima, gauranlen and the like (and modded plants with their own behaviour) stay out; and
        /// nothing that dies where it is put: cave plants, which light kills, plants tied to a terrain tag the
        /// squeeze's floor won't have, and plants that need pollution.
        /// </summary>
        public static List<ThingDef> CoverPlantsOf(BiomeDef biome)
        {
            if (biome == null) return new List<ThingDef>();
            if (!coverPlants.TryGetValue(biome, out var list))
            {
                list = biome.AllWildPlants.Where(p => p.plant != null && p.thingClass == typeof(Plant) && p.comps.NullOrEmpty()
                        && p.fillPercent >= 0.2f && !p.plant.cavePlant && !p.plant.diesToLight && !p.plant.isStump
                        && p.plant.WildTerrainTags.Count == 0 && !p.plant.RequiresPollution && biome.CommonalityOfPlant(p) > 0f)
                    .Distinct().ToList();
                coverPlants[biome] = list;
            }
            return list;
        }
    }
}
