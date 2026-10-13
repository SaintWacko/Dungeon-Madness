using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace DungeonMadness
{
    /// <summary>
    /// Soft support for Vehicle Framework, found by type name at runtime so the mod neither references nor requires
    /// it. Vehicles never go into a dungeon. What VF does that matters here (read from its 1.6 assembly):
    /// - A vehicle is a Pawn subclass (Vehicles.VehiclePawn) and sits in the caravan's pawn list like any member.
    /// - Passengers are held by the vehicle's role handlers, not by the caravan, though VF's PawnsListForReading patch
    ///   still lists them. Caravan.RemovePawn does nothing to one; the vehicle's own RemovePawn(Pawn) takes them out
    ///   (and also takes a pawn out of the vehicle's inventory).
    /// - Cargo is the vehicle's ordinary pawn inventory, so vanilla's caravan inventory helpers already see it.
    /// - Caravan.AddPawn(vehicle) on a plain caravan swaps it for a vehicle caravan keeping only one vehicle, so
    ///   vehicles are merged back by remaking the caravan with CaravanMaker.MakeCaravan, which VF turns into a
    ///   vehicle caravan whenever the list holds a vehicle.
    /// </summary>
    public static class VehicleCompat
    {
        private static bool initialized;
        private static Type vehiclePawnType, vehicleCaravanType, stashedVehicleType;
        private static MethodInfo removePawn, distribute, stashArrived;
        private static WorldObjectDef stashedVehicleDef;

        private static void Init()
        {
            if (initialized) return;
            initialized = true;
            vehiclePawnType = GenTypes.GetTypeInAnyAssembly("Vehicles.VehiclePawn");
            vehicleCaravanType = GenTypes.GetTypeInAnyAssembly("Vehicles.World.VehicleCaravan");
            if (vehiclePawnType != null)
            {
                removePawn = AccessTools.Method(vehiclePawnType, "RemovePawn", new[] { typeof(Pawn) });
                // RoleHelper.Distribute(in List<VehiclePawn> vehicles, List<Pawn> pawns): seats people in vehicles,
                // drivers first; VF's own unstash calls it before rebuilding a caravan
                Type roleHelper = GenTypes.GetTypeInAnyAssembly("Vehicles.RoleHelper");
                Type vehicleList = typeof(List<>).MakeGenericType(vehiclePawnType);
                if (roleHelper != null)
                    distribute = AccessTools.Method(roleHelper, "Distribute", new[] { vehicleList.MakeByRefType(), typeof(List<Pawn>) });
                // VF's own stash on the world map (Vehicles.World.StashedVehicle): an IThingHolder world object whose
                // Notify_CaravanArrived(Caravan) seats the caravan in its vehicles and returns the new vehicle caravan
                stashedVehicleType = GenTypes.GetTypeInAnyAssembly("Vehicles.World.StashedVehicle");
                stashedVehicleDef = DefDatabase<WorldObjectDef>.GetNamedSilentFail("StashedVehicle");
                if (stashedVehicleType != null)
                    stashArrived = AccessTools.Method(stashedVehicleType, "Notify_CaravanArrived", new[] { typeof(Caravan) });
                if (stashedVehicleDef != null && (stashedVehicleType == null || !stashedVehicleType.IsAssignableFrom(stashedVehicleDef.worldObjectClass)
                    || !typeof(IThingHolder).IsAssignableFrom(stashedVehicleDef.worldObjectClass) || stashArrived == null))
                    stashedVehicleDef = null;
            }
            if (vehiclePawnType != null && removePawn == null)
                Log.Warning("[Dungeon Madness] Vehicle Framework found, but VehiclePawn.RemovePawn(Pawn) is missing: passengers cannot be taken out of vehicles at dungeon entrances.");
        }

        public static bool Active { get { Init(); return vehiclePawnType != null; } }

        /// <summary>Whether riders can be taken out of a vehicle. False only when VF is loaded but its API has changed;
        /// the enter dialog then refuses a party that includes a rider.</summary>
        public static bool CanDisembark { get { Init(); return removePawn != null; } }

        public static bool IsVehicle(Pawn p)
        {
            Init();
            return p != null && vehiclePawnType != null && vehiclePawnType.IsInstanceOfType(p);
        }

        public static bool IsVehicleCaravan(Caravan c)
        {
            Init();
            return c != null && vehicleCaravanType != null && vehicleCaravanType.IsInstanceOfType(c);
        }

        /// <summary>The vehicle whose inventory (cargo) holds this thing, or null.</summary>
        public static Pawn VehicleHolding(Thing t) =>
            t?.holdingOwner?.Owner is Pawn_InventoryTracker inv && IsVehicle(inv.pawn) ? inv.pawn : null;

        /// <summary>A caravan member who is not held by the caravan itself: riding a vehicle or stowed in one.</summary>
        public static bool IsAboard(Pawn p, Caravan caravan) =>
            Active && !IsVehicle(p) && p.holdingOwner != null && p.holdingOwner != caravan.pawns;

        /// <summary>Takes these pawns out of whatever vehicle in the caravan holds them, leaving them in no container,
        /// ready for CaravanMaker.MakeCaravan or Caravan.AddPawn. Pawns not aboard are left alone.</summary>
        public static void Disembark(Caravan caravan, IEnumerable<Pawn> pawns)
        {
            if (!Active || removePawn == null) return;
            var vehicles = caravan.PawnsListForReading.Where(IsVehicle).ToList();
            if (vehicles.Count == 0) return;
            foreach (var p in pawns.ToList())
            {
                if (!IsAboard(p, caravan)) continue;
                foreach (var v in vehicles)
                    if (removePawn.Invoke(v, new object[] { p }) is bool removed && removed) break;
            }
        }

        /// <summary>Stashes vehicles on the world map the way VF's own "stash vehicle" does, so any caravan can recover
        /// them. The timeout is off: the party collects them on the way out (DungeonSite starts it if the party is
        /// lost). Vehicles it could not take are returned in notStashed for the caller to keep; null comes back, with
        /// every vehicle in notStashed, when VF's stash cannot be made at all.</summary>
        public static WorldObject StashVehicles(PlanetTile tile, List<Pawn> vehicles, out List<Pawn> notStashed)
        {
            Init();
            notStashed = new List<Pawn>();
            if (vehicles.NullOrEmpty()) return null;
            if (stashedVehicleDef == null) { notStashed.AddRange(vehicles); return null; }
            WorldObject stash = WorldObjectMaker.MakeWorldObject(stashedVehicleDef);
            stash.Tile = tile;
            stash.SetFaction(Faction.OfPlayer);
            stash.GetComponent<TimeoutComp>()?.StopTimeout();
            ThingOwner held = ((IThingHolder)stash).GetDirectlyHeldThings();
            foreach (var v in vehicles)
            {
                // The stash deep-saves its vehicles, as VF's does, so they leave world pawns
                bool wasWorldPawn = Find.WorldPawns.Contains(v);
                if (wasWorldPawn) Find.WorldPawns.RemovePawn(v);
                if (!held.TryAdd(v, canMergeWithExistingStacks: false))
                {
                    if (wasWorldPawn) Find.WorldPawns.PassToWorld(v);
                    notStashed.Add(v);
                }
            }
            // VF's stash draws its largest vehicle, so an empty one must never reach the map
            if (held.Count == 0) return null;
            Find.WorldObjects.Add(stash);
            return stash;
        }

        public static bool StashStillThere(WorldObject stash) => stash != null && stash.Spawned && !stash.Destroyed;

        /// <summary>The caravan collects the stashed vehicles, seated by VF. Returns the caravan that now holds
        /// everyone: VF's new vehicle caravan, or the same caravan if the stash is gone or VF refused.</summary>
        public static Caravan RetrieveStash(WorldObject stash, Caravan caravan)
        {
            if (caravan == null || !StashStillThere(stash) || stashArrived == null) return caravan;
            try
            {
                return stashArrived.Invoke(stash, new object[] { caravan }) as Caravan ?? caravan;
            }
            catch (Exception e)
            {
                Log.Warning("[Dungeon Madness] Could not hand stashed vehicles back to the party; they stay stashed: " + (e.InnerException ?? e).Message);
                return caravan.Destroyed ? null : caravan;
            }
        }

        /// <summary>Starts the stash's timeout as VF's own stash would: 15 to 30 days by the largest vehicle's size.</summary>
        public static void StartStashTimeout(WorldObject stash)
        {
            if (!StashStillThere(stash)) return;
            var timeout = stash.GetComponent<TimeoutComp>();
            if (timeout == null || timeout.Active) return;
            float size = ((IThingHolder)stash).GetDirectlyHeldThings().OfType<Pawn>().Where(IsVehicle)
                .Select(v => v.def.Size.Magnitude).DefaultIfEmpty(1f).Max();
            float days = UnityEngine.Mathf.Lerp(15f, 30f, UnityEngine.Mathf.InverseLerp(1f, 10f, size));
            timeout.StartTimeout(UnityEngine.Mathf.CeilToInt(days * GenDate.TicksPerDay));
        }

        /// <summary>Puts vehicles back into a caravan. A vehicle caravan takes them directly; a plain one is remade
        /// with them, which VF turns into a vehicle caravan. Returns the caravan that now holds everyone.</summary>
        public static Caravan AddVehicles(Caravan caravan, List<Pawn> vehicles)
        {
            if (vehicles.NullOrEmpty()) return caravan;
            // A stashed vehicle was taken out of world pawns so the stash's deep save owns it; a caravan only saves
            // references, so it has to be passed back to the world when it rejoins one
            if (IsVehicleCaravan(caravan))
            {
                foreach (var v in vehicles)
                {
                    caravan.AddPawn(v, addCarriedPawnToWorldPawnsIfAny: true);
                    if (!v.IsWorldPawn()) Find.WorldPawns.PassToWorld(v);
                }
                return caravan;
            }
            // VF's own unstash (StashedVehicle): empty the caravan, seat its people in the vehicles, then make a vehicle
            // caravan of people and vehicles together
            Faction faction = caravan.Faction;
            PlanetTile tile = caravan.Tile;
            var people = caravan.pawns.InnerListForReading.ToList();
            caravan.RemoveAllPawns();
            if (distribute != null)
            {
                var typed = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(vehiclePawnType));
                foreach (var v in vehicles) typed.Add(v);
                try { distribute.Invoke(null, new object[] { typed, people }); }
                catch (Exception e) { Log.Warning("[Dungeon Madness] Could not seat the party in their vehicles; they will have to board by hand: " + e.InnerException?.Message); }
            }
            var all = people.Concat(vehicles).ToList();
            Caravan made = CaravanMaker.MakeCaravan(all, faction, tile, addToWorldPawnsIfNotAlready: true);
            if (!caravan.Destroyed) caravan.Destroy();
            return made;
        }
    }
}
