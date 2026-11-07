using HarmonyLib;
using RimWorld;
using Verse;

namespace DungeonMadness
{
    /// <summary>
    /// The invisible edifice under a plant plug. A plant is never an edifice, and fog and line of sight only stop at
    /// edifices, so this holds the cavern behind fogged and blocks sight and shots like the other plugs. It goes when
    /// its plant goes (Patch_Plant_DeSpawn), and Building.DeSpawn then reveals the cavern behind through vanilla's
    /// own fog-blocker logic.
    ///
    /// It also keeps its plant from rotting in the dark: a vanilla tree or bush counts unlit time and starts dying
    /// after 7.5 days of it, which a long visit could reach. Cold is left alone, so a plug may go leafless.
    /// </summary>
    public class DungeonPlantCore : Building
    {
        static readonly AccessTools.FieldRef<Plant, int> unlitTicks = AccessTools.FieldRefAccess<Plant, int>("unlitTicks");

        public override void TickLong()
        {
            base.TickLong();
            Plant plant = Position.GetPlant(Map);
            if (plant != null)
            {
                unlitTicks(plant) = 0;
                return;
            }
            // A plug whose plant went some way Patch_Plant_DeSpawn did not see (one turned up in testing with no plant
            // over it): an invisible wall nobody can remove, so it clears itself and opens the squeeze
            if (Prefs.DevMode) Log.Warning($"[Dungeon Madness] Plant plug core at {Position} had no plant; removing it.");
            Destroy();
        }
    }

    [HarmonyPatch(typeof(Plant), nameof(Plant.DeSpawn))]
    public static class Patch_Plant_DeSpawn
    {
        // One edifice-grid lookup per despawned plant; plants despawn rarely
        public static void Prefix(Plant __instance)
        {
            if (!__instance.Spawned) return;
            if (__instance.Position.GetEdifice(__instance.Map) is DungeonPlantCore core && !core.Destroyed) core.Destroy();
        }
    }
}
