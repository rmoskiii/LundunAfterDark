using UnityEngine;

/// G8.2a: the ONE place that answers "is James inside a car or van, and in which seat?". The radio and the live police
/// feed use only this check, so a passenger seat added later only has to report through here.
///
/// Eligible vehicles: a CarController that is a car or van. Not a bus (LundunTrafficCar.IsBus: James can drive one, it's
/// a CarController) and not a police car (PoliceAI). Hire bikes (LundunBike), the Wheel and the flyable helicopter
/// (LundunFlyHeli) never set VehicleSystem.CurrentCar, so they are never "in a car or van".
///
/// Seat: Driver today. There is no passenger-seat state for James in the game, so Passenger is supported by the type
/// but never reported (PENDING: nothing here invents one).
public static class LundunOccupancy
{
    public enum Seat { None, Driver, Passenger }

    static VehicleSystem vehicles;

    public static bool InCarOrVan(out CarController car, out Seat seat)
    {
        string why;
        return InCarOrVan(out car, out seat, out why);
    }

    /// why: "" when true, otherwise a short reason for the log ("on foot", "bus", "police car").
    public static bool InCarOrVan(out CarController car, out Seat seat, out string why)
    {
        car = null; seat = Seat.None; why = "on foot";
        if (vehicles == null) vehicles = Object.FindAnyObjectByType<VehicleSystem>();
        if (vehicles == null) { why = "no city"; return false; }
        CarController c = vehicles.CurrentCar;
        if (c == null) return false;                                   // on foot, on a bike, on the Wheel, flying, riding a bus or Tube
        if (!IsCarOrVan(c, out why)) return false;
        car = c; seat = Seat.Driver; why = "";
        return true;
    }

    /// The vehicle-type check on its own (used by the occupancy check and the tests).
    public static bool IsCarOrVan(CarController c, out string why)
    {
        why = "";
        if (c == null) { why = "on foot"; return false; }
        LundunTrafficCar t = c.GetComponent<LundunTrafficCar>();
        if (t != null && t.IsBus) { why = "bus"; return false; }
        if (c.GetComponent<PoliceAI>() != null) { why = "police car"; return false; }
        return true;
    }

    /// A city scene (re)loaded: look the VehicleSystem up again.
    public static void Forget() { vehicles = null; }
}
