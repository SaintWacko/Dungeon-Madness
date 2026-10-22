using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace DungeonMadness
{
    public static class DungeonEntry
    {
        public const int MinMapSize = 60;
        public const int MaxMapSize = 150;

        /// <summary>Side length by the dungeon's own level (not the party's wealth), from MinMapSize at level 1 to
        /// MaxMapSize at the world's top level. The same dungeon is the same size for every party.</summary>
        public static IntVec3 MapSizeFor(int level)
        {
            int max = DungeonWorldComponent.Instance?.MaxLevel ?? DungeonsMod.Settings.maxLevel;
            float t = max <= 1 ? 0f : Mathf.Clamp01((level - 1f) / (max - 1f));
            int side = Mathf.RoundToInt(Mathf.Lerp(MinMapSize, MaxMapSize, t));
            return new IntVec3(side, 1, side);
        }

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
                else return "DMAD_PartyInside".Translate();
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
        /// <summary>The colony's prisoners in this caravan, including any stowed in a pawn's inventory (Vehicle
        /// Framework cargo). Prisoners never enter a dungeon: a humanlike going in has to count toward the party cap.</summary>
        public static List<Pawn> Prisoners(Caravan caravan)
        {
            var list = caravan.PawnsListForReading.Where(p => p.IsPrisonerOfColony).ToList();
            foreach (var p in caravan.PawnsListForReading)
                if (p.inventory != null)
                    foreach (var stowed in p.inventory.innerContainer.OfType<Pawn>())
                        if (stowed.IsPrisonerOfColony && !list.Contains(stowed)) list.Add(stowed);
            return list;
        }

        public static bool AnyoneStaysOutside(Caravan caravan, ICollection<Pawn> party) =>
            caravan.PawnsListForReading.Any(p => !party.Contains(p) && CaravanUtility.IsOwner(p, Faction.OfPlayer) && !p.Downed);

        /// <summary>
        /// Generates a fresh dungeon on the caravan's tile and moves the party into it. `transferables` holds the
        /// items with how many of each go in. When nobody able stays outside, every pawn goes in and the items
        /// left behind wait in the site's stash for the party to come back out.
        /// </summary>
        public static void Enter(Caravan caravan, List<Pawn> party, List<TransferableOneWay> itemTransferables, DungeonInfluence influence = null)
        {
            PlanetTile tile = caravan.Tile;
            if (!CanEnterAt(tile, out int baseLevel).Accepted) return;
            float wealth = PartyWealth(party);
            // Influence was paid for by the dialog just before this; the level row moves the dungeon's own level, so
            // map size, the wealth multiplier and everything after it see the influenced one
            if (influence != null && !influence.Any(DungeonThemeUtility.ThemeFor(tile))) influence = null;
            int level = influence?.Level(baseLevel) ?? baseLevel;

            LongEventHandler.QueueLongEvent(() =>
            {
                var site = (DungeonSite)WorldObjectMaker.MakeWorldObject(DungeonDefOf.DMAD_DungeonSite);
                site.Tile = tile;
                site.SetFaction(null);
                site.level = level;
                site.baseLevel = baseLevel;
                site.influence = influence;
                // GenStep_DungeonLayout keeps a theme already on the site
                if (influence?.theme != null) site.theme = influence.theme;
                site.partyWealth = wealth;
                site.effectiveLevel = EffectiveLevel(level, wealth);
                site.layoutSeed = Rand.Int | 1;
                // Doors are made without a faction, which animals cannot open; the party's animals must follow it
                site.doorsAlwaysOpenForPlayerPawns = true;
                Find.WorldObjects.Add(site);
                Map map;
                try { map = MapGenerator.GenerateMap(MapSizeFor(level), site, site.MapGeneratorDef); }
                catch (System.Exception e)
                {
                    // Nobody has moved yet: the party is still in its caravan. Give back the verity paid for this visit.
                    Log.Error($"[Dungeon Madness] Generating the dungeon failed; the party stays outside. {e}");
                    if (influence != null)
                        EntadFramework.EntadApi.AddVerity(influence.TotalCost(baseLevel, DungeonThemeUtility.ThemeFor(tile)));
                    if (site.HasMap) Current.Game.DeinitAndRemoveMap(site.Map, false);
                    if (!site.Destroyed) site.Destroy();
                    return;
                }

                bool everyone = !AnyoneStaysOutside(caravan, party);
                Caravan partyCaravan;
                if (everyone)
                {
                    // Vehicles never go in (Vehicle Framework): they wait in the stash with their cargo. Everyone else,
                    // passengers included, goes in on foot.
                    var vehicles = caravan.PawnsListForReading.Where(VehicleCompat.IsVehicle).ToList();
                    party = caravan.PawnsListForReading.Where(p => !VehicleCompat.IsVehicle(p)).ToList();
                    var fromVehicles = new List<(Pawn vehicle, Thing thing, int count)>();
                    // Unchosen items go to the stash; chosen ones stay in their owners' inventories and go in with them.
                    // The amount left behind is taken from vehicle cargo first, since that stays put in its vehicle;
                    // chosen cargo is moved to the people going in.
                    foreach (var tr in itemTransferables)
                    {
                        int leave = tr.things.Sum(t => t.stackCount) - tr.CountToTransfer;
                        var ordered = tr.things.OrderBy(t => VehicleCompat.VehicleHolding(t) != null ? 0 : 1).ToList();
                        var leftInVehicle = new Dictionary<Thing, int>();
                        if (leave > 0)
                            TransferableUtility.TransferNoSplit(ordered, leave, (thing, n) =>
                            {
                                if (VehicleCompat.VehicleHolding(thing) != null) leftInVehicle[thing] = n;
                                else thing.holdingOwner?.TryTransferToContainer(thing, site.GetDirectlyHeldThings(), n);
                            }, removeIfTakingEntireThing: false);
                        foreach (var thing in ordered)
                        {
                            Pawn v = VehicleCompat.VehicleHolding(thing);
                            if (v == null) continue;
                            int take = thing.stackCount - leftInVehicle.TryGetValue(thing, 0);
                            if (take > 0) fromVehicles.Add((v, thing, take));
                        }
                    }
                    foreach (var (v, thing, n) in fromVehicles)
                        CaravanInventoryUtility.MoveInventoryToSomeoneElse(v, thing, party, null, n);

                    // A pawn stowed in a vehicle's cargo (an animal, say) is part of everyone too; left there it would be
                    // deep-saved in the stash and destroyed with it. Prisoners cannot get this far: the dialog refuses.
                    foreach (var v in vehicles)
                        foreach (var stowed in v.inventory.innerContainer.OfType<Pawn>().ToList())
                            if (!party.Contains(stowed) && !stowed.IsPrisonerOfColony) party.Add(stowed);
                    VehicleCompat.Disembark(caravan, party);
                    // People before vehicles: VF converts or destroys a vehicle caravan as its last vehicle leaves, and
                    // destroys any vehicle still in one that is destroyed
                    foreach (var p in party) caravan.RemovePawn(p);
                    foreach (var v in vehicles) caravan.RemovePawn(v);
                    if (!caravan.Destroyed) caravan.Destroy();
                    // Into VF's own stash on the world map, which the party collects on the way out; any other caravan
                    // can recover it meanwhile. Kept in the site's stash if VF's cannot be made.
                    site.vehicleStash = VehicleCompat.StashVehicles(tile, vehicles, out var notStashed);
                    foreach (var v in notStashed)
                    {
                        if (Find.WorldPawns.Contains(v)) Find.WorldPawns.RemovePawn(v);
                        site.AddToStash(v);
                    }
                    partyCaravan = CaravanMaker.MakeCaravan(party, Faction.OfPlayer, tile, addToWorldPawnsIfNotAlready: true);
                }
                else
                {
                    // Vanilla's caravan split (Dialog_SplitCaravan.TrySplitCaravan). Vehicles stay outside; anyone going
                    // in who rides one gets off first, since the caravan does not hold them.
                    foreach (var p in party) CaravanInventoryUtility.MoveAllInventoryToSomeoneElse(p, caravan.PawnsListForReading, party);
                    VehicleCompat.Disembark(caravan, party);
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

                site.party = party.Where(p => p.IsColonist).ToList();
                var comp = DungeonWorldComponent.Instance;
                foreach (var p in party)
                    if (p.RaceProps.Humanlike) comp.MarkEntered(tile.tileId, p);

                IntVec3 entrance = (map.listerThings.ThingsOfDef(DungeonDefOf.DMAD_DungeonExit).FirstOrDefault() as DungeonExit)?.InsideCell ?? map.Center;
                CaravanEnterMapUtility.Enter(partyCaravan, map, p => CellFinder.RandomClosewalkCellNear(entrance, map, 4),
                    CaravanDropInventoryMode.DoNotDrop, draftColonists: false);
                CameraJumper.TryJump(entrance, map);
                string text = "DMAD_Entered_Text".Translate(level, site.effectiveLevel.ToString("0.#"));
                if (influence != null)
                    text += "\n\n" + "DMAD_Entered_Influenced".Translate() + "\n" + string.Join("\n", influence.Describe(baseLevel, DungeonThemeUtility.ThemeFor(tile)).Select(l => "  - " + l));
                Find.LetterStack.ReceiveLetter("DMAD_Entered_Label".Translate(), text,
                    LetterDefOf.NeutralEvent, new LookTargets(entrance, map));
            }, "GeneratingMapForNewEncounter", false, null);
        }
    }
}
