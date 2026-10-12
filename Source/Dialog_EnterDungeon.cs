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
    /// Pick who and what goes into the dungeon from a caravan on its tile. Laid out like vanilla's split caravan
    /// dialog (pawns, items and travel supplies tabs), with the dungeon's level and the party's effect on it on top.
    /// </summary>
    public class Dialog_EnterDungeon : Window
    {
        private enum Tab { Pawns, Items, Supplies }

        private const float TitleHeight = 35f;
        private const float RowLabelWidth = 130f;
        private const float RowHeight = 44f; // room for the everyone warning on two lines
        private const float RowGap = 8f;
        private const float NoteHeight = 40f; // a long already-entered list wraps to two lines
        private const float HeaderHeight = RowHeight * 2f + RowGap + 4f + NoteHeight;
        private static readonly Vector2 ButtonSize = new Vector2(160f, 40f);

        private readonly Caravan caravan;
        private readonly int level;
        private List<TransferableOneWay> transferables;
        private TransferableOneWayWidget pawnsTransfer, itemsTransfer, suppliesTransfer;
        private Tab tab;
        private static readonly List<TabRecord> tabs = new List<TabRecord>();

        // Recomputed when the selection changes
        private List<Pawn> party = new List<Pawn>();
        private bool everyone;
        private float wealth;
        // Vanilla caravan stats: the caravan left outside and the party going in, as in the split caravan dialog
        private CaravanUIUtility.CaravanInfo outsideInfo, partyInfo;

        public override Vector2 InitialSize => new Vector2(1024f, UI.screenHeight);
        protected override float Margin => 0f;

        public Dialog_EnterDungeon(Caravan caravan, int level)
        {
            this.caravan = caravan;
            this.level = level;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override void PostOpen()
        {
            base.PostOpen();
            Recache();
        }

        /// <summary>Pawn rows ticked and locked by the dialog because nobody able would stay outside with them.
        /// Read by the row tooltip patch; empty while no enter dialog is open.</summary>
        public static readonly HashSet<TransferableOneWay> Locked = new HashSet<TransferableOneWay>();

        /// <summary>Pawn rows locked unticked because that pawn has already entered this dungeon. Read by the row
        /// indicator patch; empty while no enter dialog is open.</summary>
        public static readonly HashSet<TransferableOneWay> EnteredRows = new HashSet<TransferableOneWay>();

        // Pawns who have been in before but would be swept in anyway, because nobody able stays outside with them
        private List<Pawn> stuckEntered = new List<Pawn>();

        public override void PostClose()
        {
            base.PostClose();
            Locked.Clear();
            EnteredRows.Clear();
        }

        private void Recache()
        {
            Locked.Clear();
            EnteredRows.Clear();
            transferables = new List<TransferableOneWay>();
            foreach (var p in caravan.PawnsListForReading) Add(p);
            foreach (var t in CaravanInventoryUtility.AllInventoryItems(caravan)) Add(t);
            CaravanUIUtility.CreateCaravanTransferableWidgets(transferables, out pawnsTransfer, out itemsTransfer, out suppliesTransfer,
                "DMAD_EnterThingCountTip".Translate(), IgnorePawnsInventoryMode.Ignore, () => partyInfo.massCapacity - partyInfo.massUsage,
                ignoreSpawnedCorpsesGearAndInventoryMass: false, caravan.Tile);
            // Anyone who has been in this dungeon before cannot go again: shown unticked and locked, with a marker
            foreach (var tr in transferables)
                if (tr.AnyThing is Pawn p && DungeonEntry.AlreadyEntered(caravan.Tile, Gen.YieldSingle(p)).Any())
                {
                    tr.AdjustTo(0);
                    tr.interactive = false;
                    EnteredRows.Add(tr);
                }
            SelectionChanged();
        }

        private void Add(Thing t)
        {
            var tr = TransferableUtility.TransferableMatching(t, transferables, TransferAsOneMode.Normal);
            if (tr == null)
            {
                tr = new TransferableOneWay();
                transferables.Add(tr);
            }
            if (!tr.things.Contains(t)) tr.things.Add(t);
        }

        private void SelectionChanged()
        {
            // Undo the previous auto-ticks, so the check below sees only what the player chose
            foreach (var tr in Locked)
            {
                tr.interactive = true;
                tr.AdjustTo(0);
            }
            Locked.Clear();

            party = TransferableUtility.GetPawnsFromTransferables(transferables);
            everyone = party.Count > 0 && !DungeonEntry.AnyoneStaysOutside(caravan, party);
            if (everyone)
            {
                // Nobody left outside could lead a caravan, so they all go in: show them ticked and locked
                foreach (var tr in transferables)
                    if (tr.AnyThing is Pawn && !EnteredRows.Contains(tr) && tr.CountToTransfer < tr.GetMaximumToTransfer())
                    {
                        tr.AdjustTo(tr.GetMaximumToTransfer());
                        tr.interactive = false;
                        Locked.Add(tr);
                    }
                party = TransferableUtility.GetPawnsFromTransferables(transferables);
            }
            stuckEntered = everyone
                ? EnteredRows.SelectMany(tr => tr.things).OfType<Pawn>().ToList()
                : new List<Pawn>();
            var going = party;
            wealth = DungeonEntry.PartyWealth(going);
            RecacheStats();
        }

        private void RecacheStats()
        {
            PlanetTile tile = caravan.Tile;
            var sb = new System.Text.StringBuilder();
            var biome = caravan.Biome;

            float outMass = CollectionsMassCalculator.MassUsageLeftAfterTransfer(transferables, IgnorePawnsInventoryMode.Ignore);
            sb.Length = 0;
            float outCap = CollectionsMassCalculator.CapacityLeftAfterTransfer(transferables, sb);
            string outCapTip = sb.ToString();
            sb.Length = 0;
            float outSpeed = TilesPerDayCalculator.ApproxTilesPerDayLeftAfterTransfer(transferables, outMass, outCap, tile, PlanetTile.Invalid, caravan.Shuttle != null, sb);
            string outSpeedTip = sb.ToString();
            float outFood = DaysWorthOfFoodCalculator.ApproxDaysWorthOfFoodLeftAfterTransfer(transferables, tile, IgnorePawnsInventoryMode.Ignore, caravan.Faction);
            float outRot = DaysUntilRotCalculator.ApproxDaysUntilRotLeftAfterTransfer(transferables, tile, IgnorePawnsInventoryMode.Ignore);
            sb.Length = 0;
            var outForage = ForagedFoodPerDayCalculator.ForagedFoodPerDayLeftAfterTransfer(transferables, biome, Faction.OfPlayer, sb);
            string outForageTip = sb.ToString();
            sb.Length = 0;
            float outVis = CaravanVisibilityCalculator.VisibilityLeftAfterTransfer(transferables, sb);
            outsideInfo = new CaravanUIUtility.CaravanInfo(outMass, outCap, outCapTip, outSpeed, outSpeedTip, (outFood, outRot), outForage, outForageTip, outVis, sb.ToString());

            float inMass = CollectionsMassCalculator.MassUsageTransferables(transferables, IgnorePawnsInventoryMode.Ignore);
            sb.Length = 0;
            float inCap = CollectionsMassCalculator.CapacityTransferables(transferables, sb);
            string inCapTip = sb.ToString();
            sb.Length = 0;
            float inSpeed = TilesPerDayCalculator.ApproxTilesPerDay(transferables, inMass, inCap, tile, PlanetTile.Invalid, caravan.Shuttle != null, sb);
            string inSpeedTip = sb.ToString();
            float inFood = DaysWorthOfFoodCalculator.ApproxDaysWorthOfFood(transferables, tile, IgnorePawnsInventoryMode.Ignore, caravan.Faction);
            float inRot = DaysUntilRotCalculator.ApproxDaysUntilRot(transferables, tile, IgnorePawnsInventoryMode.Ignore);
            sb.Length = 0;
            var inForage = ForagedFoodPerDayCalculator.ForagedFoodPerDay(transferables, biome, Faction.OfPlayer, sb);
            string inForageTip = sb.ToString();
            sb.Length = 0;
            float inVis = CaravanVisibilityCalculator.Visibility(transferables, sb);
            partyInfo = new CaravanUIUtility.CaravanInfo(inMass, inCap, inCapTip, inSpeed, inSpeedTip, (inFood, inRot), inForage, inForageTip, inVis, sb.ToString());

        }

        public override void DoWindowContents(Rect inRect)
        {
            using (new TextBlock(GameFont.Medium, TextAnchor.MiddleCenter))
                Widgets.Label(new Rect(0f, 0f, inRect.width, TitleHeight), "DMAD_EnterDungeon_Title".Translate(level));
            DoHeader(new Rect(17f, TitleHeight, inRect.width - 34f, HeaderHeight));

            tabs.Clear();
            tabs.Add(new TabRecord("PawnsTab".Translate(), () => tab = Tab.Pawns, tab == Tab.Pawns));
            tabs.Add(new TabRecord("ItemsTab".Translate(), () => tab = Tab.Items, tab == Tab.Items));
            tabs.Add(new TabRecord("TravelSupplies".Translate(), () => tab = Tab.Supplies, tab == Tab.Supplies));
            inRect.yMin += TitleHeight + HeaderHeight + TabDrawer.TabHeight + 4f;
            Widgets.DrawMenuSection(inRect);
            TabDrawer.DrawTabs(inRect, tabs);
            inRect = inRect.ContractedBy(17f);
            Widgets.BeginGroup(inRect);
            Rect local = inRect.AtZero();
            DoButtons(local);
            Rect body = local;
            body.yMax -= 59f;
            bool changed = false;
            switch (tab)
            {
                case Tab.Pawns: pawnsTransfer.OnGUI(body, out changed); break;
                case Tab.Items: itemsTransfer.OnGUI(body, out changed); break;
                case Tab.Supplies: suppliesTransfer.OnGUI(body, out changed); break;
            }
            if (changed) SelectionChanged();
            Widgets.EndGroup();
        }

        private static readonly List<TransferableUIUtility.ExtraInfo> partyExtra = new List<TransferableUIUtility.ExtraInfo>();

        /// <summary>
        /// Two labelled rows on one five-column grid. "Staying outside": vanilla's full caravan strip, since that
        /// caravan still has to travel. "Entering": only mass and days of food from vanilla (speed, forage and
        /// visibility mean nothing underground), then people, wealth and effective level in the remaining columns.
        /// A note line below for anything that blocks entry or needs a warning.
        /// </summary>
        private void DoHeader(Rect rect)
        {
            float col = Mathf.Floor((rect.width - RowLabelWidth) / 5f);
            float x0 = rect.x + RowLabelWidth;
            var outsideRow = new Rect(x0, rect.y, col * 5f, RowHeight);
            var partyRow = new Rect(x0, rect.y + RowHeight + RowGap, col * 5f, RowHeight);

            using (new TextBlock(GameFont.Small, TextAnchor.MiddleLeft))
            {
                GUI.color = ColoredText.SubtleGrayColor;
                Widgets.Label(new Rect(rect.x, outsideRow.y, RowLabelWidth, RowHeight), "DMAD_Enter_RowOutside".Translate());
                Widgets.Label(new Rect(rect.x, partyRow.y, RowLabelWidth, RowHeight), "DMAD_Enter_RowParty".Translate());
                GUI.color = Color.white;
                Widgets.DrawLineHorizontal(rect.x, partyRow.y - RowGap / 2f, rect.width, ColoredText.SubtleGrayColor.ToTransparent(0.3f));
            }

            if (everyone)
            {
                using (new TextBlock(GameFont.Small, TextAnchor.MiddleCenter))
                {
                    GUI.color = ColoredText.WarningColor;
                    Widgets.Label(outsideRow, "DMAD_Enter_Everyone".Translate());
                    GUI.color = Color.white;
                }
            }
            else CaravanUIUtility.DrawCaravanInfo(outsideInfo, null, caravan.Tile, null, -9999f, outsideRow);

            CaravanUIUtility.DrawCaravanInfo(partyInfo, null, caravan.Tile, null, -9999f,
                new Rect(partyRow.x, partyRow.y, col * 2f, RowHeight), isCaravan: false);

            int cap = DungeonsMod.Settings.maxPartySize;
            int people = DungeonEntry.HumanlikeCount(party);
            float mult = DungeonEntry.WealthMultiplier(wealth);
            partyExtra.Clear();
            partyExtra.Add(new TransferableUIUtility.ExtraInfo("DMAD_Enter_People".Translate(), people + " / " + cap,
                people > cap ? ColorLibrary.RedReadable : Color.white, "DMAD_Enter_People_Tip".Translate(cap)));
            partyExtra.Add(new TransferableUIUtility.ExtraInfo("DMAD_Enter_Wealth".Translate(), wealth.ToStringMoney(), Color.white,
                "DMAD_Enter_Wealth_Tip".Translate()));
            partyExtra.Add(new TransferableUIUtility.ExtraInfo("DMAD_Enter_Effective".Translate(),
                (level * mult).ToString("0.#") + " (x" + mult.ToString("0.00") + ")", Color.white,
                "DMAD_Enter_Effective_Tip".Translate(level, wealth.ToStringMoney(), mult.ToString("0.00"), DungeonsMod.Settings.wealthToDouble.ToStringMoney())));
            TransferableUIUtility.DrawExtraInfo(partyExtra, new Rect(partyRow.x + col * 2f, partyRow.y, col * 3f, RowHeight));
            GenUI.ResetLabelAlign();
            GUI.color = Color.white;

            // Pawns who have entered before are locked out of the party, so the only way one goes in is being swept
            // along when nobody able stays outside with them
            var entered = stuckEntered;
            if (entered.Any())
            {
                using (new TextBlock(GameFont.Small, TextAnchor.MiddleCenter))
                {
                    GUI.color = ColorLibrary.RedReadable;
                    Widgets.Label(new Rect(rect.x, partyRow.yMax + 4f, rect.width, NoteHeight),
                        "DMAD_Enter_EnteredStuck".Translate(entered.Select(p => p.LabelShortCap).ToCommaList(true)));
                    GUI.color = Color.white;
                }
            }
        }

        private void DoButtons(Rect rect)
        {
            var accept = new Rect(rect.width / 2f - ButtonSize.x / 2f, rect.height - 55f, ButtonSize.x, ButtonSize.y);
            if (Widgets.ButtonText(accept, "DMAD_EnterButton".Translate()) && TryAccept())
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                Close(doCloseSound: false);
            }
            if (Widgets.ButtonText(new Rect(accept.x - 10f - ButtonSize.x, accept.y, ButtonSize.x, ButtonSize.y), "ResetButton".Translate()))
            {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                Recache();
            }
            if (Widgets.ButtonText(new Rect(accept.xMax + 10f, accept.y, ButtonSize.x, ButtonSize.y), "CancelButton".Translate()))
                Close();
        }

        private bool TryAccept()
        {
            SelectionChanged();
            if (!party.Any(p => p.RaceProps.Humanlike && CaravanUtility.IsOwner(p, Faction.OfPlayer) && !p.Downed))
            {
                Messages.Message("DMAD_Enter_NeedColonist".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            var going = party; // already every pawn when everyone goes in
            int cap = DungeonsMod.Settings.maxPartySize;
            if (DungeonEntry.HumanlikeCount(going) > cap)
            {
                Messages.Message((everyone ? "DMAD_Enter_TooManyEveryone" : "DMAD_Enter_TooMany").Translate(cap), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            if (stuckEntered.Any())
            {
                Messages.Message("DMAD_Enter_EnteredStuck".Translate(stuckEntered.Select(p => p.LabelShortCap).ToCommaList(true)), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            // Safety net: the locked rows should already keep these out of the party
            var entered = DungeonEntry.AlreadyEntered(caravan.Tile, going).ToList();
            if (entered.Any())
            {
                Messages.Message("DMAD_Enter_AlreadyEntered".Translate(entered.Select(p => p.LabelShortCap).ToCommaList(true)), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            var report = DungeonEntry.CanEnterAt(caravan.Tile, out _);
            if (!report.Accepted)
            {
                if (!report.Reason.NullOrEmpty()) Messages.Message(report.Reason, MessageTypeDefOf.RejectInput, false);
                return false;
            }
            // Everyone going in counts toward the effective level shown above; a copy, since Enter empties the caravan
            DungeonEntry.Enter(caravan, going.ToList(), transferables.Where(t => !(t.AnyThing is Pawn)).ToList());
            return true;
        }
    }
}
