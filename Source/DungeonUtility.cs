using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace DungeonMadness
{
    public static class DungeonUtility
    {
        public const int MaxPartyHumanlikes = 5;

        // Relative to the world's own range, so a world of levels 50-80 still runs the whole gradient
        public static float LevelFraction(int level, int minLevel, int maxLevel) =>
            maxLevel <= minLevel ? 0f : Mathf.Clamp01((level - (float)minLevel) / (maxLevel - minLevel));

        public static Color LevelColor(int level, int minLevel, int maxLevel) =>
            WorldDrawLayer_Dungeons.ColorAt(LevelFraction(level, minLevel, maxLevel));

        /// <summary>"Temperate forest, large hills, river" for a surface tile.</summary>
        public static string DescribeTile(PlanetTile tile)
        {
            var t = Find.WorldGrid[tile];
            var parts = new List<string> { t.PrimaryBiome.LabelCap };
            if (!t.WaterCovered && t.hilliness != Hilliness.Undefined && t.hilliness != Hilliness.Flat)
                parts.Add(t.hilliness.GetLabel());
            if (t is SurfaceTile st && st.Rivers != null && st.Rivers.Count > 0) parts.Add("DMAD_River".Translate());
            if (!t.WaterCovered && t.IsCoastal) parts.Add("DMAD_Coastal".Translate());
            if (t.Landmark != null) parts.Add(t.Landmark.name);
            return string.Join(", ", parts);
        }

        public static void JumpTo(PlanetTile tile)
        {
            if (!WorldRendererUtility.WorldSelected) CameraJumper.TryShowWorld();
            Find.WorldSelector.ClearSelection();
            Find.WorldSelector.SelectedTile = tile;
            Find.WorldCameraDriver.JumpTo(tile);
        }
    }
}
