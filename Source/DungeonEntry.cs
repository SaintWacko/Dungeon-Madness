using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ThisUsedToBeAboutDungeons
{
    public static class DungeonEntry
    {
        public static readonly IntVec3 MapSize = new IntVec3(100, 1, 100);

        public static DungeonSite SiteAt(PlanetTile tile) => Find.WorldObjects.WorldObjectAt<DungeonSite>(tile);

        /// <summary>Whether a caravan could enter the dungeon on this tile at all, ignoring who is in it.</summary>
        public static AcceptanceReport CanEnterAt(PlanetTile tile, out int level)
        {
            var comp = DungeonWorldComponent.Instance;
            if (comp == null || !comp.TryGetLevel(tile, out level) || comp.IsBlocked(tile))
            {
                level = 0;
                return false;
            }
            var site = SiteAt(tile);
            if (site != null)
            {
                // A site without a map is left over from a generation that failed; it must not lock the dungeon
                if (!site.HasMap) site.Destroy();
                else return "TUBAD_PartyInside".Translate();
            }
            return true;
        }

        public static int HumanlikeCount(IEnumerable<Pawn> pawns) => pawns.Count(p => p.RaceProps.Humanlike);

        public static IEnumerable<Pawn> AlreadyEntered(PlanetTile tile, IEnumerable<Pawn> pawns)
        {
            var comp = DungeonWorldComponent.Instance;
            return pawns.Where(p => p.RaceProps.Humanlike && comp != null && comp.HasEntered(tile.tileId, p));
        }

        /// <summary>Market value of the pawns plus what they wear and wield. Inventory is not counted: the
        /// party's strength is in its people and their gear, not what they carry in.</summary>
        public static float PartyWealth(IEnumerable<Pawn> pawns)
        {
            float total = 0f;
            foreach (var p in pawns)
            {
                total += p.MarketValue;
                if (p.apparel != null) foreach (var a in p.apparel.WornApparel) total += a.MarketValue;
                if (p.equipment != null) foreach (var e in p.equipment.AllEquipmentListForReading) total += e.MarketValue;
            }
            return total;
        }

        public static float WealthMultiplier(float wealth) => 1f + wealth / DungeonsMod.Settings.wealthToDouble;
        public static float EffectiveLevel(int level, float wealth) => level * WealthMultiplier(wealth);

        /// <summary>Whether someone able to lead the caravan stays outside when these pawns go in.</summary>
        public static bool AnyoneStaysOutside(Caravan caravan, ICollection<Pawn> party) =>
            caravan.PawnsListForReading.Any(p => !party.Contains(p) && CaravanUtility.IsOwner(p, Faction.OfPlayer) && !p.Downed);

        /// <summary>
        /// Generates a fresh dungeon on the caravan's tile and moves the party into it. `transferables` holds the
        /// items with how many of each go in. When nobody able stays outside, every pawn goes in and the items
        /// left behind wait in the site's stash for the party to come back out.
        /// </summary>
        public static void Enter(Caravan caravan, List<Pawn> party, List<TransferableOneWay> itemTransferables)
        {
            PlanetTile tile = caravan.Tile;
            if (!CanEnterAt(tile, out int level).Accepted) return;
            float wealth = PartyWealth(party);

            LongEventHandler.QueueLongEvent(() =>
            {
                var site = (DungeonSite)WorldObjectMaker.MakeWorldObject(DungeonDefOf.TUBAD_DungeonSite);
                site.Tile = tile;
                site.SetFaction(null);
                site.level = level;
                site.partyWealth = wealth;
                site.effectiveLevel = EffectiveLevel(level, wealth);
                Find.WorldObjects.Add(site);
                Map map = MapGenerator.GenerateMap(MapSize, site, site.MapGeneratorDef);

                bool everyone = !AnyoneStaysOutside(caravan, party);
                Caravan partyCaravan;
                if (everyone)
                {
                    // Unchosen items go to the stash; chosen ones stay in their owners' inventories and go in with them
                    foreach (var tr in itemTransferables)
                    {
                        int leave = tr.things.Sum(t => t.stackCount) - tr.CountToTransfer;
                        if (leave <= 0) continue;
                        TransferableUtility.TransferNoSplit(tr.things, leave, (thing, n) =>
                        {
                            var owner = thing.holdingOwner;
                            if (owner != null) owner.TryTransferToContainer(thing, site.GetDirectlyHeldThings(), n);
                        });
                    }
                    party = caravan.PawnsListForReading.ToList();
                    foreach (var p in party) caravan.RemovePawn(p);
                    if (!caravan.Destroyed) caravan.Destroy();
                    partyCaravan = CaravanMaker.MakeCaravan(party, Faction.OfPlayer, tile, addToWorldPawnsIfNotAlready: true);
                }
                else
                {
                    // Vanilla's caravan split (Dialog_SplitCaravan.TrySplitCaravan)
                    foreach (var p in party) CaravanInventoryUtility.MoveAllInventoryToSomeoneElse(p, caravan.PawnsListForReading, party);
                    foreach (var p in party) caravan.RemovePawn(p);
                    partyCaravan = CaravanMaker.MakeCaravan(party, Faction.OfPlayer, tile, addToWorldPawnsIfNotAlready: true);
                    foreach (var tr in itemTransferables)
                    {
                        if (tr.CountToTransfer <= 0) continue;
                        TransferableUtility.TransferNoSplit(tr.things, tr.CountToTransfer, (thing, n) =>
                        {
                            Pawn owner = CaravanInventoryUtility.GetOwnerOf(caravan, thing);
                            if (owner != null)
                                CaravanInventoryUtility.MoveInventoryToSomeoneElse(owner, thing, partyCaravan.PawnsListForReading, null, n);
                        });
                    }
                }

                var comp = DungeonWorldComponent.Instance;
                foreach (var p in party)
                    if (p.RaceProps.Humanlike) comp.MarkEntered(tile.tileId, p);

                IntVec3 entrance = (map.listerThings.ThingsOfDef(DungeonDefOf.TUBAD_DungeonExit).FirstOrDefault() as DungeonExit)?.InsideCell ?? map.Center;
                CaravanEnterMapUtility.Enter(partyCaravan, map, p => CellFinder.RandomClosewalkCellNear(entrance, map, 4),
                    CaravanDropInventoryMode.DoNotDrop, draftColonists: false);
                CameraJumper.TryJump(entrance, map);
                Find.LetterStack.ReceiveLetter("TUBAD_Entered_Label".Translate(), "TUBAD_Entered_Text".Translate(level, site.effectiveLevel.ToString("0.#")),
                    LetterDefOf.NeutralEvent, new LookTargets(entrance, map));
            }, "GeneratingMapForNewEncounter", false, null);
        }
    }
}
