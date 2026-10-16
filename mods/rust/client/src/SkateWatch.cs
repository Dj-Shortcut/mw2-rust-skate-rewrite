// A watch on what is drawn, frame to frame: the board's nose and deck angles, the board's place
// against the player, and the rider's feet, hands and head. A step larger than a frame could
// carry at any speed is a snap the player sees, and it is logged with what was going on, so a
// session's log names its own visual bugs ("SKATE snap ..."), which `zz last` counts. At most a
// few hundred a ride, so the log stays readable.
using System;
using Shortcut.RustMod;
using UnityEngine;

public static class SkateWatch
{
    public const float NoseStep = 30f, DeckStep = 30f, BoardStep = 0.3f, FootStep = 0.2f, HandStep = 0.35f, HeadStep = 0.2f;
    public const int MostSaid = 300;
    public static int Snaps, Said;
    private static bool had;
    private static float nose, deck, at = -99f;
    private static Vector3 board, lFoot, rFoot, lHand, rHand, head, neck;
    private static int applied;
    private static RideMode mode;
    private static string trick = "";

    // A new ride: the frames before it are not compared, and its snaps are said afresh (Snaps keeps
    // counting over the session, for the summary).
    public static void Reset() { had = false; Said = 0; }

    // Called once a frame after the board and the rider are placed. Skipped across a pause and
    // when the ride is off or bailed, where big steps are the point.
    public static void Frame(Transform player, GameObject boardGo, float now)
    {
        if (!SkateRide.On || player == null) { had = false; return; }
        var fresh = !had || now - at > 0.25f || SkateRide.Mode == RideMode.Bail || mode == RideMode.Bail;
        var p = player.position;
        var noseNow = SkateRide.NoseYaw; var deckNow = SkateRide.DeckYaw;
        var boardNow = boardGo != null && boardGo.activeSelf ? boardGo.transform.position - p : Vector3.zero;
        var rig = RiderRig.Bound && RiderRig.LFoot != null && RiderRig.RFoot != null && RiderRig.LHand != null && RiderRig.RHand != null && RiderRig.Head != null;
        Vector3 lf = rig ? RiderRig.LFoot.position - p : Vector3.zero, rf = rig ? RiderRig.RFoot.position - p : Vector3.zero;
        Vector3 lh = rig ? RiderRig.LHand.position - p : Vector3.zero, rh = rig ? RiderRig.RHand.position - p : Vector3.zero, hd = rig ? RiderRig.Head.position - p : Vector3.zero;
        var nk = rig && RiderRig.Neck != null ? RiderRig.Neck.position - p : Vector3.zero;
        if (!fresh)
        {
            var line = "";
            // The steps are those of a frame at 60 a second; a slower frame may carry more.
            var k = SkateRide.Clamp((now - at) * 60f, 1f, 4f);
            var dn = SkateRide.Delta(nose, noseNow); var dd = SkateRide.Delta(deck, deckNow);
            if (Math.Abs(dn) > NoseStep * k) line += " nose " + dn.ToString("F0") + " deg";
            // A shove-it turns the deck under the feet as fast as the trick does: not a snap.
            var deckStep = SkateRide.Mode == RideMode.Air ? DeckStep + (float)SkateMotion.ShoveRate / 60f + (float)SkateMotion.RotationRate / 60f : DeckStep;
            if (Math.Abs(dd) > deckStep * k) line += " deck " + dd.ToString("F0") + " deg";
            var db = (boardNow - board).magnitude;
            if (boardNow.sqrMagnitude > 0f && board.sqrMagnitude > 0f && db > BoardStep * k) line += " board " + db.ToString("F2") + " m";
            // The pushing foot stands on the ground while the player rolls past it: not a snap.
            var pushing = SkateRide.PushPhase > 0f;
            if (rig && !pushing)
            {
                var d1 = (lf - lFoot).magnitude; var d2 = (rf - rFoot).magnitude;
                if (d1 > FootStep * k) line += " left foot " + d1.ToString("F2") + " m";
                if (d2 > FootStep * k) line += " right foot " + d2.ToString("F2") + " m";
            }
            if (rig)
            {
                var d3 = (lh - lHand).magnitude; var d4 = (rh - rHand).magnitude; var d5 = (hd - head).magnitude;
                if (d3 > HandStep * k) line += " left hand " + d3.ToString("F2") + " m";
                if (d4 > HandStep * k) line += " right hand " + d4.ToString("F2") + " m";
                if (d5 > HeadStep * k)
                {
                    // What the head moved with: the neck (the spine moved), a frame without a pose,
                    // or something that moved the head after the pose was written.
                    var posed = RiderRig.Applied != applied;
                    line += " head " + d5.ToString("F2") + " m (neck " + (nk - neck).magnitude.ToString("F2") + " m, head-neck " + (hd - nk).magnitude.ToString("F2")
                        + " m, " + (posed ? "posed, moved after " + (RiderRig.Head.position - RiderRig.HeadPosed).magnitude.ToString("F2") + " m" : "not posed this frame") + ")";
                }
            }
            if (line != "")
            {
                Snaps++;
                if (Said < MostSaid)
                {
                    Said++;
                    Out.Say("SKATE snap" + line + " | " + mode + (mode != SkateRide.Mode ? " -> " + SkateRide.Mode : "") + " speed " + SkateRide.Speed.ToString("F1")
                        + (SkateRide.Mode == RideMode.Grind ? " " + SkateRide.GrindKind : "") + (SkateRide.PushPhase > 0f ? " push " + SkateRide.PushPhase.ToString("F2") + (SkateRide.PushLead ? " lead foot" : "") : "")
                        + (SkateRide.TailFirst ? " tail first" : "") + (SkateRide.Handplant ? " handplant" : "") + (SkateRide.Grabbing ? " grab" : "") + " last=" + trick + (Said == MostSaid ? " (no more said this ride)" : ""));
                }
            }
        }
        nose = noseNow; deck = deckNow; board = boardNow; lFoot = lf; rFoot = rf; lHand = lh; rHand = rh; head = hd; neck = nk; applied = RiderRig.Applied;
        mode = SkateRide.Mode; trick = SkateRide.TrickName ?? ""; at = now; had = true;
    }
}
