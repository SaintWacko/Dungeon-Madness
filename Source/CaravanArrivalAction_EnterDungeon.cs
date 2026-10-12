using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace DungeonMadness
{
    /// <summary>Travel to a dungeon and open the party dialog on arrival.</summary>
    public class CaravanArrivalAction_EnterDungeon : CaravanArrivalAction
    {
        private PlanetTile tile;

        public CaravanArrivalAction_EnterDungeon() { }
        public CaravanArrivalAction_EnterDungeon(PlanetTile tile) { this.tile = tile; }

        public override string Label => "DMAD_EnterDungeon".Translate();
        public override string ReportString => "DMAD_TravellingToDungeon".Translate();

        public override FloatMenuAcceptanceReport StillValid(Caravan caravan, PlanetTile destinationTile)
        {
            var baseReport = base.StillValid(caravan, destinationTile);
            if (!baseReport) return baseReport;
            return destinationTile == tile && DungeonEntry.CanEnterAt(tile, out _).Accepted;
        }

        public override void Arrived(Caravan caravan) => OpenDialog(caravan);

        public static void OpenDialog(Caravan caravan)
        {
            var report = DungeonEntry.CanEnterAt(caravan.Tile, out int level);
            if (!report.Accepted)
            {
                if (!report.Reason.NullOrEmpty()) Messages.Message(report.Reason, caravan, MessageTypeDefOf.RejectInput, false);
                return;
            }
            Find.WindowStack.Add(new Dialog_EnterDungeon(caravan, level));
        }

        public static IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan, PlanetTile tile)
        {
            if (!DungeonEntry.CanEnterAt(tile, out int level).Accepted) yield break;
            string label = "DMAD_EnterDungeonLevel".Translate(level);
            if (caravan.Tile == tile && !caravan.pather.Moving)
            {
                yield return new FloatMenuOption(label, () => OpenDialog(caravan));
                yield break;
            }
            foreach (var o in CaravanArrivalActionUtility.GetFloatMenuOptions(() => true,
                         () => new CaravanArrivalAction_EnterDungeon(tile), label, caravan, tile, null))
                yield return o;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref tile, "tile");
        }
    }
}
