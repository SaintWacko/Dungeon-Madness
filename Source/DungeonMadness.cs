using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace DungeonMadness
{
    public static class DungeonMadUtility
    {
        public static bool IsMad(Pawn p) => p?.health?.hediffSet != null && p.health.hediffSet.HasHediff(DungeonDefOf.DMAD_DungeonMad);
    }

    /// <summary>
    /// The dungeon has the creature's mind and never lets go. People are held in a berserk rage that outlasts being
    /// downed; animals in a permanent manhunter state, restarted whenever they come round. Either way they leave other
    /// dungeon-mad creatures alone, and they can never be captured or tamed.
    /// </summary>
    public class Hediff_DungeonMad : Hediff
    {
        public override bool ShouldRemove => false;

        public override void PostAdd(DamageInfo? dinfo)
        {
            base.PostAdd(dinfo);
            Ensure();
        }

        public override void TickInterval(int delta)
        {
            base.TickInterval(delta);
            if (pawn.IsHashIntervalTick(60, delta)) Ensure();
        }

        void Ensure()
        {
            if (!pawn.Spawned || pawn.Dead || pawn.Downed) return;
            MentalStateDef want = pawn.RaceProps.Humanlike ? DungeonDefOf.DMAD_DungeonMadness : MentalStateDefOf.ManhunterPermanent;
            if (pawn.MentalStateDef == want) return;
            pawn.mindState.mentalStateHandler.TryStartMentalState(want, null, forced: true, forceWake: true, causedByMood: false, otherPawn: null, transitionSilently: true);
        }
    }

    /// <summary>Berserk, except that other dungeon-mad creatures are not enemies. Never ends (see the def).</summary>
    public class MentalState_DungeonMad : MentalState_Berserk
    {
        public override bool ForceHostileTo(Thing t) => !(t is Pawn p && DungeonMadUtility.IsMad(p));
    }

    /// <summary>The berserk attack job, skipping other dungeon-mad creatures.</summary>
    public class JobGiver_DungeonMad : JobGiver_Berserk
    {
        protected override bool IsGoodTarget(Thing thing) => base.IsGoodTarget(thing) && !(thing is Pawn p && DungeonMadUtility.IsMad(p));
    }

    // ---- What the madness rules out

    [HarmonyPatch(typeof(GenAI), nameof(GenAI.CanBeCaptured))]
    static class Patch_CanBeCaptured
    {
        static void Postfix(Pawn pawn, ref bool __result)
        {
            if (__result && DungeonMadUtility.IsMad(pawn)) __result = false;
        }
    }

    /// <summary>Carrying a downed person out of the dungeon would otherwise take them prisoner.</summary>
    [HarmonyPatch(typeof(CaravanUtility), nameof(CaravanUtility.ShouldAutoCapture))]
    static class Patch_ShouldAutoCapture
    {
        static void Postfix(Pawn p, ref bool __result)
        {
            if (__result && DungeonMadUtility.IsMad(p)) __result = false;
        }
    }

    [HarmonyPatch(typeof(TameUtility), nameof(TameUtility.CanTame))]
    static class Patch_CanTame
    {
        static void Postfix(Pawn pawn, ref bool __result)
        {
            if (__result && DungeonMadUtility.IsMad(pawn)) __result = false;
        }
    }

    /// <summary>A mad animal leaves other mad creatures alone, as a mad person does.</summary>
    [HarmonyPatch(typeof(MentalState_Manhunter), nameof(MentalState_Manhunter.ForceHostileTo), new[] { typeof(Thing) })]
    static class Patch_ManhunterForceHostileTo
    {
        static void Postfix(MentalState_Manhunter __instance, Thing t, ref bool __result)
        {
            if (__result && t is Pawn other && DungeonMadUtility.IsMad(other) && DungeonMadUtility.IsMad(__instance.pawn)) __result = false;
        }
    }
}
