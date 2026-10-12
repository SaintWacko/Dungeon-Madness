using System.Linq;
using System.Text;
using LudeonTK;
using RimWorld.Planet;
using Verse;

namespace DungeonMadness
{
    public static class DungeonDebugActions
    {
        /// <summary>Temporary test aid: every dungeon entered while on gets more built rooms, and every built room
        /// that can be furnished is.</summary>
        public static bool forceFurnished;

        // Pins as Actions\Force furnished rooms
        [DebugAction("Dungeons", "Force furnished rooms", allowedGameStates = AllowedGameStates.Playing)]
        private static void ToggleForceFurnished()
        {
            forceFurnished = !forceFurnished;
            Messages.Message($"Force furnished rooms: {(forceFurnished ? "on" : "off")}. Applies to the next dungeon entered.", RimWorld.MessageTypeDefOf.NeutralEvent, false);
        }

        // Pins as Actions\Wake all dungeon packs. Spawns every pack on the current dungeon map without waiting for
        // its chamber to be seen, to look them all over at once.
        [DebugAction("Dungeons", "Wake all dungeon packs", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void WakeAllPacks()
        {
            var comp = Find.CurrentMap?.GetComponent<MapComponent_DungeonCreatures>();
            if (comp == null || comp.packs.Count == 0) { Messages.Message("No dungeon packs on this map.", RimWorld.MessageTypeDefOf.RejectInput, false); return; }
            comp.WakeAll();
        }

        // Pins as Actions\Regenerate dungeons
        [DebugAction("Dungeons", "Regenerate dungeons", allowedGameStates = AllowedGameStates.Playing)]
        private static void RegenerateDungeons()
        {
            var comp = DungeonWorldComponent.Instance;
            if (comp == null) return;
            comp.Generate();
            Messages.Message($"Placed {comp.Count} dungeons.", RimWorld.MessageTypeDefOf.NeutralEvent, false);
        }

        // Pins as Actions\Log dungeon themes. Tallies the theme every dungeon on the world would get, plus the
        // rare-roll value of the first few, to check the per-tile theme pick is spread the way the defs intend
        [DebugAction("Dungeons", "Log dungeon themes", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogThemes()
        {
            var comp = DungeonWorldComponent.Instance;
            if (comp == null) return;
            var surface = Find.WorldGrid.Surface;
            var sb = new StringBuilder($"[Dungeon Madness] {comp.Count} dungeons by theme:");
            var picks = Enumerable.Range(0, comp.Count).Select(i => DungeonThemeUtility.ThemeFor(new PlanetTile(comp.GetTile(i), surface))).ToList();
            foreach (var g in picks.GroupBy(t => t.defName).OrderByDescending(g => g.Count())) sb.Append($"\n  {g.Key}: {g.Count()}");
            for (int i = 0; i < System.Math.Min(5, comp.Count); i++)
            {
                var tile = new PlanetTile(comp.GetTile(i), surface);
                var info = Find.WorldGrid[tile];
                sb.Append($"\n  tile {tile.tileId} ({info.PrimaryBiome?.defName}, {info.temperature:F0}C, rain {info.rainfall:F0}, swamp {info.swampiness:F2}): {picks[i].defName}");
            }
            Log.Message(sb.ToString());
        }

        // Pins as Actions\Log dungeon levels
        [DebugAction("Dungeons", "Log dungeon levels", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogLevels()
        {
            var comp = DungeonWorldComponent.Instance;
            if (comp == null) return;
            var counts = Enumerable.Range(0, comp.Count).GroupBy(comp.GetLevel).OrderBy(g => g.Key);
            var sb = new StringBuilder($"[Dungeon Madness] {comp.Count} dungeons by level:");
            foreach (var g in counts) sb.Append($"\n  level {g.Key}: {g.Count()}");
            Log.Message(sb.ToString());
        }
    }
}
