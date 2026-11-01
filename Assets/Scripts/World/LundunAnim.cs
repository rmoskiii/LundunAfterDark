using System.Collections.Generic;
using UnityEngine;

/// Safe animator calls: only touch parameters the character's controller actually has
/// (so old characters without Punch/Shoot etc. don't spam warnings).
public static class LundunAnim
{
    static readonly Dictionary<RuntimeAnimatorController, HashSet<string>> known = new Dictionary<RuntimeAnimatorController, HashSet<string>>();

    static bool Has(Animator a, string p)
    {
        if (a == null || a.runtimeAnimatorController == null || !a.isActiveAndEnabled) return false;
        HashSet<string> set;
        if (!known.TryGetValue(a.runtimeAnimatorController, out set))
        {
            set = new HashSet<string>();
            foreach (AnimatorControllerParameter ap in a.parameters) set.Add(ap.name);
            known[a.runtimeAnimatorController] = set;
        }
        return set.Contains(p);
    }

    public static void Trigger(Animator a, string p) { if (Has(a, p)) a.SetTrigger(p); }
    public static void Bool(Animator a, string p, bool v) { if (Has(a, p)) a.SetBool(p, v); }
    public static void Float(Animator a, string p, float v, float damp = 0f)
    {
        if (!Has(a, p)) return;
        if (damp > 0f) a.SetFloat(p, v, damp, Time.deltaTime); else a.SetFloat(p, v);
    }
}
