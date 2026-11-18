using UnityEngine;

/// Phase 3: the outfits one character can wear, as materials. Lundun → 35 fills it (on James). For each of his
/// materials that an outfit changes (a "slot": Ch06_body, Ch06_body1), the original and each outfit's version.
/// Wearing an outfit swaps any of those materials, wherever they're used under a model, for that outfit's one.
public class LundunOutfitSet : MonoBehaviour
{
    [System.Serializable]
    public class Slot
    {
        public Material original;
        public Material[] byOutfit = new Material[0];     // lined up with ids (null = this outfit leaves it as it is)
    }

    [SerializeField] private string[] ids = new string[0];
    [SerializeField] private Slot[] slots = new Slot[0];

    public string Wearing { get; private set; }

#if UNITY_EDITOR
    /// Lundun → 35.
    public void Set(string[] outfitIds, Slot[] outfitSlots) { ids = outfitIds; slots = outfitSlots; }
#endif

    /// This character has the outfit (Midnight, their own clothes, always).
    public bool Has(string id)
    {
        if (id == LundunOutfits.DefaultId) return true;
        int i = System.Array.IndexOf(ids, id);
        if (i < 0) return false;
        foreach (Slot s in slots) if (s != null && s.byOutfit != null && i < s.byOutfit.Length && s.byOutfit[i] != null) return true;
        return false;
    }

    /// The player's set (James's), if Lundun → 35 has been run.
    public static LundunOutfitSet OnPlayer
    {
        get
        {
            PlayerController pc = FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);   // (he's switched off while driving)
            return pc != null ? pc.GetComponent<LundunOutfitSet>() : null;
        }
    }

    /// Put the outfit on this character (body and first-person arms).
    public void Wear(string id)
    {
        if (!Has(id)) id = LundunOutfits.DefaultId;
        WearOn(gameObject, id);
        Wearing = id;
    }

    /// Put the outfit on any copy of this character (the select screen's preview).
    public void WearOn(GameObject root, string id)
    {
        if (root == null || slots == null) return;
        if (!Has(id)) id = LundunOutfits.DefaultId;
        int at = System.Array.IndexOf(ids, id);
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int m = 0; m < mats.Length; m++)
            {
                Slot s = SlotOf(mats[m]);
                if (s == null) continue;
                Material want = at >= 0 && s.byOutfit != null && at < s.byOutfit.Length && s.byOutfit[at] != null ? s.byOutfit[at] : s.original;
                if (want != null && want != mats[m]) { mats[m] = want; changed = true; }
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    Slot SlotOf(Material m)
    {
        if (m == null) return null;
        foreach (Slot s in slots)
        {
            if (s == null) continue;
            if (s.original == m) return s;
            if (s.byOutfit != null) foreach (Material o in s.byOutfit) if (o == m) return s;
        }
        return null;
    }
}
