using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using static MiniFootball.FootballArt;

namespace MiniFootball
{
    public sealed class Match
    {
        public const float Duration = 180;
        public readonly Fixture Fixture;
        public readonly int Opponent;
        public readonly int[] Appearances = new int[50], Goals = new int[50], Assists = new int[50];
        public int UserScore { get; private set; }
        public int OpponentScore { get; private set; }
        public float Seconds { get; private set; } = Duration;
        public bool Finished => Seconds <= 0;
        public string Notice { get; private set; }
        public int SelectedId => teams[0][selected].Id;
        public float Energy => teams[0][selected].Energy;
        public int ReserveId => reserve;
        public bool SubUsed { get; private set; }
        public bool AiSubUsed { get; private set; }
        public bool CanSub(int slot)
        {
            if (slot <= 0 || slot > 4 || SubUsed || Finished) return false;
            var roles = teams[0].Skip(1).Select((p, i) => i == slot - 1 ? League.Players[reserve].Position : p.Record.Position).ToArray();
            return roles.Contains(Role.Defender) && roles.Contains(Role.Midfielder) && roles.Contains(Role.Forward);
        }
        public int StarterId(int slot) => teams[0][slot].Id;
        public string Clock => TimeSpan.FromSeconds(Mathf.CeilToInt(Seconds)).ToString(@"m\:ss");

        sealed class Athlete
        {
            public int Id, Team, Slot;
            public Transform Actor, Marker;
            public Animation Animation;
            public Footballer Record => League.Players[Id];
            public float Energy = 1, Cooldown, KickTime;
            public Vector3 Position => Actor.position;
        }

        readonly GameObject root;
        readonly Athlete[][] teams = { new Athlete[5], new Athlete[5] };
        readonly Rigidbody ball;
        readonly TrailRenderer trail;
        readonly Camera camera;
        readonly GameObject character;
        readonly Season season;
        readonly Material markerMaterial;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        Athlete carrier, lastTouch, assister;
        int selected = 3, reserve, aiReserve;
        float restart, looseTime, holdTime, noticeTime;
        Vector3 previousBall;
        static readonly Vector3[] Formation = {
            new Vector3(0,0,-18.2f), new Vector3(-4.2f,0,-10),
            new Vector3(3.9f,0,-5.8f), new Vector3(0,0,-2.3f), new Vector3(-5.1f,0,-4.6f)
        };
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), StandardColor = Shader.PropertyToID("_Color");
        static readonly Color[] Skin = {
            new Color32(244,191,146,255), new Color32(211,155,111,255), new Color32(177,116,80,255),
            new Color32(131,81,57,255), new Color32(97,59,47,255)
        };
        static readonly Color[] Hair = { new Color32(29,24,32,255), new Color32(73,44,30,255), new Color32(158,105,51,255) };

        public Match(Season season, Fixture fixture, GameObject character, GameObject ballPrefab, Camera camera, Material markerMaterial)
        {
            this.season = season;
            this.character = character;
            this.camera = camera;
            this.markerMaterial = markerMaterial;
            Fixture = fixture;
            Opponent = fixture.Home == League.UserClub ? fixture.Away : fixture.Home;
            root = new GameObject("Live Match");
            for (int team = 0; team < 2; team++)
            {
                int[] ids = League.Squad(season, team == 0 ? League.UserClub : Opponent);
                for (int slot = 0; slot < 5; slot++) teams[team][slot] = Spawn(ids[slot], team, slot);
                if (team == 0) reserve = ids[5];
                else aiReserve = ids[5];
            }
            var ballObject = UnityEngine.Object.Instantiate(ballPrefab, root.transform);
            ballObject.SetActive(true);
            ball = ballObject.GetComponent<Rigidbody>();
            trail = ballObject.AddComponent<TrailRenderer>();
            trail.time = .14f; trail.startWidth = .13f; trail.endWidth = 0;
            trail.sharedMaterial = markerMaterial; trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ResetKickoff(0, "KICKOFF");
            UpdateCamera(true);
        }

        Athlete Spawn(int id, int team, int slot)
        {
            Transform actor = UnityEngine.Object.Instantiate(character, root.transform).transform;
            actor.name = season.Player(id);
            actor.gameObject.SetActive(true);
            Transform marker = Part("Selection Marker", PrimitiveType.Cylinder, actor, new Vector3(0, .026f, 0),
                new Vector3(.83f, .013f, .83f), markerMaterial).transform;
            marker.gameObject.SetActive(false);
            Color primary = League.Kits[team == 0 ? season.Primary : Opponent];
            Color trim = team == 0 ? League.Kits[season.Trim] : Ink;
            if (team == 1 && Vector4.Distance((Vector4)primary, (Vector4)League.Kits[season.Primary]) < .43f)
                primary = League.Kits[season.Primary] == League.Kits[0] ? new Color32(26, 34, 51, 255) : League.Kits[0];
            if (slot == 0) primary = team == 0 ? new Color32(49, 221, 173, 255) : new Color32(250, 145, 54, 255);
            foreach (Renderer renderer in actor.GetComponentsInChildren<Renderer>())
            {
                string part = renderer.name;
                Color? color = null;
                if (part.StartsWith("Kit_", StringComparison.Ordinal)) color = primary;
                else if (part.StartsWith("Trim_", StringComparison.Ordinal) || part == "Crest") color = trim;
                else if (part.StartsWith("Skin_", StringComparison.Ordinal)) color = Skin[id % Skin.Length];
                else if (part.StartsWith("Hair_", StringComparison.Ordinal)) color = Hair[(id / 2) % Hair.Length];
                if (!color.HasValue) continue;
                block.Clear();
                block.SetColor(BaseColor, color.Value);
                block.SetColor(StandardColor, color.Value);
                renderer.SetPropertyBlock(block);
            }
            actor.Find("Body/Hair_Fringe").localScale *= .75f + (id % 5) * .10f;
            Appearances[id]++;
            return new Athlete { Id = id, Team = team, Slot = slot, Actor = actor, Marker = marker, Animation = actor.GetComponent<Animation>() };
        }

        public void Dispose()
        {
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
        }

        public void Substitute(int slot)
        {
            if (!CanSub(slot)) return;
            Replace(0, slot, ref reserve);
            SubUsed = true;
            Announce("SUBSTITUTION", 1.5f);
        }

        void Replace(int team, int slot, ref int bench)
        {
            Athlete leaving = teams[team][slot];
            if (carrier == leaving)
            {
                carrier = null;
                looseTime = .2f;
            }
            var arriving = Spawn(bench, team, slot);
            arriving.Actor.SetPositionAndRotation(leaving.Position, leaving.Actor.rotation);
            bench = leaving.Id;
            leaving.Actor.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(leaving.Actor.gameObject);
            teams[team][slot] = arriving;
        }

        void ResetKickoff(int team, string message)
        {
            foreach (Athlete[] side in teams)
            foreach (Athlete athlete in side)
            {
                int sign = Sign(athlete);
                athlete.Actor.SetPositionAndRotation(Formation[athlete.Slot] * sign, Quaternion.Euler(0, sign == 1 ? 0 : 180, 0));
                athlete.Animation.Play("Idle");
            }
            Athlete kickoff = teams[team].First(athlete => athlete.Record.Position == Role.Forward);
            kickoff.Actor.position = new Vector3(0, 0, -.64f * Sign(kickoff));
            selected = team == 0 ? kickoff.Slot : 2;
            carrier = kickoff;
            holdTime = 0;
            lastTouch = null;
            assister = null;
            restart = 1.35f;
            SetBall(new Vector3(0, BallRadius + .02f, 0));
            Announce(message, restart);
        }

        void SetBall(Vector3 position)
        {
            ball.position = position;
            ball.linearVelocity = Vector3.zero;
            ball.angularVelocity = Vector3.zero;
            previousBall = position;
            trail.Clear();
        }

        int Sign(Athlete athlete) => athlete.Team == 0 ? 1 : -1;
        void Announce(string message, float duration) { Notice = message; noticeTime = duration; }

        public void Input(Keyboard keys)
        {
            if (keys == null || restart > 0 || Finished) return;
            if (keys.qKey.wasPressedThisFrame)
            {
                var nearest = teams[0].Where(a => a.Slot != selected)
                    .OrderBy(a => (a.Position - ball.position).sqrMagnitude).First();
                selected = nearest.Slot;
            }
            Athlete player = teams[0][selected];
            if (keys.jKey.wasPressedThisFrame && CanPlay(player)) Pass(player);
            if (keys.kKey.wasPressedThisFrame && CanPlay(player)) Shoot(player);
            if (keys.lKey.wasPressedThisFrame) Tackle(player);
        }

        public void Tick(float dt, Keyboard keys)
        {
            if (Finished) return;
            noticeTime -= dt;
            if (noticeTime <= 0) Notice = "";
            foreach (Athlete athlete in teams[0]) athlete.Marker.gameObject.SetActive(athlete.Slot == selected);
            trail.emitting = carrier == null && ball.linearVelocity.sqrMagnitude > 34;
            if (restart > 0)
            {
                restart -= dt;
                return;
            }
            if (CheckGoal()) return;
            Seconds = Mathf.Max(0, Seconds - dt);
            if (Finished) return;
            if (Seconds < 75 && !AiSubUsed)
            {
                Replace(1, 3, ref aiReserve);
                AiSubUsed = true;
            }
            looseTime -= dt;
            holdTime += dt;
            KeepBallInPlay();
            foreach (Athlete[] side in teams)
            {
                Athlete chaser = side.Skip(1).OrderBy(a => (a.Position - ball.position).sqrMagnitude).First();
                foreach (Athlete athlete in side)
                {
                    athlete.Cooldown -= dt;
                    athlete.KickTime -= dt;
                    if (athlete.Team == 0 && athlete.Slot == selected) MoveUser(athlete, keys, dt);
                    else MoveAi(athlete, chaser, dt);
                }
            }
            TrackBall(dt);
            previousBall = ball.position;
        }

        void MoveUser(Athlete athlete, Keyboard keys, float dt)
        {
            Vector3 input = keys == null ? Vector3.zero : new Vector3(
                (keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0), 0,
                (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0));
            Move(athlete, input, keys != null && (keys.leftShiftKey.isPressed || keys.rightShiftKey.isPressed), dt);
        }

        void MoveAi(Athlete athlete, Athlete chaser, float dt)
        {
            int sign = Sign(athlete);
            Vector3 ballXZ = Vector3.ProjectOnPlane(ball.position, Vector3.up);
            Vector3 target = Formation[athlete.Slot] * sign;
            if (carrier == athlete)
            {
                if (athlete.Slot == 0 && holdTime > .35f)
                {
                    Pass(athlete);
                    return;
                }
                var goal = new Vector3(0, 0, sign * HalfLength);
                float goalDistance = Vector3.Distance(goal, athlete.Position);
                bool pressure = teams[1 - athlete.Team].Any(a => Vector3.Distance(a.Position, athlete.Position) < 2);
                if (holdTime > .28f && athlete.Cooldown <= 0)
                {
                    if (goalDistance < 11) { Shoot(athlete); return; }
                    if (pressure && holdTime > .55f) { Pass(athlete); return; }
                }
                target = goal + Vector3.right * Mathf.Sin(athlete.Id * 2f) * Mathf.Clamp(goalDistance - 7, 0, 4);
            }
            else if (athlete.Slot == 0)
            {
                float threat = Mathf.InverseLerp(2, 15, -ballXZ.z * sign);
                target = new Vector3(Mathf.Clamp(ballXZ.x * .65f, -2.62f, 2.62f), 0, sign * (-18.9f + threat * 2.4f));
                if (carrier == null && -sign * ballXZ.z > 14 && Mathf.Abs(ballXZ.x) < 4.8f)
                    target = ballXZ + Vector3.ProjectOnPlane(ball.linearVelocity, Vector3.up) * .12f;
                if (carrier != null && carrier.Team != athlete.Team && Vector3.Distance(athlete.Position, carrier.Position) < 1.25f)
                    Tackle(athlete);
            }
            else if (athlete == chaser && (carrier == null || carrier.Team != athlete.Team))
            {
                target = ballXZ + Vector3.ProjectOnPlane(ball.linearVelocity, Vector3.up) * (carrier == null ? .11f : 0);
                if (carrier != null && Vector3.Distance(athlete.Position, carrier.Position) < 1.25f) Tackle(athlete);
            }
            else
            {
                float advance = Mathf.Clamp(sign * ballXZ.z * .48f + (carrier != null && carrier.Team == athlete.Team ? 5 : 1), -7, 12);
                target += new Vector3(ballXZ.x * .19f, 0, advance * sign);
            }
            Vector3 direction = target - athlete.Position;
            if (direction.sqrMagnitude < .23f) direction = Vector3.zero;
            Move(athlete, direction, athlete == chaser && carrier?.Team != athlete.Team, dt);
        }

        void Move(Athlete athlete, Vector3 direction, bool sprint, float dt)
        {
            bool moving = direction.sqrMagnitude > .02f;
            bool running = moving && sprint && athlete.Energy > .08f && athlete.Slot != 0;
            float factor = athlete.Record.Stamina / 100f;
            athlete.Energy = Mathf.Clamp01(athlete.Energy + dt * (running ? -.29f / factor : .17f * factor));
            if (moving)
            {
                direction.Normalize();
                Vector3 steer = direction;
                foreach (Athlete[] side in teams)
                foreach (Athlete other in side)
                {
                    if (other == athlete) continue;
                    Vector3 away = athlete.Position - other.Position;
                    float distance = away.magnitude;
                    if (distance > .001f && distance < .68f) steer += away.normalized * (1 - distance / .68f);
                }
                float speed = 3.8f + athlete.Record.Speed * .022f;
                if (running) speed *= 1.32f;
                if (carrier == athlete) speed *= .84f;
                Vector3 pos = athlete.Position + steer.normalized * speed * dt;
                pos.x = Mathf.Clamp(pos.x, -HalfWidth + .32f, HalfWidth - .32f);
                pos.z = Mathf.Clamp(pos.z, -HalfLength + .35f, HalfLength - .35f);
                athlete.Actor.position = pos;
                athlete.Actor.rotation = Quaternion.RotateTowards(athlete.Actor.rotation, Quaternion.LookRotation(direction), 820 * dt);
            }
            if (athlete.KickTime > 0) return;
            string clip = moving ? "Run" : "Idle";
            if (!athlete.Animation.IsPlaying(clip)) athlete.Animation.CrossFade(clip, .10f);
            athlete.Animation[clip].speed = running ? 1.33f : 1;
        }

        bool CanPlay(Athlete athlete) => athlete.Cooldown <= 0 && (carrier == athlete ||
            (carrier == null && Vector3.Distance(ball.position, athlete.Position) < 1.23f && ball.position.y < .9f));

        void Pass(Athlete athlete)
        {
            var target = teams[athlete.Team].Where(a => a != athlete && a.Slot != 0)
                .OrderBy(a => (a.Position - athlete.Position).magnitude - Sign(athlete) * (a.Position.z - athlete.Position.z) * .54f
                    - Vector3.Dot(athlete.Actor.forward, (a.Position - athlete.Position).normalized) * 2.1f).First();
            Vector3 direction = target.Position - ball.position;
            float distance = direction.magnitude;
            float error = (100 - athlete.Record.Passing) * .033f;
            direction.y = 0;
            direction.x += UnityEngine.Random.Range(-error, error);
            Strike(athlete, direction, Mathf.Clamp(7 + distance * .62f, 9, 17), .48f, true);
            if (athlete.Team == 0) selected = target.Slot;
        }

        void Shoot(Athlete athlete)
        {
            float sign = Sign(athlete);
            float x = Mathf.Clamp(athlete.Actor.forward.x * GoalHalf + ball.position.x * .075f, -2.42f, 2.42f);
            float error = (100 - athlete.Record.Shooting) * .034f;
            x += UnityEngine.Random.Range(-error, error);
            Vector3 direction = new Vector3(x, ball.position.y, sign * (HalfLength + 1)) - ball.position;
            if (sign * direction.z < .01f) direction.z = sign;
            Strike(athlete, direction, 13 + athlete.Record.Shooting * .1f, 2.6f, false);
        }

        void Strike(Athlete athlete, Vector3 direction, float power, float lift, bool pass)
        {
            if (direction.sqrMagnitude < .001f) direction = athlete.Actor.forward;
            carrier = null;
            looseTime = .26f;
            if (pass) assister = athlete;
            else if (assister?.Team != athlete.Team || assister == athlete) assister = null;
            lastTouch = athlete;
            athlete.Actor.rotation = Quaternion.LookRotation(direction);
            ball.linearVelocity = direction.normalized * power + Vector3.up * lift;
            ball.angularVelocity = Vector3.Cross(Vector3.up, direction.normalized) * (power / BallRadius);
            athlete.Cooldown = .53f;
            athlete.KickTime = .34f;
            athlete.Animation.Play("Kick");
        }

        void Tackle(Athlete athlete)
        {
            if (athlete.Cooldown > 0 || carrier == null || carrier.Team == athlete.Team) return;
            athlete.Cooldown = .72f;
            athlete.KickTime = .34f;
            athlete.Animation.Play("Kick");
            float reach = .8f + athlete.Record.Defending * .008f;
            if (Vector3.Distance(athlete.Position, carrier.Position) > reach ||
                Vector3.Distance(athlete.Position, ball.position) > reach + .26f) return;
            carrier.Cooldown = .78f;
            Claim(athlete);
        }

        void Claim(Athlete athlete)
        {
            if (assister?.Team != athlete.Team) assister = null;
            carrier = athlete;
            lastTouch = athlete;
            holdTime = 0;
        }

        void TrackBall(float dt)
        {
            if (carrier == null && looseTime <= 0)
            {
                Athlete nearest = null;
                float nearestDistance = float.MaxValue;
                foreach (Athlete[] side in teams)
                foreach (Athlete athlete in side)
                {
                    float height = athlete.Slot == 0 ? 1.35f : .8f;
                    float radius = athlete.Slot == 0 ? .8f + athlete.Record.Goalkeeping * .0045f : .81f;
                    float distance = (Vector3.ProjectOnPlane(ball.position, Vector3.up) - athlete.Position).sqrMagnitude;
                    if (ball.position.y <= height && distance < radius * radius && distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = athlete;
                    }
                }
                if (nearest != null) Claim(nearest);
            }
            if (carrier == null) return;
            Vector3 target = carrier.Position + carrier.Actor.forward * .62f + Vector3.up * (BallRadius + .025f);
            target.x = Mathf.Clamp(target.x, -HalfWidth + .15f, HalfWidth - .15f);
            target.z = Mathf.Clamp(target.z, -HalfLength + .13f, HalfLength - .13f);
            ball.linearVelocity = Vector3.ClampMagnitude((target - ball.position) / dt, 17f);
            ball.angularVelocity = Vector3.Cross(Vector3.up, ball.linearVelocity) / BallRadius;
        }

        bool CheckGoal()
        {
            if (carrier != null) return false;
            Vector3 current = ball.position;
            foreach (int sign in new[] { -1, 1 })
            {
                float plane = sign * (HalfLength + BallRadius);
                if (sign * previousBall.z >= sign * plane || sign * current.z < sign * plane) continue;
                Vector3 crossing = Vector3.Lerp(previousBall, current, (plane - previousBall.z) / (current.z - previousBall.z));
                if (Mathf.Abs(crossing.x) >= GoalHalf - BallRadius - .07f || crossing.y >= GoalHeight - BallRadius - .07f) continue;
                int side = sign > 0 ? 0 : 1;
                if (side == 0) UserScore++; else OpponentScore++;
                if (lastTouch != null && lastTouch.Team == side)
                {
                    Goals[lastTouch.Id]++;
                    if (assister != null && assister != lastTouch && assister.Team == side) Assists[assister.Id]++;
                }
                string name = season.Team(side == 0 ? League.UserClub : Opponent);
                ResetKickoff(1 - side, "GOAL  /  " + name);
                return true;
            }
            return false;
        }

        void KeepBallInPlay()
        {
            if (carrier != null) return;
            Vector3 position = ball.position, velocity = ball.linearVelocity;
            if (position.y < -.5f)
            {
                SetBall(new Vector3(Mathf.Clamp(position.x, -12, 12), BallRadius + .02f, Mathf.Clamp(position.z, -19, 19)));
                return;
            }
            bool bounced = false;
            if (Mathf.Abs(position.x) > HalfWidth)
            {
                position.x = Mathf.Sign(position.x) * HalfWidth;
                velocity.x = -Mathf.Sign(position.x) * Mathf.Abs(velocity.x) * .7f;
                bounced = true;
            }
            bool goalMouth = Mathf.Abs(position.x) < GoalHalf - BallRadius - .07f && position.y < GoalHeight - BallRadius - .07f;
            if (Mathf.Abs(position.z) > HalfLength && !goalMouth)
            {
                position.z = Mathf.Sign(position.z) * HalfLength;
                velocity.z = -Mathf.Sign(position.z) * Mathf.Abs(velocity.z) * .7f;
                bounced = true;
            }
            if (!bounced) return;
            ball.position = position;
            ball.linearVelocity = velocity;
        }

        public void UpdateCamera(bool immediate = false)
        {
            Vector3 focus = ball.position;
            Athlete player = teams[0][selected];
            focus = Vector3.Lerp(player.Position, focus, .64f);
            focus = new Vector3(Mathf.Clamp(focus.x * .68f, -6, 6), 0, Mathf.Clamp(focus.z, -12, 14));
            Vector3 position = focus + new Vector3(0, 21.7f, -15.4f);
            camera.transform.position = immediate ? position : Vector3.Lerp(camera.transform.position, position, 1 - Mathf.Exp(-5 * Time.unscaledDeltaTime));
            camera.transform.rotation = Quaternion.LookRotation(new Vector3(0, -21.7f, 15.4f));
        }
    }
}
