// The game puts the main camera at the player's eyes every frame; moving it later in the same
// frame is what gives the view from behind. A second camera is not an alternative: it lacks the
// main camera's image effects. Sample() must run before Apply() moves the camera, or the rider
// steers by the moved camera.
using System;
using UnityEngine;

public static class SkateCamera
{
    public static bool Chase = true, Fixed;
    public const float FollowPitch = 14f, FollowRate = 3.5f;
    public static float LookYaw, LookPitch, SampledAt = -99f, Distance = 3.4f, Bearing = 180f, FixedDistance = 2.7f, FixedHeight = 1.15f, FollowYaw;
    // The chase camera falls back a little as the board goes faster, for the sense of speed, and
    // comes in again as it slows: up to SpeedPull further at top speed, eased at PullRate per second.
    public const float SpeedPull = 1.2f, PullRate = 2.5f;
    private static float followAt = -99f, pullNow, pullAt = -99f;
    private static bool failed, ahead = true;

    public static void Sample()
    {
        var cam = Camera.main;
        if (cam == null) return;
        var e = cam.transform.eulerAngles;
        SampledAt = Time.realtimeSinceStartup;
        LookYaw = e.y;
        LookPitch = e.x > 180f ? e.x - 360f : e.x;
    }

    public static void Apply(Vector3 at)
    {
        if (failed || !(Chase || Fixed)) return;
        try
        {
            var cam = Camera.main;
            if (cam == null) return;
            var focus = at + Vector3.up * 1.05f;
            Vector3 want;
            if (Fixed)
            {
                // Bearing from the rider to the camera, measured from the direction of travel:
                // 0 ahead of the board, 90 on its right, 180 behind it.
                want = focus + SkateRide.Dir(SkateRide.Yaw + Bearing) * FixedDistance + Vector3.up * (FixedHeight - 1.05f);
            }
            else
            {
                float pitch = SkateRide.Clamp(LookPitch, -20f, 60f), yaw = LookYaw;
                var now = Time.realtimeSinceStartup;
                if (SkateKeys.Pad)
                {
                    // With a controller the right stick is the board, so the camera finds its own
                    // way: it swings in behind the way the board goes.
                    if (SkateRide.Speed > 1f) ahead = true; else if (SkateRide.Speed < -1f) ahead = false;
                    var seconds = SkateRide.Clamp(now - followAt, 0f, 0.1f);
                    FollowYaw += SkateRide.Delta(FollowYaw, SkateRide.Yaw + (ahead ? 0f : 180f)) * (1f - (float)Math.Exp(-seconds * FollowRate));
                    yaw = FollowYaw; pitch = FollowPitch;
                }
                else { FollowYaw = LookYaw; ahead = true; }
                followAt = now;
                var back = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
                var pullWant = SpeedPull * SkateRide.Clamp(Math.Abs(SkateRide.HorizontalSpeed) / SkateRide.MaxSpeed, 0f, 1f);
                var pullSeconds = SkateRide.Clamp(now - pullAt, 0f, 0.1f); pullAt = now;
                pullNow += (pullWant - pullNow) * (1f - (float)Math.Exp(-pullSeconds * PullRate));
                want = focus - back * (Distance + pullNow) + Vector3.up * (0.3f + pullNow * 0.15f);
            }
            // Nothing between the rider and the camera: it comes in to this side of a wall or a fence.
            var dir = want - focus; var len = dir.magnitude;
            RaycastHit hit;
            if (len > 0.01f && Physics.SphereCast(focus, 0.2f, dir / len, out hit, len, SkateRide.GroundMask, QueryTriggerInteraction.Ignore))
                want = focus + dir / len * Math.Max(0.6f, hit.m_Distance - 0.05f);
            cam.transform.position = want;
            cam.transform.LookAt(focus);
        }
        catch (Exception e) { failed = true; Out.Say("CAMERA threw " + e.GetType().Name + ": " + e.Message); }
    }
}
