using RimWorld;
using Verse;

namespace DungeonMadness
{
    /// <summary>
    /// A plant plug (the root tangle). It is a Plant so the vanilla cut and chop-wood orders clear it, but a plant is
    /// never an edifice, and fog and line of sight only stop at edifices. So the generator spawns an invisible
    /// DMAD_TangleCore edifice in the same cell to hold the fog and block sight like the other plugs; when the
    /// tangle goes (cut, chopped, burned) it takes its core with it, and the core's Building.DeSpawn reveals the
    /// cavern behind through vanilla's own fog-blocker logic.
    /// </summary>
    public class Plant_RootTangle : Plant
    {
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Map map = Map;
            IntVec3 pos = Position;
            base.DeSpawn(mode);
            if (map == null) return;
            Thing core = pos.GetFirstThing(map, DungeonDefOf.DMAD_TangleCore);
            if (core != null && !core.Destroyed) core.Destroy();
        }
    }
}
