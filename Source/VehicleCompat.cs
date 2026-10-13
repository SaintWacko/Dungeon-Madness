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
        private static Type vehiclePawnType, vehicleCaravanType;
        private static MethodInfo removePawn;

        private static void Init()
        {
            if (initialized) return;
            initialized = true;
            vehiclePawnType = GenTypes.GetTypeInAnyAssembly("Vehicles.VehiclePawn");
            vehicleCaravanType = GenTypes.GetTypeInAnyAssembly("Vehicles.World.VehicleCaravan");
            if (vehiclePawnType != null)
                removePawn = AccessTools.Method(vehiclePawnType, "RemovePawn", new[] { typeof(Pawn) });
            if (vehiclePawnType != null && removePawn == null)
                Log.Warning("[Dungeon Madness] Vehicle Framework found, but VehiclePawn.RemovePawn(Pawn) is missing: passengers cannot be taken out of vehicles at dungeon entrances.");
        }

        public static bool Active { get { Init(); return vehiclePawnType != null; } }

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

        /// <summary>Puts vehicles back into a caravan. A vehicle caravan takes them directly; a plain one is remade
        /// with them, which VF turns into a vehicle caravan. Returns the caravan that now holds everyone.</summary>
        public static Caravan AddVehicles(Caravan caravan, List<Pawn> vehicles)
        {
            if (vehicles.NullOrEmpty()) return caravan;
            if (IsVehicleCaravan(caravan))
            {
                foreach (var v in vehicles) caravan.AddPawn(v, addCarriedPawnToWorldPawnsIfAny: true);
                return caravan;
            }
            var all = caravan.pawns.InnerListForReading.ToList();
            all.AddRange(vehicles);
            Caravan made = CaravanMaker.MakeCaravan(all, caravan.Faction, caravan.Tile, addToWorldPawnsIfNotAlready: true);
            if (!caravan.Destroyed && caravan.pawns.Count == 0) caravan.Destroy();
            return made;
        }
    }
}
