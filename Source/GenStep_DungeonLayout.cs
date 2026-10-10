using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.BaseGen;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ThisUsedToBeAboutDungeons
{
    /// <summary>
    /// Builds a dungeon map from a fresh layout, dressed by the dungeon's theme and its tile.
    ///
    /// From the tile: the rock (walls are unbreakable but take the primary rock's colour; rough floors, flagstone
    /// and chunks are that rock), the biome's own gravel, mud and water, patches of the biome's natural ground, the
    /// biome's wild plants under collapsed ceilings, and dried remains of its wild animals. Rivers and coast add
    /// standing water to themes that have none. Hilliness scales how many rooms are built.
    /// From the theme: which floor patches, which plugs fill the squeezes, how much glowing fungus, how many rooms
    /// are built and how they are furnished.
    /// </summary>
    public class GenStep_DungeonLayout : GenStep
    {
        public override int SeedPart => 0x7D0A6E7;

        struct Patch
        {
            public TerrainDef terrain;
            public float share;
            public FloorMode mode;
            public ValueNoise noise;

            public bool Covers(int x, int z)
            {
                float v = noise.At(x, z);
                switch (mode)
                {
                    case FloorMode.Low: return v < 0.25f + share * 0.5f;
                    case FloorMode.High: return v > 0.75f - share * 0.5f;
                    default: return Mathf.Abs(v - 0.5f) < share * 0.25f;
                }
            }
        }

        public override void Generate(Map map, GenStepParams parms)
        {
            var site = map.Parent as DungeonSite;
            PlanetTile tile = map.Tile;
            Tile info = map.TileInfo;
            DungeonThemeDef theme = site?.theme ?? DungeonThemeUtility.ThemeFor(tile);
            if (site != null) site.theme = theme;
            List<ThingDef> rocks = DungeonThemeUtility.RocksFor(tile);
            float builtChance = Mathf.Clamp01(theme.builtChance * DungeonThemeUtility.HillinessBuiltFactor(info.hilliness));

            // MapGenerator seeds from the world and the tile, which would give every party the same dungeon. The
            // site carries a seed rolled when the party entered; dressing uses it too, so plants and remains vary.
            Rand.PushState(site != null && site.layoutSeed != 0 ? site.layoutSeed : Rand.Int);
            try
            {
                var layout = DungeonLayoutGenerator.Generate(map.Size.x, theme, builtChance);
                if (site != null) site.layout = layout.data;
                Dress(map, layout, theme, rocks, info);
                if (Prefs.DevMode)
                    Log.Message($"[Dungeons] Tile {tile.tileId} layer {tile.Layer?.LayerID}, level {site?.level}, {theme.defName} in {rocks[0].label} ({map.Biome.defName}): " +
                        $"{layout.data.chambers.Count} chambers ({layout.data.chambers.Count(c => !c.Cave)} built, {layout.data.chambers.Count(c => c.skylight)} open to the sky), " +
                        $"{layout.data.dividers.Count} dividers on {map.Size.x}x{map.Size.z}.");
            }
            finally { Rand.PopState(); }
        }

        static void Dress(Map map, DungeonLayoutResult layout, DungeonThemeDef theme, List<ThingDef> rocks, Tile info)
        {
            int n = layout.size;
            float s = n / 100f;
            BiomeDef biome = map.Biome;
            ThingDef rock = rocks[0];
            ThingDef blocks = BlocksOf(rock) ?? ThingDefOf.BlocksGranite;
            TerrainDef rough = rock.building.naturalTerrain ?? TerrainDefOf.Gravel;
            TerrainDef builtFloor = BaseGenUtility.CorrespondingTerrainDef(blocks, theme.tiledBuiltFloor) ?? rough.smoothedTerrain ?? rough;
            var data = layout.data;
            bool[] open = layout.open;

            // ---- Floor patches: the theme's, then water for a river or coast tile, then the biome's own ground
            var patches = new List<Patch>();
            foreach (var f in theme.floors)
            {
                TerrainDef t = f.Resolve(biome);
                if (t != null) patches.Add(new Patch { terrain = t, share = f.share, mode = f.mode, noise = new ValueNoise(Rand.Range(5f, 10f) * s + 2f) });
            }
            bool wetTile = ((info as SurfaceTile)?.Rivers?.Count ?? 0) > 0 || Find.World.CoastDirectionAt(map.Tile).IsValid;
            if (wetTile && !patches.Any(p => p.terrain.IsWater))
            {
                TerrainDef water = DungeonThemeUtility.BiomeTerrainFor(biome, BiomeTerrain.ShallowWater);
                if (water != null) patches.Insert(0, new Patch { terrain = water, share = 0.12f, mode = FloorMode.Low, noise = new ValueNoise(Rand.Range(5f, 10f) * s + 2f) });
            }
            if (theme.biomeGroundShare > 0f)
            {
                var ground = DungeonThemeUtility.BiomeGroundOf(biome).Where(t => t != rough && !patches.Any(p => p.terrain == t)).InRandomOrder().Take(2).ToList();
                foreach (var t in ground)
                    patches.Add(new Patch { terrain = t, share = theme.biomeGroundShare / ground.Count, mode = FloorMode.Mid, noise = new ValueNoise(Rand.Range(6f, 12f) * s + 2f) });
            }
            var drift = new ValueNoise(5f);

            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int k = z * n + x;
                    var c = new IntVec3(x, 0, z);
                    TerrainDef t = rough;
                    if (open[k])
                    {
                        if (layout.built[k] >= 0 || layout.feature[k] == CellFeature.Door)
                            t = theme.builtFloorDrift != null && layout.built[k] >= 0 && drift.At(x, z) > 0.6f ? theme.builtFloorDrift : builtFloor;
                        else if (layout.feature[k] == CellFeature.None)
                            foreach (var p in patches)
                                if (p.Covers(x, z)) { t = p.terrain; break; }
                    }
                    map.terrainGrid.SetTerrain(c, t);
                }

            // ---- Structure. The map generator has already put thick rock roof everywhere (isUnderground).
            IntVec3 exitCell = data.exitCell;
            var exitRect = GenAdj.OccupiedRect(exitCell, Rot4.North, DungeonDefOf.TUBAD_DungeonExit.size);
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    var c = new IntVec3(x, 0, z);
                    if (open[z * n + x] || exitRect.Contains(c)) continue;
                    GenSpawn.Spawn(ThingMaker.MakeThing(DungeonDefOf.TUBAD_DungeonWall, blocks), c, map);
                }
            foreach (var d in data.dividers)
                foreach (var c in d.cells)
                {
                    Thing thing = d.door ? ThingMaker.MakeThing(ThingDefOf.Door, blocks) : ThingMaker.MakeThing(d.def ?? DungeonDefOf.TUBAD_DungeonRubble);
                    // A plant plug (root tangle) spawns at its starting growth, which would draw it tiny and
                    // halve its wood; it never grows underground, so set it mature here
                    if (thing is Plant plant) plant.Growth = 1f;
                    GenSpawn.Spawn(thing, c, map);
                }
            GenSpawn.Spawn(DungeonDefOf.TUBAD_DungeonExit, exitCell, map);
            MapGenerator.PlayerStartSpot = DungeonExit.InsideCellFor(exitCell);

            Skylights(map, layout, theme, biome);
            Furnish(map, layout, theme, blocks, rocks);
            CaveLife(map, layout, theme, biome, rocks);
            Remains(map, layout, theme, biome);
        }

        static ThingDef BlocksOf(ThingDef rock)
        {
            var chunk = rock?.building?.mineableThing;
            var blocks = chunk?.butcherProducts?.FirstOrDefault()?.thingDef;
            return blocks != null && blocks.IsStuff && blocks.stuffProps.CanMake(DungeonDefOf.TUBAD_DungeonWall) ? blocks : null;
        }

        static bool Natural(DungeonLayoutResult layout, int k) => layout.open[k] && layout.built[k] < 0 && layout.feature[k] == CellFeature.None;

        /// <summary>Part of a large cavern's ceiling has fallen in: daylight and weather reach the floor, and the
        /// biome's own wild plants grow on the biome's own soil there. Cave plants are kept out, since light kills them.</summary>
        static void Skylights(Map map, DungeonLayoutResult layout, DungeonThemeDef theme, BiomeDef biome)
        {
            int n = layout.size;
            TerrainDef soil = DungeonThemeUtility.FertileGroundOf(biome);
            var plants = biome.AllWildPlants.Where(p => p.plant != null && !p.plant.cavePlant && biome.CommonalityOfPlant(p) > 0f).ToList();
            var sky = new ValueNoise(4f);
            foreach (var ch in layout.data.chambers)
            {
                if (!ch.Cave || ch.id == 0 || ch.radius < 7f || !Rand.Chance(theme.skylightChance)) continue;
                var cells = new List<IntVec3>();
                foreach (var c in ch.Rect)
                {
                    int k = layout.Index(c);
                    if (layout.owner[k] != ch.id || !Natural(layout, k)) continue;
                    if (c.DistanceTo(ch.center) > ch.radius * 0.8f || sky.At(c.x, c.z) < 0.35f) continue;
                    cells.Add(c);
                }
                if (cells.Count < 8) continue;
                ch.skylight = true;
                foreach (var c in cells)
                {
                    map.roofGrid.SetRoof(c, null);
                    TerrainDef here = c.GetTerrain(map);
                    if (soil != null && !here.IsWater) { map.terrainGrid.SetTerrain(c, soil); here = soil; }
                    if (plants.Count == 0 || here.IsWater || c.GetFirstThing<Thing>(map) != null) continue;
                    if (!Rand.Chance(Mathf.Clamp(biome.plantDensity * 0.45f, 0.05f, 0.6f))) continue;
                    var def = plants.Where(p => p.plant.fertilityMin <= here.fertility && (!p.plant.IsTree || NoWallAround(c, map)))
                        .RandomElementByWeightWithFallback(p => biome.CommonalityOfPlant(p));
                    if (def == null) continue;
                    var plant = (Plant)GenSpawn.Spawn(def, c, map);
                    plant.Growth = Rand.Range(0.2f, 1f);
                }
            }
        }

        static bool NoWallAround(IntVec3 c, Map map)
        {
            foreach (var a in GenAdj.CellsAdjacent8Way(new TargetInfo(c, map)))
                if (!a.InBounds(map) || a.GetEdifice(map) != null) return false;
            return true;
        }

        /// <summary>Halls get rows of columns; crypt rooms get sarcophagi; other built rooms get a little debris.</summary>
        static void Furnish(Map map, DungeonLayoutResult layout, DungeonThemeDef theme, ThingDef blocks, List<ThingDef> rocks)
        {
            ThingDef column = DefDatabase<ThingDef>.GetNamedSilentFail("Column");
            ThingDef sarcophagus = DefDatabase<ThingDef>.GetNamedSilentFail("Sarcophagus");
            foreach (var ch in layout.data.chambers)
            {
                if (ch.Cave) continue;
                foreach (var c in ch.Rect)
                {
                    int k = layout.Index(c);
                    if (layout.built[k] != ch.id || c.GetFirstThing<Thing>(map) != null || !Inner(layout, c, ch.id)) continue;
                    int dx = c.x - ch.center.x, dz = c.z - ch.center.z;
                    if (ch.shape == RoomShape.Hall && column != null && dx % 3 == 0 && dz % 4 == 0 && dx != 0)
                        GenSpawn.Spawn(ThingMaker.MakeThing(column, blocks), c, map);
                    else if (theme.cryptRooms && ch.shape == RoomShape.Rect && sarcophagus != null && dx % 3 == 0 && dz % 3 == 0)
                    {
                        var rect = GenAdj.OccupiedRect(c, Rot4.North, sarcophagus.size);
                        if (rect.All(r => r.InBounds(map) && layout.built[layout.Index(r)] == ch.id && r.GetFirstThing<Thing>(map) == null))
                            GenSpawn.Spawn(ThingMaker.MakeThing(sarcophagus, blocks), c, map, Rot4.North);
                    }
                    else if (!theme.cryptRooms && Rand.Chance(0.04f)) Debris(map, c, rocks);
                }
            }
        }

        static bool Inner(DungeonLayoutResult layout, IntVec3 c, int id)
        {
            for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = c.x + dx, z = c.z + dz;
                    if (x < 0 || z < 0 || x >= layout.size || z >= layout.size || layout.built[z * layout.size + x] != id) return false;
                }
            return true;
        }

        static void Debris(Map map, IntVec3 c, List<ThingDef> rocks)
        {
            if (Rand.Chance(0.5f)) FilthMaker.TryMakeFilth(c, map, ThingDefOf.Filth_RubbleRock);
            else
            {
                var chunk = (Rand.Chance(0.8f) ? rocks[0] : rocks.RandomElement()).building.mineableThing;
                if (chunk != null) GenSpawn.Spawn(chunk, c, map);
            }
        }

        /// <summary>Glowing cave plants in patches, away from the exit's light; rock chunks and rubble.</summary>
        static void CaveLife(Map map, DungeonLayoutResult layout, DungeonThemeDef theme, BiomeDef biome, List<ThingDef> rocks)
        {
            var cavePlants = DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.plant != null && d.plant.cavePlant).ToList();
            TerrainDef bed = DungeonThemeUtility.BiomeTerrainFor(biome, BiomeTerrain.Gravel) ?? TerrainDefOf.Gravel;
            var glow = new ValueNoise(4f);
            IntVec3 exit = layout.data.exitCell;
            foreach (var c in map.AllCells)
            {
                int k = layout.Index(c);
                if (!Natural(layout, k) || c.GetFirstThing<Thing>(map) != null) continue;
                TerrainDef t = c.GetTerrain(map);
                if (t.IsWater) continue;
                if (cavePlants.Count > 0 && c.Roofed(map) && glow.At(c.x, c.z) > 0.62f && Rand.Chance(theme.caveFungus) && c.DistanceToSquared(exit) > 14 * 14)
                {
                    var def = cavePlants.RandomElementByWeight(p => p.plant.cavePlantWeight);
                    // Bare rock is too poor for anything to grow; a fungus bed of gravel goes under it
                    if (t.fertility < def.plant.fertilityMin && bed.fertility >= def.plant.fertilityMin) map.terrainGrid.SetTerrain(c, bed);
                    if (c.GetTerrain(map).fertility >= def.plant.fertilityMin)
                    {
                        var plant = (Plant)GenSpawn.Spawn(def, c, map);
                        plant.Growth = Rand.Range(0.3f, 1f);
                        continue;
                    }
                }
                if (Rand.Chance(theme.chunks)) Debris(map, c, rocks);
            }
        }

        /// <summary>Dried-out remains of the biome's own wild animals, which wandered in and never left.</summary>
        static void Remains(Map map, DungeonLayoutResult layout, DungeonThemeDef theme, BiomeDef biome)
        {
            if (theme.remainsPerThousand <= 0f) return;
            var cells = map.AllCells.Where(c => Natural(layout, layout.Index(c)) && !c.GetTerrain(map).IsWater && c.GetFirstThing<Thing>(map) == null).ToList();
            int count = Mathf.Min(6, GenMath.RoundRandom(cells.Count / 1000f * theme.remainsPerThousand));
            if (count <= 0) return;
            var kinds = biome.AllWildAnimals.Where(k => k.RaceProps.Animal && k.race.race.baseBodySize <= 2.5f && biome.CommonalityOfAnimal(k) > 0f).ToList();
            if (kinds.Count == 0) return;
            for (int i = 0; i < count; i++)
            {
                var kind = kinds.RandomElementByWeight(k => biome.CommonalityOfAnimal(k));
                if (!cells.TryRandomElement(out var cell)) break;
                cells.Remove(cell);
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, null, PawnGenerationContext.NonPlayer));
                HealthUtility.SimulateKilled(pawn, DamageDefOf.Blunt);
                Corpse corpse = pawn.Corpse;
                if (corpse == null) continue;
                var rot = corpse.TryGetComp<CompRottable>();
                if (rot != null) rot.RotProgress = 1e8f;
                GenSpawn.Spawn(corpse, cell, map);
                corpse.SetForbidden(true, warnOnFail: false);
            }
        }
    }
}
