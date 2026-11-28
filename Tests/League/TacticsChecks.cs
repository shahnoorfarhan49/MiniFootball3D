using System.Numerics;
using MiniFootball;
using Play = MiniFootball.FootballTactics.Play;

internal static class TacticsChecks
{
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // Feed the same formation rotated through 180 degrees to exercise both ends of the pitch.
    static FootballTactics Formation()
    {
        var tactics = new FootballTactics(13, 20, 3.2f, .43f, .355f);
        var positions = new Vector2[] { new(0, -18.5f), new(0, -7), new(-7, -3), new(7, -3), new(0, 4) };
        var roles = new[] { Role.Keeper, Role.Defender, Role.Midfielder, Role.Midfielder, Role.Forward };
        for (int team = 0; team < 2; team++)
        for (int slot = 0; slot < 5; slot++)
            tactics.Players[team, slot] = new FootballTactics.Player
            {
                Position = positions[slot] * (team == 0 ? 1 : -1),
                Role = roles[slot],
                Velocity = Vector2.Zero,
                Pace = 6
            };
        return tactics;
    }

    public static void Run()
    {
        for (int team = 0; team < 2; team++)
        {
            int other = 1 - team;
            int sign = team == 0 ? 1 : -1;
            var tactics = Formation();
            Vector2 ball = Vector2.Zero;
            tactics.Players[team, 2].Position = ball;
            tactics.Decide(ball, Vector2.Zero, .23f, 0, team, 2, .15f, -1);
            Require(tactics.Plans[team, 1].Target.Y * sign < -1, "Defender should offer a passing option behind possession.");
            Require(tactics.Plans[team, 4].Target.Y * sign > 6, "Forward should stretch the opposition.");
            Require(Math.Abs(tactics.Plans[team, 3].Target.X) > 6, "Far midfielder should retain width.");

            // Surround the ball but provide multiple opponents to mark. Only the closest
            // presser may tackle while the other outfielders cover the passing lanes.
            tactics.Players[other, 1].Position = new Vector2(0, .4f);
            tactics.Players[other, 2].Position = new Vector2(0, .7f);
            tactics.Players[other, 3].Position = new Vector2(1, .2f);
            tactics.Decide(ball, Vector2.Zero, .23f, 0, team, 2, .8f, -1);
            Require(Enumerable.Range(1, 4).Count(s => tactics.Plans[other, s].Action == Play.Tackle) == 1,
                "A single outfielder should commit to the challenge.");
            Require(tactics.Plans[team, 2].Action == Play.Pass && tactics.Plans[team, 2].Receiver != 2,
                "Pressured carrier should release the ball to a teammate.");
            Require(Enumerable.Range(1, 4).Count(s => tactics.Plans[other, s].Target.Length() > 2.5f) >= 2,
                "Covering players should stay goal-side of receivers.");

            tactics = Formation();
            tactics.Players[team, 1].Position = Vector2.Zero;
            // Leave both wide outlets open for a deliberate sideways pass.
            tactics.Players[other, 4].Position = new Vector2(0, 8 * sign);
            int left = tactics.FindPass(team, 1, new Vector2(-sign, 0));
            int right = tactics.FindPass(team, 1, new Vector2(sign, 0));
            Require(left == 2 && right == 3, "Directional passing should select the requested wide teammate.");
            tactics.Players[team, 4].Position = new Vector2(.4f, sign * 16);
            tactics.Decide(tactics.Players[team, 4].Position, Vector2.Zero, .23f, 0, team, 4, .7f, -1);
            Require(tactics.Plans[team, 4].Action == Play.Shoot, "Forward should finish a central chance near goal.");

            ball = new Vector2(1, -sign * 11);
            Vector2 velocity = new Vector2(1, -sign * 15);
            tactics.Decide(ball, velocity, .7f, 1, -1, -1, 0, -1);
            Require(tactics.Plans[team, 0].Action == Play.Save && tactics.Plans[team, 0].Target.X > 1,
                "Keeper should move to the predicted crossing and attempt a reachable save.");
            Require(Math.Abs(tactics.Plans[team, 0].WindupDelay - .07f) < .002f,
                "Keeper should time the animation contact with the predicted ball arrival.");
            tactics.Decide(ball, -velocity, .7f, 1, -1, -1, 0, -1);
            Require(tactics.Plans[team, 0].Action != Play.Save, "Keeper should stay on feet when ball moves away.");

            tactics = Formation();
            tactics.Players[team, 2].Position = new Vector2(2, 0);
            tactics.Decide(Vector2.Zero, new Vector2(7, 0), .23f, 0, -1, -1, 0, -1);
            Require(tactics.Plans[team, 2].Target.X > .5f && Math.Abs(tactics.Plans[team, 2].Target.Y) < .01f,
                "Nearby midfielder should lead a moving ball to its interception point.");
            tactics.Decide(tactics.Players[team, 0].Position, Vector2.Zero, .23f, 0, team, 0, 1, -1);
            Require(tactics.Plans[team, 0].Action == Play.Pass && tactics.Plans[team, 0].Receiver != 0,
                "Keeper in possession should distribute the ball.");
        }
    }
}
