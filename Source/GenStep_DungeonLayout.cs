using RimWorld;
using Verse;

namespace ThisUsedToBeAboutDungeons
{
    /// <summary>
    /// Builds a dungeon map from a freshly generated chamber layout: solid unbreakable wall everywhere except the
    /// chambers and the passages joining them, a divider in every passage, and the exit stairway in the first
    /// chamber's north wall. The layout is kept on the site for phase 4.
    /// </summary>
    public class GenStep_DungeonLayout : GenStep
    {
        public override int SeedPart => 0x7D0A6E7;

        public override void Generate(Map map, GenStepParams parms)
        {
            var site = map.Parent as DungeonSite;

            // MapGenerator seeds from the world and the tile, which would give every party the same dungeon. The
            // site carries a seed rolled when the party entered.
            DungeonLayoutResult layout;
            Rand.PushState(site != null && site.layoutSeed != 0 ? site.layoutSeed : Rand.Int);
            try { layout = DungeonLayoutGenerator.Generate(map.Size.x, 0.35f); }
            finally { Rand.PopState(); }
            if (site != null) site.layout = layout.data;

            TerrainDef built = DefDatabase<TerrainDef>.GetNamed("PavedTile", false) ?? TerrainDefOf.Concrete;
            TerrainDef rough = DefDatabase<TerrainDef>.GetNamed("Gravel", false) ?? TerrainDefOf.Concrete;
            foreach (var c in map.AllCells)
            {
                TerrainDef t = TerrainDefOf.Concrete;
                int id = layout.chamberAt[layout.Index(c)];
                if (id >= 0) t = layout.data.chambers[id].cave ? rough : built;
                else if (layout.corridor[layout.Index(c)]) t = built;
                map.terrainGrid.SetTerrain(c, t);
                map.roofGrid.SetRoof(c, RoofDefOf.RoofRockThick); // no infestations: the site def has no incident target tags
            }

            // The exit is a stairway set into the first chamber's north wall: 3 cells of wall become the exit, and
            // pawns use it from the room side
            IntVec3 exitCell = layout.data.exitCell;
            var exitRect = GenAdj.OccupiedRect(exitCell, Rot4.North, DungeonDefOf.TUBAD_DungeonExit.size);

            foreach (var c in map.AllCells)
                if (!layout.IsOpen(c) && !exitRect.Contains(c)) GenSpawn.Spawn(DungeonDefOf.TUBAD_DungeonWall, c, map);

            foreach (var d in layout.data.dividers)
            {
                Thing divider = d.type == DividerType.Door
                    ? ThingMaker.MakeThing(ThingDefOf.Door, ThingDefOf.WoodLog)
                    : ThingMaker.MakeThing(DungeonDefOf.TUBAD_DungeonRubble);
                GenSpawn.Spawn(divider, d.cell, map);
            }

            GenSpawn.Spawn(DungeonDefOf.TUBAD_DungeonExit, exitCell, map);
            MapGenerator.PlayerStartSpot = DungeonExit.InsideCellFor(exitCell);
            Log.Message($"[Dungeons] Level {site?.level} layout: {layout.data.chambers.Count} chambers, {layout.data.dividers.Count} dividers on {map.Size.x}x{map.Size.z}.");
        }
    }
}
