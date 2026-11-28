using System;
using System.Numerics;

namespace MiniFootball
{
    // Horizontal coordinates are metres along the touchline width (X) and pitch length (Y).
    // The match supplies one snapshot per decision. Steering and contacts remain on the physics clock.
    internal sealed class FootballTactics
    {
        internal enum Play { Move, Pass, Shoot, Tackle, Save, Jump }
        internal struct Player
        {
            public Vector2 Position, Velocity;
            public Role Role;
            public float Pace;
        }

        internal struct Intent
        {
            public Vector2 Target;
            public Play Action;
            public int Receiver;
            public bool Sprint;
            public float WindupDelay;
        }

        internal readonly Player[,] Players = new Player[2, 5];
        internal readonly Intent[,] Plans = new Intent[2, 5];
        internal const float DecisionInterval = .125f;
        readonly float halfWidth, halfLength, goalHalfWidth, diveContact, jumpContact;
        readonly int[] presser = { -1, -1 };
        readonly int[] marks = new int[5];
        readonly bool[] marked = new bool[5];
        readonly Vector2[] intercept = new Vector2[5];
        readonly Vector2[] shape = new Vector2[5];
        Vector2 ball, velocity;
        float ballHeight, verticalVelocity, heldFor;
        int possession, owner;

        internal FootballTactics(float halfWidth, float halfLength, float goalHalfWidth, float diveContact, float jumpContact)
        {
            this.halfWidth = halfWidth;
            this.halfLength = halfLength;
            this.goalHalfWidth = goalHalfWidth;
            this.diveContact = diveContact;
            this.jumpContact = jumpContact;
        }

        static float Clamp(float value, float lo, float hi) => Math.Min(hi, Math.Max(value, lo));
        static int Sign(int team) => team == 0 ? 1 : -1;
        static Vector2 Unit(Vector2 direction)
        {
            float length = direction.Length();
            return length > .001f ? direction / length : Vector2.Zero;
        }

        Vector2 InPlay(Vector2 point) => new Vector2(
            Clamp(point.X, -halfWidth + .7f, halfWidth - .7f),
            Clamp(point.Y, -halfLength + .55f, halfLength - .55f));

        internal void Decide(Vector2 ballPosition, Vector2 ballVelocity, float height, float vertSpeed,
            int ownerTeam, int ownerSlot, float holdTime, int manualSlot)
        {
            ball = ballPosition;
            velocity = ballVelocity;
            ballHeight = height;
            verticalVelocity = vertSpeed;
            possession = ownerTeam;
            owner = ownerSlot;
            heldFor = holdTime;
            for (int team = 0; team < 2; team++)
            {
                bool defending = possession != team;
                Formation(team, defending);
                presser[team] = defending ? ChoosePresser(team, team == 0 ? manualSlot : -1) : -1;
                AssignMarks(team);
                for (int slot = 0; slot < 5; slot++)
                {
                    Intent plan;
                    if (team == possession && slot == owner) plan = OnBall(team, slot);
                    else if (slot == 0) plan = Keeper(team);
                    else if (slot == presser[team]) plan = Pressure(team, slot);
                    else if (defending) plan = Defend(team, slot);
                    else plan = Support(team, slot);
                    plan.Target = InPlay(plan.Target);
                    Plans[team, slot] = plan;
                }
            }
        }

        // One covering back, one high outlet, and two staggered wide players.
        // Resolve these from the submitted positions so swapping shirt numbers or slots preserves shape.
        void Formation(int team, bool defending)
        {
            int back = -1, nine = -1;
            for (int slot = 1; slot < 5; slot++)
            {
                Role role = Players[team, slot].Role;
                if (role == Role.Defender && back < 0) back = slot;
                if (role == Role.Forward && nine < 0) nine = slot;
            }
            bool left = true;
            int sign = Sign(team);
            float depth = ball.Y * sign;
            float center = Clamp(depth * .56f + (defending ? -3.5f : 1.8f), -11f, 11f);
            for (int slot = 1; slot < 5; slot++)
            {
                float x, z;
                if (slot == back)
                {
                    x = ball.X * .26f;
                    z = center - (defending ? 4.8f : 7.8f);
                }
                else if (slot == nine)
                {
                    x = Clamp(-ball.X * .48f, -5f, 5f);
                    z = center + (defending ? 5.7f : 8f);
                }
                else
                {
                    int side = (left ? -1 : 1) * sign;
                    left = false;
                    x = side * halfWidth * (defending ? .44f : .72f) + ball.X * .12f;
                    bool farSide = ball.X * side < -1.4f;
                    z = center + (farSide && !defending ? 4.4f : -.7f);
                    if (Players[team, slot].Role == Role.Defender) z -= 1.5f;
                }
                shape[slot] = InPlay(new Vector2(x, z * sign));
            }
        }

        int ChoosePresser(int team, int manualSlot)
        {
            float bestTime = float.MaxValue;
            int best = 1;
            for (int slot = 1; slot < 5; slot++)
            {
                Player player = Players[team, slot];
                float pace = Math.Max(2f, player.Pace);
                float arrival = Vector2.Distance(player.Position, ball) / pace;
                Vector2 target = ball;
                if (possession < 0)
                {
                    // Earliest feasible interception of a rolling pass, including a short reaction delay.
                    for (int step = 1; step <= 10; step++)
                    {
                        float t = step * .12f;
                        target = InPlay(ball + velocity * (t / (1f + .38f * t)));
                        arrival = Vector2.Distance(player.Position, target) / pace + .08f;
                        if (arrival <= t) { arrival = t; break; }
                    }
                }
                else
                {
                    // Close from the goal side, keeping the central shooting lane covered.
                    Vector2 lead = Players[possession, owner].Velocity * .14f;
                    target = InPlay(ball + lead + new Vector2(0, -.4f * Sign(team)));
                }
                intercept[slot] = target;
                if (slot == presser[team]) arrival -= .23f;
                if (slot == manualSlot && Vector2.DistanceSquared(player.Position, ball) > 5f) arrival += .7f;
                if (arrival < bestTime) { bestTime = arrival; best = slot; }
            }
            return best;
        }

        void AssignMarks(int team)
        {
            for (int slot = 0; slot < 5; slot++) { marks[slot] = -1; marked[slot] = false; }
            if (possession < 0 || possession == team) return;
            int attack = 1 - team;
            for (int count = 0; count < 3; count++)
            {
                // Pick the most advanced unmarked receiver, then the closest plausible defender.
                int threat = -1;
                float highest = float.MinValue;
                for (int opponent = 1; opponent < 5; opponent++)
                {
                    if (marked[opponent] || opponent == owner) continue;
                    Vector2 pos = Players[attack, opponent].Position;
                    float danger = -Sign(team) * pos.Y - .18f * Math.Abs(pos.X);
                    if (danger > highest) { highest = danger; threat = opponent; }
                }
                if (threat < 0) return;
                int marker = -1;
                float best = float.MaxValue;
                for (int slot = 1; slot < 5; slot++)
                {
                    if (slot == presser[team] || marks[slot] >= 0) continue;
                    Vector2 threatPos = Players[attack, threat].Position;
                    float cost = Vector2.DistanceSquared(shape[slot], threatPos) +
                        .75f * Vector2.DistanceSquared(Players[team, slot].Position, threatPos);
                    if (cost < best) { best = cost; marker = slot; }
                }
                if (marker < 0) return;
                marked[threat] = true;
                marks[marker] = threat;
            }
        }

        Intent Pressure(int team, int slot)
        {
            Vector2 pos = Players[team, slot].Position;
            float distance = Vector2.Distance(pos, ball);
            var plan = new Intent { Target = intercept[slot], Sprint = distance > 2.4f };
            if (possession >= 0 && distance < 1.45f) plan.Action = Play.Tackle;
            else if (possession < 0 && ballHeight > .65f)
            {
                // Judge the ball at the apex of the jumping animation.
                float height = ballHeight + verticalVelocity * jumpContact - 4.905f * jumpContact * jumpContact;
                Vector2 contact = ball + velocity * jumpContact;
                if (height >= 1.12f && height <= 2f && Vector2.DistanceSquared(pos, contact) < 1.2f)
                {
                    plan.Action = Play.Jump;
                    plan.Target = contact;
                }
            }
            return plan;
        }

        Intent Defend(int team, int slot)
        {
            Vector2 target = shape[slot];
            if (marks[slot] >= 0)
            {
                Player opponent = Players[1 - team, marks[slot]];
                Vector2 ownGoal = new Vector2(0, -Sign(team) * halfLength);
                Vector2 block = opponent.Position + opponent.Velocity * .16f +
                    Unit(ownGoal - opponent.Position) * 1.25f + Unit(ball - opponent.Position) * .5f;
                float depth = Sign(team) * opponent.Position.Y;
                target = Vector2.Lerp(target, block, Clamp(.63f - depth * .028f, .25f, .92f));
            }
            return new Intent { Target = target, Sprint = Vector2.DistanceSquared(Players[team, slot].Position, target) > 42f };
        }

        Intent Support(int team, int slot)
        {
            Vector2 basePoint = shape[slot], target = basePoint;
            int sign = Sign(team);
            float best = float.MinValue;
            // Find a passing window near the role's anchor, preserving width and a recycling option.
            for (int i = 0; i < 5; i++)
            {
                Vector2 candidate = basePoint;
                if (i == 1) candidate.Y += 2.8f * sign;
                else if (i == 2) candidate.Y -= 2.3f * sign;
                else if (i == 3) candidate.X += 2.2f;
                else if (i == 4) candidate.X -= 2.2f;
                candidate = InPlay(candidate);
                float score = Freedom(team, candidate) + .6f * LaneClearance(team, ball, candidate)
                    - .3f * Vector2.Distance(basePoint, candidate);
                for (int mate = 1; mate < 5; mate++)
                    if (mate != slot) score -= Math.Max(0, 3.2f - Vector2.Distance(candidate, Players[team, mate].Position)) * 1.3f;
                if (score > best) { best = score; target = candidate; }
            }
            var player = Players[team, slot];
            return new Intent { Target = target, Sprint = player.Role == Role.Forward &&
                sign * (target.Y - player.Position.Y) > 4f && ball.Y * sign > -4f };
        }

        Intent Keeper(int team)
        {
            int sign = Sign(team);
            Vector2 goal = new Vector2(0, -sign * halfLength);
            Player keeper = Players[team, 0];
            Vector2 line = Unit(ball - goal);
            float distance = Vector2.Distance(ball, goal);
            float depth = Clamp(3.5f - distance * .105f, .85f, 2.7f);
            Vector2 target = goal + line * depth;
            var plan = new Intent { Target = target };
            if (possession == team) return plan;
            if (possession < 0 && velocity.Y * sign < -2)
            {
                float meet = (keeper.Position.Y - ball.Y) / velocity.Y;
                float crossing = (goal.Y - ball.Y) / velocity.Y;
                float goalX = ball.X + crossing * velocity.X;
                if (crossing > 0 && crossing < 1.1f && Math.Abs(goalX) < goalHalfWidth + .35f)
                {
                    float t = Clamp(meet, 0, crossing);
                    float shotX = ball.X + t * velocity.X;
                    float shotHeight = Math.Max(.2f, ballHeight + verticalVelocity * t - 4.9f * t * t);
                    plan.Target = new Vector2(Clamp(shotX, -goalHalfWidth, goalHalfWidth), keeper.Position.Y);
                    plan.Sprint = true;
                    if (meet > 0 && meet <= diveContact + DecisionInterval + .02f &&
                        Math.Abs(shotX - keeper.Position.X) < 2.1f && shotHeight <= 1.85f)
                    {
                        plan.Action = Play.Save;
                        plan.WindupDelay = Math.Max(0, meet - diveContact);
                    }
                    return plan;
                }
            }
            if (distance < 6f && Math.Abs(ball.X) < goalHalfWidth + 1f)
            {
                float reach = Vector2.Distance(keeper.Position, ball);
                if (possession < 0 && ballHeight < 1.4f && reach < 3.1f)
                    plan.Target = ball + velocity * .08f;
                else if (possession >= 0 && reach < 2.15f)
                {
                    plan.Target = ball;
                    if (reach < 1.35f) plan.Action = Play.Tackle;
                }
            }
            return plan;
        }

        Intent OnBall(int team, int slot)
        {
            Player player = Players[team, slot];
            Vector2 pos = player.Position;
            int sign = Sign(team);
            Vector2 goal = new Vector2(0, sign * halfLength);
            float distance = Vector2.Distance(goal, pos);
            float space = Freedom(team, pos);
            int receiver = FindPass(team, slot, Vector2.Zero);
            Player outlet = Players[team, receiver];
            var plan = new Intent { Target = pos, Receiver = receiver };
            if (slot == 0)
            {
                if (heldFor > .65f) plan.Action = Play.Pass;
                return plan;
            }
            if (heldFor > .28f && distance < 9.6f && Math.Abs(pos.X) < goalHalfWidth + 3.5f &&
                (distance < 5.8f || LaneClearance(team, pos, goal, true) > .92f))
            {
                plan.Action = Play.Shoot;
                return plan;
            }
            float progress = (outlet.Position.Y - pos.Y) * sign;
            float clearance = LaneClearance(team, pos, outlet.Position);
            if (heldFor > .5f && (space < 1.85f || (progress > 3.7f && clearance > 1.25f) ||
                (heldFor > 1.65f && clearance > 1.15f && Freedom(team, outlet.Position) > space)))
            {
                plan.Action = Play.Pass;
                return plan;
            }
            Vector2 forward = Unit(goal - pos);
            Vector2 side = new Vector2(forward.Y, -forward.X);
            float best = float.MinValue;
            for (int lane = -1; lane <= 1; lane++)
            {
                Vector2 candidate = InPlay(pos + forward * 3.1f + lane * 2.15f * side);
                float score = Freedom(team, candidate) + LaneClearance(team, pos, candidate)
                    + .32f * (candidate.Y - pos.Y) * sign - Math.Abs(lane) * .18f;
                if (score > best) { best = score; plan.Target = candidate; }
            }
            plan.Sprint = space > 4.4f && distance > 7f;
            return plan;
        }

        internal int FindPass(int team, int slot, Vector2 aim)
        {
            Vector2 origin = Players[team, slot].Position;
            Vector2 heading = Unit(aim);
            float best = float.MinValue;
            int choice = slot == 0 ? 1 : 0;
            for (int mate = 0; mate < 5; mate++)
            {
                if (mate == slot) continue;
                Player receiver = Players[team, mate];
                float distance = Vector2.Distance(origin, receiver.Position);
                Vector2 destination = InPlay(receiver.Position + receiver.Velocity * Clamp(distance / 24f, .06f, .65f));
                float score = LaneClearance(team, origin, destination) * 1.65f + Freedom(team, destination) * .72f
                    + Clamp((destination.Y - origin.Y) * Sign(team), -8f, 10f) * .29f
                    - Math.Abs(distance - 6.7f) * .24f
                    + Vector2.Dot(heading, Unit(destination - origin)) * 8.5f;
                if (mate == 0) score -= 2.2f;
                if (score > best) { best = score; choice = mate; }
            }
            return choice;
        }

        float Freedom(int team, Vector2 point)
        {
            float nearest = 5f;
            for (int i = 0; i < 5; i++)
                nearest = Math.Min(nearest, Vector2.Distance(Players[1 - team, i].Position, point));
            return nearest;
        }

        float LaneClearance(int team, Vector2 from, Vector2 to, bool ignoreKeeper = false)
        {
            Vector2 segment = to - from;
            float lengthSq = Math.Max(.01f, segment.LengthSquared());
            float nearest = 4f;
            for (int i = ignoreKeeper ? 1 : 0; i < 5; i++)
            {
                Player defender = Players[1 - team, i];
                Vector2 predicted = defender.Position + defender.Velocity * .18f;
                float fraction = Clamp(Vector2.Dot(predicted - from, segment) / lengthSq, .08f, 1f);
                nearest = Math.Min(nearest, Vector2.Distance(from + segment * fraction, predicted));
            }
            return nearest;
        }
    }
}
