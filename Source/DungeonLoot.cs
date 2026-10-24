using System.Collections.Generic;
using System.Linq;
using EntadFramework;
using RimWorld;
using RimWorld.BaseGen;
using UnityEngine;
using Verse;

namespace DungeonMadness
{
    /// <summary>One furnished room (a suite's leaves count separately) and what it was furnished as.</summary>
    public struct FurnishedRoom
    {
        public CellRect rect;
        public RoomUse use;
        public FurnishedRoom(CellRect rect, RoomUse use) { this.rect = rect; this.use = use; }
    }

    /// <summary>
    /// A dungeon's loot, made during map generation inside its seeded Rand. Ordinary goods come from vanilla's
    /// reward maker; each loot slot is an entad at the settings' chance instead, made by Entad Framework with points
    /// equal to the effective level. Entad furniture is only made when the dungeon has furnished rooms, and is set up
    /// in the room that suits it best rather than lying in a pile. Every entad made here waits on the site to be
    /// bound when it leaves the dungeon.
    /// </summary>
    public class DungeonLootContext
    {
        public Map map;
        public DungeonSite site;
        public float level;
        // Furniture only when there is a furnished room to put it in
        public bool furnitureAllowed;
        // The theme's chance that a library book is a dangerous one (an Anomaly tome)
        public float tomeChance;
        public readonly List<FurnishedRoom> rooms = new List<FurnishedRoom>();
        // Verity spent on this visit: entad chance and value, loot value
        public readonly DungeonInfluence influence;
        public float EntadChance => influence.EntadChance(DungeonsMod.Settings.entadChance);
        public float LootValueFactor => influence.LootValueFactor;
        // Entad furniture made while rooms were still being furnished, set up once they all are
        private readonly List<Thing> furnitureQueue = new List<Thing>();
        private EntadItemRequest request;
        // Every entad made here, so setting up furniture never replaces or wipes one
        private readonly HashSet<Thing> entads = new HashSet<Thing>();

        // Rooms are at least 5 cells on a side; a piece needs space around it for its interaction cell and a way past
        const int MaxFurnitureSide = 3;

        public DungeonLootContext(Map map, bool furnitureAllowed)
        {
            this.map = map;
            site = map.Parent as DungeonSite;
            level = site == null ? 1f : Mathf.Max(1f, site.effectiveLevel > 0f ? site.effectiveLevel : site.level);
            this.furnitureAllowed = furnitureAllowed;
            influence = DungeonInfluence.Of(map);
        }

        /// <summary>The colony's current tech level: the highest tech level with at least half of its research
        /// projects finished, never below the player faction's own. A tribe starts Neolithic and climbs as it
        /// researches (the faction def alone never changes); one techprint project does not lift the whole colony.</summary>
        // Read for every entad request, roll and bookcase of one map, and it walks every research project
        private TechLevel? colonyTech;
        internal TechLevel Tech => colonyTech ?? (colonyTech = ColonyTech).Value;

        public static TechLevel ColonyTech
        {
            get
            {
                TechLevel tech = Faction.OfPlayerSilentFail?.def?.techLevel ?? TechLevel.Industrial;
                var total = new Dictionary<TechLevel, int>();
                var done = new Dictionary<TechLevel, int>();
                foreach (var r in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
                {
                    if (r.techLevel <= tech) continue;
                    total[r.techLevel] = total.TryGetValue(r.techLevel) + 1;
                    if (r.IsFinished) done[r.techLevel] = done.TryGetValue(r.techLevel) + 1;
                }
                foreach (var kv in total)
                    if (kv.Key > tech && done.TryGetValue(kv.Key) * 2 >= kv.Value) tech = kv.Key;
                return tech;
            }
        }

        /// <summary>Furniture a party can take home: whatever is minifiable right now, which already includes what
        /// Minify Everything (or a mod like it) made minifiable and leaves out what its player switched off.</summary>
        static bool FurnitureFits(ThingDef d)
        {
            if (!d.Minifiable || d.building == null) return false;
            if (d.size.x > MaxFurnitureSide || d.size.z > MaxFurnitureSide) return false;
            if (d.thingClass != null && typeof(Building_Door).IsAssignableFrom(d.thingClass)) return false;
            return true;
        }

        EntadItemRequest Request()
        {
            if (request != null) return request;
            TechLevel tech = Tech;
            bool furniture = furnitureAllowed;
            request = new EntadItemRequest
            {
                kinds = furniture ? EntadItemKind.All : EntadItemKind.Weapon | EntadItemKind.Apparel,
                thingPredicate = d => (d.techLevel == TechLevel.Undefined || d.techLevel <= tech)
                    && (d.category != ThingCategory.Building || (furniture && FurnitureFits(d))),
            };
            return request;
        }

        /// <summary>An unspawned entad at this dungeon's level, registered to be bound on the way out; null when no
        /// item fits.</summary>
        public Thing MakeEntad()
        {
            Thing t = EntadApi.GenerateEntadItem(DungeonsMod.Settings.entadPointsPerLevel * level * influence.EntadValueFactor, Request());
            if (t != null)
            {
                site?.RegisterEntad(t);
                entads.Add(t);
            }
            return t;
        }

        public void QueueFurniture(Thing t) => furnitureQueue.Add(t);

        class LootPile
        {
            public List<IntVec3> cells;
            public readonly List<Thing> things = new List<Thing>();
            // What this pile's ordinary slots are worth, and how much of that the dungeon's rolls have handed it
            public float budget;
            public float given;
            public int slots;
        }

        /// <summary>Rolls one pile's slots for entads. Entad furniture is queued for a room, so a slot that rolled one
        /// is simply missing from the pile; every other slot adds its share of the pile's value to the budget.</summary>
        void RollEntadSlots(LootPile pile, int count, float valuePerSlot)
        {
            for (int i = 0; i < count; i++)
            {
                Thing entad = Rand.Chance(EntadChance) ? MakeEntad() : null;
                if (entad == null) { pile.slots++; pile.budget += valuePerSlot; }
                else if (entad.def.category == ThingCategory.Building) furnitureQueue.Add(entad);
                else pile.things.Add(entad);
            }
        }

        /// <summary>The ordinary goods of every pile, rolled for the whole dungeon and then shared out. Vanilla's
        /// reward maker only picks items that fit what is left of a roll's budget, so a pile-sized roll at a low level
        /// keeps landing on the few cheap items in a category (a masterwork roll at 300 silver has four to choose
        /// from). Pooling the piles into rolls of the setting's value per level widens the choice, and that value is
        /// also the most any single item can be worth. A bigger dungeon has more piles, so more rolls rather than
        /// bigger ones. Largest first, each item goes to the pile with the most budget still unspent.</summary>
        void FillPiles(List<LootPile> piles)
        {
            var funded = piles.Where(p => p.slots > 0).ToList();
            float total = funded.Sum(p => p.budget);
            int slots = funded.Sum(p => p.slots);
            if (funded.Count == 0 || total <= 0f) return;

            float rollValue = Mathf.Max(1f, DungeonsMod.Settings.lootRollValuePerLevel * level);
            // Rounded up, so a roll is never over the cap. No more rolls than slots, which keeps generation to what
            // per-pile rolls cost; only a roll setting far below the pile value then leaves some value unrolled.
            int rolls = Mathf.Clamp(Mathf.CeilToInt(total / rollValue), 1, slots);
            float each = total / rolls;
            var range = new FloatRange(Mathf.Min(each * 0.8f, rollValue * 0.8f), Mathf.Min(each * 1.2f, rollValue));
            TechLevel tech = Tech;
            var goods = new List<Thing>();
            for (int r = 0; r < rolls; r++)
            {
                int count = Mathf.Max(1, slots * (r + 1) / rolls - slots * r / rolls);
                var parms = new ThingSetMakerParams
                {
                    techLevel = tech,
                    totalMarketValueRange = range,
                    countRange = new IntRange(1, count),
                };
                goods.AddRange(ThingSetMakerDefOf.Reward_ItemsStandard.root.Generate(parms));
            }

            foreach (var t in goods.OrderByDescending(t => t.MarketValue * t.stackCount))
            {
                var pile = funded.MaxBy(p => p.budget - p.given);
                pile.things.Add(t);
                pile.given += t.MarketValue * t.stackCount;
            }

            if (Prefs.DevMode)
                Log.Message($"[Dungeon Madness] Loot: {piles.Count} piles, {total:0} silver in {rolls} rolls of about {each:0}: " +
                    string.Join(", ", goods.Select(t => t.LabelCap)));
        }

        /// <summary>Fills every bookcase standing in a furnished room with books: half full to full, quality rising
        /// with the effective level, within the colony's tech cap. These are the only books the mod adds; vanilla's
        /// reward maker (the loot piles) already rolls an occasional book of its own. Books that do nothing but start a
        /// quest (Odyssey maps) are left out, since a shelf of them would be a shelf of quests. Books that can cause a
        /// mental break (Anomaly tomes) only appear at the theme's tome chance. Runs while rooms are furnished, before
        /// PlaceFurniture spawns any entad furniture, so an entad bookcase stays empty.</summary>
        public void StockBookcases(CellRect rect)
        {
            var cases = new List<Building_Bookcase>();
            foreach (var c in rect)
                if (c.GetEdifice(map) is Building_Bookcase b && !cases.Contains(b)) cases.Add(b);
            if (cases.Count == 0) return;

            TechLevel tech = Tech;
            var books = new List<ThingDef>();
            var tomes = new List<ThingDef>();
            foreach (var d in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                var props = d.GetCompProperties<CompProperties_Book>();
                if (props == null || props.pickWeight <= 0f || d.thingClass == null || !typeof(Book).IsAssignableFrom(d.thingClass)) continue;
                // Only what a bookcase would take and can draw: vanilla bookcases hold the Books category and draw
                // each book's vertical graphic
                if (props.verticalGraphic == null || cases[0].GetParentStoreSettings()?.AllowedToAccept(d) == false) continue;
                if (d.techLevel != TechLevel.Undefined && d.techLevel > tech) continue;
                var doers = props.doers;
                if (doers != null && doers.Count > 0 && doers.All(o => o is BookOutcomeProperties_GiveQuest)) continue;
                if (doers != null && doers.Any(o => o is BookOutcomeProperties_MentalBreak)) tomes.Add(d);
                else books.Add(d);
            }
            bool tomesAllowed = tomes.Count > 0 && tomeChance > 0f;
            if (books.Count == 0 && !tomesAllowed) return;

            // Level 1 writes like a skill 4 author, the world's highest level like skill 20
            int maxLevel = Mathf.Max(1, DungeonWorldComponent.Instance?.MaxLevel ?? DungeonsMod.Settings.maxLevel);
            int skill = Mathf.Clamp(Mathf.RoundToInt(4f + 16f * level / maxLevel), 0, 20);

            foreach (var bookcase in cases)
            {
                var holder = bookcase.GetDirectlyHeldThings();
                int max = bookcase.MaximumBooks;
                int n = Rand.RangeInclusive(Mathf.CeilToInt(max / 2f), max) - bookcase.HeldBooks.Count;
                for (; n > 0; n--)
                {
                    bool tome = tomesAllowed && (books.Count == 0 || Rand.Chance(tomeChance));
                    var def = (tome ? tomes : books).RandomElementByWeight(d => d.GetCompProperties<CompProperties_Book>().pickWeight);
                    // BookUtility.MakeBook's body with our own quality roll: setting quality is what writes the book
                    Thing book = ThingMaker.MakeThing(def, GenStuff.RandomStuffFor(def));
                    book.TryGetComp<CompQuality>()?.SetQuality(QualityUtility.GenerateQualityCreatedByPawn(skill, false), ArtGenerationContext.Outsider);
                    book.SetForbidden(true, false);
                    if (!holder.TryAdd(book)) { book.Destroy(); break; }
                }
            }
        }

        /// <summary>Loot piles in random chambers other than the entrance, rolled per chamber like packs and
        /// independent of them, so dungeons with no furnished rooms still have something to find.</summary>
        public void PlaceCaches(DungeonLayoutResult layout)
        {
            var s = DungeonsMod.Settings;
            var byChamber = new Dictionary<int, List<IntVec3>>();
            for (int k = 0; k < layout.owner.Length; k++)
            {
                int id = layout.owner[k];
                if (id <= 0 || !layout.open[k] || layout.feature[k] != CellFeature.None) continue;
                if (!byChamber.TryGetValue(id, out var list)) byChamber[id] = list = new List<IntVec3>();
                list.Add(new IntVec3(k % layout.size, 0, k / layout.size));
            }
            var piles = new List<LootPile>();
            foreach (var ch in layout.data.chambers)
            {
                // Furnished rooms have their own shelves, and entad furniture set up there later clears the floor
                // it stands on, which would take a pile's goods with it
                if (ch.id == 0 || ch.Furnished || !Rand.Chance(influence.LootChance(s.lootChance))) continue;
                if (!byChamber.TryGetValue(ch.id, out var cells)) continue;
                cells = cells.Where(c => c.Standable(map) && c.GetFirstItem(map) == null && !c.GetTerrain(map).IsWater).ToList();
                if (cells.Count == 0) continue;
                int n = Rand.RangeInclusive(2, 4);
                float value = s.lootValuePerLevel * level * LootValueFactor * Rand.Range(0.75f, 1.25f);
                var pile = new LootPile { cells = cells };
                RollEntadSlots(pile, n, value / n);
                piles.Add(pile);
            }
            FillPiles(piles);

            foreach (var pile in piles)
            {
                var cells = pile.cells;
                IntVec3 center = cells.RandomElement();
                var spots = cells.OrderBy(c => c.DistanceToSquared(center)).ToList();
                int i = 0;
                foreach (var t in pile.things)
                {
                    bool placed = false;
                    while (!placed && i < spots.Count)
                        placed = GenPlace.TryPlaceThing(t, spots[i++], map, ThingPlaceMode.Direct, (put, _) => put.SetForbidden(true, false));
                    if (!placed && !t.Spawned && !t.Destroyed) Discard(t);
                }
            }
        }

        /// <summary>Destroys loot that found no place, forgetting it as a pending entad.</summary>
        public void Discard(Thing t)
        {
            site?.UnregisterEntad(t);
            if (!t.Destroyed) t.Destroy();
        }

        /// <summary>Sets up every queued piece of entad furniture in the room that suits it best. One that fits
        /// nowhere is packed up and left in a room as a minified item instead.</summary>
        public void PlaceFurniture()
        {
            foreach (var t in furnitureQueue)
            {
                if (TryPlaceInRoom(t)) continue;
                var spots = rooms.SelectMany(r => r.rect.Cells).Where(c => c.Standable(map) && c.GetFirstItem(map) == null).ToList();
                if (spots.Count > 0 && t.def.Minifiable)
                {
                    var mini = MinifyUtility.MakeMinified(t);
                    if (GenPlace.TryPlaceThing(mini, spots.RandomElement(), map, ThingPlaceMode.Direct, (put, _) => put.SetForbidden(true, false))) continue;
                    if (!mini.Destroyed) mini.Destroy();
                }
                else if (!t.Destroyed) t.Destroy();
                site?.UnregisterEntad(t);
            }
            furnitureQueue.Clear();
        }

        bool TryPlaceInRoom(Thing t)
        {
            var prefs = DungeonRoomMatch.UsesFor(t.def);
            // Rooms in order of preference, the best-matching use first, then every other furnished room
            var ordered = rooms.InRandomOrder().OrderBy(r => { int i = prefs.IndexOf(r.use); return i < 0 ? 100 : i; }).ToList();
            foreach (var room in ordered)
                if (TryPlaceIn(t, room)) return true;
            return false;
        }

        /// <summary>The cheapest spot in the room: empty floor first, then one where it takes the place of
        /// something with the same role, then anything else. Walls, doors and the cells in front of doors are never
        /// used; what it replaces is removed.</summary>
        bool TryPlaceIn(Thing t, FurnishedRoom room)
        {
            ThingDef def = t.def;
            var role = DungeonRoomMatch.UsesFor(def);
            float bestCost = float.MaxValue;
            IntVec3 bestCell = IntVec3.Invalid;
            Rot4 bestRot = Rot4.North;
            List<Thing> bestRemove = null;
            var remove = new List<Thing>();
            for (int r = 0; r < 4; r++)
            {
                var rot = new Rot4(r);
                foreach (var c in room.rect.Cells.InRandomOrder())
                {
                    var occ = GenAdj.OccupiedRect(c, rot, def.size);
                    if (!room.rect.Contains(occ.Min) || !room.rect.Contains(occ.Max)) continue;
                    remove.Clear();
                    float cost = 0f;
                    bool ok = true;
                    foreach (var o in occ)
                    {
                        if (BaseGenUtility.AnyDoorAdjacentCardinalTo(o, map)) { ok = false; break; }
                        cost += Blockers(o, role, remove);
                    }
                    if (!ok) continue;
                    if (def.hasInteractionCell)
                    {
                        IntVec3 ic = ThingUtility.InteractionCellWhenAt(def, c, rot, map);
                        if (!room.rect.Contains(ic) || occ.Contains(ic)) continue;
                        cost += Blockers(ic, role, remove);
                    }
                    // Something else whose interaction cell this would cover goes too
                    foreach (var b in RoomBuildings(room))
                        if (!remove.Contains(b) && b.def.hasInteractionCell && occ.Contains(b.InteractionCell)) { remove.Add(b); cost += entads.Contains(b) ? 1000f : 3f; }
                    if (cost >= 1000f || cost >= bestCost) continue;
                    if (def.PlaceWorkers != null && !def.PlaceWorkers.All(pw => pw.AllowsPlacing(def, c, rot, map).Accepted)) continue;
                    bestCost = cost;
                    bestCell = c;
                    bestRot = rot;
                    bestRemove = new List<Thing>(remove);
                    if (cost == 0f) break;
                }
                if (bestCost == 0f) break;
            }
            if (!bestCell.IsValid) return false;
            // A replaced bookcase takes its books with it: books only ever turn up on shelves, never loose
            foreach (var b in bestRemove)
            {
                if (b.Destroyed) continue;
                if (b is Building_Bookcase shelf) shelf.GetDirectlyHeldThings().ClearAndDestroyContents();
                b.Destroy();
            }
            foreach (var o in GenAdj.OccupiedRect(bestCell, bestRot, def.size))
                foreach (var item in o.GetThingList(map).Where(x => x.def.category == ThingCategory.Item).ToList())
                    item.Destroy();
            GenSpawn.Spawn(t, bestCell, map, bestRot, WipeMode.Vanish);
            roomBuildingsCache = null;
            return true;
        }

        List<Thing> roomBuildingsCache;
        CellRect roomBuildingsRect;

        List<Thing> RoomBuildings(FurnishedRoom room)
        {
            if (roomBuildingsCache != null && roomBuildingsRect == room.rect) return roomBuildingsCache;
            roomBuildingsRect = room.rect;
            roomBuildingsCache = room.rect.Cells.Select(c => (Thing)c.GetEdifice(map)).Where(b => b != null && IsFurniture(b)).Distinct().ToList();
            return roomBuildingsCache;
        }

        static bool IsFurniture(Thing b) =>
            !(b is Building_Door) && b.def != DungeonDefOf.DMAD_DungeonWall && b.def != DungeonDefOf.DMAD_DungeonWallPowered;

        /// <summary>Cost of clearing one cell, adding what has to go to `remove`. Walls and doors make it unusable.</summary>
        float Blockers(IntVec3 c, List<RoomUse> role, List<Thing> remove)
        {
            float cost = 0f;
            foreach (var x in c.GetThingList(map))
            {
                if (entads.Contains(x)) return 1000f;
                if (x.def.category == ThingCategory.Item) { cost += 1f; continue; }
                if (x.def.category != ThingCategory.Building || x.def.building == null) continue;
                if (!IsFurniture(x)) return 1000f;
                if (remove.Contains(x)) continue;
                remove.Add(x);
                var other = DungeonRoomMatch.UsesFor(x.def);
                cost += role.Count > 0 && other.Count > 0 && role[0] == other[0] ? 1f : 3f;
            }
            return cost;
        }
    }

    /// <summary>
    /// Which furnished rooms suit a building, read from what kind of thing its def is (bed, seat, eating surface,
    /// work table and what its recipes train, storage, power, facility) rather than from a list of defNames, so
    /// modded furniture finds a room the same way vanilla's does. Most suitable first; empty when nothing matches.
    /// </summary>
    public static class DungeonRoomMatch
    {
        static readonly Dictionary<ThingDef, List<RoomUse>> cache = new Dictionary<ThingDef, List<RoomUse>>();

        public static List<RoomUse> UsesFor(ThingDef def)
        {
            if (def == null) return new List<RoomUse>();
            if (!cache.TryGetValue(def, out var uses)) cache[def] = uses = Compute(def, 0);
            return uses;
        }

        static bool Is<T>(ThingDef d) => d.thingClass != null && typeof(T).IsAssignableFrom(d.thingClass);

        static List<RoomUse> Compute(ThingDef d, int depth)
        {
            var u = new List<RoomUse>();
            var b = d.building;
            if (b == null) return u;
            if (d.IsBed)
            {
                if (b.bed_defaultMedical) u.Add(RoomUse.Hospital);
                u.Add(RoomUse.Barracks);
                if (b.bed_humanlike) u.Add(RoomUse.PrisonCell);
                if (!b.bed_defaultMedical) u.Add(RoomUse.Hospital);
                return u;
            }
            if (Is<Building_Throne>(d)) u.Add(RoomUse.ThroneRoom);
            if (Is<Building_Bookcase>(d)) u.Add(RoomUse.Library);
            else if (Is<Building_Storage>(d)) u.Add(RoomUse.Storage);
            if (Is<Building_FermentingBarrel>(d)) u.Add(RoomUse.Brewery);
            if (d.GetCompProperties<CompProperties_Battery>() != null) u.Add(RoomUse.BatteryRoom);
            else if (d.GetCompProperties<CompProperties_Power>() is CompProperties_Power p && p.PowerConsumption < 0f) u.Add(RoomUse.BatteryRoom);
            var facility = d.GetCompProperties<CompProperties_Facility>();
            if (facility?.linkableBuildings != null && depth == 0)
                foreach (var linked in facility.linkableBuildings)
                    foreach (var use in Compute(linked, depth + 1))
                        if (!u.Contains(use)) u.Add(use);
            if (Is<Building_WorkTable>(d))
            {
                bool cooking = d.AllRecipes.Any(r => r.workSkill == SkillDefOf.Cooking);
                bool drugs = d.AllRecipes.Any(r => r.ProducedThingDef?.IsDrug ?? false);
                if (cooking) u.Add(RoomUse.Kitchen);
                if (drugs) u.Add(RoomUse.Brewery);
                u.Add(RoomUse.Workshop);
            }
            if (Is<Building_ResearchBench>(d)) { u.Add(RoomUse.Workshop); u.Add(RoomUse.Library); }
            if (d.surfaceType == SurfaceType.Eat) { u.Add(RoomUse.DiningRoom); u.Add(RoomUse.Kitchen); }
            if (b.isSittable) { u.Add(RoomUse.DiningRoom); u.Add(RoomUse.Library); u.Add(RoomUse.ThroneRoom); }
            if (b.joyKind != null) { u.Add(RoomUse.Library); u.Add(RoomUse.DiningRoom); }
            return u.Distinct().ToList();
        }
    }
}
