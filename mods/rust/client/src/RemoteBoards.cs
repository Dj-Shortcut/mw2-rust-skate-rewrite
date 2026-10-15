// Other players on boards. The server sends this client only where each other player is, not
// that they ride, so a player seen travelling along the ground faster than anyone runs (the same
// speed the server takes as riding) is drawn on a board of its own: flat on the ground under the
// feet, nose along the travel, its wheels rolling and its rolling sound coming from it. A player
// in a seat or on a horse is left alone; a board stays through a jump and goes once the player
// has been slow for a moment.
using System;
using System.Collections.Generic;
using UnityEngine;

public static class RemoteBoards
{
    public const float ShowSpeed = 7f, HideSpeed = 4.5f, HideAfter = 1.2f, LookEvery = 1f, GroundEvery = 0.1f;
    public const float GroundAbove = 0.5f, GroundReach = 1.6f, OnGround = 0.4f, Window = 0.2f, Jump = 20f, Reach = 200f, Hearing = 35f;
    public const int Most = 16;
    public static int Shown;
    private static readonly Dictionary<int, Rider> riders = new Dictionary<int, Rider>();
    private static readonly List<int> gone = new List<int>();
    private static float nextLook = -1f;
    private static bool failed, noTemplate;
    private static void Say(string m) { Out.Say("REMOTE " + m); }

    private class Rider
    {
        public int Id; public Transform T;
        public GameObject Board; public Transform[] Wheels = new Transform[0]; public Vector3[] Axes = new Vector3[0]; public AudioSource Sound;
        public Vector3 Last, Velocity, Ground, Normal = Vector3.up, Nose = Vector3.forward;
        public float LastAt = -1f, GroundAt = -1f, SlowSince = -1f, Spin, Top;
        public bool Grounded, Mounted, On;
    }

    public static void Frame(float now, float frame)
    {
        if (failed) return;
        try
        {
            if (now >= nextLook) { nextLook = now + LookEvery; Look(); }
            Shown = 0;
            foreach (var r in riders.Values)
            {
                if (r.T == null) { gone.Add(r.Id); continue; }
                Move(r, now, frame);
                if (r.On) Shown++;
            }
            for (var i = 0; i < gone.Count; i++) Drop(gone[i]);
            gone.Clear();
        }
        catch (Exception e) { failed = true; Say("threw " + e.GetType().Name + ": " + e.Message + "; other players' boards are off"); Clear(); }
    }

    // Every other player near enough, looked up again now and then: players come and go.
    private static void Look()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var cp = cam.transform.position;
        var local = SkateRig.Local == null ? 0 : SkateRig.Local.GetInstanceID();
        var seen = new HashSet<int>();
        var players = UnityEngine.Object.FindObjectsOfType<BasePlayer>();
        for (var i = 0; i < players.Length; i++)
        {
            var p = players[i];
            if (p == null) continue;
            var id = p.GetInstanceID();
            if (id == local || (p.transform.position - cp).sqrMagnitude > Reach * Reach) continue;
            seen.Add(id);
            Rider r;
            if (!riders.TryGetValue(id, out r))
            {
                if (riders.Count >= Most) continue;
                r = new Rider { Id = id, T = p.transform };
                riders[id] = r;
            }
            var parent = r.T.parent;
            r.Mounted = parent != null && parent.GetComponentInParent<BaseMountable>() != null;
        }
        foreach (var id in riders.Keys) if (!seen.Contains(id)) gone.Add(id);
        for (var i = 0; i < gone.Count; i++) Drop(gone[i]);
        gone.Clear();
    }

    private static void Move(Rider r, float now, float frame)
    {
        var at = r.T.position;
        if (r.LastAt < 0f) { r.Last = at; r.LastAt = now; return; }
        var dt = now - r.LastAt;
        if (dt <= 0f) return;
        var step = at - r.Last;
        r.Last = at; r.LastAt = now;
        // A respawn or a teleport is no ride.
        if (step.magnitude > Jump) { r.Velocity = Vector3.zero; Hide(r, "jumped away"); return; }
        var seen = new Vector3(step.x, 0f, step.z) / dt;
        r.Velocity += (seen - r.Velocity) * (1f - (float)Math.Exp(-dt / Window));
        var speed = r.Velocity.magnitude;
        if (now - r.GroundAt >= GroundEvery) { r.GroundAt = now; Feel(r, at); }

        if (!r.On)
        {
            if (speed > ShowSpeed && r.Grounded && !r.Mounted) Show(r, speed);
            if (!r.On) return;
        }
        if (r.Mounted) { Hide(r, "seated"); return; }
        if (speed < HideSpeed) { if (r.SlowSince < 0f) r.SlowSince = now; else if (now - r.SlowSince > HideAfter) { Hide(r, "slow"); return; } }
        else r.SlowSince = -1f;
        if (speed > r.Top) r.Top = speed;

        if (speed > 0.5f) r.Nose = r.Velocity / speed;
        var up = r.Grounded ? r.Normal : Vector3.up;
        var nose = Vector3.ProjectOnPlane(r.Nose, up);
        if (nose.sqrMagnitude < 0.0001f) nose = r.Nose;
        var want = Quaternion.LookRotation(nose.normalized, up);
        var bt = r.Board.transform;
        bt.rotation = Quaternion.Slerp(bt.rotation, want, SkateRide.Clamp(frame * 12f, 0f, 1f));
        bt.position = r.Grounded ? new Vector3(at.x, Math.Max(at.y, r.Ground.y), at.z) : at;
        r.Spin = (r.Spin + speed / SkateBoard.WheelRadius * 57.29578f * frame) % 360f;
        for (var i = 0; i < r.Wheels.Length; i++) if (r.Wheels[i] != null) r.Wheels[i].localRotation = Quaternion.AngleAxis(r.Spin, r.Axes[i]);
        if (r.Sound != null)
        {
            r.Sound.volume = r.Grounded ? SkateRide.Clamp(speed / 9f, 0f, 1f) * 0.55f : 0f;
            r.Sound.pitch = 0.75f + SkateRide.Clamp(speed / SkateRide.MaxSpeed, 0f, 1f) * 0.55f;
        }
    }

    // The ground under the player, not the player itself or anyone else.
    private static void Feel(Rider r, Vector3 at)
    {
        RaycastHit hit;
        r.Grounded = Physics.Raycast(at + Vector3.up * GroundAbove, Vector3.down, out hit, GroundAbove + GroundReach, SkateRide.GroundMask, QueryTriggerInteraction.Ignore)
            && at.y - hit.m_Point.y < OnGround;
        if (!r.Grounded) return;
        r.Ground = hit.m_Point;
        r.Normal = hit.m_Normal.y > 0.3f ? hit.m_Normal : Vector3.up;
    }

    private static void Show(Rider r, float speed)
    {
        if (r.Board == null && !Make(r)) return;
        r.Board.SetActive(true);
        if (r.Sound != null && !r.Sound.isPlaying) r.Sound.Play();
        r.On = true; r.SlowSince = -1f; r.Top = speed;
        if (speed > 0.5f) r.Nose = r.Velocity / speed;
        r.Board.transform.rotation = Quaternion.LookRotation(r.Nose, r.Grounded ? r.Normal : Vector3.up);
        Say("board on player " + r.Id + " at " + Out.V1(r.T.position) + " speed=" + speed.ToString("F1"));
    }

    private static void Hide(Rider r, string why)
    {
        if (!r.On) return;
        r.On = false; r.SlowSince = -1f;
        if (r.Board != null) r.Board.SetActive(false);
        if (r.Sound != null) r.Sound.volume = 0f;
        Say("board off player " + r.Id + " (" + why + ") top=" + r.Top.ToString("F1"));
    }

    // A copy of this client's own board, which stays the rider's: its moving parts are found in
    // the copy by where they sit, so its trucks and wheels are left to it.
    private static bool Make(Rider r)
    {
        var template = SkateRig.Board;
        if (template == null) { if (!noTemplate) { noTemplate = true; Say("no board of our own to copy yet"); } return false; }
        var copy = UnityEngine.Object.Instantiate<GameObject>(template);
        copy.name = "skate_board_remote";
        UnityEngine.Object.DontDestroyOnLoad(copy);
        var wheels = SkateBoard.WheelParts; var axes = SkateBoard.WheelAxes;
        var found = new List<Transform>(); var foundAxes = new List<Vector3>();
        for (var i = 0; i < wheels.Length && i < axes.Length; i++)
        {
            var w = Same(wheels[i], template.transform, copy.transform);
            if (w != null) { found.Add(w); foundAxes.Add(axes[i]); }
        }
        r.Wheels = found.ToArray(); r.Axes = foundAxes.ToArray();
        r.Board = copy;
        SkateSfx.Start();
        var clip = SkateSfx.RollClip;
        if (clip != null)
        {
            var s = copy.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.volume = 0f; s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 3f; s.maxDistance = Hearing;
            r.Sound = s;
        }
        Say("made a board for player " + r.Id + ": " + r.Wheels.Length + " wheels, sound=" + (r.Sound != null));
        return true;
    }

    private static Transform Same(Transform part, Transform from, Transform to)
    {
        if (part == null) return null;
        var path = new List<int>();
        var t = part;
        while (t != null && t != from) { path.Add(t.GetSiblingIndex()); t = t.parent; }
        if (t == null) return null;
        var c = to;
        for (var i = path.Count - 1; i >= 0; i--)
        {
            if (path[i] >= c.childCount) return null;
            c = c.GetChild(path[i]);
        }
        return c;
    }

    private static void Drop(int id)
    {
        Rider r;
        if (!riders.TryGetValue(id, out r)) return;
        riders.Remove(id);
        if (r.On) Say("board off player " + id + " (gone) top=" + r.Top.ToString("F1"));
        try { if (r.Board != null) UnityEngine.Object.Destroy(r.Board); } catch (Exception) { }
    }

    public static void Clear()
    {
        var ids = new List<int>(riders.Keys);
        for (var i = 0; i < ids.Count; i++) Drop(ids[i]);
        Shown = 0;
    }

    public static string Status()
    {
        return riders.Count + " players near, " + Shown + " on boards" + (failed ? ", off after an error" : "");
    }
}
