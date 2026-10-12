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
        public readonly List<FurnishedRoom> rooms = new List<FurnishedRoom>();
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
        }

        public static TechLevel ColonyTech => Faction.OfPlayer?.def?.techLevel ?? TechLevel.Industrial;

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
            TechLevel tech = ColonyTech;
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
            Thing t = EntadApi.GenerateEntadItem(DungeonsMod.Settings.entadPointsPerLevel * level, Request());
            if (t != null)
            {
                site?.RegisterEntad(t);
                entads.Add(t);
            }
            return t;
        }

        public void QueueFurniture(Thing t) => furnitureQueue.Add(t);

        /// <summary>`count` loot slots worth about `valuePerSlot` each. Entad furniture is queued for a room and
        /// not returned, so a slot that rolled one is simply missing from the pile.</summary>
        public List<Thing> MakeLoot(int count, float valuePerSlot)
        {
            var result = new List<Thing>();
            int ordinary = 0;
            for (int i = 0; i < count; i++)
            {
                Thing entad = Rand.Chance(DungeonsMod.Settings.entadChance) ? MakeEntad() : null;
                if (entad == null) ordinary++;
                else if (entad.def.category == ThingCategory.Building) furnitureQueue.Add(entad);
                else result.Add(entad);
            }
            if (ordinary > 0)
            {
                float value = ordinary * valuePerSlot;
                var parms = new ThingSetMakerParams
                {
                    techLevel = ColonyTech,
                    totalMarketValueRange = new FloatRange(value * 0.8f, value * 1.2f),
                    countRange = new IntRange(1, ordinary),
                };
                result.AddRange(ThingSetMakerDefOf.Reward_ItemsStandard.root.Generate(parms));
            }
            return result;
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
            foreach (var ch in layout.data.chambers)
            {
                if (ch.id == 0 || !Rand.Chance(s.lootChance)) continue;
                if (!byChamber.TryGetValue(ch.id, out var cells)) continue;
                cells = cells.Where(c => c.Standable(map) && c.GetFirstItem(map) == null && !c.GetTerrain(map).IsWater).ToList();
                if (cells.Count == 0) continue;
                int n = Rand.RangeInclusive(2, 4);
                float value = s.lootValuePerLevel * level * Rand.Range(0.75f, 1.25f);
                var loot = MakeLoot(n, value / n);
                IntVec3 center = cells.RandomElement();
                var spots = cells.OrderBy(c => c.DistanceToSquared(center)).ToList();
                int i = 0;
                foreach (var t in loot)
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
            foreach (var b in bestRemove)
                if (!b.Destroyed) b.Destroy();
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
