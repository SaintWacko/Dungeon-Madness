using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace DungeonMadness
{
    public static class DungeonMadUtility
    {
        /// <summary>Mad and awake. A pack still waiting to wake behaves, and can be handled, like a sane one.</summary>
        public static bool IsMad(Pawn p) =>
            p?.health?.hediffSet?.GetFirstHediffOfDef(DungeonDefOf.DMAD_DungeonMad) is Hediff hediff && !(hediff is Hediff_DungeonMad h && h.dormant);

        /// <summary>Mad, awake or still waiting: what woken mad creatures leave alone.</summary>
        public static bool IsDungeonMad(Pawn p) => p?.health?.hediffSet != null && p.health.hediffSet.HasHediff(DungeonDefOf.DMAD_DungeonMad);
    }

    /// <summary>
    /// The dungeon has the creature's mind and never lets go. People are held in a berserk rage that outlasts being
    /// downed; animals in a permanent manhunter state, restarted whenever they come round. Either way they leave other
    /// dungeon-mad creatures alone, and they can never be captured or tamed.
    /// </summary>
    public class Hediff_DungeonMad : Hediff
    {
        // A pack's madness waits, hidden, until the pack wakes (LordJob_DungeonGuard), so a mad pack that has not
        // noticed the party looks and handles like any calm one: it can be tamed, invited or arrested, and the
        // attempt itself wakes it (Patch_WakeOnInteraction, Patch_WakeOnForceWait, Patch_WakeOnArrest).
        public bool dormant;

        public override bool ShouldRemove => false;

        public override bool Visible => !dormant && base.Visible;

        public void Awaken()
        {
            if (!dormant) return;
            dormant = false;
            Ensure();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref dormant, "dormant");
        }

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
            if (dormant || !pawn.Spawned || pawn.Dead || pawn.Downed) return;
            MentalStateDef want = pawn.RaceProps.Humanlike ? DungeonDefOf.DMAD_DungeonMadness : MentalStateDefOf.ManhunterPermanent;
            if (pawn.MentalStateDef == want) return;
            pawn.mindState.mentalStateHandler.TryStartMentalState(want, null, forced: true, forceWake: true, causedByMood: false, otherPawn: null, transitionSilently: true);
        }
    }

    /// <summary>Berserk, except that other dungeon-mad creatures are not enemies. Never ends (see the def).</summary>
    public class MentalState_DungeonMad : MentalState_Berserk
    {
        public override bool ForceHostileTo(Thing t) => !(t is Pawn p && DungeonMadUtility.IsDungeonMad(p));
    }

    /// <summary>The berserk attack job, skipping other dungeon-mad creatures.</summary>
    public class JobGiver_DungeonMad : JobGiver_Berserk
    {
        protected override bool IsGoodTarget(Thing thing) => base.IsGoodTarget(thing) && !(thing is Pawn p && DungeonMadUtility.IsDungeonMad(p));
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
            if (__result && t is Pawn other && DungeonMadUtility.IsDungeonMad(other) && DungeonMadUtility.IsMad(__instance.pawn)) __result = false;
        }
    }

    // ---- Handling a waiting pack wakes it, so a pawn the pack cannot see (invisible, or in the dark) still gets
    // ---- no further than starting the work

    /// <summary>Taming and animal chat, and any social interaction, go through here. A prefix, so the pack is awake
    /// before a tame attempt can roll; an interaction aimed at a pawn whose madness just started is dropped.</summary>
    [HarmonyPatch(typeof(Pawn_InteractionsTracker), nameof(Pawn_InteractionsTracker.TryInteractWith))]
    static class Patch_WakeOnInteraction
    {
        static bool Prefix(Pawn ___pawn, Pawn recipient, ref bool __result)
        {
            if (___pawn.Faction != Faction.OfPlayer || recipient == null || recipient.Faction == Faction.OfPlayer || !recipient.Spawned) return true;
            if (!LordJob_DungeonGuard.WakePackOf(recipient) || !DungeonMadUtility.IsMad(recipient)) return true;
            __result = false;
            return false;
        }
    }

    /// <summary>The dweller invite, and vanilla work like feeding a taming target, hold the target still with
    /// ForceWait when the work begins.</summary>
    [HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.ForceWait))]
    static class Patch_WakeOnForceWait
    {
        static void Postfix(Pawn pawn, Thing faceTarget)
        {
            if (faceTarget is Pawn actor && actor.Faction == Faction.OfPlayer && pawn.Faction != Faction.OfPlayer && pawn.Spawned)
                LordJob_DungeonGuard.WakePackOf(pawn);
        }
    }

    /// <summary>Vanilla's arrest and the dungeon's arrest in place both roll here. The pack wakes first, so a mad
    /// person turns on the arrester instead of going quietly.</summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.CheckAcceptArrest))]
    static class Patch_WakeOnArrest
    {
        static void Prefix(Pawn __instance, Pawn arrester)
        {
            if (arrester?.Faction == Faction.OfPlayer && __instance.Spawned) LordJob_DungeonGuard.WakePackOf(__instance);
        }

        static void Postfix(Pawn __instance, ref bool __result)
        {
            if (__result && DungeonMadUtility.IsMad(__instance) && !__instance.Downed) __result = false;
        }
    }
}
