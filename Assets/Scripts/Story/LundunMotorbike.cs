using UnityEngine;
using UnityEngine.AI;

/// Phase 5 (gate C1): a motorbike, made in code (no model needed): two spoked wheels, the frame, tank, seat, forks and
/// bars, a headlight and a tail light, and a Rider in a helmet on it, crouched over the bars. M06's Rye Lane Rider.
/// It's dressed over one of the city's cars (LundunPopulation.NewCar + LundunChaseCar), so it drives, chases, rams,
/// shoots, takes hits and burns like the chase cars do: the car's own body is hidden and its collider narrowed to a
/// bike's width. The bike leans into turns and its wheels turn with its speed; the rider's pose is set every frame on
/// top of his idle animation (legs over the tank, hands on the bars, leaning forward).
public class LundunMotorbike : MonoBehaviour
{
    Transform look, frontWheel, rearWheel, rider;
    Rigidbody body;
    Animator anim;
    Transform spine, lThigh, rThigh, lShin, rShin, lArm, rArm, lFore, rFore, head;
    float lean;

    /// Turn a city car (just made, switched on) into a motorbike with a rider. Returns the bike.
    public static LundunMotorbike Dress(GameObject car)
    {
        if (car == null) return null;
        // where the ground is under it (its wheels' lowest point), before its body's hidden
        float groundY = car.transform.position.y;
        bool any = false; Bounds b = new Bounds();
        foreach (Renderer r in car.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is LineRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            r.enabled = false;                                                       // the car's body: not seen
        }
        if (any) groundY = b.min.y;
        // a bike's width for bullets and bumps
        Vector3 ls = car.transform.lossyScale;
        foreach (BoxCollider bc in car.GetComponentsInChildren<BoxCollider>())
        {
            if (bc.isTrigger) continue;
            Vector3 sc = bc.transform.lossyScale;
            Vector3 s = bc.size, c = bc.center;
            s.x = 0.9f / Mathf.Max(0.01f, Mathf.Abs(sc.x));
            s.z = Mathf.Min(s.z, 2.2f / Mathf.Max(0.01f, Mathf.Abs(sc.z)));
            c.z = Mathf.Clamp(c.z, -0.4f / Mathf.Max(0.01f, Mathf.Abs(sc.z)), 0.4f / Mathf.Max(0.01f, Mathf.Abs(sc.z)));
            bc.size = s; bc.center = c;
        }
        LundunMotorbike m = car.AddComponent<LundunMotorbike>();
        m.Build(new Vector3(car.transform.position.x, groundY, car.transform.position.z), car.transform.rotation);
        car.name = "RiderBike";
        return m;
    }

    void Build(Vector3 ground, Quaternion rot)
    {
        body = GetComponent<Rigidbody>();
        GameObject root = new GameObject("BikeLook");
        look = root.transform;
        Material paint = Mat(new Color(0.75f, 0.32f, 0.08f), 0.7f);                  // Riders' orange
        Material black = Mat(new Color(0.04f, 0.04f, 0.045f), 0.35f);
        Material chrome = Mat(new Color(0.7f, 0.72f, 0.75f), 0.9f);
        Material rubber = Mat(new Color(0.02f, 0.02f, 0.02f), 0.15f);

        frontWheel = Wheel("FrontWheel", new Vector3(0f, 0.33f, 0.74f), rubber, chrome);
        rearWheel = Wheel("RearWheel", new Vector3(0f, 0.33f, -0.72f), rubber, chrome);
        Part(PrimitiveType.Cube, "Engine", new Vector3(0f, 0.52f, 0.02f), new Vector3(0.3f, 0.3f, 0.42f), Quaternion.identity, black);
        Part(PrimitiveType.Cube, "Tank", new Vector3(0f, 0.86f, 0.2f), new Vector3(0.32f, 0.22f, 0.5f), Quaternion.Euler(-8f, 0f, 0f), paint);
        Part(PrimitiveType.Cube, "Seat", new Vector3(0f, 0.86f, -0.32f), new Vector3(0.28f, 0.1f, 0.56f), Quaternion.Euler(4f, 0f, 0f), black);
        Part(PrimitiveType.Cube, "Tail", new Vector3(0f, 0.86f, -0.66f), new Vector3(0.22f, 0.12f, 0.3f), Quaternion.Euler(10f, 0f, 0f), paint);
        Part(PrimitiveType.Cube, "Frame", new Vector3(0f, 0.62f, 0.38f), new Vector3(0.08f, 0.08f, 0.5f), Quaternion.Euler(-35f, 0f, 0f), black);
        Part(PrimitiveType.Cube, "Swingarm", new Vector3(0f, 0.38f, -0.4f), new Vector3(0.1f, 0.06f, 0.66f), Quaternion.Euler(-6f, 0f, 0f), black);
        Part(PrimitiveType.Cylinder, "Exhaust", new Vector3(0.18f, 0.42f, -0.42f), new Vector3(0.08f, 0.3f, 0.08f), Quaternion.Euler(80f, 0f, 0f), chrome);
        foreach (float x in new[] { -0.09f, 0.09f })
            Part(PrimitiveType.Cylinder, "Fork", new Vector3(x, 0.66f, 0.66f), new Vector3(0.04f, 0.34f, 0.04f), Quaternion.Euler(-24f, 0f, 0f), chrome);
        Part(PrimitiveType.Cube, "Headstock", new Vector3(0f, 0.98f, 0.55f), new Vector3(0.2f, 0.16f, 0.12f), Quaternion.Euler(-20f, 0f, 0f), paint);
        Part(PrimitiveType.Cylinder, "Bars", new Vector3(0f, 1.06f, 0.5f), new Vector3(0.035f, 0.36f, 0.035f), Quaternion.Euler(0f, 0f, 90f), black);
        Part(PrimitiveType.Sphere, "Headlamp", new Vector3(0f, 0.92f, 0.66f), new Vector3(0.16f, 0.16f, 0.1f), Quaternion.identity, Glow(new Color(1f, 0.95f, 0.8f)));
        Part(PrimitiveType.Cube, "Taillamp", new Vector3(0f, 0.9f, -0.82f), new Vector3(0.12f, 0.05f, 0.03f), Quaternion.identity, Glow(new Color(1f, 0.1f, 0.06f)));
        Light beam = new GameObject("HeadlampLight").AddComponent<Light>();
        beam.transform.SetParent(look, false);
        beam.transform.localPosition = new Vector3(0f, 0.92f, 0.72f);
        beam.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
        beam.type = LightType.Spot; beam.range = 28f; beam.spotAngle = 55f; beam.intensity = 3f; beam.color = new Color(1f, 0.95f, 0.85f); beam.shadows = LightShadows.None;

        // the rider, in a helmet, crouched over the bars
        LundunPopulation pop = LundunPopulation.Instance;
        GameObject r = pop != null ? pop.NewPerson(null, "Street") : null;
        if (r != null)
        {
            NPCBrain br = r.GetComponent<NPCBrain>(); if (br != null) Destroy(br);
            NavMeshAgent ag = r.GetComponent<NavMeshAgent>(); if (ag != null) Destroy(ag);
            foreach (Collider c in r.GetComponentsInChildren<Collider>()) c.enabled = false;   // bullets hit the bike
            foreach (Rigidbody rb in r.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
            Health h = r.GetComponent<Health>(); if (h != null) h.Invulnerable = true;
            r.name = "Rider";
            rider = r.transform;
            rider.SetParent(look, false);
            rider.localPosition = new Vector3(0f, -0.12f, -0.3f);
            rider.localRotation = Quaternion.identity;
            r.SetActive(true);
            anim = r.GetComponentInChildren<Animator>();
            if (anim != null) anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            spine = Bone(HumanBodyBones.Spine, "Spine");
            lThigh = Bone(HumanBodyBones.LeftUpperLeg, "LeftUpLeg"); rThigh = Bone(HumanBodyBones.RightUpperLeg, "RightUpLeg");
            lShin = Bone(HumanBodyBones.LeftLowerLeg, "LeftLeg"); rShin = Bone(HumanBodyBones.RightLowerLeg, "RightLeg");
            lArm = Bone(HumanBodyBones.LeftUpperArm, "LeftArm"); rArm = Bone(HumanBodyBones.RightUpperArm, "RightArm");
            lFore = Bone(HumanBodyBones.LeftLowerArm, "LeftForeArm"); rFore = Bone(HumanBodyBones.RightLowerArm, "RightForeArm");
            head = Bone(HumanBodyBones.Head, "Head");
            if (head != null)
            {
                GameObject helmet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                helmet.name = "Helmet";
                Destroy(helmet.GetComponent<Collider>());
                helmet.transform.SetParent(head, false);
                helmet.transform.position = head.position + rider.up * 0.1f;
                helmet.transform.rotation = rider.rotation;
                Vector3 hs = head.lossyScale;
                helmet.transform.localScale = new Vector3(0.3f / Mathf.Max(0.001f, hs.x), 0.3f / Mathf.Max(0.001f, hs.y), 0.32f / Mathf.Max(0.001f, hs.z));
                helmet.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.05f, 0.05f, 0.06f), 0.85f);
            }
        }

        root.transform.SetPositionAndRotation(ground, rot);
        root.transform.SetParent(transform, true);
    }

    void LateUpdate()
    {
        if (look == null) return;
        // lean into the turn (how fast it's turning × how fast it's going), wheels round with the speed
        float speed = body != null ? Vector3.Dot(body.linearVelocity, transform.forward) : 0f;
        float yawRate = body != null ? body.angularVelocity.y : 0f;
        lean = Mathf.Lerp(lean, Mathf.Clamp(-yawRate * Mathf.Abs(speed) * 2.2f, -32f, 32f), Time.deltaTime * 5f);
        look.localRotation = Quaternion.Euler(0f, 0f, lean);
        float spin = speed / 0.33f * Mathf.Rad2Deg * Time.deltaTime;
        if (frontWheel != null) frontWheel.Rotate(spin, 0f, 0f, Space.Self);
        if (rearWheel != null) rearWheel.Rotate(spin, 0f, 0f, Space.Self);
        Pose();
    }

    /// Over his idle: leaning forward, knees up either side of the tank, feet back on the pegs, hands on the bars.
    void Pose()
    {
        if (rider == null) return;
        Vector3 right = look.right;
        Turn(spine, right, 24f);
        Turn(lThigh, right, -78f); Turn(rThigh, right, -78f);
        Turn(lShin, right, 100f); Turn(rShin, right, 100f);
        Turn(lArm, right, -58f); Turn(rArm, right, -58f);
        Turn(lFore, right, -22f); Turn(rFore, right, -22f);
        Turn(head, right, -18f);                                                      // eyes up the road
    }

    static void Turn(Transform bone, Vector3 axis, float degrees)
    {
        if (bone != null) bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
    }

    Transform Bone(HumanBodyBones hb, string name)
    {
        Transform t = null;
        if (anim != null && anim.isHuman) t = anim.GetBoneTransform(hb);
        if (t == null && rider != null)
            foreach (Transform c in rider.GetComponentsInChildren<Transform>(true))
                if (c.name == "mixamorig:" + name) { t = c; break; }
        return t;
    }

    Transform Wheel(string name, Vector3 at, Material tyre, Material hub)
    {
        GameObject w = new GameObject(name);
        w.transform.SetParent(look, false);
        w.transform.localPosition = at;
        GameObject t = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        t.name = "Tyre"; Destroy(t.GetComponent<Collider>());
        t.transform.SetParent(w.transform, false);
        t.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        t.transform.localScale = new Vector3(0.66f, 0.06f, 0.66f);
        t.GetComponent<Renderer>().sharedMaterial = tyre;
        for (int i = 0; i < 3; i++)
        {
            GameObject s = GameObject.CreatePrimitive(PrimitiveType.Cube);
            s.name = "Spoke"; Destroy(s.GetComponent<Collider>());
            s.transform.SetParent(w.transform, false);
            s.transform.localRotation = Quaternion.Euler(i * 60f, 0f, 0f);
            s.transform.localScale = new Vector3(0.13f, 0.46f, 0.04f);
            s.GetComponent<Renderer>().sharedMaterial = hub;
        }
        return w.transform;
    }

    void Part(PrimitiveType type, string name, Vector3 at, Vector3 size, Quaternion rot, Material m)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(look, false);
        g.transform.localPosition = at; g.transform.localRotation = rot; g.transform.localScale = size;
        Renderer r = g.GetComponent<Renderer>();
        r.sharedMaterial = m;
    }

    static Material Mat(Color c, float smooth)
    {
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        Material m = new Material(s != null ? s : Shader.Find("Standard")) { name = "Bike" };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        return m;
    }

    static Material Glow(Color c)
    {
        Material m = Mat(c, 0.8f);
        if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 2.5f); }
        return m;
    }
}
