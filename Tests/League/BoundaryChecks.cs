using MiniFootball;

internal static class BoundaryChecks
{
    const float HalfWidth = 13f, HalfLength = 20f, GoalHalf = 3.13f, GoalHeight = 2.08f, Radius = .23f;

    public static void Run()
    {
        static BoundaryDecision Cross(float ax, float ay, float az, float bx, float by, float bz, int lastTouch = 0)
        {
            MatchRules.TryCrossing(ax, ay, az, bx, by, bz, HalfWidth, HalfLength, GoalHalf, GoalHeight, Radius, lastTouch, out var result);
            return result;
        }

        static void Expect(BoundaryDecision actual, RestartKind kind, int team, int end)
        {
            if (actual.Kind != kind || actual.Team != team || actual.EndSign != end)
                throw new InvalidOperationException($"Expected {kind} for team {team} at end {end}, got {actual.Kind}, {actual.Team}, {actual.EndSign}.");
        }

        // A rolling ball which overlaps the line or moves back in is still live.
        Expect(Cross(0, Radius, 19f, 0, Radius, 20.2f), RestartKind.None, 0, 0);
        Expect(Cross(0, Radius, 20.2f, 0, Radius, 17f), RestartKind.None, 0, 0);
        for (int end = -1; end <= 1; end += 2)
        {
            int attacker = end > 0 ? 0 : 1;
            int defender = 1 - attacker;
            Expect(Cross(0, Radius, 19 * end, 0, Radius, 22 * end, attacker), RestartKind.Goal, attacker, end);
            Expect(Cross(0, Radius, 19 * end, 0, Radius, 22 * end, defender), RestartKind.Goal, attacker, end);
            Expect(Cross(4, Radius, 19 * end, 4, Radius, 22 * end, defender), RestartKind.Corner, attacker, end);
            Expect(Cross(3, Radius, 19 * end, 3, Radius, 22 * end, attacker), RestartKind.GoalKick, defender, end);
            Expect(Cross(0, 2f, 19 * end, 0, 2f, 22 * end, attacker), RestartKind.GoalKick, defender, end);
            // A fast rising ball is judged at the crossing, even if the sample after it is above the bar.
            Expect(Cross(0, 1f, 20 * end, 0, 3f, 22 * end), RestartKind.Goal, attacker, end);
            for (int lastTouch = 0; lastTouch <= 1; lastTouch++)
            {
                var side = Cross(12 * end, Radius, 8, 15 * end, Radius, 8, lastTouch);
                Expect(side, RestartKind.ThrowIn, 1 - lastTouch, 0);
                if (Math.Abs(side.Z - 8) > .001f || Math.Abs(side.X - HalfWidth * end) > .001f)
                    throw new InvalidOperationException("Throw-in spot must project onto the touchline.");
            }
        }
        // Both axes crossed in one tick. Restart follows the earliest crossing.
        Expect(Cross(13, Radius, 19, 14, Radius, 21), RestartKind.ThrowIn, 1, 0);
        Expect(Cross(12, Radius, 20, 14, Radius, 21), RestartKind.GoalKick, 1, 1);
    }
}
