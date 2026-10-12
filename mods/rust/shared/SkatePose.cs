using System;

namespace Shortcut.RustMod
{
    public enum SkateStance { Regular, Goofy }

    public struct SkateRotation
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;
        public readonly double W;
        public SkateRotation(double x, double y, double z, double w) { X = x; Y = y; Z = z; W = w; }
    }

    public sealed class SkatePose
    {
        public const double FootOffset = 0.21;
        public const double FootLift = 0.01;
        public const double FlipFootLift = 0.12;
        // How far beside the board's centre line the pushing foot comes down, measured from the
        // ankle plane's origin. A client that sets that origin toward the heels (the feet on the
        // deck have their toes over the toe side) must count that in here: 0.24 is 0.185 beside the
        // line with the Rust client's heel shift of 0.055.
        public const double PushFootSide = 0.24;
        public const double PushFootBack = 0.25;
        public const double StandingHipHeight = 0.95;
        public const double CrouchDrop = 0.4;
        public const double MaximumLean = 20;
        private const SkateEvents AllEvents = SkateEvents.Jumped | SkateEvents.Landed | SkateEvents.Bailed |
            SkateEvents.Blocked | SkateEvents.RailCaptured | SkateEvents.RailReleased;
        private static readonly SkateVector Up = new SkateVector(0, 1, 0);

        public SkateVector BoardPosition { get; private set; }
        public SkateVector BoardForward { get; private set; }
        public SkateVector BoardUp { get; private set; }
        public SkateVector BoardRight { get; private set; }
        public SkateRotation BoardRotation { get; private set; }
        public SkateStance Stance { get; private set; }
        public SkateVector LeftFoot { get; private set; }
        public SkateVector RightFoot { get; private set; }
        public SkateVector Hip { get; private set; }
        public double FacingYaw { get; private set; }
        public double Crouch { get; private set; }
        public double Lean { get; private set; }
        public bool Pushing { get; private set; }
        public bool Bailed { get; private set; }

        private SkatePose() { }

        public static bool TryCreate(SkateState state, SkateInput input, SkateEvents events, SkateStance stance,
                                     out SkatePose pose, out string error)
        {
            pose = null;
            error = null;
            if (!SkateMotion.Valid(state) || !Axis(input.Steer) || !Axis(input.Spin) || !Axis(input.Flip) ||
                (events & ~AllEvents) != 0 || (stance != SkateStance.Regular && stance != SkateStance.Goofy))
            { error = "Invalid skate pose request."; return false; }

            bool airborne = state.Mode == SkateMode.Airborne;
            double heading = airborne ? Yaw(state.Yaw + state.Spin) : state.Yaw;
            var levelForward = Forward(heading);
            var up = state.Mode == SkateMode.Grounded ? Unit(state.GroundNormal) : Up;
            var forward = Unit(levelForward - up * SkateVector.Dot(levelForward, up));
            var right = Cross(up, forward);
            var boardUp = up;
            var boardRight = right;
            if (airborne && state.Flip != 0)
            {
                double radians = state.Flip * Math.PI / 180;
                double cos = Math.Cos(radians), sin = Math.Sin(radians);
                boardUp = Unit(up * cos + right * sin);
                boardRight = Unit(right * cos - up * sin);
            }

            int side = stance == SkateStance.Regular ? 1 : -1;
            var deck = state.Position + up * (airborne && state.Flip != 0 ? FlipFootLift : FootLift);
            var front = deck + forward * FootOffset;
            var back = deck - forward * FootOffset;
            bool bailed = state.Mode == SkateMode.Bailed;
            bool pushing = state.Mode == SkateMode.Grounded && input.Push && !input.Brake;
            if (pushing)
                back = state.Position - forward * PushFootBack + right * (side * PushFootSide) - Up * 0.15;

            double crouch;
            if (bailed) crouch = 0;
            else if ((events & SkateEvents.Landed) != 0) crouch = 0.7;
            else if (state.Mode == SkateMode.Grinding) crouch = 0.35;
            else if (airborne)
                crouch = state.Spin != 0 || state.Flip != 0 || input.Spin != 0 || input.Flip != 0 ? 0.6 : 0.45;
            else crouch = input.Brake ? 0.3 : 0.15;

            double speed = state.Velocity.Length;
            double lean = state.Mode == SkateMode.Grounded ? input.Steer * MaximumLean * Math.Min(1, speed / 4) : 0;

            var feetMiddle = (front + back) * 0.5;
            if (pushing) feetMiddle = front;
            var result = new SkatePose
            {
                BoardPosition = state.Position,
                BoardForward = forward,
                BoardUp = boardUp,
                BoardRight = boardRight,
                BoardRotation = Rotation(boardRight, boardUp, forward),
                Stance = stance,
                LeftFoot = stance == SkateStance.Regular ? front : back,
                RightFoot = stance == SkateStance.Regular ? back : front,
                Hip = feetMiddle + Up * (StandingHipHeight - CrouchDrop * crouch),
                FacingYaw = Yaw(heading + side * 90),
                Crouch = crouch,
                Lean = lean,
                Pushing = pushing,
                Bailed = bailed
            };
            if (!Finite(result))
            { error = "Skate pose exceeds the allowed bounds."; return false; }
            pose = result;
            return true;
        }

        private static SkateRotation Rotation(SkateVector r, SkateVector u, SkateVector f)
        {
            double trace = r.X + u.Y + f.Z;
            if (trace > 0)
            {
                double s = Math.Sqrt(trace + 1) * 2;
                return Normal(new SkateRotation((u.Z - f.Y) / s, (f.X - r.Z) / s, (r.Y - u.X) / s, 0.25 * s));
            }
            if (r.X > u.Y && r.X > f.Z)
            {
                double s = Math.Sqrt(1 + r.X - u.Y - f.Z) * 2;
                return Normal(new SkateRotation(0.25 * s, (u.X + r.Y) / s, (f.X + r.Z) / s, (u.Z - f.Y) / s));
            }
            if (u.Y > f.Z)
            {
                double s = Math.Sqrt(1 + u.Y - r.X - f.Z) * 2;
                return Normal(new SkateRotation((u.X + r.Y) / s, 0.25 * s, (f.Y + u.Z) / s, (f.X - r.Z) / s));
            }
            double t = Math.Sqrt(1 + f.Z - r.X - u.Y) * 2;
            return Normal(new SkateRotation((f.X + r.Z) / t, (f.Y + u.Z) / t, 0.25 * t, (r.Y - u.X) / t));
        }

        private static SkateRotation Normal(SkateRotation q)
        {
            double length = Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
            if (q.W < 0) length = -length;
            return new SkateRotation(q.X / length, q.Y / length, q.Z / length, q.W / length);
        }

        private static bool Finite(SkatePose pose)
        {
            return Vector(pose.BoardPosition) && Vector(pose.BoardForward) && Vector(pose.BoardUp) &&
                Vector(pose.BoardRight) && Vector(pose.LeftFoot) && Vector(pose.RightFoot) && Vector(pose.Hip) &&
                Finite(pose.BoardRotation.X) && Finite(pose.BoardRotation.Y) && Finite(pose.BoardRotation.Z) &&
                Finite(pose.BoardRotation.W) && Finite(pose.FacingYaw) && Finite(pose.Crouch) && Finite(pose.Lean) &&
                Math.Abs(pose.BoardForward.LengthSquared - 1) < 1e-6 && Math.Abs(pose.BoardUp.LengthSquared - 1) < 1e-6;
        }
        private static SkateVector Cross(SkateVector a, SkateVector b)
        { return new SkateVector(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X); }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool Vector(SkateVector value) { return Finite(value.X) && Finite(value.Y) && Finite(value.Z); }
        private static bool Axis(double value) { return Finite(value) && Math.Abs(value) <= 1; }
        private static double Yaw(double value) { var result = value % 360; return result < 0 ? result + 360 : result; }
        private static SkateVector Forward(double yaw) { var radians = yaw * Math.PI / 180; return new SkateVector(Math.Sin(radians), 0, Math.Cos(radians)); }
        private static SkateVector Unit(SkateVector value) { var length = value.Length; return length > 1e-12 ? value * (1 / length) : default(SkateVector); }
    }
}
