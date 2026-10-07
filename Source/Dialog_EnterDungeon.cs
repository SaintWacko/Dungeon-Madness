using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ThisUsedToBeAboutDungeons
{
    /// <summary>
    /// Pick who and what goes into the dungeon from a caravan on its tile. Laid out like vanilla's split caravan
    /// dialog (pawns, items and travel supplies tabs), with the dungeon's level and the party's effect on it on top.
    /// </summary>
    public class Dialog_EnterDungeon : Window
    {
        private enum Tab { Pawns, Items, Supplies }

        private const float TitleHeight = 35f;
        private const float StatsHeight = 40f;
        private const float InfoHeight = 66f;
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

        public override void PostClose()
        {
            base.PostClose();
            Locked.Clear();
        }

        private void Recache()
        {
            Locked.Clear();
            transferables = new List<TransferableOneWay>();
            foreach (var p in caravan.PawnsListForReading) Add(p);
            foreach (var t in CaravanInventoryUtility.AllInventoryItems(caravan)) Add(t);
            CaravanUIUtility.CreateCaravanTransferableWidgets(transferables, out pawnsTransfer, out itemsTransfer, out suppliesTransfer,
                "TUBAD_EnterThingCountTip".Translate(), IgnorePawnsInventoryMode.Ignore, () => partyInfo.massCapacity - partyInfo.massUsage,
                ignoreSpawnedCorpsesGearAndInventoryMass: false, caravan.Tile);
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
                    if (tr.AnyThing is Pawn && tr.CountToTransfer < tr.GetMaximumToTransfer())
                    {
                        tr.AdjustTo(tr.GetMaximumToTransfer());
                        tr.interactive = false;
                        Locked.Add(tr);
                    }
                party = TransferableUtility.GetPawnsFromTransferables(transferables);
            }
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
                Widgets.Label(new Rect(0f, 0f, inRect.width, TitleHeight), "TUBAD_EnterDungeon_Title".Translate(level));
            // Outside caravan on the left, party on the right, like split caravan. When everyone goes in there is no
            // caravan left outside, so only the party is shown.
            var statsRect = new Rect(12f, TitleHeight, inRect.width - 24f, StatsHeight);
            if (everyone)
                CaravanUIUtility.DrawCaravanInfo(partyInfo, null, caravan.Tile, null, -9999f, statsRect);
            else
                CaravanUIUtility.DrawCaravanInfo(outsideInfo, partyInfo, caravan.Tile, null, -9999f, statsRect);
            DoInfo(new Rect(17f, TitleHeight + StatsHeight, inRect.width - 34f, InfoHeight));

            tabs.Clear();
            tabs.Add(new TabRecord("PawnsTab".Translate(), () => tab = Tab.Pawns, tab == Tab.Pawns));
            tabs.Add(new TabRecord("ItemsTab".Translate(), () => tab = Tab.Items, tab == Tab.Items));
            tabs.Add(new TabRecord("TravelSupplies".Translate(), () => tab = Tab.Supplies, tab == Tab.Supplies));
            inRect.yMin += TitleHeight + StatsHeight + InfoHeight + TabDrawer.TabHeight + 4f;
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

        private void DoInfo(Rect rect)
        {
            int cap = DungeonsMod.Settings.maxPartySize;
            var going = party;
            int people = DungeonEntry.HumanlikeCount(going);
            float mult = DungeonEntry.WealthMultiplier(wealth);
            float col = rect.width / 3f;
            using (new TextBlock(TextAnchor.MiddleLeft))
            {
                GUI.color = people > cap ? ColorLibrary.RedReadable : Color.white;
                Widgets.Label(new Rect(rect.x, rect.y, col, 24f), "TUBAD_Enter_People".Translate(people, cap));
                GUI.color = Color.white;
                Widgets.Label(new Rect(rect.x + col, rect.y, col, 24f), "TUBAD_Enter_Wealth".Translate(wealth.ToStringMoney()));
                var levelRect = new Rect(rect.x + col * 2f, rect.y, col, 24f);
                Widgets.Label(levelRect, "TUBAD_Enter_Effective".Translate((level * mult).ToString("0.#"), mult.ToStringByStyle(ToStringStyle.FloatTwo)));
                TooltipHandler.TipRegion(levelRect, "TUBAD_Enter_Effective_Tip".Translate(level, wealth.ToStringMoney(), mult.ToString("0.00"), DungeonsMod.Settings.wealthToDouble.ToStringMoney()));

                string note = null;
                var entered = DungeonEntry.AlreadyEntered(caravan.Tile, going).ToList();
                if (entered.Any())
                {
                    GUI.color = ColorLibrary.RedReadable;
                    note = "TUBAD_Enter_AlreadyEntered".Translate(entered.Select(p => p.LabelShortCap).ToCommaList(true));
                }
                else if (everyone)
                {
                    GUI.color = ColoredText.WarningColor;
                    note = "TUBAD_Enter_Everyone".Translate();
                }
                if (note != null) Widgets.Label(new Rect(rect.x, rect.y + 26f, rect.width, 40f), note);
                GUI.color = Color.white;
            }
        }

        private void DoButtons(Rect rect)
        {
            var accept = new Rect(rect.width / 2f - ButtonSize.x / 2f, rect.height - 55f, ButtonSize.x, ButtonSize.y);
            if (Widgets.ButtonText(accept, "TUBAD_EnterButton".Translate()) && TryAccept())
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
                Messages.Message("TUBAD_Enter_NeedColonist".Translate(), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            var going = party; // already every pawn when everyone goes in
            int cap = DungeonsMod.Settings.maxPartySize;
            if (DungeonEntry.HumanlikeCount(going) > cap)
            {
                Messages.Message((everyone ? "TUBAD_Enter_TooManyEveryone" : "TUBAD_Enter_TooMany").Translate(cap), MessageTypeDefOf.RejectInput, false);
                return false;
            }
            var entered = DungeonEntry.AlreadyEntered(caravan.Tile, going).ToList();
            if (entered.Any())
            {
                Messages.Message("TUBAD_Enter_AlreadyEntered".Translate(entered.Select(p => p.LabelShortCap).ToCommaList(true)), MessageTypeDefOf.RejectInput, false);
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
