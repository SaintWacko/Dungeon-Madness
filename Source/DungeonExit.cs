using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace DungeonMadness
{
    /// <summary>
    /// The way out, at the dungeon's entrance. Built on vanilla's MapPortal so the leave dialog, the haul-to-portal
    /// jobs and the load-and-enter lord all work unchanged. There is no map on the other side: the portal "leads" to
    /// its own cell, and whatever comes through is handed to the caravan waiting on the world tile.
    /// </summary>
    [StaticConstructorOnStartup]
    public class DungeonExit : MapPortal
    {
        private static readonly Texture2D LeaveTex = ContentFinder<Texture2D>.Get("UI/Commands/ExitCave");

        // Pawns that came through this tick. JobDriver_EnterPortal keeps touching the pawn (drafting, dropping what it
        // carries, telling its lord) right after OnEntered, so they are moved out on the next tick instead.
        private List<Pawn> arrived = new List<Pawn>();

        public DungeonSite Site => Map?.Parent as DungeonSite;

        public override string EnterString => "DMAD_LeaveDungeon".Translate();
        public override string CancelEnterString => "DMAD_CancelLeave".Translate();
        public override string EnteringString => "DMAD_Leaving".Translate();
        protected override Texture2D EnterTex => LeaveTex;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            containerProxy = new DungeonExitProxy { portal = this };
        }

        /// <summary>The floor cell in front of the stairway. The exit itself is part of the wall and cannot be stood on.</summary>
        public static IntVec3 InsideCellFor(IntVec3 exitPos) => exitPos + IntVec3.South;
        public IntVec3 InsideCell => InsideCellFor(Position);

        public override Map GetOtherMap() => Map;
        public override IntVec3 GetDestinationLocation() => InsideCell;

        public override void OnEntered(Pawn pawn)
        {
            Notify_ThingAdded(pawn);
            if (!arrived.Contains(pawn)) arrived.Add(pawn);
        }

        protected override void Tick()
        {
            base.Tick();
            MoveArrivedOut();
        }

        public void MoveArrivedOut()
        {
            if (arrived.Count == 0) return;
            var site = Site;
            for (int i = arrived.Count - 1; i >= 0; i--)
            {
                Pawn p = arrived[i];
                if (p.DestroyedOrNull() || !p.Spawned || p.Map != Map) { arrived.RemoveAt(i); continue; }
                if (site != null && site.TryMovePawnOut(p))
                {
                    arrived.RemoveAt(i);
                    // A dweller who went their own way is not the party leaving
                    if (p.Faction == Faction.OfPlayer) site.Notify_PawnLeft();
                }
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            // Building's gizmos, then our own leave command in place of MapPortal's enter one, which would open
            // Dialog_EnterPortal; that dialog only lists items inside the home area, and a dungeon has none.
            foreach (var g in base.GetGizmos())
            {
                if (g is Command_Action ca && ca.defaultLabel == EnterString + "...") continue;
                yield return g;
            }
            yield return new Command_Action
            {
                defaultLabel = EnterString + "...",
                defaultDesc = "DMAD_LeaveDungeon_Desc".Translate(),
                icon = LeaveTex,
                action = () => Find.WindowStack.Add(new Dialog_LeaveDungeon(this)),
            };
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref arrived, "arrived", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (arrived == null) arrived = new List<Pawn>();
                arrived.RemoveAll(p => p == null);
            }
        }
    }

    /// <summary>Items hauled into the exit go to the site's stash, then to the caravan outside.</summary>
    public class DungeonExitProxy : PortalContainerProxy
    {
        public override bool TryAdd(Thing item, bool canMergeWithExistingStacks = true)
        {
            portal.Notify_ThingAdded(item);
            var site = (portal as DungeonExit)?.Site;
            if (site == null) return false;
            // An entad hauled to the exit counts as carried out by whoever hauled it; a pawn carried out takes
            // what they have on them
            if (item is Pawn p) site.ResolveHeldBy(p);
            else site.ResolveEntad(item, (item.ParentHolder as Pawn_CarryTracker)?.pawn);
            site.AddToStash(item);
            site.FlushStash(site.WaitingCaravan());
            return true;
        }
    }
}
