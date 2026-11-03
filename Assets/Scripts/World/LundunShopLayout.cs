// S3: where things are inside LAGOS FOODS, shared by the generator (Lundun → 13, which builds it) and the game (which
// opens and shuts it and puts people in it). Pure C#, no UnityEngine, so the generator can be tested outside Unity.
// Local frame: x along the shop front (+x = the door side), z in from the front face, measured from the middle of
// the unit's front. W = the unit's width, D = the building's depth (both saved on the place: car.x and car.z).
public static class LundunShopLayout
{
    public const double FloorY = 0.15;                // the shop floor: level with the pavement
    public const double CeilingY = 3.4;
    public const double GroundFloorTop = 4.0;         // the building kit's ground floor (cut away above the shop)
    public const double Partition = 7.2;              // the shop floor runs back to here, then the back room

    public static double DoorX(double W) { return W / 2 - 1.15; }       // door centre
    public const double DoorHalf = 0.7;
    public const double DoorTop = 2.45;
    public static double WindowX0(double W) { return -W / 2 + 0.3; }
    public static double WindowX1(double W) { return W / 2 - 2.05; }
    public const double WindowY0 = 0.6, WindowY1 = 2.75;

    public static double CounterX0(double W) { return W / 2 - 1.8; }
    public static double CounterX1(double W) { return W / 2 - 1.2; }
    public const double CounterZ0 = 2.0, CounterZ1 = 4.4, CounterTop = 1.0;

    // where people stand: x, z, and which way they face (degrees from the +z "into the shop" direction, + = towards +x)
    public static double[] Behind(double W) { return new[] { W / 2 - 0.65, 3.2, -90.0 }; }          // the shopkeeper
    public static double[] AtCounter(double W) { return new[] { W / 2 - 2.35, 3.2, 90.0 }; }         // a customer
    public static double[] ByFridge(double W) { return new[] { -W / 2 + 1.5, 6.0, 180.0 }; }         // facing the door
    public static double[] InAisle(double W) { return new[] { -W / 2 + 1.4, 3.6, 90.0 }; }
    public static double[] Inside(double W) { return new[] { DoorX(W) - 0.3, 1.2, 0.0 }; }           // just in from the door
    public static double[] BackRoom(double W) { return new[] { 0.9, 8.6, 180.0 }; }
}
