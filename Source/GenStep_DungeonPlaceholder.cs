using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ThisUsedToBeAboutDungeons
{
    /// <summary>
    /// Phase 2 stand-in for real dungeon generation (phase 3): a chain of rectangular rooms joined by short
    /// corridors, cut out of solid dungeon wall, roofed throughout, with the exit in the first room.
    /// </summary>
    public class GenStep_DungeonPlaceholder : GenStep
    {
        public override int SeedPart => 0x7D0A6E6;

        public override void Generate(Map map, GenStepParams parms)
        {
            TerrainDef floor = TerrainDefOf.Concrete;
            foreach (var c in map.AllCells)
            {
                map.terrainGrid.SetTerrain(c, floor);
                map.roofGrid.SetRoof(c, RoofDefOf.RoofRockThick); // no infestations: the site def has no incident target tags
            }

            // Rooms laid along a wandering line from one side of the map to the other
            var rooms = new List<CellRect>();
            int margin = 4;
            int x = margin + 2;
            int z = map.Size.z / 2;
            while (x < map.Size.x - margin - 8)
            {
                int w = Rand.RangeInclusive(8, 14), h = Rand.RangeInclusive(8, 14);
                if (x + w >= map.Size.x - margin) break;
                z = Mathf.Clamp(z + Rand.RangeInclusive(-8, 8), margin + h / 2, map.Size.z - margin - h / 2 - 1);
                rooms.Add(new CellRect(x, z - h / 2, w, h));
                x += w + Rand.RangeInclusive(3, 6);
            }

            var open = new HashSet<IntVec3>();
            foreach (var r in rooms) foreach (var c in r) open.Add(c);
            for (int i = 0; i + 1 < rooms.Count; i++) Corridor(rooms[i].CenterCell, rooms[i + 1].CenterCell, open);

            // The exit is a stairway set into the middle of the first room's north wall: 3 cells of wall become the
            // exit, and pawns use it from the room side
            CellRect first = rooms.Count > 0 ? rooms[0] : CellRect.CenteredOn(map.Center, 4);
            IntVec3 exitCell = new IntVec3(first.CenterCell.x, 0, first.maxZ + 1);
            var exitRect = GenAdj.OccupiedRect(exitCell, Rot4.North, DungeonDefOf.TUBAD_DungeonExit.size);

            foreach (var c in map.AllCells)
                if (!open.Contains(c) && !exitRect.Contains(c)) GenSpawn.Spawn(DungeonDefOf.TUBAD_DungeonWall, c, map);

            GenSpawn.Spawn(DungeonDefOf.TUBAD_DungeonExit, exitCell, map);
            MapGenerator.PlayerStartSpot = DungeonExit.InsideCellFor(exitCell);
        }

        // L-shaped, 3 cells wide
        private static void Corridor(IntVec3 a, IntVec3 b, HashSet<IntVec3> open)
        {
            int step = a.x <= b.x ? 1 : -1;
            for (int x = a.x; x != b.x + step; x += step)
                for (int d = -1; d <= 1; d++) open.Add(new IntVec3(x, 0, a.z + d));
            step = a.z <= b.z ? 1 : -1;
            for (int z = a.z; z != b.z + step; z += step)
                for (int d = -1; d <= 1; d++) open.Add(new IntVec3(b.x + d, 0, z));
        }
    }
}
