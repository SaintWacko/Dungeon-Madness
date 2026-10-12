using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace DungeonMadness
{
    /// <summary>
    /// Vanilla Dialog_EnterPortal with a dungeon's item list: every item on the map the party can reach, not just
    /// those in a home area. Pawns chosen here fetch the chosen items, carry them to the exit and leave.
    /// </summary>
    public class Dialog_LeaveDungeon : Window
    {
        private enum Tab { Pawns, Items }
        private static readonly Vector2 ButtonSize = new Vector2(160f, 40f);
        private static readonly List<TabRecord> tabs = new List<TabRecord>();

        private readonly DungeonExit exit;
        private List<TransferableOneWay> transferables;
        private TransferableOneWayWidget pawnsTransfer, itemsTransfer;
        private Tab tab;

        public override Vector2 InitialSize => new Vector2(1024f, UI.screenHeight);
        protected override float Margin => 0f;

        public Dialog_LeaveDungeon(DungeonExit exit)
        {
            this.exit = exit;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override void PostOpen()
        {
            base.PostOpen();
            Recache();
        }

        public override void DoWindowContents(Rect inRect)
        {
            using (new TextBlock(GameFont.Medium, TextAnchor.MiddleCenter))
                Widgets.Label(new Rect(0f, 0f, inRect.width, 35f), exit.EnterString);
            tabs.Clear();
            tabs.Add(new TabRecord("PawnsTab".Translate(), () => tab = Tab.Pawns, tab == Tab.Pawns));
            tabs.Add(new TabRecord("ItemsTab".Translate(), () => tab = Tab.Items, tab == Tab.Items));
            inRect.yMin += 67f;
            Widgets.DrawMenuSection(inRect);
            TabDrawer.DrawTabs(inRect, tabs);
            inRect = inRect.ContractedBy(17f);
            Widgets.BeginGroup(inRect);
            Rect local = inRect.AtZero();
            DoButtons(local);
            Rect body = local;
            body.yMax -= 76f;
            switch (tab)
            {
                case Tab.Pawns: pawnsTransfer.OnGUI(body, out _); break;
                case Tab.Items: itemsTransfer.OnGUI(body, out _); break;
            }
            Widgets.EndGroup();
        }

        private void DoButtons(Rect rect)
        {
            float y = rect.height - 55f - 17f;
            if (Widgets.ButtonText(new Rect(rect.width / 2f - ButtonSize.x / 2f, y, ButtonSize.x, ButtonSize.y), "ResetButton".Translate()))
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                Recache();
            }
            if (Widgets.ButtonText(new Rect(0f, y, ButtonSize.x, ButtonSize.y), "CancelButton".Translate())) Close();
            if (Widgets.ButtonText(new Rect(rect.width - ButtonSize.x, y, ButtonSize.x, ButtonSize.y), "AcceptButton".Translate()) && TryAccept())
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                Close(doCloseSound: false);
            }
        }

        public override void OnAcceptKeyPressed()
        {
            if (TryAccept())
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                Close(doCloseSound: false);
            }
        }

        private bool TryAccept()
        {
            var pawns = TransferableUtility.GetPawnsFromTransferables(transferables);
            // Dwellers who agreed to come, and the party's healthy animals, walk out on their own; nobody hauls them.
            // A downed follower or animal is not among them: it is carried out like any downed pawn
            var followers = pawns.Where(p => p.Spawned && !p.Downed && !p.InMentalState
                && (DungeonDwellerUtility.IsFollower(p) || (p.RaceProps.Animal && p.Faction == Faction.OfPlayer))).ToList();
            pawns.RemoveAll(followers.Contains);
            if (pawns.Count == 0)
            {
                Messages.Message("DMAD_Leave_NoPawns".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            exit.leftToLoad = new List<TransferableOneWay>();
            foreach (var t in transferables)
                if (!(t.AnyThing is Pawn p && followers.Contains(p))) exit.AddToTheToLoadList(t, t.CountToTransfer);
            EnterPortalUtility.MakeLordsAsAppropriate(pawns, exit);
            foreach (var f in followers) DungeonDwellerUtility.SendThroughExit(f, exit);
            return true;
        }

        private void Recache()
        {
            transferables = new List<TransferableOneWay>();
            if (exit.LoadInProgress) transferables.AddRange(exit.leftToLoad);
            Map map = exit.Map;
            // Prisoners count even outside a cell: a dungeon has no prison, so an arrested dweller stands where they were
            foreach (var p in CaravanFormingUtility.AllSendablePawns(map, allowEvenIfDowned: true, allowEvenIfInMentalState: false,
                         allowEvenIfPrisonerNotSecure: true, allowCapturableDownedPawns: true, allowLodgers: true))
                Add(p);
            var comp = DungeonDwellerUtility.Comp(map);
            if (comp != null)
                foreach (var p in comp.followers)
                    if (p.Spawned && !p.Downed && !p.InMentalState && p.Map == map) Add(p);
            // Everything the party could carry out: home area and reservations do not apply in a dungeon
            foreach (var t in CaravanFormingUtility.AllReachableColonyItems(map, allowEvenIfOutsideHomeArea: true, allowEvenIfReserved: true))
                Add(t);
            foreach (var t in EnterPortalUtility.ThingsBeingHauledTo(exit)) Add(t);

            pawnsTransfer = new TransferableOneWayWidget(null, null, null, "DMAD_LeaveThingCountTip".Translate(), drawMass: true,
                IgnorePawnsInventoryMode.IgnoreIfAssignedToUnload, includePawnsMassInMassUsage: true, () => float.MaxValue, 0f,
                ignoreSpawnedCorpseGearAndInventoryMass: false, map.Tile, drawMarketValue: false, drawEquippedWeapon: true);
            CaravanUIUtility.AddPawnsSections(pawnsTransfer, transferables);
            pawnsTransfer.AddSection("DMAD_Leave_DwellersSection".Translate(),
                transferables.Where(x => x.AnyThing is Pawn p && DungeonDwellerUtility.IsFollower(p) && !p.Downed && !p.InMentalState));
            itemsTransfer = new TransferableOneWayWidget(transferables.Where(x => x.ThingDef.category != ThingCategory.Pawn), null, null,
                "DMAD_LeaveThingCountTip".Translate(), drawMass: true, IgnorePawnsInventoryMode.IgnoreIfAssignedToUnload,
                includePawnsMassInMassUsage: true, () => float.MaxValue, 0f, ignoreSpawnedCorpseGearAndInventoryMass: false, map.Tile);
        }

        private void Add(Thing t)
        {
            if (exit.LoadInProgress && exit.leftToLoad.Any(tr => tr.things.Contains(t))) return;
            var tr2 = TransferableUtility.TransferableMatching(t, transferables, TransferAsOneMode.PodsOrCaravanPacking);
            if (tr2 == null)
            {
                tr2 = new TransferableOneWay();
                transferables.Add(tr2);
            }
            if (!tr2.things.Contains(t)) tr2.things.Add(t);
        }
    }
}
