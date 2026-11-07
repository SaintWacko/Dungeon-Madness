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
            // A plug whose plant went some way Patch_Plant_DeSpawn did not see (saves made before
            // Patch_CoreKeepsPlant lost their plug plants on load): an invisible wall nobody can remove, so it clears
            // itself and opens the squeeze
            if (Prefs.DevMode) Log.Warning($"[Dungeon Madness] Plant plug core at {Position} had no plant; removing it.");
            Destroy();
        }
    }

    /// <summary>Loading a save spawns buildings after everything else, with a wipe, and an edifice wipes the plant
    /// under it, so every plug plant was refunded away on load while its core was not yet there for
    /// Patch_Plant_DeSpawn to find. The core is meant to share its cell with the plant.</summary>
    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.SpawningWipes))]
    public static class Patch_CoreKeepsPlant
    {
        public static void Postfix(BuildableDef newEntDef, BuildableDef oldEntDef, ref bool __result)
        {
            if (__result && newEntDef == DungeonDefOf.DMAD_PlantCore && oldEntDef is ThingDef old && old.category == ThingCategory.Plant)
                __result = false;
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
