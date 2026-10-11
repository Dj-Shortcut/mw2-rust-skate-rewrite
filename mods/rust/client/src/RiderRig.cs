// The pose must be written after the game's own animation (the late driver's LateUpdate): what is
// written there is what gets drawn. The game still adjusts the skeleton before the next frame
// starts, so nothing may be read back from the bones as if it were this module's pose.
// The rider module is given the plane the ankle joints rest on, not the deck: its ankles are the
// skeleton's ankle joints. Where the deck is below that plane goes in separately, for the grab.
using System;
using Shortcut.RustMod;
using UnityEngine;

public static class RiderRig
{
    private static float grabSince = -1f;
    private static float grabHold = -1f;
    // A view that holds the grab at a given moment of its folding in (the pose check).
    public static void GrabFor(float seconds) { grabHold = seconds; }
    public const float AnkleHeight = 0.095f, ToeHeight = 0.025f, DeckTop = 0.092f, HeelShift = 0.055f;
    public const float HeadLiftFrom = 20f, HeadLiftShare = 0.8f, HeadLiftMost = 50f;
    public static bool Bound;
    public static Animator Anim;
    public static Transform Root, Pelvis, Neck, Head, LHip, LKnee, LFoot, LToe, RHip, RKnee, RFoot, RToe, LUpper, LFore, LHand, RUpper, RFore, RHand;
    public static SkateRiderRig Rig;
    public static float Thigh, Shin, Foot, UpperArm, Forearm, Crouch = 0.18f;
    public static float Miss;
    public static string MissAt = "", Error = "";
    public static int Applied, Refusals;
    private static Transform[] spine = new Transform[0];
    private static Vector3 lSole, rSole;
    private static bool failed;

    // The module answers every frame from that frame's state alone. Between two states (rolling, a
    // push, the air, a grab, a bail) the joints ease over, in the board's own frame so that a turning
    // or tipping board does not leave them behind.
    public const float EaseSeconds = 0.07f;
    private const int PelvisJ = 0, LHipJ = 1, RHipJ = 2, LKneeJ = 3, RKneeJ = 4, LAnkleJ = 5, RAnkleJ = 6, LToeJ = 7, RToeJ = 8, ChestJ = 9, NeckJ = 10, HeadJ = 11,
        LShoulderJ = 12, RShoulderJ = 13, LElbowJ = 14, RElbowJ = 15, LHandJ = 16, RHandJ = 17, Points = 18,
        PelvisForwardJ = 18, PelvisUpJ = 19, ChestForwardJ = 20, ChestUpJ = 21, HeadForwardJ = 22, All = 23;
    private static readonly Vector3[] eased = new Vector3[All], joint = new Vector3[All];
    private static float easedAt = -99f;

    // The game draws the local player's model twice: the full body casts shadows only, and a second
    // set of skinned meshes named "leg-..." is what first person shows, with the upper body folded
    // out of view. A camera behind the rider needs the full body drawn and that set hidden; both
    // must be put back for first person.
    private static SkinnedMeshRenderer[] body = new SkinnedMeshRenderer[0], legSet = new SkinnedMeshRenderer[0];
    private static bool third;
    private static int lookFailures;
    private static long skinSet;
    private static float nextScan, lookRetryAt;
    public const float ScanEvery = 5f;
    public static string LookState = "first person";

    // The item in the hands and the first-person arms are drawn right in front of the camera,
    // wherever it is: behind the rider they would cover him. The game makes a new set for every
    // item taken in hand, so the set is looked for again several times a second.
    // Switching the parts off is not enough in this client: with a rock in hand they stayed on the
    // screen. So the set is also made too small to see. The game may set the size again at any
    // time, so it is made small again every frame, and the size the game last gave is put back.
    // A look through the whole scene for held items costs the game a fifth of a second, so it is
    // done at most every HeldWhole seconds, and each time it finds nothing the next one waits twice
    // as long, up to HeldWholeMost (empty hands would otherwise cost a frame every two seconds);
    // in between the object that held the last item found (their parent) is looked through, which
    // is cheap, every HeldEvery seconds.
    public const float HeldEvery = 0.25f, HeldWhole = 2f, HeldWholeMost = 32f, HeldSmall = 0.0001f;
    private const int HeldTries = 5, HeldScans = 3;

    // One thing of the set: the object that carries it (Root) or one drawn part of it (Part). What
    // was changed on it stays noted until it has been put back, each thing on its own: one that
    // throws is tried again later and never leaves the others small or off. A thing of a set that
    // has been replaced is only ever put back (Old).
    private sealed class HeldThing
    {
        public Transform Root; public Renderer Part;
        public Vector3 Size; public bool Was, Changed, Forced, Old;
        public int Tries;
    }
    private static HeldThing[] held = new HeldThing[0];
    private static long heldSet;
    private static float nextHeld;
    private static bool heldForced = true, heldThird, heldOff;
    private static long heldSlow;
    // A frame's worth of this is a frame lost: past this the handling is switched off for the session.
    private const double HeldBudgetMs = 8.0; private const int HeldSlowFrames = 30;
    private static Transform heldHolder;
    private static float nextWhole, wholeWait = HeldWhole;
    private static int heldScanFailures;

    private static void Held(bool wantThird, float now)
    {
        if (heldOff) return;
        var began = SkateClock.Now();
        // The view going behind the rider is worth one look through the whole scene.
        if (wantThird && !heldThird) { nextHeld = nextWhole = 0f; wholeWait = HeldWhole; }
        heldThird = wantThird;
        // A set that lost a thing is looked for again at once, but once: the lost thing is dropped
        // so that it cannot ask for a look every frame, which cost the owner his frame rate.
        if (wantThird && HeldGone()) { nextHeld = nextWhole = 0f; wholeWait = HeldWhole; }
        if (wantThird && now >= nextHeld && heldScanFailures < HeldScans)
        {
            nextHeld = now + HeldEvery;
            try
            {
                Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<BaseViewModel> models = null;
                if (!ReferenceEquals(heldHolder, null) && heldHolder != null) models = heldHolder.GetComponentsInChildren<BaseViewModel>(true);
                if ((models == null || models.Length == 0) && now >= nextWhole)
                {
                    nextWhole = now + wholeWait;
                    models = UnityEngine.Object.FindObjectsOfType<BaseViewModel>();
                    heldHolder = models.Length > 0 && models[0] != null ? models[0].transform.parent : null;
                    wholeWait = models.Length > 0 ? HeldWhole : Math.Min(wholeWait * 2f, HeldWholeMost);
                }
                if (models == null) return;
                var found = new System.Collections.Generic.List<HeldThing>(); long set = models.Length; var names = ""; var parts = 0;
                for (var i = 0; i < models.Length; i++)
                {
                    if (models[i] == null) continue;
                    found.Add(new HeldThing { Root = models[i].transform }); set = set * 31 + models[i].Pointer.ToInt64();
                    if (i < 3) names += (names == "" ? "" : ", ") + models[i].gameObject.name;
                    var drawn = models[i].GetComponentsInChildren<Renderer>(true);
                    for (var k = 0; k < drawn.Length; k++) { found.Add(new HeldThing { Part = drawn[k] }); set = set * 31 + drawn[k].Pointer.ToInt64(); parts++; }
                }
                if (set != heldSet)
                {
                    HeldRestore(false);
                    HeldKeep(found);
                    held = found.ToArray(); heldSet = set;
                    if (parts > 0 || names != "") Say("held item: " + names + ", " + parts + " parts out of the camera's way");
                }
                heldScanFailures = 0;
            }
            catch (Exception e)
            {
                heldScanFailures++;
                Say("held item: looking for it threw " + e.GetType().Name + ": " + e.Message + (heldScanFailures >= HeldScans ? "; given up until the rider is found anew" : ""));
            }
        }
        if (wantThird) HeldHide();
        HeldRestore(wantThird);
        var ms = (SkateClock.Now() - began) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        heldSlow = ms > HeldBudgetMs ? heldSlow + 1 : 0;
        if (heldSlow >= HeldSlowFrames)
        {
            heldOff = true; HeldRestore(false);
            Say("held item: keeping it out of the way cost " + ms.ToString("F0") + " ms a frame for " + HeldSlowFrames + " frames; it is left alone from here on");
        }
    }

    // A held item that was swapped is destroyed with its parts: the set is looked for again then.
    private static bool HeldGone()
    {
        var gone = false;
        for (var i = 0; i < held.Length; i++)
        {
            var t = held[i];
            if (t.Old) continue;
            if (!ReferenceEquals(t.Root, null) && t.Root == null) { t.Old = true; t.Changed = false; gone = true; }
        }
        return gone;
    }

    // What could not be put back yet goes along with the next set, to be tried again.
    private static void HeldKeep(System.Collections.Generic.List<HeldThing> into)
    {
        for (var i = 0; i < held.Length; i++)
            if (held[i].Changed) { held[i].Old = true; into.Add(held[i]); }
    }

    private static void HeldHide()
    {
        for (var i = 0; i < held.Length; i++)
        {
            var t = held[i];
            if (t.Old || t.Tries >= HeldTries) continue;
            try
            {
                if (t.Root != null)
                {
                    var size = t.Root.localScale;
                    if (Math.Abs(size.x) > HeldSmall * 2f || Math.Abs(size.y) > HeldSmall * 2f || Math.Abs(size.z) > HeldSmall * 2f)
                    { t.Size = size; t.Changed = true; t.Root.localScale = Vector3.one * HeldSmall; }
                }
                else if (t.Part != null && !t.Changed)
                {
                    // Off without touching what the game itself switches on and off. A client build
                    // without that setter switches the part itself off and remembers how it was.
                    if (heldForced)
                    {
                        try { t.Part.forceRenderingOff = true; t.Forced = true; t.Changed = true; continue; }
                        catch (Exception e) { heldForced = false; Say("held item: no rendering switch in this client (" + e.GetType().Name + "); the parts are switched off instead"); }
                    }
                    t.Was = t.Part.enabled; t.Forced = false; t.Changed = true; t.Part.enabled = false;
                }
            }
            catch (Exception e) { HeldTrouble(t, "taking it out of the way", e); }
        }
    }

    // oldOnly: while the view is behind the rider, only what is left of a replaced set is put back.
    private static void HeldRestore(bool oldOnly)
    {
        for (var i = 0; i < held.Length; i++)
        {
            var t = held[i];
            if (!t.Changed || (oldOnly && !t.Old)) continue;
            try
            {
                if (!ReferenceEquals(t.Root, null)) { if (t.Root != null) t.Root.localScale = t.Size; }
                else if (t.Part != null) { if (t.Forced) t.Part.forceRenderingOff = false; else t.Part.enabled = t.Was; }
                t.Changed = false; t.Tries = 0;
            }
            catch (Exception e)
            {
                HeldTrouble(t, "putting it back", e);
                // Given up: an object the engine no longer lets anyone touch has nothing left to put back.
                if (t.Tries >= HeldTries) t.Changed = false;
            }
        }
    }

    private static void HeldTrouble(HeldThing t, string doing, Exception e)
    {
        t.Tries++;
        if (t.Tries == 1 || t.Tries == HeldTries)
            Say("held item: " + doing + " threw " + e.GetType().Name + ": " + e.Message + (t.Tries >= HeldTries ? "; left alone from here on" : "; tried again"));
    }

    // For the scripted pose check: what is drawn within arm's length of the camera, the way to find
    // out what a client build puts in front of a camera that has left the player's eyes.
    private static string nearSaid = "";

    public static void Near()
    {
        try
        {
            var cam = Camera.main;
            if (cam == null) return;
            var at = cam.transform.position;
            var all = UnityEngine.Object.FindObjectsOfType<Renderer>();
            var text = ""; var n = 0;
            for (var i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r == null || !r.enabled) continue;
                var b = r.bounds;
                if ((b.center - at).sqrMagnitude > 1.44f || b.extents.sqrMagnitude > 9f) continue;
                n++;
                if (n <= 10) text += " | " + r.gameObject.name + " layer=" + r.gameObject.layer + " size=" + r.transform.lossyScale.x.ToString("F4") + " seen=" + r.isVisible + " under " + Out.Chain(r.transform);
            }
            var line = n + " drawn parts within 1.2 m of the camera" + text;
            if (line == nearSaid) return;
            nearSaid = line; Say("near: " + line);
        }
        catch (Exception e) { Say("near threw " + e.GetType().Name + ": " + e.Message); }
    }

    public static void Look(bool wantThird)
    {
        var now = Time.realtimeSinceStartup;
        var began = SkateClock.Now(); Held(wantThird, now); SkateClock.Add(SkateClock.Held, began);
        if (!Bound || Root == null || lookFailures >= 3 || now < lookRetryAt) return;
        try
        {
            if (now >= nextScan || SkinGone())
            {
                // Clothing changes replace the meshes, also one for one, so the set is compared by
                // identity now and then. Walking the whole model costs a visible hitch, so it is done
                // every few seconds, or at once when a known mesh has been destroyed. The old set goes
                // back to the game's own state before the new one is sorted: a mesh is recognised as
                // body by that state.
                nextScan = now + ScanEvery;
                var all = Root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                long set = all.Length;
                for (var i = 0; i < all.Length; i++) set = set * 31 + all[i].Pointer.ToInt64();
                if (set != skinSet)
                {
                    Show(false, true);
                    var b = new System.Collections.Generic.List<SkinnedMeshRenderer>(); var l = new System.Collections.Generic.List<SkinnedMeshRenderer>();
                    for (var i = 0; i < all.Length; i++)
                    {
                        if (all[i].gameObject.name.StartsWith("leg-")) l.Add(all[i]);
                        else if (all[i].shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) b.Add(all[i]);
                    }
                    body = b.ToArray(); legSet = l.ToArray(); skinSet = set;
                    Say("look: " + all.Length + " skinned meshes, " + body.Length + " shadow-only body, " + legSet.Length + " first-person");
                }
            }
            if (wantThird != third) Show(wantThird, false);
            lookFailures = 0;
        }
        catch (Exception e)
        {
            // Back to the game's own state with the set as it was known, and another try later.
            lookFailures++; lookRetryAt = now + 3f; skinSet = 0;
            Say("look threw " + e.GetType().Name + ": " + e.Message);
            try { Show(false, true); } catch (Exception) { }
        }
    }

    // A destroyed Unity object compares equal to null; checking the few known meshes is cheap, and
    // catches a clothing change the moment it happens.
    private static bool SkinGone()
    {
        for (var i = 0; i < body.Length; i++) if (body[i] == null) return true;
        for (var i = 0; i < legSet.Length; i++) if (legSet[i] == null) return true;
        return false;
    }

    public static void Unbind()
    {
        try { Show(false, true); } catch (Exception) { }
        // What cannot be put back now is kept, and tried again from the next frame on.
        HeldRestore(false);
        var left = new System.Collections.Generic.List<HeldThing>(); HeldKeep(left);
        held = left.ToArray(); heldSet = 0; heldScanFailures = 0; nextHeld = nextWhole = 0f; wholeWait = HeldWhole; heldHolder = null; heldOff = false; heldSlow = 0;
        Bound = false; body = new SkinnedMeshRenderer[0]; legSet = new SkinnedMeshRenderer[0]; skinSet = 0;
    }

    private static void Show(bool wantThird, bool always)
    {
        if (third == wantThird && !always) return;
        third = wantThird;
        LookState = wantThird ? "third person" : "first person";
        // Each mesh on its own: one that fails must not leave the others in the wrong state.
        var failures = 0; var first = "";
        for (var i = 0; i < body.Length; i++)
        {
            try { if (body[i] != null) body[i].shadowCastingMode = wantThird ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly; }
            catch (Exception e) { if (failures++ == 0) first = e.GetType().Name + ": " + e.Message; }
        }
        for (var i = 0; i < legSet.Length; i++)
        {
            try { if (legSet[i] != null) legSet[i].enabled = !wantThird; }
            catch (Exception e) { if (failures++ == 0) first = e.GetType().Name + ": " + e.Message; }
        }
        if (failures > 0) throw new InvalidOperationException(failures + " meshes could not be switched, first " + first);
    }

    private static void Say(string m) { Out.Say("RIDER " + m); }
    private static float Dist(Transform a, Transform b) { return Vector3.Distance(a.position, b.position); }
    private static Vector3 V(SkateVector v) { return new Vector3((float)v.X, (float)v.Y, (float)v.Z); }

    public static bool Bind(Transform playerT)
    {
        Unbind(); nextScan = 0f; lookFailures = 0; lookRetryAt = 0f;
        var models = UnityEngine.Object.FindObjectsOfType<PlayerModel>();
        PlayerModel best = null; var bd = 2f;
        for (var i = 0; i < models.Length; i++)
        {
            var d = Vector3.Distance(models[i].transform.position, playerT.position);
            if (d < bd) { bd = d; best = models[i]; }
        }
        if (best == null) return false;
        var a = best.GetComponentInChildren<Animator>(true);
        if (a == null || !a.isHuman) { Say("model without a humanoid animator"); return false; }
        Anim = a; Root = best.transform;
        Pelvis = a.GetBoneTransform(HumanBodyBones.Hips); Neck = a.GetBoneTransform(HumanBodyBones.Neck); Head = a.GetBoneTransform(HumanBodyBones.Head);
        LHip = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg); LKnee = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg); LFoot = a.GetBoneTransform(HumanBodyBones.LeftFoot); LToe = a.GetBoneTransform(HumanBodyBones.LeftToes);
        RHip = a.GetBoneTransform(HumanBodyBones.RightUpperLeg); RKnee = a.GetBoneTransform(HumanBodyBones.RightLowerLeg); RFoot = a.GetBoneTransform(HumanBodyBones.RightFoot); RToe = a.GetBoneTransform(HumanBodyBones.RightToes);
        LUpper = a.GetBoneTransform(HumanBodyBones.LeftUpperArm); LFore = a.GetBoneTransform(HumanBodyBones.LeftLowerArm); LHand = a.GetBoneTransform(HumanBodyBones.LeftHand);
        RUpper = a.GetBoneTransform(HumanBodyBones.RightUpperArm); RFore = a.GetBoneTransform(HumanBodyBones.RightLowerArm); RHand = a.GetBoneTransform(HumanBodyBones.RightHand);
        if (Pelvis == null || Neck == null || Head == null || LHip == null || LKnee == null || LFoot == null || LToe == null || RHip == null || RKnee == null || RFoot == null || RToe == null
            || LUpper == null || LFore == null || LHand == null || RUpper == null || RFore == null || RHand == null)
        { Say("humanoid mapping incomplete"); return false; }

        // The spine is every bone between the pelvis and the neck, whatever the avatar calls them.
        var chain = new System.Collections.Generic.List<Transform>();
        var t = Neck.parent;
        while (t != null && t != Pelvis && chain.Count < 8) { chain.Insert(0, t); t = t.parent; }
        if (t != Pelvis || chain.Count == 0) { Say("no spine between the pelvis and the neck"); return false; }
        spine = chain.ToArray();
        var back = Dist(Pelvis, spine[0]);
        for (var i = 0; i < spine.Length; i++) back += Dist(spine[i], i + 1 < spine.Length ? spine[i + 1] : Neck);

        Thigh = (Dist(LHip, LKnee) + Dist(RHip, RKnee)) * 0.5f; Shin = (Dist(LKnee, LFoot) + Dist(RKnee, RFoot)) * 0.5f;
        Foot = (Dist(LFoot, LToe) + Dist(RFoot, RToe)) * 0.5f;
        UpperArm = (Dist(LUpper, LFore) + Dist(RUpper, RFore)) * 0.5f; Forearm = (Dist(LFore, LHand) + Dist(RFore, RHand)) * 0.5f;
        var hipWidth = Dist(LHip, RHip);
        var hipDrop = Vector3.Distance(Pelvis.position, (LHip.position + RHip.position) * 0.5f);
        var drop = AnkleHeight - ToeHeight;
        var footFlat = (float)Math.Sqrt(Math.Max(0.0001f, Foot * Foot - drop * drop));
        // The module puts the neck 0.12 of the spine above the chest, and the chest a spine above the hips.
        Rig = new SkateRiderRig((Thigh + Shin) * 0.945f, hipWidth, Thigh, Shin, footFlat, (back + hipDrop) / 1.12f, Dist(LUpper, RUpper), UpperArm, Forearm, Dist(Neck, Head));
        // Which way the soles face, seen from each foot bone, while the player stands.
        var up = Root.rotation * Vector3.up;
        lSole = Quaternion.Inverse(LFoot.rotation) * up; rSole = Quaternion.Inverse(RFoot.rotation) * up;
        failed = false; Bound = true; Error = "";
        Say("bound to " + Root.gameObject.name + " spine bones=" + spine.Length + " thigh=" + Thigh.ToString("F3") + " shin=" + Shin.ToString("F3") + " foot=" + Foot.ToString("F3") + " upperArm=" + UpperArm.ToString("F3")
            + " forearm=" + Forearm.ToString("F3") + " hips=" + hipWidth.ToString("F3") + " hipDrop=" + hipDrop.ToString("F3") + " back=" + back.ToString("F3") + " shoulders=" + Rig.ShoulderWidth.ToString("F3")
            + " neck=" + Rig.NeckToHeadLength.ToString("F3") + " headScale=" + Out.V(Head.localScale));
        return true;
    }

    private static void Aim(Transform bone, Transform child, Vector3 want)
    {
        var cur = child.position - bone.position;
        if (cur.sqrMagnitude < 0.000001f || want.sqrMagnitude < 0.000001f) return;
        bone.rotation = Quaternion.FromToRotation(cur, want) * bone.rotation;
    }

    private static void Roll(Transform bone, Vector3 axis, Vector3 local, Vector3 want)
    {
        var cur = bone.rotation * local;
        cur = cur - axis * Vector3.Dot(cur, axis); want = want - axis * Vector3.Dot(want, axis);
        if (cur.sqrMagnitude < 0.0001f || want.sqrMagnitude < 0.0001f) return;
        bone.rotation = Quaternion.FromToRotation(cur, want) * bone.rotation;
    }

    public static Vector3 Bend(Vector3 root, Vector3 target, float upper, float lower, Vector3 pole)
    {
        var to = target - root; var d = to.magnitude;
        var max = (upper + lower) * 0.999f; var min = Math.Abs(upper - lower) * 1.001f + 0.001f;
        if (d > max) d = max; if (d < min) d = min;
        var axis = to.sqrMagnitude > 0.000001f ? to.normalized : Vector3.down;
        var along = (upper * upper - lower * lower + d * d) / (2f * d);
        var h2 = upper * upper - along * along;
        var side = pole - axis * Vector3.Dot(pole, axis);
        side = side.sqrMagnitude > 0.000001f ? side.normalized : Vector3.Cross(axis, Vector3.right).normalized;
        return root + axis * along + side * (float)Math.Sqrt(h2 > 0f ? h2 : 0f);
    }

    public static void Frame(Vector3 feet, Vector3 up, bool legsOnly, float frameSeconds)
    {
        if (!Bound || failed) return;
        try
        {
            if (Pelvis == null) { Bound = false; return; }
            double ux = up.x, uy = up.y, uz = up.z;
            var ul = Math.Sqrt(ux * ux + uy * uy + uz * uz);
            if (ul < 0.5 || uy / ul < 0.2) { ux = 0; uy = 1; uz = 0; ul = 1; }
            ux /= ul; uy /= ul; uz /= ul;
            var rad = SkateRide.NoseYaw * Math.PI / 180.0;
            double fx = Math.Sin(rad), fy = 0, fz = Math.Cos(rad);
            var d = fx * ux + fy * uy + fz * uz;
            fx -= ux * d; fy -= uy * d; fz -= uz * d;
            var fl = Math.Sqrt(fx * fx + fy * fy + fz * fz);
            if (fl < 0.2) return;
            fx /= fl; fy /= fl; fz /= fl;
            var upV = new Vector3((float)ux, (float)uy, (float)uz);
            // The module's ankles are on the board's centre line; a foot is longer than the deck is wide.
            var toToes = new Vector3((float)(uy * fz - uz * fy), (float)(uz * fx - ux * fz), (float)(ux * fy - uy * fx));
            var plane = feet + upV * (DeckTop + AnkleHeight - (float)SkatePose.FootLift) - toToes * HeelShift;

            var now = Time.realtimeSinceStartup;
            var mode = SkateRide.Mode;
            // In a jump the legs fold by as much as the board has come up, so the body keeps its line.
            var inAir = SkateRide.Grabbing ? 0.95f : SkateRide.Jumped ? 0.2f + SkateBoard.Pop / (float)SkatePose.CrouchDrop : 0.45f;
            var want = mode == RideMode.Air ? inAir : mode == RideMode.Grind ? 0.38f : SkateRide.Braking ? 0.4f : 0.18f;
            // A right stick held back is the rider gathering for the pop; a trigger held is the crouch of a pump.
            if (SkateKeys.Charging && mode == RideMode.Ground) want = Math.Max(want, 0.3f + 0.3f * SkateKeys.Charge);
            if (mode == RideMode.Ground && SkateRide.PumpHeld > 0f) want = Math.Max(want, 0.25f + 0.45f * SkateRide.PumpHeld / SkateRide.PumpCharge);
            var since = now - SkateRide.LandAt;
            if (mode == RideMode.Ground && since < 0.35f) want += (1f - since / 0.35f) * SkateRide.Clamp(SkateRide.LandImpact / 8f, 0.2f, 1f) * 0.45f;
            Crouch += (SkateRide.Clamp(want, 0f, 1f) - Crouch) * SkateRide.Clamp(frameSeconds * 14f, 0f, 1f);
            // The board's rise is eased already; the legs follow it at once, or the body would bob.
            if (mode == RideMode.Air && SkateRide.Jumped && !SkateRide.Grabbing && Crouch < want) Crouch = want;
            // The module leans toward the board's own right; the ride leans toward the right of travel.
            var lean = SkateRide.Clamp(SkateRide.Trick.Switch ? -SkateRide.Lean : SkateRide.Lean, -(float)SkatePose.MaximumLean, (float)SkatePose.MaximumLean);
            var speed = Math.Min((float)SkateMotion.MaximumGroundSpeed, Math.Abs(SkateRide.Speed));
            var grab = mode == RideMode.Air ? SkateRide.Grab : SkateGrabKind.None;
            if (grab == SkateGrabKind.None) { grabSince = -1f; grabHold = -1f; } else if (grabSince < 0f) grabSince = now;
            var plant = SkateRide.FootPlanted ? SkateRide.Clamp((SkateRide.PlantedUntil - now) / SkateRide.PlantSeconds * 1.5f, 0f, 1f) : 0f;
            var input = new SkateRiderInput(new SkateVector(plane.x, plane.y, plane.z), new SkateVector(fx, fy, fz), new SkateVector(ux, uy, uz), SkateStance.Regular, SkateRide.TailFirst,
                speed, lean, Crouch, mode == RideMode.Air, mode == RideMode.Ground && SkateRide.PushPhase > 0f, SkateRide.Clamp(SkateRide.PushPhase, 0f, 1f),
                SkateRide.FlipDeg, grab, mode == RideMode.Bail, SkateKeys.LookYaw % 360f, SkatePose.FootLift - AnkleHeight,
                DeckTop, mode == RideMode.Bail ? SkateRide.Clamp((now - SkateRide.ModeAt) / SkateRide.BailSeconds, 0f, 1f) : 0f,
                SkateRide.PushMongo, plant, grabHold >= 0f ? grabHold : grabSince < 0f ? 0f : now - grabSince, SkateRide.Handplant ? 1f : 0f);
            SkateRiderPose pose; string error;
            if (!SkateRider.TryCreate(Rig, input, out pose, out error))
            {
                if (Refusals++ == 0 || error != Error) Say("pose refused: " + error);
                Error = error; return;
            }
            // In a handplant the board is overhead, against the feet: the board follows the pose.
            SkateBoard.Held = SkateRide.Handplant;
            if (SkateBoard.Held)
            {
                Vector3 la = V(pose.LeftAnkle), ra = V(pose.RightAnkle), pel = V(pose.Pelvis);
                var mid = (la + ra) * 0.5f;
                var grip = (pel - mid).normalized;
                SkateBoard.HeldNose = (la - ra).normalized * (SkateRide.TailFirst ? -1f : 1f);
                SkateBoard.HeldUp = grip;
                SkateBoard.HeldAt = mid - grip * (AnkleHeight + DeckTop);
            }
            Ease(pose, plane, toToes, upV, new Vector3((float)fx, (float)fy, (float)fz), now, frameSeconds);
            Shape(upV);
            Apply(upV, legsOnly);
            Applied++;
        }
        catch (Exception e) { failed = true; Say("pose threw " + e.GetType().Name + ": " + e.Message); }
    }

    private static void Ease(SkateRiderPose p, Vector3 origin, Vector3 x, Vector3 y, Vector3 z, float now, float frameSeconds)
    {
        joint[PelvisJ] = V(p.Pelvis); joint[LHipJ] = V(p.LeftHip); joint[RHipJ] = V(p.RightHip); joint[LKneeJ] = V(p.LeftKnee); joint[RKneeJ] = V(p.RightKnee);
        joint[LAnkleJ] = V(p.LeftAnkle); joint[RAnkleJ] = V(p.RightAnkle); joint[LToeJ] = V(p.LeftToe); joint[RToeJ] = V(p.RightToe);
        joint[ChestJ] = V(p.Chest); joint[NeckJ] = V(p.Neck); joint[HeadJ] = V(p.Head); joint[LShoulderJ] = V(p.LeftShoulder); joint[RShoulderJ] = V(p.RightShoulder);
        joint[LElbowJ] = V(p.LeftElbow); joint[RElbowJ] = V(p.RightElbow); joint[LHandJ] = V(p.LeftHand); joint[RHandJ] = V(p.RightHand);
        joint[PelvisForwardJ] = V(p.PelvisForward); joint[PelvisUpJ] = V(p.PelvisUp); joint[ChestForwardJ] = V(p.ChestForward); joint[ChestUpJ] = V(p.ChestUp); joint[HeadForwardJ] = V(p.HeadForward);
        // After a pause (getting on, a new player model) there is nothing to ease from.
        var share = now - easedAt > 0.25f ? 1f : 1f - (float)Math.Exp(-frameSeconds / EaseSeconds);
        easedAt = now;
        for (var i = 0; i < All; i++)
        {
            var fresh = joint[i];
            var w = i < Points ? fresh - origin : fresh;
            var local = new Vector3(Vector3.Dot(w, x), Vector3.Dot(w, y), Vector3.Dot(w, z));
            eased[i] = eased[i] + (local - eased[i]) * share;
            var back = x * eased[i].x + y * eased[i].y + z * eased[i].z;
            joint[i] = i < Points ? origin + back : back.sqrMagnitude > 0.0001f ? back.normalized : fresh;
        }
        // Halfway between two poses the back would come out shorter than it is: the chest, the neck
        // and the head are put back on the eased line of the back, at the module's distances.
        var hips = (joint[LHipJ] + joint[RHipJ]) * 0.5f; var line = joint[ChestUpJ];
        joint[ChestJ] = hips + line * (float)Rig.SpineLength;
        joint[NeckJ] = hips + line * (float)(Rig.SpineLength * 1.12);
        joint[HeadJ] = joint[NeckJ] + line * (float)Rig.NeckToHeadLength;
    }

    // What the module does not know: in a manual the weight goes over the axle that carries it, the
    // body back toward the tail (or forward over the nose), and the arms come out for balance.
    public const float ManualShift = 0.12f, ManualArms = 0.14f;
    private static void Shape(Vector3 up)
    {
        var tip = SkateBoard.Tip;
        if (Math.Abs(tip) < 0.01f) return;
        var lead = SkateRide.Dir(SkateRide.Yaw); lead = lead - up * Vector3.Dot(lead, up);
        if (lead.sqrMagnitude < 0.0001f) return;
        lead = SkateRide.Speed >= 0f ? lead.normalized : -lead.normalized;
        var shift = -lead * (ManualShift * tip);
        int[] body = { PelvisJ, LHipJ, RHipJ, ChestJ, NeckJ, HeadJ, LShoulderJ, RShoulderJ, LElbowJ, RElbowJ, LHandJ, RHandJ };
        for (var i = 0; i < body.Length; i++) joint[body[i]] += shift;
        var side = Vector3.Cross(up, lead); var amount = Math.Abs(tip);
        joint[LHandJ] += (-side * ManualArms + up * ManualArms * 0.8f) * amount; joint[LElbowJ] += (-side * ManualArms * 0.5f + up * ManualArms * 0.3f) * amount;
        joint[RHandJ] += (side * ManualArms + up * ManualArms * 0.8f) * amount; joint[RElbowJ] += (side * ManualArms * 0.5f + up * ManualArms * 0.3f) * amount;
    }

    private static void Apply(Vector3 up, bool legsOnly)
    {
        var hipsNow = RHip.position - LHip.position; var upNow = spine[0].position - Pelvis.position;
        if (hipsNow.sqrMagnitude > 0.000001f && upNow.sqrMagnitude > 0.000001f)
        {
            var from = Quaternion.LookRotation(Vector3.Cross(hipsNow.normalized, upNow.normalized), upNow.normalized);
            var to = Quaternion.LookRotation(joint[PelvisForwardJ], joint[PelvisUpJ]);
            Pelvis.rotation = to * Quaternion.Inverse(from) * Pelvis.rotation;
        }
        Pelvis.position = Pelvis.position + ((joint[LHipJ] + joint[RHipJ]) * 0.5f - (LHip.position + RHip.position) * 0.5f);

        Miss = 0f; MissAt = "";
        if (!legsOnly)
        {
            var chestUp = joint[ChestUpJ];
            // The module folds the body at the hip joints; the skeleton's spine starts above them, on
            // the pelvis bone. So the pelvis tips about the line through the hips until the spine's
            // root is where the module's straight back passes, or the neck would be out of reach.
            var hipLine = RHip.position - LHip.position;
            var hips = (LHip.position + RHip.position) * 0.5f;
            if (hipLine.sqrMagnitude > 0.000001f)
            {
                hipLine = hipLine.normalized;
                var now = spine[0].position - hips; now = now - hipLine * Vector3.Dot(now, hipLine);
                var want = chestUp - hipLine * Vector3.Dot(chestUp, hipLine);
                if (now.sqrMagnitude > 0.000001f && want.sqrMagnitude > 0.000001f)
                {
                    var tip = Quaternion.FromToRotation(now, want);
                    Pelvis.rotation = tip * Pelvis.rotation;
                    Pelvis.position = hips + tip * (Pelvis.position - hips);
                }
            }
            for (var i = 0; i < spine.Length; i++) Aim(spine[i], i + 1 < spine.Length ? spine[i + 1] : Neck, chestUp);
            // The module turns the chest further toward travel than the pelvis: the spine twists,
            // bone by bone, until the shoulders are across the module's.
            var across = RUpper.position - LUpper.position; across = across - chestUp * Vector3.Dot(across, chestUp);
            var wanted = joint[RShoulderJ] - joint[LShoulderJ]; wanted = wanted - chestUp * Vector3.Dot(wanted, chestUp);
            if (across.sqrMagnitude > 0.0001f && wanted.sqrMagnitude > 0.0001f)
            {
                var twist = (float)(Math.Atan2(Vector3.Dot(Vector3.Cross(across, wanted), chestUp), Vector3.Dot(across, wanted)) * 180.0 / Math.PI);
                for (var i = 0; i < spine.Length; i++) spine[i].rotation = SkateRide.Turn(twist / spine.Length, chestUp) * spine[i].rotation;
            }
            Aim(spine[0], Neck, joint[NeckJ] - spine[0].position);
            Aim(Neck, Head, joint[HeadJ] - joint[NeckJ]);
            var chestForward = joint[ChestForwardJ]; var headForward = joint[HeadForwardJ];
            var cf = chestForward - chestUp * Vector3.Dot(chestForward, chestUp); var hf = headForward - chestUp * Vector3.Dot(headForward, chestUp);
            if (cf.sqrMagnitude > 0.0001f && hf.sqrMagnitude > 0.0001f)
            {
                cf = cf.normalized; hf = hf.normalized;
                var turn = (float)(Math.Atan2(Vector3.Dot(Vector3.Cross(cf, hf), chestUp), Vector3.Dot(cf, hf)) * 180.0 / Math.PI);
                turn = SkateRide.Clamp(turn, -75f, 75f);
                Neck.rotation = SkateRide.Turn(turn * 0.4f, chestUp) * Neck.rotation;
                Head.rotation = SkateRide.Turn(turn * 0.6f, chestUp) * Head.rotation;
            }
            // With the chest folded far forward the rider would look at the board; the neck and the
            // head lift most of the way back toward where the board's up is.
            var foldAxis = Vector3.Cross(chestUp, up);
            var fold = (float)(Math.Atan2(foldAxis.magnitude, Vector3.Dot(chestUp, up)) * 180.0 / Math.PI);
            if (fold > HeadLiftFrom && foldAxis.sqrMagnitude > 0.0001f)
            {
                var lift = SkateRide.Clamp((fold - HeadLiftFrom) * HeadLiftShare, 0f, HeadLiftMost);
                Neck.rotation = SkateRide.Turn(lift * 0.35f, foldAxis) * Neck.rotation;
                Head.rotation = SkateRide.Turn(lift * 0.65f, foldAxis) * Head.rotation;
            }
            Note(Neck, joint[NeckJ], "neck");
            Limb(LUpper, LFore, LHand, joint[LHandJ], joint[LElbowJ], UpperArm, Forearm, "left hand");
            Limb(RUpper, RFore, RHand, joint[RHandJ], joint[RElbowJ], UpperArm, Forearm, "right hand");
        }

        Limb(LHip, LKnee, LFoot, joint[LAnkleJ], joint[LKneeJ], Thigh, Shin, "left ankle");
        Limb(RHip, RKnee, RFoot, joint[RAnkleJ], joint[RKneeJ], Thigh, Shin, "right ankle");
        var drop = up * (AnkleHeight - ToeHeight);
        Aim(LFoot, LToe, joint[LToeJ] - drop - LFoot.position);
        Aim(RFoot, RToe, joint[RToeJ] - drop - RFoot.position);
        Roll(LFoot, (LToe.position - LFoot.position).normalized, lSole, up);
        Roll(RFoot, (RToe.position - RFoot.position).normalized, rSole, up);
    }

    private static void Limb(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint, float upper, float lower, string name)
    {
        var mid = Bend(a.position, target, upper, lower, hint - a.position);
        Aim(a, b, mid - a.position);
        Aim(b, c, target - b.position);
        Note(c, target, name);
    }

    private static void Note(Transform bone, Vector3 target, string name)
    {
        var miss = Vector3.Distance(bone.position, target);
        if (miss > Miss) { Miss = miss; MissAt = name; }
    }
}
