using UnityEngine;
using Planar = System.Numerics.Vector2;

namespace MiniFootball
{
    public sealed partial class Match
    {
        readonly FootballTactics tactics = new FootballTactics(
            FootballArt.HalfWidth, FootballArt.HalfLength, FootballArt.GoalHalf,
            MotionTiming.ContactTime(FootballAction.SaveLeft), MotionTiming.ContactTime(FootballAction.Jump));
        float decisionIn, decisionAge;
        Athlete lastDecisionCarrier;

        static Planar PlanarPoint(Vector3 value) => new Planar(value.x, value.z);

        void SnapshotTeams()
        {
            for (int team = 0; team < 2; team++)
            for (int slot = 0; slot < 5; slot++)
            {
                Athlete athlete = teams[team][slot];
                tactics.Players[team, slot] = new FootballTactics.Player
                {
                    Position = PlanarPoint(athlete.Position),
                    Velocity = PlanarPoint(athlete.Velocity),
                    Role = athlete.Record.Position,
                    Pace = 4.6f + .028f * athlete.Record.Speed
                };
            }
        }

        void PlanTactics(float dt)
        {
            decisionIn -= dt;
            decisionAge += dt;
            if (decisionIn > 0 && carrier == lastDecisionCarrier) return;
            decisionIn = carrier == lastDecisionCarrier && decisionIn > -dt
                ? decisionIn + FootballTactics.DecisionInterval : FootballTactics.DecisionInterval;
            decisionAge = 0;
            lastDecisionCarrier = carrier;
            SnapshotTeams();
            Vector3 velocity = ball.linearVelocity;
            tactics.Decide(PlanarPoint(ball.position), PlanarPoint(velocity), ball.position.y, velocity.y,
                carrier?.Team ?? -1, carrier?.Slot ?? -1, holdTime, selected);
        }

        Athlete BestPass(Athlete athlete, Vector3 aim)
        {
            SnapshotTeams();
            int receiver = tactics.FindPass(athlete.Team, athlete.Slot, PlanarPoint(aim));
            return teams[athlete.Team][receiver];
        }

        void MoveAi(Athlete athlete, float dt)
        {
            FootballTactics.Intent plan = tactics.Plans[athlete.Team, athlete.Slot];
            Vector3 direction = new Vector3(plan.Target.X, 0f, plan.Target.Y) - athlete.Position;
            direction.y = 0;
            // Arrive gradually so pressing, marking and the keeper's adjustment steps stay balanced.
            Vector3 steering = direction.sqrMagnitude < .06f ? Vector3.zero : Vector3.ClampMagnitude(direction / 1.25f, 1f);
            Move(athlete, steering, plan.Sprint, dt);
            if (athlete.Cooldown > 0 || athlete.ActionTime > 0) return;
            switch (plan.Action)
            {
                case FootballTactics.Play.Pass:
                    if (carrier == athlete) Pass(athlete, teams[athlete.Team][plan.Receiver]);
                    break;
                case FootballTactics.Play.Shoot:
                    if (carrier == athlete) Shoot(athlete, Mathf.Clamp01(.47f + Mathf.Abs(FootballArt.HalfLength * Sign(athlete) - ball.position.z) * .038f));
                    break;
                case FootballTactics.Play.Tackle:
                    if (carrier != null && carrier.Team != athlete.Team) Tackle(athlete);
                    break;
                case FootballTactics.Play.Save:
                    if (carrier == null && decisionAge >= plan.WindupDelay) StartSave(athlete, direction);
                    break;
                case FootballTactics.Play.Jump:
                    if (carrier == null) StartJump(athlete);
                    break;
            }
        }
    }
}
