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

        private void Recache()
        {
            transferables = new List<TransferableOneWay>();
            foreach (var p in caravan.PawnsListForReading) Add(p);
            foreach (var t in CaravanInventoryUtility.AllInventoryItems(caravan)) Add(t);
            CaravanUIUtility.CreateCaravanTransferableWidgets(transferables, out pawnsTransfer, out itemsTransfer, out suppliesTransfer,
                "TUBAD_EnterThingCountTip".Translate(), IgnorePawnsInventoryMode.Ignore, () => float.MaxValue,
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
            party = TransferableUtility.GetPawnsFromTransferables(transferables);
            everyone = party.Count > 0 && !DungeonEntry.AnyoneStaysOutside(caravan, party);
            var going = everyone ? caravan.PawnsListForReading : party;
            wealth = DungeonEntry.PartyWealth(going);
        }

        public override void DoWindowContents(Rect inRect)
        {
            using (new TextBlock(GameFont.Medium, TextAnchor.MiddleCenter))
                Widgets.Label(new Rect(0f, 0f, inRect.width, TitleHeight), "TUBAD_EnterDungeon_Title".Translate(level));
            DoInfo(new Rect(17f, TitleHeight, inRect.width - 34f, InfoHeight));

            tabs.Clear();
            tabs.Add(new TabRecord("PawnsTab".Translate(), () => tab = Tab.Pawns, tab == Tab.Pawns));
            tabs.Add(new TabRecord("ItemsTab".Translate(), () => tab = Tab.Items, tab == Tab.Items));
            tabs.Add(new TabRecord("TravelSupplies".Translate(), () => tab = Tab.Supplies, tab == Tab.Supplies));
            inRect.yMin += TitleHeight + InfoHeight + TabDrawer.TabHeight + 4f;
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
            var going = everyone ? caravan.PawnsListForReading : party;
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
            var going = everyone ? caravan.PawnsListForReading : party;
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
            DungeonEntry.Enter(caravan, party, transferables.Where(t => !(t.AnyThing is Pawn)).ToList());
            return true;
        }
    }
}
