using System;
using System.Collections.Generic;
using UnityEngine;

/// E0: every place the expansions add to the map, in one list. The GPS (LundunNavigation), the minimap, the full map
/// and its PLACES list all read from here, so a new station, landmark or shop only has to be registered once
/// (by the menu that builds it) to be findable and routable in game.
///   kind   decides the icon, colour, filter chip and card wording (see Kinds below; new kinds go there)
///   position  the street entrance: where SET ROUTE takes you
///   label  big landmarks also get their name written on the map
/// Lives on the "LundunMapRegistry" object (made by Lundun → 26).
public class LundunMapRegistry : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public string id, name, kind;
        public Vector3 position;
        public bool label;
    }

    public static LundunMapRegistry Instance { get; private set; }

    [SerializeField] private List<Entry> entries = new List<Entry>();
    private readonly Dictionary<string, Transform> markers = new Dictionary<string, Transform>();

    public IReadOnlyList<Entry> Entries { get { return entries; } }

    void Awake()
    {
        Instance = this;
        foreach (Entry e in entries)
        {
            GameObject g = new GameObject("Map_" + e.id);
            g.transform.SetParent(transform, false);
            g.transform.position = e.position;
            markers[e.id] = g.transform;
        }
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// A marker object at the entry's position (the minimap follows transforms).
    public Transform Marker(string id) { Transform t; return markers.TryGetValue(id, out t) ? t : null; }

    /// Adds or replaces an entry (used by the editor menus).
    public void Set(string id, string name, string kind, Vector3 position, bool label)
    {
        entries.RemoveAll(e => e.id == id);
        entries.Add(new Entry { id = id, name = name, kind = kind, position = position, label = label });
        entries.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
    }

    public void Remove(string id) { entries.RemoveAll(e => e.id == id); }

    // ------------------------------------------------------------------ kinds: how each sort of place looks on the maps
    public class Kind
    {
        public string chip, card, letter;
        public Color colour;
        public Kind(string chip, string card, string letter, Color colour) { this.chip = chip; this.card = card; this.letter = letter; this.colour = colour; }
    }

    /// The kinds the expansions use. Built-in kinds (safehouse, hospital, police, tube, bike, landmark, mission)
    /// keep their own look in LundunFullMap; these fill in the rest.
    public static readonly Dictionary<string, Kind> Kinds = new Dictionary<string, Kind>
    {
        { "rail",     new Kind("RAIL",       "Lundun Rail station", "R", new Color(0.17f, 0.55f, 0.38f)) },
        { "shop",     new Kind("SHOPS",      "Shop",                "£", new Color(0.93f, 0.42f, 0.62f)) },
        { "market",   new Kind("MARKETS",    "Market",              "M", new Color(1f, 0.55f, 0.18f)) },
        { "venue",    new Kind("NIGHTLIFE",  "Venue",               "V", new Color(0.62f, 0.4f, 0.95f)) },
        { "attraction", new Kind("ATTRACTIONS", "Attraction",       "A", new Color(0.98f, 0.86f, 0.35f)) },
        { "homestore", new Kind("HOME STORE", "Home store",         "F", new Color(1f, 0.42f, 0.04f)) },       // E6: FLATPAK
    };

    public static bool Known(string kind) { return kind != null && Kinds.ContainsKey(kind); }
    public static Color Colour(string kind) { Kind k; return kind != null && Kinds.TryGetValue(kind, out k) ? k.colour : new Color(0.92f, 0.92f, 0.92f); }
    public static string Letter(string kind) { Kind k; return kind != null && Kinds.TryGetValue(kind, out k) ? k.letter : null; }
    public static string ChipName(string kind) { Kind k; return kind != null && Kinds.TryGetValue(kind, out k) ? k.chip : "LANDMARKS"; }
    public static string CardName(string kind) { Kind k; return kind != null && Kinds.TryGetValue(kind, out k) ? k.card : ""; }
}
