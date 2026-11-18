using System.Collections.Generic;
using UnityEngine;

/// Phase 3: what a character wears (cosmetic only: no stats, nothing to buy).
///   catalogue   LundunOutfits.All: five colourways for James. Each one is a recolour of his own clothes baked into
///               textures (Assets/Characters/Outfits/<Model>_<File>_<set>.png): top, trousers, trainers and cap change;
///               skin, face, hands and headphones never do. Midnight is what he's always worn (his own textures).
///   wearing     LundunOutfitSet on the character holds the materials for each outfit (made by Lundun → 35) and swaps
///               them on every part of him that uses them: his body, his first-person arms, the select screen's preview.
///   choosing    on the character screen (Story: NEW STORY; Free Roam: every time), and at any safehouse door
///               (WARDROBE). Saved per mode by LundunCharacters, so Story and Free Roam keep their own.
/// Character (who) and outfit (what he wears) stay separate: a future character brings its own outfit set.
public static class LundunOutfits
{
    public const string DefaultId = "midnight";

    public class Outfit
    {
        public string id;          // saved
        public string name;        // shown, in capitals
        public string file;        // the texture name part: <Model>_<file>_1001.png ("" = the model's own textures)
        public Color swatch;       // the top's colour, for the swatch
        public string note;        // one line on the wardrobe
    }

    static List<Outfit> all;
    public static IList<Outfit> All
    {
        get
        {
            if (all == null)
                all = new List<Outfit>
                {
                    new Outfit { id = DefaultId, name = "MIDNIGHT",  file = "",         swatch = Hex("1b2233"), note = "Navy tracksuit, red and white trainers" },
                    new Outfit { id = "greymarl", name = "GREY MARL", file = "GreyMarl", swatch = Hex("8a8c90"), note = "Grey tracksuit, white trainers" },
                    new Outfit { id = "blackout", name = "BLACKOUT",  file = "Blackout", swatch = Hex("0c0c0e"), note = "All black, white soles" },
                    new Outfit { id = "olive",    name = "OLIVE",     file = "Olive",    swatch = Hex("4f5530"), note = "Olive top, black trousers, gum soles" },
                    new Outfit { id = "claret",   name = "CLARET",    file = "Claret",   swatch = Hex("6b1220"), note = "Claret top, charcoal trousers" },
                };
            return all;
        }
    }

    public static Outfit Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (Outfit o in All) if (o.id == id) return o;
        return null;
    }

    public static Outfit Default { get { return Find(DefaultId); } }

    static Color Hex(string h)
    {
        Color c;
        return ColorUtility.TryParseHtmlString("#" + h, out c) ? c : Color.grey;
    }
}
