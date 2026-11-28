using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using static MiniFootball.FootballArt;

namespace MiniFootball
{
    public sealed partial class Match
    {
        public const float Duration = 180;
        public readonly Fixture Fixture;
        public readonly int Opponent;
        public readonly int[] Appearances = new int[50], Goals = new int[50], Assists = new int[50];
        public int UserScore { get; private set; }
        public int OpponentScore { get; private set; }
        public float Seconds { get; private set; } = Duration;
        public bool Finished => Seconds <= 0;
        public bool IsReplaying => broadcast.IsReplaying;
        public string Notice { get; private set; } = "";
        public int SelectedId => teams[0][selected].Id;
        public float Energy => teams[0][selected].Energy;
        public float ShotCharge { get; private set; }
        public int ReserveId => reserve;
        public bool SubUsed { get; private set; }
        public bool AiSubUsed { get; private set; }
        public int StarterId(int slot) => teams[0][slot].Id;
        public string Clock => TimeSpan.FromSeconds(Mathf.CeilToInt(Seconds)).ToString(@"m\:ss");

        sealed class Athlete
        {
            public int Id, Team, Slot;
            public Transform Actor, Marker;
            public FootballMotion Motion;
            public Footballer Record => League.Players[Id];
            public float Energy = 1, Cooldown, ActionTime;
            public Vector3 Position => Actor.position;
            public Vector3 Velocity, ActionDirection;
            public FootballAction Action;
            public bool AwaitContact, SprintExhausted;
            public Athlete PassTarget;
            public float KickPower, KickLift;
        }

        enum Phase { Live, Restart, Celebration }
        readonly GameObject root;
        readonly Athlete[][] teams = { new Athlete[5], new Athlete[5] };
        readonly Rigidbody ball;
        readonly Camera camera;
        readonly BroadcastCamera broadcast;
        readonly Season season;
        Athlete carrier, taker;
        int selected = 3, reserve, aiReserve;
        int lastTeam, lastId = -1, assistId = -1, assistTeam = -1, concedingTeam;
        float holdTime, noticeTime, touchDelay, freeTime, phaseTime;
        bool charging, restartReady, sprintInput;
        Vector3 aimInput;
        Phase phase;
        RestartKind restartKind;
        Vector3 previousBall, restartSpot;
        static readonly Vector3[] Formation = {
            new Vector3(0,0,-18.2f), new Vector3(-4.2f,0,-10),
            new Vector3(3.9f,0,-5.8f), new Vector3(0,0,-2.3f), new Vector3(-5.1f,0,-4.6f)
        };

        public Match(Season season, Fixture fixture, GameObject character, GameObject ballPrefab, Camera camera, Material markerMaterial)
        {
            this.season = season;
            this.camera = camera;
            Fixture = fixture;
            Opponent = fixture.Home == League.UserClub ? fixture.Away : fixture.Home;
            root = new GameObject("Live Match");
            var actors = new Transform[10];
            for (int team = 0; team < 2; team++)
            {
                int[] ids = League.Squad(season, team == 0 ? League.UserClub : Opponent);
                for (int slot = 0; slot < 5; slot++)
                {
                    Transform actor = UnityEngine.Object.Instantiate(character, root.transform).transform;
                    actor.gameObject.SetActive(true);
                    var marker = Part("Selection Marker", PrimitiveType.Cylinder, actor, new Vector3(0, .032f, 0),
                        new Vector3(.81f, .008f, .81f), markerMaterial).transform;
                    marker.gameObject.SetActive(false);
                    var athlete = new Athlete { Team = team, Slot = slot, Actor = actor, Marker = marker, Motion = actor.GetComponent<FootballMotion>() };
                    teams[team][slot] = athlete;
                    Dress(athlete, ids[slot]);
                    actor.SetPositionAndRotation(Formation[slot] * Sign(athlete), Quaternion.LookRotation(Vector3.forward * Sign(athlete)));
                    actors[team * 5 + slot] = actor;
                }
                if (team == 0) reserve = ids[5];
                else aiReserve = ids[5];
            }
            var ballObject = UnityEngine.Object.Instantiate(ballPrefab, root.transform);
            ballObject.SetActive(true);
            ball = ballObject.GetComponent<Rigidbody>();
            broadcast = new BroadcastCamera(camera, ball.transform, actors);
            BeginRestart(RestartKind.None, fixture.Home == League.UserClub ? 0 : 1, Vector3.zero);
            UpdateCamera(true);
        }

        void Dress(Athlete player, int id)
        {
            player.Id = id;
            player.Actor.name = season.Player(id);
            Color primary = League.Kits[player.Team == 0 ? season.Primary : Opponent];
            Color trim = player.Team == 0 ? League.Kits[season.Trim] : Ink;
            if (player.Team == 1 && Vector4.Distance(primary, League.Kits[season.Primary]) < .46f)
                primary = season.Primary == 0 ? Ink : League.Kits[0];
            if (player.Slot == 0) primary = player.Team == 0 ? new Color32(49, 221, 173, 255) : new Color32(250, 145, 54, 255);
            AthleteAppearance.Apply(player.Actor.gameObject, id, season.Player(id), player.Record.Number, primary, trim);
            Appearances[id]++;
        }

        public void Dispose()
        {
            broadcast.Dispose();
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
        }

        public bool CanSub(int slot)
        {
            if (slot <= 0 || slot > 4 || SubUsed || Finished || phase == Phase.Celebration) return false;
            var roles = teams[0].Skip(1).Select((p, i) => i == slot - 1 ? League.Players[reserve].Position : p.Record.Position).ToArray();
            return roles.Contains(Role.Defender) && roles.Contains(Role.Midfielder) && roles.Contains(Role.Forward);
        }

        public void Substitute(int slot)
        {
            if (!CanSub(slot)) return;
            Replace(teams[0][slot], ref reserve);
            SubUsed = true;
            Announce("Substitution", 1.5f);
        }

        void Replace(Athlete player, ref int bench)
        {
            if (player == carrier && phase == Phase.Live) carrier = null;
            int oldId = player.Id;
            Dress(player, bench);
            bench = oldId;
            player.Energy = 1;
            player.SprintExhausted = false;
            player.Cooldown = .25f;
            CancelAction(player);
            if (player.Team == 0 && player.Slot == selected) CancelCharge();
            broadcast.SetPlayer(player.Team * 5 + player.Slot, player.Actor);
        }

        static int Sign(Athlete athlete) => athlete.Team == 0 ? 1 : -1;
        static Vector3 Flat(Vector3 value) => new Vector3(value.x, 0, value.z);
        void Announce(string message, float duration) { Notice = message; noticeTime = duration; }
        void CancelCharge() { charging = false; ShotCharge = 0; }

        public void ClearInput()
        {
            aimInput = Vector3.zero;
            sprintInput = false;
            CancelCharge();
        }

        Vector3 UserAim(Keyboard keys)
        {
            if (keys == null) return Vector3.zero;
            float x = (keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0);
            float z = (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0);
            return Vector3.ClampMagnitude(Flat(camera.transform.right).normalized * x + Flat(camera.transform.forward).normalized * z, 1);
        }

        public void Input(Keyboard keys)
        {
            aimInput = UserAim(keys);
            sprintInput = keys != null && (keys.leftShiftKey.isPressed || keys.rightShiftKey.isPressed);
            if (keys == null || Finished || phase == Phase.Celebration) return;
            if (phase == Phase.Restart && (!restartReady || taker.Team != 0 || taker.AwaitContact)) return;
            if (keys.qKey.wasPressedThisFrame && phase == Phase.Live)
            {
                Athlete next = Nearest(0, Flat(ball.position) + Flat(ball.linearVelocity) * .16f, true, selected);
                selected = next.Slot;
                CancelCharge();
            }
            Athlete player = teams[0][selected];
            if (keys.jKey.wasPressedThisFrame && CanPlay(player))
            {
                CancelCharge();
                Pass(player);
            }
            if (keys.kKey.wasPressedThisFrame && CanPlay(player)) { charging = true; ShotCharge = .12f; }
            if (charging && !CanPlay(player)) CancelCharge();
            if (charging && keys.kKey.isPressed) ShotCharge = Mathf.Min(1, ShotCharge + Time.deltaTime * 1.45f);
            if (charging && !keys.kKey.isPressed)
            {
                float power = ShotCharge;
                CancelCharge();
                if (phase == Phase.Restart && restartKind == RestartKind.ThrowIn) Pass(player);
                else Shoot(player, power);
            }
            if (phase == Phase.Live && keys.lKey.wasPressedThisFrame) Tackle(player);
            if (phase == Phase.Live && keys.kKey.wasPressedThisFrame && carrier == null && ball.position.y > .9f &&
                (Flat(ball.position) - player.Position).sqrMagnitude < 3.2f) StartJump(player);
        }

        public void Tick(float dt)
        {
            if (Finished) return;
            noticeTime -= dt;
            if (noticeTime <= 0) Notice = "";
            for (int i = 0; i < 5; i++)
            {
                GameObject marker = teams[0][i].Marker.gameObject;
                bool active = i == selected && phase != Phase.Celebration;
                if (marker.activeSelf != active) marker.SetActive(active);
            }
            if (phase == Phase.Celebration)
            {
                phaseTime -= dt;
                if (phaseTime <= 0) BeginRestart(RestartKind.None, concedingTeam, Vector3.zero);
                return;
            }
            foreach (Athlete[] side in teams)
            foreach (Athlete a in side) { a.Cooldown -= dt; a.ActionTime = Mathf.Max(0, a.ActionTime - dt); }

            if (phase == Phase.Restart) { TickRestart(dt); return; }
            broadcast.Record(dt);
            DeflectFastBall();
            if (ProcessBoundary()) return;
            Seconds = Mathf.Max(0, Seconds - dt);
            if (Finished) return;
            if (Seconds < 75 && !AiSubUsed)
            {
                Replace(teams[1][3], ref aiReserve);
                AiSubUsed = true;
            }
            holdTime += dt;
            freeTime += dt;
            PlanTactics(dt);
            foreach (Athlete[] side in teams)
            foreach (Athlete a in side)
            {
                if (a.Team == 0 && a.Slot == selected)
                    Move(a, aimInput, sprintInput, dt);
                else MoveAi(a, dt);
            }
            SeparatePlayers();
            foreach (Athlete[] side in teams)
            foreach (Athlete a in side) ResolveContact(a);
            HandleBall(dt);
            previousBall = ball.position;
        }

        void Move(Athlete a, Vector3 direction, bool sprint, float dt)
        {
            Vector3 aim = Flat(direction);
            bool moving = aim.sqrMagnitude > .013f;
            if (a.Energy < .04f) a.SprintExhausted = true;
            if (a.Energy > .24f) a.SprintExhausted = false;
            bool fast = moving && sprint && !a.SprintExhausted && a.ActionTime <= 0;
            float stamina = a.Record.Stamina / 100f;
            a.Energy = Mathf.Clamp01(a.Energy + dt * (fast ? -.087f / stamina : .058f * stamina));
            float top = (fast ? 5.15f : 2.95f) + a.Record.Speed * .026f;
            if (a == carrier) top *= fast ? .89f : .95f;
            if (a.ActionTime > 0) top *= .28f;
            Vector3 desired = Vector3.ClampMagnitude(aim, 1) * top;
            // A keeper side-steps across the goal while keeping the chest and gloves facing play.
            bool trackBall = a.Slot == 0 && phase == Phase.Live && carrier != a && (a.Team != 0 || a.Slot != selected);
            Vector3 travelFacing = trackBall ? Flat(ball.position - a.Position) : aim;
            Vector3 face = a.AwaitContact ? a.ActionDirection : travelFacing;
            Vector3 oldFacing = a.Actor.forward;
            float angle = face.sqrMagnitude > .01f ? Vector3.SignedAngle(oldFacing, face, Vector3.up) : 0;
            // Cutting across a sprint costs momentum. Acceleration then builds out of the turn.
            if (!trackBall && Mathf.Abs(angle) > 70) desired *= .67f;
            bool braking = Vector3.Dot(desired - a.Velocity, a.Velocity) < 0;
            a.Velocity = Vector3.MoveTowards(a.Velocity, desired, (braking ? 17f : 8.2f + a.Record.Speed * .045f) * dt);
            if (face.sqrMagnitude > .01f)
                a.Actor.rotation = Quaternion.RotateTowards(a.Actor.rotation, Quaternion.LookRotation(face), (a.AwaitContact ? 740 : Mathf.Lerp(620, 255, a.Velocity.magnitude / 8)) * dt);
            Vector3 before = a.Position;
            a.Actor.position = PitchClamp(before + a.Velocity * dt);
            a.Velocity = (a.Position - before) / dt;
            float angularSpeed = Vector3.SignedAngle(oldFacing, a.Actor.forward, Vector3.up) / dt;
            a.Motion.SetLocomotion(a.Velocity.magnitude, angularSpeed, a == carrier && phase == Phase.Live);
        }

        static Vector3 PitchClamp(Vector3 position)
        {
            position.x = Mathf.Clamp(position.x, -HalfWidth - .5f, HalfWidth + .5f);
            position.z = Mathf.Clamp(position.z, -HalfLength - .45f, HalfLength + .45f);
            return position;
        }

        void SeparatePlayers()
        {
            // Ten athletes produce 45 pairs. Solve both sides of every contact symmetrically.
            for (int i = 0; i < 9; i++)
            for (int j = i + 1; j < 10; j++)
            {
                Athlete a = teams[i / 5][i % 5], b = teams[j / 5][j % 5];
                Vector3 delta = b.Position - a.Position;
                const float diameter = .60f;
                if (delta.sqrMagnitude >= diameter * diameter) continue;
                float distance = delta.magnitude;
                Vector3 axis = distance < .0001f ? Vector3.right : delta / distance;
                Vector3 correction = axis * ((diameter - distance) * .5f);
                a.Actor.position = PitchClamp(a.Position - correction);
                b.Actor.position = PitchClamp(b.Position + correction);
                float closing = Vector3.Dot(a.Velocity - b.Velocity, axis);
                if (closing <= 0) continue;
                a.Velocity -= axis * (closing * .5f);
                b.Velocity += axis * (closing * .5f);
            }
        }

        Athlete Nearest(int team, Vector3 point, bool includeKeeper, int excludeSlot = -1)
        {
            Athlete best = null;
            float distance = float.MaxValue;
            foreach (Athlete a in teams[team])
            {
                if (a.Slot == excludeSlot || (!includeKeeper && a.Slot == 0)) continue;
                float d = (point - a.Position).sqrMagnitude;
                if (d < distance) { best = a; distance = d; }
            }
            return best;
        }

        bool CanPlay(Athlete a) => !a.AwaitContact && a.Cooldown <= 0 && (carrier == a ||
            (phase == Phase.Restart && restartReady && a == taker));

        void Pass(Athlete a, Athlete recipient = null)
        {
            if (!CanPlay(a)) return;
            Vector3 aim = a.Team == 0 && a.Slot == selected ? aimInput : Vector3.zero;
            Athlete target = recipient ?? BestPass(a, aim);
            if (target == null || target == a) return;
            bool throwing = phase == Phase.Restart && restartKind == RestartKind.ThrowIn;
            float distance = Vector3.Distance(target.Position, a.Position);
            Vector3 lead = target.Position + target.Velocity * Mathf.Clamp(distance / 18, .12f, .85f);
            Vector3 direction = Flat(lead - ball.position);
            float error = (100 - a.Record.Passing) * .03f;
            direction = Quaternion.AngleAxis(UnityEngine.Random.Range(-error, error), Vector3.up) * direction;
            float lift = throwing ? 3.5f + distance * .055f : .18f;
            QueueKick(a, throwing ? FootballAction.ThrowIn : FootballAction.Pass, direction,
                Mathf.Clamp(5.7f + distance * .68f, 5.8f, throwing ? 16f : 20f), lift, target);
        }

        void Shoot(Athlete a, float power01)
        {
            if (!CanPlay(a)) return;
            float power = Mathf.Clamp01(power01);
            Vector3 aim = a.Team == 0 && a.Slot == selected ? aimInput : a.Actor.forward;
            float targetX = Mathf.Clamp(aim.x * GoalHalf * .87f, -GoalHalf + .24f, GoalHalf - .24f);
            float scatter = (100 - a.Record.Shooting) * (.011f + .01f * power);
            targetX += UnityEngine.Random.Range(-scatter, scatter);
            Vector3 target = new Vector3(targetX, 0, Sign(a) * (HalfLength + .7f));
            bool clearance = phase == Phase.Restart && restartKind == RestartKind.GoalKick;
            if (clearance) target = a.Position + (aim.sqrMagnitude > .05f ? aim : Vector3.forward * Sign(a)) * 20;
            QueueKick(a, FootballAction.Shoot, Flat(target - ball.position),
                (10 + .033f * a.Record.Shooting) * Mathf.Lerp(1, 2.05f, power), (clearance ? 4.3f : 1.0f) + power * 3.2f, null);
        }

        void QueueKick(Athlete a, FootballAction action, Vector3 direction, float speed, float lift, Athlete target)
        {
            BeginAction(a, action, direction, .64f);
            a.KickPower = speed;
            a.KickLift = lift;
            a.PassTarget = target;
        }

        void BeginAction(Athlete a, FootballAction action, Vector3 direction, float recovery)
        {
            a.Action = action;
            a.ActionTime = recovery;
            a.Cooldown = recovery;
            a.ActionDirection = direction.sqrMagnitude < .0001f ? a.Actor.forward : Flat(direction).normalized;
            a.AwaitContact = true;
            a.PassTarget = null;
            a.Motion.Play(action);
        }

        void CancelAction(Athlete a)
        {
            a.AwaitContact = false;
            a.ActionTime = 0;
            a.PassTarget = null;
            a.Motion.ResetPose();
        }

        void StartSave(Athlete a, Vector3 direction)
        {
            if (a.Slot != 0 || a.AwaitContact || a.Cooldown > 0 || carrier == a) return;
            float side = Vector3.Dot(direction, a.Actor.right);
            BeginAction(a, side < 0 ? FootballAction.SaveLeft : FootballAction.SaveRight, a.Actor.forward, .92f);
        }

        void StartJump(Athlete a)
        {
            if (a.AwaitContact || a.Cooldown > 0) return;
            BeginAction(a, FootballAction.Jump, a.Actor.forward, .78f);
        }

        void Tackle(Athlete a)
        {
            if (a.AwaitContact || a.Cooldown > 0 || carrier == null || carrier.Team == a.Team) return;
            if ((carrier.Position - a.Position).sqrMagnitude > 3.5f) return;
            BeginAction(a, FootballAction.Tackle, Flat(ball.position) - a.Position, .76f);
        }

        void ResolveContact(Athlete a)
        {
            if (!a.AwaitContact || !a.Motion.ConsumeContact()) return;
            a.AwaitContact = false;
            float distance = (Flat(ball.position) - a.Position).magnitude;
            if (a.Action == FootballAction.Tackle)
            {
                if (carrier != null && carrier.Team != a.Team && distance < .67f + a.Record.Defending * .0085f && ball.position.y < .75f)
                {
                    carrier.Cooldown = .38f;
                    carrier = null;
                    Touch(a);
                    assistId = -1;
                    ball.linearVelocity = a.Actor.forward * 3.6f + Vector3.up * .23f;
                    freeTime = 0;
                }
                return;
            }
            if (a.Action == FootballAction.SaveLeft || a.Action == FootballAction.SaveRight)
            {
                Vector3 intercept = a.Position + a.Actor.right * (a.Action == FootballAction.SaveLeft ? -.52f : .52f);
                if (carrier != null || (Flat(ball.position) - intercept).magnitude > 1.22f || ball.position.y > 1.85f) return;
                if (ball.linearVelocity.magnitude < 8.3f + a.Record.Goalkeeping * .045f) Claim(a);
                else
                {
                    Vector3 outward = new Vector3(ball.position.x >= a.Position.x ? 1 : -1, .28f, .55f * Sign(a)).normalized;
                    ball.linearVelocity = outward * Mathf.Clamp(ball.linearVelocity.magnitude * .53f, 4, 12);
                    Touch(a);
                    assistId = -1;
                    freeTime = 0;
                }
                return;
            }
            if (a.Action == FootballAction.Jump)
            {
                if (carrier != null || distance > .83f || ball.position.y < 1.05f || ball.position.y > 2.1f) return;
                ball.linearVelocity = a.Actor.forward * 9 + Vector3.up * 1.2f;
                freeTime = 0;
                Touch(a);
                return;
            }
            bool restart = phase == Phase.Restart && a == taker;
            if (!restart && (carrier != a || distance > 1.35f)) return;
            if (restart)
            {
                // Release from the touchline as the taker's arms travel over the head.
                if (restartKind == RestartKind.ThrowIn) ball.position = restartSpot + Vector3.up * 1.78f;
                phase = Phase.Live;
                ball.isKinematic = false;
                ball.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                previousBall = ball.position;
                Announce("", 0);
            }
            carrier = null;
            freeTime = 0;
            Touch(a);
            if (restart && restartKind == RestartKind.ThrowIn) directThrower = a.Id;
            if (a.PassTarget != null)
            {
                assistId = a.Id;
                assistTeam = a.Team;
                if (a.Team == 0 && selected == a.Slot) { selected = a.PassTarget.Slot; CancelCharge(); }
            }
            else if (assistId == a.Id || assistTeam != a.Team) assistId = -1;
            ball.linearVelocity = a.ActionDirection * a.KickPower + Vector3.up * a.KickLift;
            ball.angularVelocity = Vector3.Cross(Vector3.up, a.ActionDirection) * Mathf.Min(45f, a.KickPower / BallRadius);
        }

        void Touch(Athlete a)
        {
            if (a.Id != directThrower) directThrower = -1;
            lastTeam = a.Team;
            lastId = a.Id;
            if (assistTeam != a.Team) assistId = -1;
        }

        void Claim(Athlete a)
        {
            carrier = a;
            holdTime = 0;
            touchDelay = .10f;
            Touch(a);
            // First touch absorbs part of the incoming pace. A heavy pass may run away again.
            ball.linearVelocity = Vector3.Lerp(ball.linearVelocity, a.Velocity, .57f + a.Record.Passing * .0025f);
        }

        void HandleBall(float dt)
        {
            if (carrier == null)
            {
                if (freeTime < .075f) return;
                Vector3 position = ball.position;
                Athlete nearest = null;
                float best = float.MaxValue;
                foreach (Athlete[] side in teams)
                foreach (Athlete a in side)
                {
                    if (a.Id == lastId && freeTime < .28f) continue;
                    float distance = (Flat(position) - a.Position).sqrMagnitude;
                    bool keeper = a.Slot == 0 && -Sign(a) * a.Position.z > HalfLength - 5 && Mathf.Abs(a.Position.x) < 6;
                    float radius = keeper ? .94f : .69f;
                    float maxPace = keeper ? 12.5f : 8.6f + a.Record.Passing * .06f;
                    if (distance >= best || distance > radius * radius || position.y > (keeper ? 1.24f : .79f)) continue;
                    if ((ball.linearVelocity - a.Velocity).sqrMagnitude > maxPace * maxPace) continue;
                    nearest = a; best = distance;
                }
                if (nearest != null) Claim(nearest);
                return;
            }
            if ((carrier.Position - Flat(ball.position)).sqrMagnitude > 2.56f)
            {
                carrier = null;
                freeTime = 1;
                return;
            }
            touchDelay -= dt;
            Vector3 forward = carrier.Actor.forward;
            float speed = carrier.Velocity.magnitude;
            // A sequence of short toe touches leaves the physical ball free between contacts.
            float reach = carrier.AwaitContact ? .43f : .41f + speed * .047f;
            float side = Mathf.Sin(holdTime * (6 + speed * .6f)) * .085f;
            Vector3 target = carrier.Position + forward * reach + carrier.Actor.right * side;
            Vector3 correction = Flat(target - ball.position) * (speed > 3 ? 5.2f : 7.6f);
            if (touchDelay <= 0 || carrier.AwaitContact)
            {
                Vector3 horizontal = Vector3.ClampMagnitude(carrier.Velocity + correction, 9.6f);
                ball.linearVelocity = new Vector3(horizontal.x, Mathf.Min(ball.linearVelocity.y, .35f), horizontal.z);
                ball.angularVelocity = Vector3.Cross(Vector3.up, horizontal) / BallRadius;
                touchDelay = Mathf.Lerp(.13f, .25f, speed / 7.5f);
            }
        }

        void DeflectFastBall()
        {
            if (carrier != null) return;
            Vector3 step = ball.position - previousBall;
            Vector3 flatStep = Flat(step);
            float stepLengthSq = flatStep.sqrMagnitude;
            if (stepLengthSq < .00001f) return;

            // Outfielders can block a driven pass that is too fast to trap. Find the first
            // body hit along the previous physics step, before any whole-ball line crossing.
            float earliest = float.PositiveInfinity;
            Athlete blocker = null;
            Vector3 hit = default, normal = default;
            const float radius = BallRadius + .18f;
            foreach (Athlete[] side in teams)
            foreach (Athlete player in side)
            {
                if (player.Slot == 0 || player.Id == lastId && freeTime < .28f) continue;
                float controlSpeed = 8.6f + player.Record.Passing * .06f;
                Vector3 incoming = ball.linearVelocity - player.Velocity;
                if (incoming.sqrMagnitude <= controlSpeed * controlSpeed) continue;
                Vector3 offset = Flat(previousBall - player.Position);
                float along = Vector3.Dot(offset, flatStep);
                float discriminant = along * along - stepLengthSq * (offset.sqrMagnitude - radius * radius);
                if (along >= 0 || discriminant < 0) continue;
                float fraction = Mathf.Max(0, (-along - Mathf.Sqrt(discriminant)) / stepLengthSq);
                if (fraction > 1 || fraction >= earliest) continue;
                Vector3 point = previousBall + step * fraction;
                if (point.y > 1.72f * player.Actor.lossyScale.y + BallRadius ||
                    Mathf.Abs(point.x) > HalfWidth + BallRadius || Mathf.Abs(point.z) > HalfLength + BallRadius) continue;
                Vector3 outward = Flat(point - player.Position).normalized;
                if (Vector3.Dot(incoming, outward) >= 0) continue;
                earliest = fraction;
                hit = point;
                normal = outward;
                blocker = player;
            }
            if (blocker == null) return;
            ball.position = hit;
            ball.linearVelocity = blocker.Velocity + Vector3.Reflect(ball.linearVelocity - blocker.Velocity, normal) * .43f;
            ball.angularVelocity *= .45f;
            Touch(blocker);
            freeTime = 0;
        }

        bool ProcessBoundary()
        {
            Vector3 now = ball.position;
            if (!MatchRules.TryCrossing(previousBall.x, previousBall.y, previousBall.z, now.x, now.y, now.z,
                HalfWidth, HalfLength, GoalHalf - .07f, GoalHeight - .07f, BallRadius, lastTeam, out BoundaryDecision result))
                return false;

            if (result.Kind == RestartKind.Goal)
            {
                int scoringTeam = result.Team;
                // A throw-in must touch another player before a goal can be awarded.
                if (directThrower >= 0)
                {
                    bool intoOpponentsGoal = lastTeam == scoringTeam;
                    var kind = intoOpponentsGoal ? RestartKind.GoalKick : RestartKind.Corner;
                    BeginRestart(kind, 1 - lastTeam, new Vector3(now.x, 0, result.Z));
                    return true;
                }
                if (scoringTeam == 0) UserScore++; else OpponentScore++;
                Athlete scorer = Nearest(scoringTeam, Flat(now), true);
                if (lastTeam == scoringTeam && lastId >= 0)
                {
                    Goals[lastId]++;
                    if (assistTeam == scoringTeam && assistId >= 0 && assistId != lastId) Assists[assistId]++;
                    foreach (Athlete athlete in teams[scoringTeam]) if (athlete.Id == lastId) scorer = athlete;
                }
                phase = Phase.Celebration;
                carrier = null;
                CancelCharge();
                FreezeBall(now);
                foreach (Athlete[] team in teams)
                foreach (Athlete player in team)
                {
                    CancelAction(player);
                    player.Velocity = Vector3.zero;
                }
                scorer.Motion.Play(FootballAction.Celebrate);
                phaseTime = broadcast.Goal(scoringTeam * 5 + scorer.Slot);
                concedingTeam = 1 - scoringTeam;
                Announce("GOAL  /  " + season.Team(scoringTeam == 0 ? League.UserClub : Opponent), phaseTime);
                return true;
            }

            BeginRestart(result.Kind, result.Team, new Vector3(result.X, 0, result.Z));
            return true;
        }

        int directThrower = -1;
        float readyTime;

        void FreezeBall(Vector3 point)
        {
            if (!ball.isKinematic)
            {
                ball.linearVelocity = Vector3.zero;
                ball.angularVelocity = Vector3.zero;
                ball.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                ball.isKinematic = true;
            }
            ball.position = point;
            previousBall = point;
        }

        void BeginRestart(RestartKind kind, int team, Vector3 linePoint)
        {
            phase = Phase.Restart;
            restartKind = kind;
            carrier = null;
            directThrower = -1;
            assistId = -1;
            assistTeam = -1;
            freeTime = 0;
            holdTime = 0;
            decisionIn = 0;
            restartReady = false;
            readyTime = 0;
            CancelCharge();
            broadcast.ResetSequence();
            foreach (Athlete[] side in teams)
            foreach (Athlete athlete in side)
            {
                CancelAction(athlete);
                athlete.Cooldown = 0;
            }
            string label;
            float end = Mathf.Sign(linePoint.z);
            switch (kind)
            {
                case RestartKind.GoalKick:
                    label = "Goal kick";
                    restartSpot = new Vector3(Mathf.Sign(linePoint.x) * 2, 0, end * (HalfLength - 2.6f));
                    taker = teams[team][0];
                    break;
                case RestartKind.Corner:
                    label = "Corner";
                    restartSpot = new Vector3(Mathf.Sign(linePoint.x) * (HalfWidth - .14f), 0, end * (HalfLength - .14f));
                    taker = Nearest(team, restartSpot, false);
                    break;
                case RestartKind.ThrowIn:
                    label = "Throw-in";
                    restartSpot = new Vector3(Mathf.Sign(linePoint.x) * HalfWidth, 0,
                        Mathf.Clamp(linePoint.z, -HalfLength + .38f, HalfLength - .38f));
                    taker = Nearest(team, restartSpot, false);
                    break;
                default:
                    label = "Kickoff";
                    restartSpot = Vector3.zero;
                    taker = teams[team].First(a => a.Record.Position == Role.Forward);
                    break;
            }
            FreezeBall(restartSpot + Vector3.up * (BallRadius + .015f));
            if (team == 0) selected = taker.Slot;
            else if (selected == 0 || kind == RestartKind.None) selected = Nearest(0, restartSpot, false).Slot;
            Announce(label + "  /  " + season.Team(team == 0 ? League.UserClub : Opponent), float.PositiveInfinity);
        }

        Vector3 TakerApproach()
        {
            float sign = Sign(taker);
            switch (restartKind)
            {
                case RestartKind.ThrowIn: return Vector3.right * -Mathf.Sign(restartSpot.x);
                case RestartKind.Corner: return new Vector3(-Mathf.Sign(restartSpot.x), 0, -Mathf.Sign(restartSpot.z) * .5f).normalized;
                default: return Vector3.forward * sign;
            }
        }

        Vector3 RestartPosition(Athlete a)
        {
            if (a == taker) return PitchClamp(restartSpot - TakerApproach() * .42f);
            int sign = Sign(a);
            if (restartKind == RestartKind.None) return Formation[a.Slot] * sign;
            if (a.Slot == 0) return new Vector3(Mathf.Clamp(restartSpot.x * .27f, -1.8f, 1.8f), 0, -sign * (HalfLength - 1.65f));
            Vector3 home = Formation[a.Slot] * sign;
            home.x += restartSpot.x * .30f;
            bool attacking = a.Team == taker.Team;
            home.z += Mathf.Clamp(restartSpot.z * .60f + sign * (attacking ? 6.0f : 1.0f), -15, 15);
            if (restartKind == RestartKind.Corner)
            {
                // Keep one player outside the area while the forwards make near- and far-post runs.
                home.z = sign * (attacking ? (a.Slot == 1 ? 2.4f : HalfLength - 4f - a.Slot * .60f) :
                    (-HalfLength + 1.75f + a.Slot * .81f));
                home.x = Mathf.Sign(restartSpot.x) * (a.Slot % 2 == 0 ? -1 : 1) * (1.3f + a.Slot * .65f);
            }
            Vector3 separation = Flat(home - restartSpot);
            if (separation.sqrMagnitude < 7f)
                home = restartSpot + (separation.sqrMagnitude > .01f ? separation.normalized : Vector3.forward * -sign) * 2.7f;
            return PitchClamp(home);
        }

        void TickRestart(float dt)
        {
            bool inOwnHalves = true;
            foreach (Athlete[] side in teams)
            foreach (Athlete a in side)
            {
                Vector3 delta = RestartPosition(a) - a.Position;
                Vector3 steering = Vector3.ClampMagnitude(delta * 1.8f, 1);
                if (delta.sqrMagnitude > .36f)
                {
                    // Walk around players returning the other way, including after a goal.
                    Vector3 heading = steering.normalized;
                    Vector3 sidestep = Vector3.Cross(Vector3.up, heading);
                    foreach (Athlete[] team in teams)
                    foreach (Athlete other in team)
                    {
                        if (a == other) continue;
                        Vector3 gap = other.Position - a.Position;
                        float ahead = Vector3.Dot(gap, heading);
                        if (ahead <= 0 || ahead > 1.25f || Mathf.Abs(Vector3.Dot(gap, sidestep)) >= .62f) continue;
                        steering += sidestep * Mathf.Clamp01(1.35f - ahead);
                        break;
                    }
                }
                Move(a, steering, false, dt);
                if (a != taker && restartKind == RestartKind.None && Sign(a) * a.Position.z > 0)
                    inOwnHalves = false;
            }
            SeparatePlayers();
            if (!restartReady)
            {
                if (!inOwnHalves || (RestartPosition(taker) - taker.Position).sqrMagnitude > .14f) return;
                restartReady = true;
                if (restartKind == RestartKind.ThrowIn)
                    FreezeBall(taker.Position + Vector3.up * 1.73f - TakerApproach() * .06f);
            }
            if (taker.AwaitContact)
            {
                ResolveContact(taker);
                return;
            }
            Quaternion facing = Quaternion.LookRotation(TakerApproach());
            taker.Actor.rotation = Quaternion.RotateTowards(taker.Actor.rotation, facing, 370 * dt);
            readyTime += dt;
            if (readyTime >= (taker.Team == 0 ? 3f : .65f) && !charging) Pass(taker);
        }

        public void UpdateCamera(bool immediate = false) => broadcast.Track(selected, immediate);
    }
}
