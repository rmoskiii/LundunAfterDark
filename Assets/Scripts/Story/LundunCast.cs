using System.Collections.Generic;
using UnityEngine;

/// S2: how one of the story's people looks. Built from one of the city's people models but made their own, so they never
/// look like someone in the crowd: their clothes recoloured (a flat colour that keeps the folds, or a tint over the
/// original), their skin tone, their hair colour, and their build (height and width).
public class LundunLook
{
    public struct Part { public string match; public Color colour; public bool flat, hide; }

    public readonly string who, model;           // model: part of the people prefab's name ("Casual_2" = Ped_Casual_2)
    public float height = 1f, width = 1f;
    public readonly List<Part> parts = new List<Part>();

    public LundunLook(string who, string model) { this.who = who; this.model = model; }

    /// A flat colour in place of the clothing's own picture (its shading and folds stay).
    public LundunLook Paint(string match, string hex) { parts.Add(new Part { match = match.ToLowerInvariant(), colour = Hex(hex), flat = true }); return this; }
    /// A colour over the original (skin, hair, denim): darker or warmer, its detail kept.
    public LundunLook Tint(string match, string hex) { parts.Add(new Part { match = match.ToLowerInvariant(), colour = Hex(hex) }); return this; }
    /// Not worn (e.g. a builder's hard hat).
    public LundunLook Hide(string match) { parts.Add(new Part { match = match.ToLowerInvariant(), hide = true }); return this; }
    public LundunLook Build(float h, float w) { height = h; width = w; return this; }

    static Color Hex(string h) { Color c; return ColorUtility.TryParseHtmlString("#" + h.TrimStart('#'), out c) ? c : Color.white; }
}

/// S2: the cast: who looks like what, and making them.
public static class LundunCast
{
    static Dictionary<string, LundunLook> looks;

    static Dictionary<string, LundunLook> Looks
    {
        get
        {
            if (looks != null) return looks;
            looks = new Dictionary<string, LundunLook>();
            // James's older brother: James's light-brown skin, a maroon hoodie, grey joggers. Tall and broad.
            Add(new LundunLook("MARCUS", "Street_1").Build(1.04f, 1.12f)
                .Tint("Body", "d4c1b4").Paint("Hoody", "7a2230").Paint("Pants", "45484d"));
            // James's best friend: dark brown skin, a green tee, grey shorts. Tall and slim.
            Add(new LundunLook("TAYO", "Street_3").Build(1.02f, 0.95f)
                .Tint("Body", "9a7d6a").Paint("Shirt", "1f7a4d").Tint("Shorts", "8a8f96").Tint("Hair", "1a1410"));
            // James's cousin: brown skin, dark hair, a purple co-ord. A little shorter than James.
            Add(new LundunLook("KEMI", "Casual_2").Build(0.97f, 1f)
                .Tint("Body", "a58873").Paint("Cloth", "6a3fa0").Tint("Hair", "2a1c16"));
            // owns LAGOS FOODS: dark brown skin, greying hair, an ochre shirt, brown trousers. Short and stocky.
            Add(new LundunLook("UNCLE FEMI", "Work_1").Build(0.96f, 1.2f)
                .Hide("Helmet").Paint("Vest", "b5762b").Paint("Shirt", "b5762b").Tint("Body", "86705f").Paint("Pants", "3b3029").Tint("Hair", "555555"));
            // James's girlfriend, a nurse: light brown skin, dark hair, blue scrubs.
            Add(new LundunLook("LEAH", "Suit_2").Build(0.98f, 0.97f)
                .Tint("Body", "e1d1c3").Paint("Coat", "3a7fa8").Paint("Shirt", "3a7fa8").Paint("Pants", "3a7fa8").Tint("Hair", "2a1d18"));
            // (later passes) Canary Quay money: a charcoal suit, a burgundy tie. Tall and lean.
            Add(new LundunLook("HOLLIS", "Suit_1").Build(1.05f, 0.97f)
                .Paint("Suit", "2a2c30").Paint("Pants", "2a2c30").Paint("Tie", "6b1424"));
            return looks;
        }
    }

    static void Add(LundunLook l) { looks[l.who] = l; }

    public static bool Has(string who) { return who != null && Looks.ContainsKey(who); }
    public static LundunLook Look(string who) { LundunLook l; return who != null && Looks.TryGetValue(who, out l) ? l : null; }

    /// A new, switched-off person dressed as 'who', under parent. Never killed (hits make them flinch); not a pedestrian
    /// (no wandering brain, so the crowd, the aim assist and punches leave them alone). Null if the city has no people.
    public static GameObject Make(string who, Transform parent)
    {
        LundunLook look = Look(who);
        LundunPopulation pop = LundunPopulation.Instance;
        if (look == null || pop == null) return null;
        GameObject go = pop.NewPerson(parent, look.model);
        if (go == null) return null;
        if (!go.name.Contains(look.model)) Debug.LogWarning("[Lundun] No Ped_" + look.model + " in the people list: " + who + " is wearing " + go.name + " instead.");
        go.name = "Cast_" + who;
        NPCBrain brain = go.GetComponent<NPCBrain>();
        if (brain != null) Object.DestroyImmediate(brain);
        Health h = go.GetComponent<Health>();
        if (h != null) h.Invulnerable = true;
        Dress(go, look);
        Vector3 s = go.transform.localScale;
        go.transform.localScale = new Vector3(s.x * look.width, s.y * look.height, s.z * look.width);
        foreach (Animator a in go.GetComponentsInChildren<Animator>(true)) a.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        return go;
    }

    /// Recolour each part (its own copies of the materials, so nobody else's change).
    static void Dress(GameObject go, LundunLook look)
    {
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            string n = r.gameObject.name.ToLowerInvariant();
            if (n.Contains("eyelash")) continue;
            foreach (LundunLook.Part p in look.parts)
            {
                if (!n.Contains(p.match)) continue;
                if (p.hide) { r.enabled = false; break; }
                Material[] ms = r.materials;
                foreach (Material m in ms)
                {
                    if (m == null) continue;
                    if (p.flat)
                    {
                        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", Texture2D.whiteTexture);
                        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", Texture2D.whiteTexture);
                    }
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", p.colour);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", p.colour);
                }
                r.materials = ms;
                break;
            }
        }
    }
}
