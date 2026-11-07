using BastleFramework;
using Verse;

namespace DungeonMadness
{
    /// <summary>The all-seeing trait sits in Bastle's ordinary trait pool, but only LordJob_DungeonGuard reads it.
    /// Bastle rolls every def with a commonality on every bastle it makes, so without this it would also roll on
    /// world bastles and tame-able animals, costing them points for nothing. Dungeon Madness raises the flag around
    /// its own roll for a waiting pack's leader; the rest of the pack gets the trait through CopyBastle, which does
    /// not ask AllowedOn.</summary>
    public class AllSeeingTraitDef : BastleTraitDef
    {
        internal static bool rollingGuardPack;

        public override bool AllowedOn(Pawn pawn) => rollingGuardPack && base.AllowedOn(pawn);
    }
}
