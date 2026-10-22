using UnityEngine;

/// L5: the colours of Lundun's street lighting, by district.
///   Sodium    Peckam, Gilmore (and the ring roads): that deep orange of old London side streets
///   Heritage  Knightsbury's lanterns: warm white
///   LED       Canary Quay and the Peninsula: cool, slightly blue white
public static class LundunLampColours
{
    public static readonly Color Sodium = new Color(1f, 0.58f, 0.2f);
    public static readonly Color Heritage = new Color(1f, 0.84f, 0.6f);
    public static readonly Color LED = new Color(0.82f, 0.9f, 1f);
    /// Shop windows, pub windows and office lobbies spilling onto the pavement.
    public static readonly Color Shop = new Color(1f, 0.92f, 0.8f);
    public static readonly Color Pub = new Color(1f, 0.7f, 0.38f);
    public static readonly Color Lobby = new Color(0.86f, 0.92f, 1f);

    /// Lamp style (Generator.LampInfo.style) → light colour.
    public static Color ForStyle(int style) { return style == 2 ? Heritage : style == 3 ? LED : Sodium; }

    /// The ground-glow colour for a LightPool material by its name.
    public static Color Pool(string material)
    {
        if (material.EndsWith("_Heritage")) return Heritage;
        if (material.EndsWith("_LED")) return LED;
        if (material.EndsWith("_Shop")) return Shop;
        if (material.EndsWith("_Pub")) return Pub;
        if (material.EndsWith("_Lobby")) return Lobby;
        return new Color(1f, 0.66f, 0.32f);       // sodium (the old pool colour, a touch more orange)
    }
}
