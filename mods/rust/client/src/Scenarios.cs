using System;
using Shortcut.RustMod;
using UnityEngine;
using UnityEngine.InputSystem;

public static class Scenarios
{
    public static string Name = "", Phase = "";
    // A check rides from where the view points at its start, turned by this much: a way round an obstacle at the spawn.
    public static float YawOffset;
    public static bool Active { get { return Name != ""; } }
    // A check that plays a controller rides through the plugin's own input, getting on and off included.
    public static bool Plays { get { return Name == "pad"; } }
    private static float startAt = -1f, startGame, yaw0;
    private static int phase, next, lands;
    private static bool done;

    private static void Say(string m) { Out.Say("SCENARIO " + m); }

    public static void Abort() { Restore(); SkatePad.FeedEnd(); Name = ""; }

    private static void Restore()
    {
        SkateKeys.Scripted = false; SkateKeys.Reset();
        SkateCamera.Fixed = false; SkateCamera.Chase = true; SkateRide.Frozen = false; SkateRide.Manual = SkateRide.NoseManual = false; SkateRide.ShoveDeg = 0; SkateBoard.ShowLift = 0f;
        SkateHud.Staged = false; SkateHud.Clear();
    }

    public static void Update(float now)
    {
        if (done) return;
        if (startAt < 0f)
        {
            if (now - SkateRig.AwakeAt < 4f) return;
            startAt = now; startGame = Time.time; yaw0 = YawOffset != 0f ? SkateCamera.LookYaw + YawOffset : Runway(SkateCamera.LookYaw);
            SkateKeys.Scripted = true; SkateKeys.Reset(); SkateKeys.LookYaw = yaw0;
            Say(Name + " begins, view yaw=" + yaw0.ToString("F0") + " at " + Out.V1(SkateRig.LocalT.position));
        }
        // The checks that ride are timed in the game's own seconds: a client that draws few frames
        // runs slower than the clock, and presses timed by the clock would come before their turn.
        var s = now - startAt; var game = Time.time - startGame;
        SkateSfx.Listen();
        if (SkateRide.Lands != lands) { lands = SkateRide.Lands; Say("landed: " + SkateRide.LastAir + " | mode=" + SkateRide.Mode + " switch=" + SkateRide.Trick.Switch + " pos=" + Out.V1(SkateRide.Position)); }
        if (Name == "ride") Ride(game);
        else if (Name == "piece") Piece(s);
        else if (Name == "status") Status(game, s);
        else if (Name == "pose") Pose(s, now);
        else if (Name == "park") Park(s, now);
        else if (Name == "send") SendCheck(s);
        else if (Name == "place") Place(s, now);
        else if (Name == "pad") Pad(game, now);
        else Trick(game);
    }

    // The way with the longest run of dry, even ground from where the rider stands, the view's own
    // way first: a check that rides into the sea or a wall says nothing.
    public const float RunwayStep = 2f, RunwayLength = 44f, RunwayBump = 0.6f;
    public static float Runway(float lookYaw)
    {
        var from = SkateRig.LocalT.position; var best = lookYaw; var bestRun = -1f; var report = "";
        for (var i = 0; i < 8; i++)
        {
            var yaw = lookYaw + i * 45f; var dir = SkateRide.Dir(yaw); var run = 0f; var lastY = from.y;
            for (var d = RunwayStep; d <= RunwayLength; d += RunwayStep)
            {
                var at = from + dir * d; RaycastHit hit;
                if (!Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out hit, 8f, SkateRide.GroundMask, QueryTriggerInteraction.Ignore)) break;
                var y = hit.m_Point.y;
                if (y < SkateRide.SeaLevel + 0.3f || Math.Abs(y - lastY) > RunwayBump) break;
                lastY = y; run = d;
            }
            report += (i == 0 ? "" : ", ") + yaw.ToString("F0") + ":" + run.ToString("F0");
            if (run > bestRun + 0.5f) { bestRun = run; best = yaw; }
        }
        Say("runway " + bestRun.ToString("F0") + " m at yaw " + best.ToString("F0") + " (" + report + ")");
        return best;
    }

    private static bool Enter(int to, string label)
    {
        if (to == phase) return false;
        if (phase > 0 && Name == "pose")
            Say("  rider: applied=" + RiderRig.Applied + " refused=" + RiderRig.Refusals + (RiderRig.Error != "" ? " (" + RiderRig.Error + ")" : "") + " miss=" + RiderRig.Miss.ToString("F3") + " at " + RiderRig.MissAt
                + " | look=" + RiderRig.LookState);
        phase = to; Phase = phase + " " + label;
        Say("phase " + Phase + " | " + SkateRide.Status());
        return true;
    }

    private static void Finish()
    {
        done = true; Phase = "done";
        SkateRide.Dismount("the scenario ended"); Restore();
        System.IO.File.WriteAllText(System.IO.Path.Combine(BepInEx.Paths.PluginPath, "skate.done"), "done");
        var real = Time.realtimeSinceStartup - startAt;
        Say("DONE pace=" + (real > 0.5f ? (Time.time - startGame) / real : 1f).ToString("F2") + " top speed=" + SkateRide.Top.ToString("F1") + " pull-backs=" + SkateRide.Resets + " late calls=" + LateDriver.Calls + " pops=" + SkateRide.Pops + " lands=" + SkateRide.Lands + " bails=" + SkateRide.Bails
            + " refused presses=" + SkateRide.Refused + " score=" + SkateRide.Trick.TotalPoints + " grind scans=" + SkateGrind.Scans + " flicks=" + SkatePad.Flicks + " audio=" + SkateSfx.State + " | loudest output: " + SkateSfx.Heard);
    }

    private static void Ride(float s)
    {
        var to = s < 2f ? 1 : s < 4f ? 2 : s < 5.5f ? 3 : s < 7.5f ? 4 : s < 9.5f ? 5 : s < 11.5f ? 6 : s < 13f ? 7 : s < 15f ? 8 : 9;
        if (!Enter(to, to == 1 ? "push out" : to == 2 ? "coast" : to == 3 ? "brake" : to == 4 ? "turn round" : to == 5 ? "push back" : to == 6 ? "coast" : to == 7 ? "brake" : to == 8 ? "dismount" : "finish")) return;
        if (phase == 1) { SkateKeys.LookYaw = yaw0 + 180f; SkateRig.MountNow(yaw0 + 180f); }
        SkateKeys.Push = phase == 1 || phase == 5;
        SkateKeys.Brake = phase == 3 || phase == 7;
        SkateKeys.LookYaw = phase <= 3 ? yaw0 + 180f : yaw0;
        if (phase == 8) SkateRide.Dismount("the scenario stepped off");
        if (phase == 9) Finish();
    }

    // The console typist, seen: one piece asked for, the console left open so the server's answer
    // is on the screen for the screenshot; then on foot for a while, then done.
    private static void Piece(float s)
    {
        var to = s < 1f ? 1 : s < 12f ? 2 : 3;
        if (!Enter(to, to == 1 ? "ask for a bank" : to == 2 ? "wait for the answer" : "finish")) return;
        if (phase == 1) { SkateConsole.KeepOpen = true; Say("console: queued=" + SkateConsole.Queue("skate.piece bank", Time.realtimeSinceStartup)); }
        if (phase == 2) Say("console: typed=" + SkateConsole.Typed + " failed=" + SkateConsole.Failed + " last=" + SkateConsole.Last + " | " + SkatePieces.Line(Time.realtimeSinceStartup));
        if (phase == 3) Finish();
    }

    // What the server says about a rider it puts back: a ride long enough to be put back, then the
    // plugin's own report and the server's speed checks typed into the F1 console, which is left
    // open, one line at a time with time for a screenshot of each answer.
    public static readonly string[] StatusLines = { "skate.status", "antihack.speedhack_protection", "antihack.flyhack_protection", "oxide.plugins" };
    private static void Status(float game, float s)
    {
        var to = game < 1f ? 1 : game < 6f ? 2 : game < 7f ? 3 : 4 + (int)((game - 7f) / 8f);
        if (to > 4 + StatusLines.Length) to = 4 + StatusLines.Length;
        if (!Enter(to, to == 1 ? "get on" : to == 2 ? "push" : to == 3 ? "stop" : to < 4 + StatusLines.Length ? "type " + StatusLines[to - 4] : "finish")) return;
        if (phase == 1) SkateRig.MountNow(yaw0);
        SkateKeys.Push = phase == 2; SkateKeys.Brake = phase == 3;
        if (phase == 3) Say("ridden: put back " + SkateRide.Resets + " times, top " + SkateRide.Top.ToString("F1") + " m/s");
        if (phase == 4) { SkateRide.Dismount("the scenario stepped off to ask the server"); SkateConsole.KeepOpen = true; }
        if (phase >= 4 && phase < 4 + StatusLines.Length)
        {
            var ok = SkateConsole.Queue(StatusLines[phase - 4], Time.realtimeSinceStartup);
            Say("console: asked " + StatusLines[phase - 4] + " queued=" + ok + " typed=" + SkateConsole.Typed + " failed=" + SkateConsole.Failed);
        }
        if (phase == 4 + StatusLines.Length) Finish();
    }

    private struct View
    {
        public readonly string Label, State; public readonly float Bearing, Value;
        public View(string label, float bearing, string state, float value) { Label = label; Bearing = bearing; State = state; Value = value; }
    }

    // Value is the point in the push for a push view, the board's flip in degrees for a flip view,
    // the kind of grab for a grab view, the board's turn under the feet in degrees for a shove view
    // and the part of the bail that has passed for a bail view.
    private static readonly View[] views = {
        new View("stance, seen from the rider's front", 90f, "stance", 0f), new View("stance, seen along the board from behind", 180f, "stance", 0f),
        new View("stance, seen from the rider's back", 270f, "stance", 0f), new View("stance, seen along the board from ahead", 0f, "stance", 0f),
        new View("stance, seen from above", 90f, "above", 0f),
        new View("push, foot coming down", 90f, "push", 0.2f), new View("push, foot on the ground", 90f, "push", 0.45f),
        new View("push, foot on the ground, seen from behind", 180f, "push", 0.45f), new View("push, foot leaving the ground", 90f, "push", 0.7f),
        new View("ollie, just popped", 90f, "ollie", 0.9f), new View("ollie, at the top", 90f, "ollie", 0f), new View("ollie, coming down", 90f, "ollie", -0.8f),
        new View("air, half a kickflip", 120f, "flip", 180f), new View("air, quarter flip", 90f, "flip", 90f),
        new View("air with a backside grab", 90f, "grab", (float)SkateGrabKind.Backside), new View("air with a backside grab, seen from behind", 180f, "grab", (float)SkateGrabKind.Backside),
        new View("air with a frontside grab (indy)", 90f, "grab", (float)SkateGrabKind.Frontside), new View("air with a nose grab", 90f, "grab", (float)SkateGrabKind.Nose), new View("air with a melon", 90f, "grab", (float)SkateGrabKind.Melon),
        new View("air with a tailbone", 90f, "grab", (float)SkateGrabKind.Tailbone), new View("air with both hands on the board", 90f, "grab", (float)SkateGrabKind.Double),
        new View("air, the board a quarter round under the feet", 120f, "shove", 90f),
        new View("bail, stumbling", 90f, "bail", 0.2f), new View("bail, going down", 90f, "bail", 0.45f),
        new View("bail, down", 90f, "bail", 0.62f), new View("bail, getting up", 90f, "bail", 0.85f),
        new View("switch stance", 90f, "switch", 0f), new View("grind", 90f, "grind", 0f),
        new View("manual", 90f, "manual", 0f), new View("manual, seen from behind", 180f, "manual", 0f), new View("nose manual", 90f, "nose", 0f),
        new View("first person", 90f, "first", 0f),
        new View("score display: rolling", 180f, "hud", 1f), new View("score display: a trick landed", 180f, "hud", 2f),
        new View("score display: a combo of four", 180f, "hud", 3f), new View("score display: a long combo, in a manual", 180f, "hud", 4f),
        new View("score display: a grind", 180f, "hud", 5f), new View("score display: banked", 180f, "hud", 6f),
        new View("score display: a bail", 180f, "hud", 7f), new View("score display: held back by the server, keyboard", 180f, "hud", 8f),
        new View("score display: controller", 180f, "hud", 9f),
        new View("push with the front foot (mongo), foot on the ground", 90f, "mongo", 0.45f), new View("mongo push, seen from behind", 180f, "mongo", 0.45f),
        new View("push rolling tail first (fakie), foot on the ground", 90f, "fakie", 0.45f),
        new View("no comply / boneless, the front foot planted", 90f, "plant", 0f), new View("front foot planted, seen from behind", 180f, "plant", 0f),
        new View("air with a backside grab, just folding in", 90f, "grabin", (float)SkateGrabKind.Backside),
        new View("board stall on an edge", 90f, "stall", (float)SkateGrindKind.BoardStall), new View("tail stall on an edge", 90f, "stall", (float)SkateGrindKind.TailStall),
        new View("nose stall on an edge", 90f, "stall", (float)SkateGrindKind.NoseStall), new View("handplant", 90f, "stall", (float)SkateGrindKind.Handplant),
        new View("handplant, seen from behind", 180f, "stall", (float)SkateGrindKind.Handplant),
        new View("handplant, bending down to the edge", 90f, "plantup", 0.22f), new View("handplant, swinging up", 90f, "plantup", 0.5f),
        new View("handplant, swinging up, seen from behind", 180f, "plantup", 0.5f), new View("handplant, nearly over", 90f, "plantup", 0.78f),
        new View("5-0 grind", 90f, "grind", (float)SkateGrindKind.FiveO), new View("nosegrind", 90f, "grind", (float)SkateGrindKind.Nosegrind),
        new View("smith grind", 90f, "grind", (float)SkateGrindKind.Smith), new View("feeble grind", 90f, "grind", (float)SkateGrindKind.Feeble),
        new View("crooked grind", 90f, "grind", (float)SkateGrindKind.Crooked), new View("boardslide, seen along the rail", 180f, "grind", (float)SkateGrindKind.Boardslide),
        new View("tailslide", 90f, "grind", (float)SkateGrindKind.Tailslide),
        new View("bluntslide", 90f, "grind", (float)SkateGrindKind.Bluntslide),
        new View("nosebluntslide", 90f, "grind", (float)SkateGrindKind.Nosebluntslide),
        new View("moving, pushing off", 115f, "move", 0f), new View("moving, turning round", 115f, "turn", 0f),
        new View("finish", 90f, "end", 0f) };
    private static float stepAt, nearAt, hudAt;
    private static bool keyWas;

    // The score display in each of its states, with made-up numbers. What fades is shown again
    // every few seconds, so that it is there whenever the view is looked at.
    private static void Hud(int state, float now)
    {
        var again = now - hudAt > 2.6f;
        if (again) hudAt = now;
        var s = new SkateHud.Shown(); s.Riding = true; s.Cap = SkateRide.MaxSpeed; s.Score = 12450; s.Speed = 6.2f;
        if (state == 1) { s.Speed = 8.1f; if (again) SkateHud.StageLine(new string[0]); }
        if (state == 2) { s.Base = 300; s.Count = 1; s.Window = 0.2f; if (again) { SkateHud.StageLine(new string[0]); SkateHud.Landed("Backside 180 Kickflip Grab", 300); } }
        if (state == 3)
        {
            s.Base = 900; s.Count = 4; s.Window = 0.7f; s.Switch = true;
            if (again) { SkateHud.StageLine(new[] { "Kickflip", "Manual", "Ollie Grab" }); SkateHud.Landed("Frontside 180 Heelflip", 250); }
        }
        if (state == 4)
        {
            s.Base = 1650; s.Count = 6; s.Live = "MANUAL"; s.LiveSeconds = 2.3f; s.LivePoints = 230;
            if (again) SkateHud.StageLine(new[] { "Ollie", "Kickflip", "Grind", "Backside 180", "Heelflip Grab", "Frontside 180 Kickflip" });
        }
        if (state == 5) { s.Base = 400; s.Count = 2; s.Live = "GRIND"; s.LiveSeconds = 1.4f; s.LivePoints = 140; if (again) SkateHud.StageLine(new[] { "Kickflip", "Frontside 180" }); }
        if (state == 6) { s.Score = 15750; if (again) SkateHud.Banked(3300, 15750); }
        if (state == 7) { if (again) SkateHud.Bailed(2400); }
        if (state == 8) { s.Cap = 2.6f; s.Speed = 2.6f; s.Hints = true; if (again) SkateHud.StageLine(new string[0]); }
        if (state == 9) { s.Speed = 0f; s.Hints = true; s.Pad = true; if (again) SkateHud.StageLine(new string[0]); }
        SkateHud.Stage = s; SkateHud.Staged = true;
    }

    private static void Pose(float s, float now)
    {
        var advance = phase == 0;
        try { var kb = Keyboard.current; var k = kb != null && SkateKeys.Down(kb.kKey); if (k && !keyWas) advance = true; keyWas = k; }
        catch (Exception) { }
        var state = phase == 0 ? "" : views[phase - 1].State;
        var moving = state == "move" || state == "turn";
        if (now - stepAt > (moving ? 7f : state == "hud" ? 12f : 45f)) advance = true;
        if (state == "hud") Hud((int)views[phase - 1].Value, now);
        // The moving views hold walking pace, which the server accepts without its skate plugin.
        if (moving) SkateKeys.Push = SkateRide.Speed < 2.4f;
        if (state == "bail") SkateRide.ModeAt = now - views[phase - 1].Value * SkateRide.BailSeconds;
        if (now >= nearAt) { nearAt = now + 1f; RiderRig.Near(); }
        if (!advance) return;
        stepAt = now;
        var v = views[phase];
        var still = v.State != "move" && v.State != "turn" && v.State != "end";
        Enter(phase + 1, v.Label + (still ? "   (K: next)" : ""));
        if (phase == 1) { SkateRig.MountNow(yaw0); SkateRide.Frozen = true; }
        if (v.State == "end") { Finish(); return; }
        hudAt = -99f; SkateHud.Clear(); SkateHud.Staged = false;
        if (v.State == "hud") Hud((int)v.Value, now);
        SkateCamera.Fixed = v.State != "first"; SkateCamera.Chase = v.State != "first";
        SkateCamera.Bearing = v.Bearing;
        SkateCamera.FixedHeight = v.State == "above" ? 2.9f : 1.15f; SkateCamera.FixedDistance = v.State == "above" ? 1.6f : 2.7f;
        var air = v.State == "flip" || v.State == "grab" || v.State == "shove" || v.State == "plant" || v.State == "grabin" || v.State == "ollie";
        SkateRide.Mode = air ? RideMode.Air : v.State == "bail" ? RideMode.Bail : v.State == "grind" || v.State == "stall" ? RideMode.Grind : RideMode.Ground;
        var pushes = v.State == "push" || v.State == "mongo" || v.State == "fakie";
        SkateRide.PushPhase = pushes ? v.Value : 0f;
        SkateRide.PushMongo = v.State == "mongo";
        // The leading foot pushes in a mongo push and in a push rolling tail first.
        SkateRide.PushLead = v.State == "mongo" || v.State == "fakie";
        SkateRide.Speed = v.State == "fakie" ? -2f : 0f;
        SkateRide.PlantedUntil = v.State == "plant" ? now + 999f : -99f;
        SkateRide.Stalling = v.State == "stall";
        SkateRide.GrindKind = v.State == "stall" || v.State == "grind" ? (SkateGrindKind)(int)v.Value : SkateGrindKind.None;
        if (v.State == "grabin") RiderRig.GrabFor(0.09f);
        RiderRig.PlantFor(v.State == "plantup" ? v.Value : -1f);
        if (v.State == "plantup") { SkateRide.Stalling = true; SkateRide.GrindKind = SkateGrindKind.Handplant; SkateRide.Mode = RideMode.Grind; }
        SkateRide.FlipDeg = v.State == "flip" ? (int)v.Value : 0;
        SkateRide.Jumped = v.State == "ollie"; SkateRide.VSpeed = v.State == "ollie" ? v.Value * SkateRide.OlliePop : 0f;
        SkateRide.Grab = v.State == "grab" || v.State == "grabin" ? (SkateGrabKind)(int)v.Value : SkateGrabKind.None;
        SkateRide.ShoveDeg = v.State == "shove" ? v.Value : 0;
        SkateRide.Manual = v.State == "manual";
        SkateRide.NoseManual = v.State == "nose";
        SkateBoard.ShowLift = air ? 0.7f : 0f;
        SkateRide.Trick = new SkateTrickState(v.State == "switch");
        if (v.State == "bail") SkateRide.ModeAt = now - v.Value * SkateRide.BailSeconds;
        if (v.State == "move") { SkateRide.Frozen = false; SkateRide.Mode = RideMode.Ground; }
        if (v.State == "turn") SkateKeys.LookYaw = yaw0 + 180f;
    }

    // The park models, one at a time 6 m ahead of the rider, seen from a fixed camera (K steps on),
    // then a ride up the bank: the models' reading, drawing and colliders, without any server piece.
    private static readonly SkateParks.Kind[] parkKinds = { SkateParks.Kind.Bank, SkateParks.Kind.Kicker, SkateParks.Kind.QuarterPipe, SkateParks.Kind.LedgeLow,
        SkateParks.Kind.LedgeHigh, SkateParks.Kind.LongLedge, SkateParks.Kind.Funbox, SkateParks.Kind.Halfpipe };
    private static Vector3 parkAt;
    // Each model is held for ParkSeconds; the check runs on its own, no key steps it.
    public const float ParkSeconds = 20f;
    private static void Park(float s, float now)
    {
        var advance = phase == 0;
        var riding = phase == parkKinds.Length + 1;
        if (now - stepAt > (riding ? 9f : ParkSeconds)) advance = true;
        if (riding) SkateKeys.Push = SkateRide.Speed < 3.5f && now - stepAt < 4f;
        if (now >= nearAt) { nearAt = now + 1f; RiderRig.Near(); }
        if (!advance) return;
        stepAt = now;
        if (phase == parkKinds.Length + 1) { Finish(); return; }
        if (phase == 0)
        {
            SkateRig.MountNow(yaw0); SkateRide.Frozen = true;
            parkAt = SkateRig.LocalT.position + SkateRide.Dir(yaw0) * 6f;
            // From behind the rider, looking along the way to the model.
            SkateCamera.Fixed = true; SkateCamera.Chase = true; SkateCamera.Bearing = 180f; SkateCamera.FixedHeight = 3.2f; SkateCamera.FixedDistance = 9f;
        }
        if (phase < parkKinds.Length)
        {
            var kind = parkKinds[phase];
            var big = kind == SkateParks.Kind.Halfpipe || kind == SkateParks.Kind.QuarterPipe;
            SkateCamera.FixedDistance = big ? 16f : 9f; SkateCamera.FixedHeight = big ? 5f : 3.2f;
            // The halfpipe's origin is its flat's centre: further out, so the near transition is not on the rider.
            var at = kind == SkateParks.Kind.Halfpipe ? parkAt + SkateRide.Dir(yaw0) * 9f : parkAt;
            var line = SkateParks.PreviewAt(kind, at, yaw0);
            Enter(phase + 1, SkateParks.KindName(kind));
            Say("park " + SkateParks.KindName(kind) + " " + line + " | " + SkateParks.Line());
            return;
        }
        // The ride: the bank ahead, the rider pushing into it.
        SkateParks.PreviewAt(SkateParks.Kind.Bank, parkAt, yaw0);
        SkateCamera.Fixed = false; SkateCamera.Chase = true; SkateRide.Frozen = false; SkateRide.Mode = RideMode.Ground;
        Enter(phase + 1, "riding up the bank");
    }

    // A command sent to the server by the network, not typed: a chat line that shows on the screen
    // when it arrives, then the plugin's own status command.
    private static void SendCheck(float s)
    {
        if (phase == 0)
        {
            Enter(1, "send a chat line");
            var ok = SkateConsole.Send("chat.say skate client console check " + DateTime.Now.ToString("HH:mm:ss"));
            Say("send: " + (ok ? "sent" : "not sent") + " | " + SkateConsole.SendState);
            return;
        }
        if (phase == 1 && s > 3f)
        {
            Enter(2, "send skate.status");
            var ok = SkateConsole.Send("skate.status");
            Say("send: " + (ok ? "sent" : "not sent") + " | " + SkateConsole.SendState);
            return;
        }
        if (phase == 2 && s > 12f) Finish();
    }

    // The end-to-end placement: `skate.place bank` sent to the server where the player looks, then
    // the server's native blocks are waited for and the model drawn over them is reported. Needs
    // ShortcutSkate 0.3.6 or later on the server and open ground in view; says so when nothing came.
    public const float PlaceWait = 20f;
    private static int placeAssemblies, placeDrawn;
    private static void Place(float s, float now)
    {
        if (phase == 0)
        {
            Enter(1, "send skate.place bank");
            placeAssemblies = SkateParks.Assemblies; placeDrawn = SkateParks.Drawn;
            var ok = SkateConsole.Send("skate.place bank");
            Say("place: " + (ok ? "sent skate.place bank" : "not sent") + " | " + SkateConsole.SendState);
            if (!ok) { Enter(3, "nothing sent"); return; }
            return;
        }
        if (phase == 1)
        {
            if (SkateParks.Assemblies > placeAssemblies)
            {
                Enter(2, "a piece appeared");
                Say("place: a piece appeared " + (s).ToString("F1") + " s after the send: " + SkateParks.Line() + (SkateParks.Drawn > placeDrawn ? " (drawn)" : " (not drawn: " + SkateParks.Status + ")"));
                return;
            }
            if (s > PlaceWait) { Enter(3, "no piece appeared"); Say("place: no piece appeared within " + (int)PlaceWait + " s (ShortcutSkate 0.3.6 or later needed on the server, open ground in view; the server's answer is in F1) | " + SkateParks.Line()); }
            return;
        }
        if (phase == 2 && s > PlaceWait + 8f) Finish();
        if (phase == 3 && s > PlaceWait + 4f) Finish();
    }

    // The ride with a controller, without one: its states are played into the plugin as the reader
    // outside the game would share them, and the keyboard is left to itself. The pace is walking
    // pace, which the server accepts without its skate plugin.
    private const string OlliePath = "0:0,-100 250:0,-20 262:0,60 274:0,100 330:0,0", KickflipPath = "0:0,-100 250:-20,-30 262:-55,45 274:-70,70 330:0,0",
        HeelflipPath = "0:0,-100 250:20,-30 262:55,45 274:70,70 330:0,0",
        // Held back and rolled a quarter round the rim to the left: the board turns half round under the feet.
        ShovePath = "0:0,-100 250:0,-100 262:-38,-92 274:-71,-71 286:-92,-38 298:-100,0 340:-100,0 380:0,0",
        // Held at the back right and flicked straight across to the front left: the same with a kickflip in it.
        VarialPath = "0:71,-71 250:71,-71 262:20,-20 274:-45,45 286:-64,64 300:-64,64 340:0,0";
    private static readonly float[] padAt = { 1f, 2f, 4.5f, 5.5f, 6.5f, 8f, 10.5f, 13f, 15.5f, 16.9f, 17.6f, 17.75f, 18.4f, 20.5f, 23f, 25.4f, 25.5f, 25.95f, 28f, 28.15f, 28.8f, 30.5f, 32f, 33.5f, 35.5f, 36.5f };
    private static readonly string[] padDo = { "y", "push", "right", "left", "straight", "ollie", "kickflip", "heelflip", "manual", "release", "ollie", "grab", "release", "shove", "varial", "left", "ollie", "straight",
        "ollie", "nose grab", "release", "nose manual", "release", "brake", "y", "finish" };
    private static long padUs = 1000000;
    private static float padLast, padX, padRightY, padTrigger, padTapUntil;
    private static bool padPush, padBrake;

    private static void Pad(float s, float now)
    {
        if (phase == 0)
        {
            Enter(1, "controller");
            SkateKeys.Scripted = false; padLast = now;
            SkateCamera.Fixed = false; SkateCamera.Chase = true;
        }
        padUs += (long)((now - padLast) * 1000000f); padLast = now;
        while (next < padAt.Length && s >= padAt[next])
        {
            var what = padDo[next++];
            Phase = (next + 1) + " " + what;
            Say("controller " + what + " | " + SkateRide.Status() + " rides=" + (SkateKeys.Pad ? "controller" : "keyboard") + " flicks=" + SkatePad.Flicks);
            if (what == "y") padTapUntil = now + 0.12f;
            if (what == "push") padPush = true;
            if (what == "right") padX = 0.7f;
            if (what == "left") padX = -0.7f;
            if (what == "straight") padX = 0f;
            if (what == "ollie") Flick(OlliePath, now);
            if (what == "kickflip") Flick(KickflipPath, now);
            if (what == "heelflip") Flick(HeelflipPath, now);
            if (what == "shove") Flick(ShovePath, now);
            if (what == "varial") Flick(VarialPath, now);
            if (what == "manual") padRightY = -0.45f;
            if (what == "nose manual") padRightY = 0.45f;
            if (what == "grab") padTrigger = 1f;
            // The trigger first, then the stick: a stick that is already there would still be the board's.
            if (what == "nose grab") { padTrigger = 1f; SkatePad.Feed(padUs, 0, 0f, padTrigger, padX, 0f, 0f, 0f, now); padRightY = -0.9f; }
            if (what == "release") { padRightY = 0f; padTrigger = 0f; }
            if (what == "brake") { padBrake = true; padPush = false; }
            if (what == "finish") { SkatePad.FeedEnd(); Finish(); return; }
        }
        var buttons = (padPush && SkateRide.On && SkateRide.Mode == RideMode.Ground && SkateRide.Speed < 2.2f ? SkatePad.A : 0) | (padBrake ? SkatePad.B : 0) | (now < padTapUntil ? SkatePad.Y : 0);
        SkatePad.Feed(padUs, buttons, 0f, padTrigger, padX, 0f, 0f, padRightY, now);
    }

    // The right stick along a path, milliseconds:x,y in percent, played between two frames.
    private static void Flick(string path, float now)
    {
        var start = padUs;
        foreach (var part in path.Split(' '))
        {
            var a = part.Split(':'); var xy = a[1].Split(',');
            padUs = start + long.Parse(a[0]) * 1000;
            SkatePad.Feed(padUs, 0, 0f, padTrigger, padX, 0f, int.Parse(xy[0]) / 100f, int.Parse(xy[1]) / 100f, now);
        }
    }

    private static readonly float[] trickAt = { 3f, 6f, 8.9f, 9f, 9.4f, 11f, 14f, 16.9f, 17f, 17.5f, 20f, 20.12f, 23f, 23.45f, 25f, 27f, 29f, 30.6f, 32.2f, 33.5f, 35.5f, 35.5f, 37.5f, 39.5f, 41.5f, 43.5f, 43.6f, 46f, 48.5f };
    private static readonly string[] trickDo = { "ollie", "ollie+kick", "left", "ollie", "release", "turn", "ollie+heel", "right+grab", "ollie", "release", "ollie+kick", "kick", "ollie", "kick",
        "ollie+shove", "ollie+kick+shove", "ollie+kick+shove+shove", "manual", "release", "ollie+shove+shove", "ollie", "revert", "nollie", "nocomply ollie", "boneless ollie", "ollie", "body", "brake", "finish" };
    private static bool wantRevert; private static int landsAtRevert; private static float revertRelease = -1f;

    private static void Trick(float s)
    {
        if (phase == 0)
        {
            Enter(1, "riding");
            SkateRig.MountNow(yaw0);
            SkateCamera.Fixed = true; SkateCamera.Bearing = 100f; SkateCamera.FixedDistance = 4.4f; SkateCamera.FixedHeight = 1.4f;
        }
        SkateKeys.Push = !SkateKeys.Brake && SkateRide.Mode == RideMode.Ground && Math.Abs(SkateRide.Speed) < 2.4f;
        // The revert is a turn pressed the moment the board is down again: timed by the landing, not the clock.
        if (wantRevert && SkateRide.Mode == RideMode.Ground && SkateRide.Lands > landsAtRevert)
        { wantRevert = false; SkateKeys.Spin = 1f; SkateKeys.SpinPressed = true; revertRelease = s + 0.15f; Say("press revert | " + SkateRide.Status()); }
        if (revertRelease > 0f && s >= revertRelease) { revertRelease = -1f; SkateKeys.Spin = 0f; }
        while (next < trickAt.Length && s >= trickAt[next])
        {
            var what = trickDo[next++];
            Phase = (next + 1) + " " + what;
            Say("press " + what + " | " + SkateRide.Status());
            var flip = (what.Contains("kick") ? 1 : 0) - (what.Contains("heel") ? 1 : 0);
            // With the pop: the flip and the board's turn under the feet, a backside half turn for each "shove".
            if (what.Contains("ollie"))
            {
                SkateKeys.JumpPressed = true; SkateKeys.PopKind = what.StartsWith("nollie") ? SkatePopKind.Nollie : what.StartsWith("nocomply") ? SkatePopKind.NoComply : what.StartsWith("boneless") ? SkatePopKind.Boneless : SkatePopKind.Ollie; SkateKeys.PopScale = 1f; SkateKeys.PopFlip = flip;
                SkateKeys.PopShove = what.EndsWith("shove+shove") ? -360f : what.Contains("shove") ? -180f : 0f;
            }
            else if (what == "revert") { wantRevert = true; landsAtRevert = SkateRide.Lands; }
            else if (what == "body") SkateKeys.BodyVarial = 180f;
            else SkateKeys.LateFlip += flip;
            if (what.Contains("left")) { SkateKeys.Spin = -1f; SkateKeys.SpinPressed = true; }
            if (what.Contains("right")) { SkateKeys.Spin = 1f; SkateKeys.SpinPressed = true; }
            if (what.Contains("grab")) SkateKeys.Grab = SkateGrabKind.Legacy;
            if (what == "manual") SkateKeys.Manual = true;
            if (what == "release") { SkateKeys.Spin = 0f; SkateKeys.Grab = SkateGrabKind.None; SkateKeys.Manual = false; }
            if (what == "turn") SkateKeys.LookYaw = yaw0 + 180f;
            if (what == "brake") SkateKeys.Brake = true;
            if (what == "finish") Finish();
        }
    }
}
