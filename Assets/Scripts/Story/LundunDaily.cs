using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// S8: daily street challenges (Story only; added by LundunStoryUI). Three new ones every real day, picked from the
/// date (no server): the same three all day, whenever you play. Each pays £200–£1,000 the moment it's done; ten done
/// in a week (Monday to Sunday) pays a £2,500 bonus. Missing a day costs nothing. Shown in the pause menu's DAILY tab.
/// Counted from what's already happening in the game: the wallet (what you bought, earned), robberies, the police,
/// punches, the Wheel, the bikes, the train, missions passed.
public class LundunDaily : MonoBehaviour
{
    public static LundunDaily Instance { get; private set; }

    public class Challenge
    {
        public string id, text; public float need; public long pay; public Func<LundunStoryData, bool> open;
        public string Unit = "";
    }

    /// Everything that can come up (the ones that need the story further along say so).
    public static readonly Challenge[] Pool =
    {
        new Challenge { id = "rob",       text = "Rob a store's till",                    need = 1,    pay = 600,  open = d => Has(d, LundunRobbery.UnlockFlag) },
        new Challenge { id = "lose3",     text = "Lose a 3★ police chase",                need = 1,    pay = 700 },
        new Challenge { id = "lose4",     text = "Lose a 4★ police chase",                need = 1,    pay = 1000 },
        new Challenge { id = "wheel",     text = "Ride the Lundun Wheel",                 need = 1,    pay = 200 },
        new Challenge { id = "ko",        text = "Knock out 3 people with your fists",    need = 3,    pay = 300 },
        new Challenge { id = "kogilmore", text = "Win a fist fight on the Gilmore Estate", need = 1,   pay = 400 },
        new Challenge { id = "respray",   text = "Get a respray at Arches Motors",        need = 1,    pay = 300 },
        new Challenge { id = "chop",      text = "Sell a car to the chop shop",           need = 1,    pay = 500 },
        new Challenge { id = "pint",      text = "Have 3 pints in a pub",                 need = 3,    pay = 200 },
        new Challenge { id = "chicken",   text = "Eat at a chicken shop",                 need = 1,    pay = 200 },
        new Challenge { id = "kemi",      text = "Do a delivery run for Kemi",            need = 1,    pay = 500,  open = d => Has(d, "done:m03_no_way_home") },
        new Challenge { id = "bike",      text = "Ride 800 m on a hire bike",             need = 800,  pay = 300,  Unit = "m" },
        new Challenge { id = "drive",     text = "Drive 3 km",                            need = 3000, pay = 300,  Unit = "m" },
        new Challenge { id = "train",     text = "Take the train",                        need = 1,    pay = 200 },
        new Challenge { id = "earn",      text = "Earn £2,000 in a day",                  need = 2000, pay = 800,  Unit = "£" },
        new Challenge { id = "rest",      text = "Get a night's rest at the lock-up",     need = 1,    pay = 200 },
    };

    public const long WeekBonus = 2500;
    public const int WeekTarget = 10;

    private readonly List<Challenge> today = new List<Challenge>();
    private string date = "";
    private int peakStars;
    private float caughtAt = -99f, nextCheck;
    private bool wheelWas;
    private Vector3 lastBike, lastCar;
    private bool hadBike, hadCar;
    private VehicleSystem vehicles;
    private LundunWheel wheel;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void OnEnable()
    {
        LundunWallet.Changed += OnMoney;
        LundunRobbery.Robbed += OnRobbed;
        PlayerCombat.KnockedOut += OnKnockedOut;
        LundunMissions.Passed += OnPassed;
        LundunTrain.Departed += OnTrain;
        WastedSystem.Happened += OnCaught;
        BustedSystem.Happened += OnCaught;
    }

    void OnDisable()
    {
        LundunWallet.Changed -= OnMoney;
        LundunRobbery.Robbed -= OnRobbed;
        PlayerCombat.KnockedOut -= OnKnockedOut;
        LundunMissions.Passed -= OnPassed;
        LundunTrain.Departed -= OnTrain;
        WastedSystem.Happened -= OnCaught;
        BustedSystem.Happened -= OnCaught;
    }

    void Start()
    {
        vehicles = FindAnyObjectByType<VehicleSystem>();
        wheel = FindAnyObjectByType<LundunWheel>();
        Roll();
        StartCoroutine(Intro());
    }

    System.Collections.IEnumerator Intro()
    {
        yield return new WaitForSeconds(25f);
        LundunStoryData d = LundunStory.Data;
        if (d == null || d.flags.Contains("seen:daily")) yield break;
        while (LundunMissions.Running || LundunMissions.Talking) yield return new WaitForSeconds(5f);
        d.flags.Add("seen:daily"); LundunStory.MarkDirty();
        LundunMissions.Toast("DAILY CHALLENGES\nThree new ones every day: see DAILY in the pause menu");
    }

    static bool Has(LundunStoryData d, string flag) { return d != null && d.flags.Contains(flag); }
    static string Today { get { return DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); } }

    // ------------------------------------------------------------------ today's three
    /// The challenges for today's date (a new date: new ones, the progress starts again).
    void Roll()
    {
        LundunStoryData d = LundunStory.Data;
        if (d == null) return;
        string now = Today;
        if (date == now && today.Count > 0) return;
        date = now;
        if (d.dailyDate != now)
        {
            d.dailyDate = now;
            d.dailyDone.Clear();
            foreach (Challenge c in Pool) LundunSave.SetFloat(Key(c), 0f);
            LundunStory.MarkDirty();
        }
        today.Clear();
        List<Challenge> open = new List<Challenge>();
        foreach (Challenge c in Pool) if (c.open == null || c.open(d)) open.Add(c);
        System.Random r = new System.Random(Seed(now));
        for (int i = open.Count - 1; i > 0; i--) { int k = r.Next(i + 1); Challenge t = open[i]; open[i] = open[k]; open[k] = t; }
        // never both police chases, never both kinds of punching
        foreach (Challenge c in open)
        {
            if (today.Count >= 3) break;
            if (Clash(c)) continue;
            today.Add(c);
        }
    }

    bool Clash(Challenge c)
    {
        foreach (Challenge t in today)
        {
            if (c.id.StartsWith("lose") && t.id.StartsWith("lose")) return true;
            if (c.id.StartsWith("ko") && t.id.StartsWith("ko")) return true;
        }
        return false;
    }

    static int Seed(string s) { unchecked { int h = 23; foreach (char ch in s) h = h * 37 + ch; return h & 0x7fffffff; } }
    static string Key(Challenge c) { return "Lundun.DailyProg." + c.id; }

    /// Today's three, with how far along each is (0..1) and whether it's done.
    public List<Challenge> Current { get { Roll(); return today; } }
    public static float Progress(Challenge c) { return LundunSave.GetFloat(Key(c), 0f); }
    public static bool Done(Challenge c) { LundunStoryData d = LundunStory.Data; return d != null && d.dailyDone.Contains(c.id); }

    /// This week (Monday to Sunday): challenges done, and whether the bonus is paid.
    public static int WeekDone { get { return LundunSave.GetInt("Lundun.WeekNo", -1) == WeekNumber ? LundunSave.GetInt("Lundun.WeekCount", 0) : 0; } }
    public static bool WeekPaid { get { return LundunSave.GetInt("Lundun.WeekPaid", -1) == WeekNumber; } }
    static int WeekNumber { get { return (int)Math.Floor((DateTime.Now.Date - new DateTime(2024, 1, 1)).TotalDays / 7.0); } }     // 1 Jan 2024 was a Monday

    // ------------------------------------------------------------------ counting
    /// Something happened that counts towards 'id' (by 'amount').
    public static void Note(string id, float amount = 1f)
    {
        if (Instance != null && LundunGame.IsStory) Instance.Add(id, amount);
    }

    void Add(string id, float amount)
    {
        Roll();
        Challenge c = today.Find(t => t.id == id);
        if (c == null || Done(c) || amount <= 0f) return;
        float p = Mathf.Min(c.need, Progress(c) + amount);
        LundunSave.SetFloat(Key(c), p);
        if (p < c.need - 0.001f) return;
        LundunStoryData d = LundunStory.Data;
        d.dailyDone.Add(c.id);
        LundunStory.MarkDirty();
        LundunWallet.Earn(c.pay, "Daily challenge");
        LundunHaptics.Medium();
        // the week
        int wk = WeekNumber;
        int n = LundunSave.GetInt("Lundun.WeekNo", -1) == wk ? LundunSave.GetInt("Lundun.WeekCount", 0) : 0;
        n++;
        LundunSave.SetInt("Lundun.WeekNo", wk); LundunSave.SetInt("Lundun.WeekCount", n);
        string extra = "";
        if (n >= WeekTarget && !WeekPaid)
        {
            LundunSave.SetInt("Lundun.WeekPaid", wk);
            LundunWallet.Earn(WeekBonus, "Weekly bonus");
            extra = "\nTEN THIS WEEK: +" + LundunWallet.Format(WeekBonus) + " bonus";
        }
        int left = 0; foreach (Challenge t in today) if (!Done(t)) left++;
        LundunMissions.Toast("DAILY CHALLENGE DONE\n" + c.text + "  +" + LundunWallet.Format(c.pay) + extra
                             + (left > 0 && extra.Length == 0 ? "\n" + left + " more today" : ""));
    }

    void OnMoney(long delta, string why)
    {
        switch (why)
        {
            case "Respray": Note("respray"); break;
            case "Chop shop": Note("chop"); break;
            case "A pint": Note("pint"); break;
            case "Chicken & chips": Note("chicken"); break;
        }
        if (delta > 0 && why != "Daily challenge" && why != "Weekly bonus" && why != "Test cash") Note("earn", delta);
    }

    void OnRobbed(string store, long cash) { Note("rob"); }
    void OnPassed(string id, int medal) { if (id == M13KemisDeliveries.MissionId) Note("kemi"); }
    void OnTrain() { if (LundunTrain.Instance != null && LundunTrain.Instance.Riding) Note("train"); }
    void OnCaught() { caughtAt = Time.time; peakStars = 0; }

    void OnKnockedOut(Health h)
    {
        LundunWeapons w = LundunWeapons.Instance;
        if (w != null && !w.IsFists) return;
        Note("ko");
        Vector3 p = h != null ? h.transform.position : Vector3.zero;
        if (p.x > 130f && p.x < 600f && p.z > 120f && p.z < 560f) Note("kogilmore");            // the Gilmore Estate
    }

    void Update()
    {
        // the police: peak stars while wanted, and losing them (not busted or wasted)
        WantedSystem ws = WantedSystem.Instance;
        int stars = ws != null ? ws.Stars : 0;
        if (stars > 0) peakStars = Mathf.Max(peakStars, stars);
        else if (peakStars > 0)
        {
            if (Time.time - caughtAt > 4f)
            {
                if (peakStars >= 3) Note("lose3");
                if (peakStars >= 4) Note("lose4");
            }
            peakStars = 0;
        }

        // distance on a hire bike and behind the wheel
        PlayerHealth ph = PlayerHealth.Instance;
        bool bike = LundunBikes.Instance != null && LundunBikes.Instance.Riding && ph != null && ph.Body != null;
        if (bike)
        {
            Vector3 p = ph.Body.position;
            if (hadBike) { float m = LundunMissionRun.FlatDistance(p, lastBike); if (m < 30f) bikeMetres += m; }
            lastBike = p;
        }
        hadBike = bike;
        bool car = vehicles != null && vehicles.IsDriving && vehicles.CurrentCar != null;
        if (car)
        {
            Vector3 p = vehicles.CurrentCar.transform.position;
            if (hadCar) { float m = LundunMissionRun.FlatDistance(p, lastCar); if (m < 60f) carMetres += m; }
            lastCar = p;
        }
        hadCar = car;

        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 1f;
        Roll();
        if (bikeMetres >= 10f) { Note("bike", bikeMetres); bikeMetres = 0f; }
        if (carMetres >= 25f) { Note("drive", carMetres); carMetres = 0f; }
        if (wheel == null) wheel = FindAnyObjectByType<LundunWheel>();
        bool riding = wheel != null && wheel.Riding;
        if (riding && !wheelWas) Note("wheel");
        wheelWas = riding;
    }

    private float bikeMetres, carMetres;
}
