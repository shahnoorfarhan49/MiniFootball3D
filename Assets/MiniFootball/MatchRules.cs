using System;

namespace MiniFootball
{
    public enum RestartKind { None, Goal, ThrowIn, Corner, GoalKick }

    public readonly struct BoundaryDecision
    {
        public readonly RestartKind Kind;
        public readonly int Team, EndSign;
        public readonly float X, Z;

        internal BoundaryDecision(RestartKind kind, int team, int endSign, float x, float z)
        {
            Kind = kind;
            Team = team;
            EndSign = endSign;
            X = x;
            Z = z;
        }
    }

    public static class MatchRules
    {
        // Return the first line wholly crossed during this physics step. Team zero attacks +Z.
        // Goal dimensions are measured to the inside edges of the woodwork.
        public static bool TryCrossing(float ax, float ay, float az, float bx, float by, float bz,
            float halfWidth, float halfLength, float goalHalf, float goalHeight, float radius,
            int lastTouchTeam, out BoundaryDecision decision)
        {
            decision = default;
            float sideTime = ExitFraction(ax, bx, halfWidth + radius);
            float endTime = ExitFraction(az, bz, halfLength + radius);
            if (float.IsPositiveInfinity(sideTime) && float.IsPositiveInfinity(endTime)) return false;

            bool touchline = sideTime < endTime;
            float time = touchline ? sideTime : endTime;
            float x = ax + time * (bx - ax);
            float z = az + time * (bz - az);
            if (touchline)
            {
                decision = new BoundaryDecision(RestartKind.ThrowIn, 1 - lastTouchTeam, 0, Math.Sign(bx) * halfWidth,
                    Math.Max(-halfLength, Math.Min(halfLength, z)));
                return true;
            }

            int end = Math.Sign(bz);
            int attacker = end > 0 ? 0 : 1;
            float y = ay + time * (by - ay);
            if (Math.Abs(x) + radius < goalHalf && y + radius < goalHeight)
                decision = new BoundaryDecision(RestartKind.Goal, attacker, end, x, end * halfLength);
            else if (lastTouchTeam == attacker)
                decision = new BoundaryDecision(RestartKind.GoalKick, 1 - attacker, end, x, end * halfLength);
            else
                decision = new BoundaryDecision(RestartKind.Corner, attacker, end, x, end * halfLength);
            return true;
        }

        static float ExitFraction(float from, float to, float extent)
        {
            if (to > extent && from <= extent) return (extent - from) / (to - from);
            if (to < -extent && from >= -extent) return (-extent - from) / (to - from);
            return float.PositiveInfinity;
        }
    }
}
