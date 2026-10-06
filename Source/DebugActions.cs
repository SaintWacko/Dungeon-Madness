using System.Linq;
using System.Text;
using LudeonTK;
using Verse;

namespace ThisUsedToBeAboutDungeons
{
    public static class DungeonDebugActions
    {
        // Pins as Actions\Regenerate dungeons
        [DebugAction("Dungeons", "Regenerate dungeons", allowedGameStates = AllowedGameStates.Playing)]
        private static void RegenerateDungeons()
        {
            var comp = DungeonWorldComponent.Instance;
            if (comp == null) return;
            comp.Generate();
            Messages.Message($"Placed {comp.Count} dungeons.", RimWorld.MessageTypeDefOf.NeutralEvent, false);
        }

        // Pins as Actions\Log dungeon levels
        [DebugAction("Dungeons", "Log dungeon levels", allowedGameStates = AllowedGameStates.Playing)]
        private static void LogLevels()
        {
            var comp = DungeonWorldComponent.Instance;
            if (comp == null) return;
            var counts = Enumerable.Range(0, comp.Count).GroupBy(comp.GetLevel).OrderBy(g => g.Key);
            var sb = new StringBuilder($"[Dungeons] {comp.Count} dungeons by level:");
            foreach (var g in counts) sb.Append($"\n  level {g.Key}: {g.Count()}");
            Log.Message(sb.ToString());
        }
    }
}
