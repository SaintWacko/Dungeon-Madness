using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ThisUsedToBeAboutDungeons
{
    /// <summary>
    /// Sane dungeon dwellers and the party. A colonist can invite one to leave with them: if they agree, they go and
    /// wait by the entrance, and they come out only when the leave dialog marks them, with the party. Outside, each
    /// one either joins the colony or goes their own way. One who refuses stays where they are and cannot be asked
    /// again, but can still be arrested, in place since a dungeon has no prison beds, and carried out as a prisoner.
    /// </summary>
    public static class DungeonDwellerUtility
    {
        public static MapComponent_DungeonCreatures Comp(Map map) => map?.GetComponent<MapComponent_DungeonCreatures>();

        /// <summary>A sane person made by the dungeon, still free and on their own.</summary>
        public static bool IsDweller(Pawn p) =>
            p != null && p.RaceProps.Humanlike && p.kindDef.HasModExtension<DungeonDweller>() && p.Faction == null
            && !p.IsPrisoner && !DungeonMadUtility.IsMad(p);

        public static bool IsFollower(Pawn p) => p?.MapHeld != null && (Comp(p.MapHeld)?.followers.Contains(p) ?? false);

        public static bool CanInvite(Pawn dweller)
        {
            if (!IsDweller(dweller) || !dweller.Spawned || dweller.Downed || dweller.InMentalState) return false;
            var comp = Comp(dweller.Map);
            return comp != null && !comp.followers.Contains(dweller) && !comp.refused.Contains(dweller);
        }

        public static float InviteChance(Pawn inviter) =>
            Mathf.Clamp01(DungeonsMod.Settings.inviteChance * inviter.GetStatValue(StatDefOf.SocialImpact));

        public static void ResolveInvite(Pawn dweller, Pawn inviter)
        {
            var comp = Comp(dweller.Map);
            if (comp == null || !CanInvite(dweller)) return;
            if (Rand.Chance(InviteChance(inviter)))
            {
                comp.followers.Add(dweller);
                SendToWait(dweller);
                Messages.Message("TUBAD_Invite_Accepted".Translate(dweller.Named("PAWN")), dweller, MessageTypeDefOf.PositiveEvent);
            }
            else
            {
                comp.refused.Add(dweller);
                Messages.Message("TUBAD_Invite_Refused".Translate(dweller.Named("PAWN")), dweller, MessageTypeDefOf.NeutralEvent);
            }
        }

        /// <summary>Moves a follower into the lord that keeps followers loitering by the entrance.</summary>
        static void SendToWait(Pawn dweller)
        {
            Map map = dweller.Map;
            var exit = map.listerThings.ThingsOfDef(DungeonDefOf.TUBAD_DungeonExit).FirstOrDefault() as DungeonExit;
            if (exit == null) return;
            Lord lord = map.lordManager.lords.Find(l => l.LordJob is LordJob_DwellersWait);
            if (lord == null)
            {
                IntVec3 spot = exit.InsideCell + IntVec3.South * 3;
                if (!spot.InBounds(map) || !spot.Standable(map)) spot = CellFinder.StandableCellNear(exit.InsideCell, map, 6f);
                if (!spot.IsValid) spot = exit.InsideCell;
                lord = LordMaker.MakeNewLord(null, new LordJob_DwellersWait(spot), map);
            }
            dweller.GetLord()?.Notify_PawnLost(dweller, PawnLostCondition.ForcedToJoinOtherLord);
            lord.AddPawn(dweller);
            dweller.jobs?.EndCurrentJob(JobCondition.InterruptForced);
        }

        /// <summary>Followers marked in the leave dialog walk to the exit themselves; nobody carries them.</summary>
        public static void SendThroughExit(Pawn dweller, DungeonExit exit)
        {
            if (!dweller.Spawned || dweller.Downed) return;
            dweller.GetLord()?.Notify_PawnLost(dweller, PawnLostCondition.LeftVoluntarily);
            dweller.jobs.StartJob(JobMaker.MakeJob(JobDefOf.EnterPortal, exit), JobCondition.InterruptForced);
        }

        /// <summary>A follower stepping out of the dungeon: joins the colony, or thanks the party and leaves. Returns
        /// true when the pawn has been dealt with.</summary>
        public static bool TryResolveFollowerOut(Pawn dweller, DungeonSite site)
        {
            var comp = Comp(dweller.Map);
            if (comp == null || !comp.followers.Contains(dweller)) return false;
            comp.followers.Remove(dweller);
            // Now a colonist, so the usual way out (caravan join, or a caravan of their own)
            if (RollJoin(dweller, site)) return false;
            dweller.ExitMap(allowedToJoinOrCreateCaravan: false, Rot4.Invalid);
            if (!dweller.IsWorldPawn()) Find.WorldPawns.PassToWorld(dweller);
            return true;
        }

        /// <summary>A follower who was downed and carried out arrives in the site's stash rather than walking through
        /// the exit, so the same join roll happens there. Returns true when the pawn went their own way and must not
        /// be added to the caravan (which would otherwise capture a factionless person as a prisoner).</summary>
        public static bool TryResolveCarriedFollower(Pawn dweller, DungeonSite site)
        {
            var comp = Comp(site.Map);
            if (comp == null || !comp.followers.Contains(dweller)) return false;
            comp.followers.Remove(dweller);
            if (RollJoin(dweller, site)) return false;
            if (!dweller.Dead && !dweller.IsWorldPawn()) Find.WorldPawns.PassToWorld(dweller);
            return true;
        }

        /// <summary>The join roll: joins with a letter and returns true, or posts the parting message.</summary>
        static bool RollJoin(Pawn dweller, DungeonSite site)
        {
            if (Rand.Chance(DungeonsMod.Settings.joinChance))
            {
                dweller.SetFaction(Faction.OfPlayer);
                Find.LetterStack.ReceiveLetter("TUBAD_Dweller_Joined_Label".Translate(dweller.Named("PAWN")),
                    "TUBAD_Dweller_Joined_Text".Translate(dweller.Named("PAWN")), LetterDefOf.PositiveEvent, new LookTargets(site));
                return true;
            }
            Messages.Message("TUBAD_Dweller_Left".Translate(dweller.Named("PAWN")), MessageTypeDefOf.NeutralEvent);
            return false;
        }
    }

    /// <summary>Followers loiter near the entrance and defend themselves there. A marker subclass, so the lord is
    /// found again for the next follower.</summary>
    public class LordJob_DwellersWait : LordJob_DefendPoint
    {
        public LordJob_DwellersWait()
        {
        }

        public LordJob_DwellersWait(IntVec3 spot) : base(spot, wanderRadius: 4f, defendRadius: 8f, isCaravanSendable: false, addFleeToil: false)
        {
        }
    }

    // ---- Inviting

    public class FloatMenuOptionProvider_InviteDweller : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;

        protected override bool AppliesInt(FloatMenuContext context) => context.map?.Parent is DungeonSite;

        protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn pawn = context.FirstSelectedPawn;
            if (!DungeonDwellerUtility.IsDweller(clickedPawn) || !pawn.IsColonistPlayerControlled) return null;
            var comp = DungeonDwellerUtility.Comp(clickedPawn.Map);
            if (comp == null || comp.followers.Contains(clickedPawn)) return null;
            string label = "TUBAD_Invite".Translate(clickedPawn.LabelShort, clickedPawn);
            if (comp.refused.Contains(clickedPawn))
                return new FloatMenuOption(label + ": " + "TUBAD_Invite_AlreadyRefused".Translate(), null);
            if (!DungeonDwellerUtility.CanInvite(clickedPawn)) return null;
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking))
                return new FloatMenuOption(label + ": " + "TUBAD_Invite_CannotTalk".Translate(pawn.LabelShort, pawn), null);
            if (!pawn.CanReach(clickedPawn, PathEndMode.Touch, Danger.Deadly))
                return new FloatMenuOption(label + ": " + "NoPath".Translate().CapitalizeFirst(), null);
            label += " (" + DungeonDwellerUtility.InviteChance(pawn).ToStringPercent() + ")";
            return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(label, () =>
            {
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DungeonDwellerDefOf.TUBAD_InviteDweller, clickedPawn), JobTag.Misc);
            }, MenuOptionPriority.Default, null, clickedPawn), pawn, clickedPawn);
        }
    }

    public class JobDriver_InviteDweller : JobDriver
    {
        const int TalkTicks = 150;
        Pawn Dweller => (Pawn)job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !DungeonDwellerUtility.CanInvite(Dweller));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil talk = Toils_General.Wait(TalkTicks).FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch)
                .WithProgressBarToilDelay(TargetIndex.A);
            talk.initAction = () => PawnUtility.ForceWait(Dweller, TalkTicks + 30, pawn);
            talk.tickIntervalAction = delta => pawn.rotationTracker.FaceTarget(Dweller);
            talk.handlingFacing = true;
            yield return talk;
            Toil ask = ToilMaker.MakeToil("TUBAD_Ask");
            ask.initAction = () => DungeonDwellerUtility.ResolveInvite(Dweller, pawn);
            ask.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return ask;
        }
    }

    // ---- Arresting where they stand

    /// <summary>Vanilla's arrest needs a prison bed to take the prisoner to, and a dungeon has none. When no bed can
    /// be found this offers an arrest on the spot: the dweller becomes a prisoner where they stand, and the leave
    /// dialog lists them to be taken out.</summary>
    public class FloatMenuOptionProvider_ArrestInDungeon : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => false;
        protected override bool Multiselect => false;
        protected override bool RequiresManipulation => true;

        protected override bool AppliesInt(FloatMenuContext context) => context.map?.Parent is DungeonSite;

        protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn pawn = context.FirstSelectedPawn;
            if (!DungeonDwellerUtility.IsDweller(clickedPawn) || !clickedPawn.CanBeArrestedBy(pawn)) return null;
            if (RestUtility.FindBedFor(clickedPawn, pawn, checkSocialProperness: false, ignoreOtherReservations: true, GuestStatus.Prisoner) != null) return null;
            string label = "TUBAD_ArrestHere".Translate(clickedPawn.LabelCap, clickedPawn, clickedPawn.GetAcceptArrestChance(pawn).ToStringPercent());
            if (!pawn.CanReach(clickedPawn, PathEndMode.Touch, Danger.Deadly))
                return new FloatMenuOption(label + ": " + "NoPath".Translate().CapitalizeFirst(), null);
            return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(label, () =>
            {
                pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DungeonDwellerDefOf.TUBAD_ArrestInPlace, clickedPawn), JobTag.Misc);
            }, MenuOptionPriority.High, null, clickedPawn), pawn, clickedPawn);
        }
    }

    public class JobDriver_ArrestInPlace : JobDriver
    {
        Pawn Takee => (Pawn)job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(Takee, job, errorOnFailed: errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !DungeonDwellerUtility.IsDweller(Takee));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil arrest = ToilMaker.MakeToil("TUBAD_Arrest");
            arrest.initAction = () =>
            {
                if (!Takee.CheckAcceptArrest(pawn))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                var comp = DungeonDwellerUtility.Comp(Takee.Map);
                comp?.followers.Remove(Takee);
                comp?.refused.Remove(Takee);
                Takee.GetLord()?.Notify_PawnLost(Takee, PawnLostCondition.MadePrisoner);
                Takee.guest.CapturedBy(Faction.OfPlayer, pawn);
                Takee.jobs?.EndCurrentJob(JobCondition.InterruptForced);
                Messages.Message("TUBAD_Arrested".Translate(Takee.Named("PAWN")), Takee, MessageTypeDefOf.NeutralEvent);
            };
            arrest.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return arrest;
        }
    }

    [DefOf]
    public static class DungeonDwellerDefOf
    {
        public static JobDef TUBAD_InviteDweller;
        public static JobDef TUBAD_ArrestInPlace;
        static DungeonDwellerDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(DungeonDwellerDefOf));
    }
}
