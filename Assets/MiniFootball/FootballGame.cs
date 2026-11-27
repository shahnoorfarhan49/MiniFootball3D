using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MiniFootball
{
    public sealed class FootballGame : MonoBehaviour
    {
        public Camera MatchCamera;
        public GameObject CharacterPrefab, BallPrefab;
        public Material HighlightMaterial;

        enum ScreenPage { Main, Create, Hub, Match, Pause, Result, Champion }
        enum HubTab { Standings, Fixtures, Squad, Statistics }

        static readonly Color Background = new Color32(13, 19, 32, 255);
        static readonly Color PanelColor = new Color32(23, 33, 51, 255);
        static readonly Color RowColor = new Color32(30, 43, 65, 255);
        static readonly Color Muted = new Color32(171, 189, 214, 255);
        static readonly Color Accent = new Color32(124, 243, 176, 255);
        static readonly Color Alert = new Color32(255, 182, 136, 255);
        static readonly Color Text = new Color32(244, 248, 255, 255);

        ScreenPage page;
        HubTab tab;
        Season season;
        Match match;
        Keyboard Keys => Keyboard.current;
        string clubInput = "", feedback = "";
        int primary = 7, trim = 0, badge, selectedSlot = -1, fixtureRound, statsClub = League.UserClub;
        bool fictional, confirmReset, confirmExit, savedResult;
        readonly List<int> selection = new List<int>();
        int[] lineup = Array.Empty<int>();
        GUIStyle normal, small, title, heading, button, field;
        Texture2D[] badgeTextures;
        string ResultLine => $"{match.UserScore}  –  {match.OpponentScore}";

        void Awake()
        {
            Time.timeScale = 1;
            Application.targetFrameRate = 60;
            season = League.Load();
            SetBackdrop();
        }

        void OnDisable()
        {
            match?.Dispose();
            match = null;
            Time.timeScale = 1;
        }

        void Update()
        {
            if (page == ScreenPage.Match && Keys?.escapeKey.wasPressedThisFrame == true)
            {
                SetPause(true);
                return;
            }
            if (page == ScreenPage.Pause && Keys?.escapeKey.wasPressedThisFrame == true)
            {
                SetPause(false);
                return;
            }
            if (page != ScreenPage.Match) return;
            if (match.Finished)
            {
                Time.timeScale = 0;
                page = ScreenPage.Result;
                savedResult = false;
                CommitResult();
                return;
            }
            match.Input(Keys);
        }

        void FixedUpdate()
        {
            if (page == ScreenPage.Match) match.Tick(Time.fixedDeltaTime, Keys);
        }

        void LateUpdate()
        {
            if (page == ScreenPage.Match) match.UpdateCamera();
        }

        void SetBackdrop()
        {
            MatchCamera.transform.SetPositionAndRotation(new Vector3(-21, 18, -23), Quaternion.LookRotation(new Vector3(21, -18, 26)));
            MatchCamera.fieldOfView = 50;
        }

        bool Persist(Season data)
        {
            try
            {
                League.Save(data);
                season = data;
                feedback = "";
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                feedback = "Save failed. Check storage permissions and try again.";
                Debug.LogWarning(exception.Message);
                return false;
            }
        }

        void CommitResult()
        {
            var copy = JsonUtility.FromJson<Season>(JsonUtility.ToJson(season));
            League.FinishRound(copy, copy.Next, match.UserScore, match.OpponentScore, match.Appearances, match.Goals, match.Assists);
            savedResult = Persist(copy);
        }

        void OpenEditor()
        {
            clubInput = "";
            primary = 7; trim = 0; badge = 0; selectedSlot = -1; fictional = false;
            selection.Clear();
            lineup = Array.Empty<int>();
            feedback = "";
            confirmReset = false;
            page = ScreenPage.Create;
        }

        void OpenHub()
        {
            tab = HubTab.Standings;
            fixtureRound = season.Next?.Round ?? 6;
            statsClub = League.UserClub;
            lineup = (int[])season.Squad.Clone();
            selectedSlot = -1;
            confirmReset = false;
            page = ScreenPage.Hub;
        }

        void StartMatch()
        {
            if (season.Next == null) return;
            feedback = "";
            match = new Match(season, season.Next, CharacterPrefab, BallPrefab, MatchCamera, HighlightMaterial);
            MatchCamera.fieldOfView = 47;
            Time.timeScale = 1;
            page = ScreenPage.Match;
        }

        void SetPause(bool paused)
        {
            confirmExit = false;
            page = paused ? ScreenPage.Pause : ScreenPage.Match;
            Time.timeScale = paused ? 0 : 1;
        }

        void EndMatchView()
        {
            match.Dispose();
            match = null;
            Time.timeScale = 1;
            SetBackdrop();
            OpenHub();
        }

        void InitStyles()
        {
            normal = new GUIStyle(GUI.skin.label) { fontSize = 18, clipping = TextClipping.Clip };
            small = new GUIStyle(normal) { fontSize = 14 };
            heading = new GUIStyle(normal) { fontSize = 26, fontStyle = FontStyle.Bold };
            title = new GUIStyle(heading) { fontSize = 54 };
            button = new GUIStyle(normal) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 16 };
            field = new GUIStyle(GUI.skin.textField) { fontSize = 22, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(12, 12, 0, 0) };
            field.normal.textColor = Text;
            field.focused.textColor = Text;
            badgeTextures = Enumerable.Range(0, League.Badges.Length).Select(BuildBadge).ToArray();
        }

        static Texture2D BuildBadge(int pattern)
        {
            const int size = 48;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nx = (2f * x / (size - 1)) - 1, ny = (2f * y / (size - 1)) - 1;
                bool inside = pattern == 0 ? Mathf.Abs(nx) + Mathf.Abs(ny) < .91f :
                    pattern == 1 ? ny > -.91f && ny < .83f && Mathf.Abs(nx) < Mathf.Lerp(.13f, .78f, Mathf.Clamp01((ny + .92f) * 1.3f)) :
                    nx * nx + ny * ny < .78f;
                if (!inside) continue;
                bool slash = Mathf.Abs(nx - ny * .62f) < .14f || (ny > .38f && Mathf.Abs(nx) < .37f);
                pixels[y * size + x] = slash ? new Color32(86, 104, 108, 255) : new Color32(255, 255, 255, 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        void OnDestroy()
        {
            if (badgeTextures == null) return;
            foreach (Texture2D texture in badgeTextures) Destroy(texture);
        }

        static Rect R(float x, float y, float w, float h) => new Rect(x, y, w, h);
        static void Fill(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        void Label(Rect rect, string text, GUIStyle style = null, Color? color = null, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            style ??= normal;
            style.alignment = alignment;
            style.normal.textColor = color ?? Text;
            GUI.Label(rect, text, style);
        }

        bool Button(Rect rect, string label, bool highlight = false, bool enabled = true)
        {
            bool hover = rect.Contains(Event.current.mousePosition);
            Fill(rect, !enabled ? PanelColor : highlight ? (hover ? Color.Lerp(Accent, Color.white, .2f) : Accent) :
                hover ? Color.Lerp(RowColor, Muted, .19f) : RowColor);
            button.normal.textColor = !enabled ? Muted : highlight ? Background : Text;
            button.hover.textColor = button.normal.textColor;
            button.active.textColor = button.normal.textColor;
            using (new GuiEnabled(enabled)) return GUI.Button(rect, label, button);
        }

        readonly struct GuiEnabled : IDisposable
        {
            readonly bool old;
            public GuiEnabled(bool value) { old = GUI.enabled; GUI.enabled = old && value; }
            public void Dispose() => GUI.enabled = old;
        }

        void Crest(Rect rect, int pattern, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, badgeTextures[pattern], ScaleMode.ScaleToFit, true);
            GUI.color = old;
        }

        void OnGUI()
        {
            if (normal == null) InitStyles();
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            bool overlay = page == ScreenPage.Match || page == ScreenPage.Pause || page == ScreenPage.Result;
            if (!overlay) Fill(R(0, 0, Screen.width, Screen.height), Background);
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1280 * scale) / 2, (Screen.height - 720 * scale) / 2), Quaternion.identity, new Vector3(scale, scale, 1));
            try
            {
                switch (page)
                {
                    case ScreenPage.Main: MainMenu(); break;
                    case ScreenPage.Create: CreateClub(); break;
                    case ScreenPage.Hub: Hub(); break;
                    case ScreenPage.Match: Scoreboard(); break;
                    case ScreenPage.Pause: Pause(); break;
                    case ScreenPage.Result: Result(); break;
                    case ScreenPage.Champion: Champion(); break;
                }
            }
            finally { GUI.matrix = old; }
        }

        void MainMenu()
        {
            for (int i = 0; i < 8; i++) Fill(R(658 + i * 76, 55, 70, 610), i % 2 == 0 ? PanelColor : RowColor);
            Fill(R(730, 358, 470, 3), Muted);
            Fill(R(730, 120, 3, 480), Muted); Fill(R(1197, 120, 3, 480), Muted);
            Fill(R(730, 120, 470, 3), Muted); Fill(R(730, 597, 470, 3), Muted);
            Crest(R(835, 218, 258, 258), 0, Accent);
            Label(R(70, 170, 570, 76), "MiniFootball", title);
            Label(R(73, 250, 526, 46), "Five-a-side league", heading, Muted);
            if (Button(R(74, 356, 385, 56), "Continue Game", season != null, season != null)) { OpenHub(); return; }
            if (Button(R(74, 427, 385, 56), "New Season", season == null))
            {
                if (season == null) { OpenEditor(); return; }
                confirmReset = true;
            }
            if (confirmReset)
            {
                Label(R(74, 503, 530, 28), "Starting a new season replaces the saved results and stats.", small, Alert);
                if (Button(R(74, 538, 185, 44), "Keep Current Club", true))
                {
                    if (Persist(League.NewSeason(season.ClubName, season.Primary, season.Trim, season.Badge, season.Squad, season.Fictional))) OpenHub();
                    return;
                }
                if (Button(R(270, 538, 189, 44), "Create New Club")) { OpenEditor(); return; }
                if (Button(R(74, 591, 185, 40), "Cancel")) confirmReset = false;
                Feedback(270, 595, 365);
            }
        }

        void CreateClub()
        {
            Label(R(52, 36, 840, 66), "Create your club", title);
            if (Button(R(1080, 59, 145, 40), "Main Menu")) { page = ScreenPage.Main; return; }
            Fill(R(52, 128, 350, 485), PanelColor);
            Fill(R(418, 128, 365, 485), PanelColor);
            Fill(R(799, 128, 426, 485), PanelColor);

            Label(R(72, 147, 310, 30), "Club identity", heading);
            GUI.SetNextControlName("ClubName");
            clubInput = GUI.TextField(R(72, 193, 310, 45), clubInput, 24, field);
            if (string.IsNullOrWhiteSpace(clubInput) && GUI.GetNameOfFocusedControl() != "ClubName")
                Label(R(86, 193, 280, 45), "Enter club name", small, Muted);
            Crest(R(189, 250, 76, 76), badge, League.Kits[primary]);
            for (int i = 0; i < League.Badges.Length; i++)
                if (Button(R(72 + i * 104, 336, 99, 36), League.Badges[i], badge == i)) badge = i;
            Palette(386, "Primary jersey color", ref primary);
            Palette(457, "Trim and shorts color", ref trim);
            if (Button(R(72, 540, 310, 42), fictional ? "Player names: Fictional" : "Player names: Real", fictional)) fictional = !fictional;

            Label(R(438, 147, 310, 30), "Selection pool", heading);
            Label(R(438, 185, 310, 24), $"{selection.Count} of 6 selected", small, Accent);
            for (int i = 0; i < 8; i++)
            {
                int id = 42 + i;
                Footballer player = League.Players[id];
                bool chosen = selection.Contains(id);
                string name = fictional ? player.FictionalName : player.Name;
                if (!Button(R(438, 218 + i * 46, 325, 39), $"{(chosen ? "✓" : "+")}   {name}  ·  {RoleName(player.Position)}", chosen,
                    chosen || selection.Count < 6)) continue;
                if (chosen) selection.Remove(id); else selection.Add(id);
                lineup = selection.OrderBy(item => League.Players[item].Position).ToArray();
                selectedSlot = -1;
                feedback = "";
            }

            Label(R(819, 147, 380, 30), "Starting five & reserve", heading);
            Label(R(819, 185, 380, 24), "Select two rows to swap their places.", small, Muted);
            Lineup(819, 218, 385, id => fictional ? League.Players[id].FictionalName : League.Players[id].Name);
            Label(R(819, 505, 385, 24), "Start GK, DEF, MID, FWD and one extra outfielder.", small, Muted);
            Label(R(819, 540, 385, 28), "The sixth slot is the substitute.", small, Muted);

            bool valid = League.ValidSquad(lineup) && !string.IsNullOrWhiteSpace(clubInput) && primary != trim;
            if (Button(R(934, 635, 291, 50), "Start Season", true, valid))
            {
                if (Persist(League.NewSeason(clubInput.Trim(), primary, trim, badge, lineup, fictional))) OpenHub();
            }
            if (selection.Count == 6 && !League.ValidSquad(lineup) && feedback.Length == 0)
                Label(R(52, 641, 850, 32), "Adjust the starting positions or select a different squad.", small, Alert);
            else if (primary == trim && feedback.Length == 0)
                Label(R(52, 641, 850, 32), "Choose different primary and trim colors.", small, Alert);
            else Feedback(52, 641, 850);
        }

        void Palette(float y, string caption, ref int color)
        {
            Label(R(72, y, 310, 24), caption, small, Muted);
            for (int i = 0; i < League.Kits.Length; i++)
            {
                Rect swatch = R(72 + 38 * i, y + 28, 32, 26);
                Fill(swatch, League.Kits[i]);
                if (color == i) Label(swatch, "✓", small, FootballArt.Ink, TextAnchor.MiddleCenter);
                if (GUI.Button(swatch, GUIContent.none, GUIStyle.none)) color = i;
            }
        }

        static string RoleName(Role role) => role switch
        {
            Role.Keeper => "GK", Role.Defender => "DEF", Role.Midfielder => "MID", _ => "FWD"
        };

        void Lineup(float x, float y, float w, Func<int, string> playerName)
        {
            for (int i = 0; i < 6; i++)
            {
                if (i >= lineup.Length)
                {
                    Fill(R(x, y + i * 46, w, 39), RowColor);
                    Label(R(x + 12, y + i * 46, w - 24, 39), SlotName(i) + "   —", small, Muted);
                    continue;
                }
                Footballer player = League.Players[lineup[i]];
                if (!Button(R(x, y + i * 46, w, 39), $"{SlotName(i)}   #{player.Number}  {playerName(player.Id)}   {RoleName(player.Position)}", selectedSlot == i)) continue;
                if (selectedSlot < 0) selectedSlot = i;
                else
                {
                    (lineup[selectedSlot], lineup[i]) = (lineup[i], lineup[selectedSlot]);
                    selectedSlot = -1;
                }
            }
        }

        static string SlotName(int slot) => slot == 0 ? "GK" : slot == 5 ? "SUB" : $"XI {slot + 1}";

        void Hub()
        {
            Crest(R(52, 37, 64, 64), season.Badge, League.Kits[season.Primary]);
            Label(R(128, 39, 770, 61), season.ClubName, title);
            if (Button(R(1080, 52, 145, 43), "Main Menu")) { page = ScreenPage.Main; return; }
            string[] labels = { "Standings", "Fixtures", "Squad", "Player Stats" };
            for (int i = 0; i < labels.Length; i++)
            {
                if (!Button(R(52 + 160 * i, 128, 149, 44), labels[i], (int)tab == i)) continue;
                tab = (HubTab)i;
                feedback = "";
            }
            Fill(R(52, 190, 1173, 423), PanelColor);

            switch (tab)
            {
                case HubTab.Standings: Standings(); break;
                case HubTab.Fixtures: Fixtures(); break;
                case HubTab.Squad: Squad(); break;
                case HubTab.Statistics: Statistics(); break;
            }

            Fixture next = season.Next;
            if (next == null)
            {
                if (Button(R(914, 634, 311, 51), "Champion Celebration", true)) page = ScreenPage.Champion;
            }
            else
            {
                int opponent = next.Home == League.UserClub ? next.Away : next.Home;
                Label(R(56, 634, 720, 51), $"Round {next.Round + 1} of 7  /  vs {season.Team(opponent)}", heading);
                if (Button(R(914, 634, 311, 51), "Play Next Match", true)) StartMatch();
            }
        }

        void Standings()
        {
            string[] labels = { "P", "W", "D", "L", "GF", "GA", "GD", "PTS" };
            Label(R(110, 206, 420, 34), "Club", small, Muted);
            for (int i = 0; i < labels.Length; i++) Label(R(580 + 74 * i, 206, 67, 34), labels[i], small, Muted, TextAnchor.MiddleCenter);
            Standing[] table = League.Table(season);
            for (int i = 0; i < table.Length; i++)
            {
                Standing row = table[i];
                float y = 242 + i * 44;
                Fill(R(66, y, 1145, 39), row.Club == League.UserClub ? new Color32(37, 68, 73, 255) : RowColor);
                Label(R(80, y, 42, 39), (i + 1).ToString(), small, row.Club == League.UserClub ? Accent : Muted);
                Label(R(128, y, 430, 39), season.Team(row.Club));
                int[] numbers = { row.Played, row.Won, row.Drawn, row.Lost, row.For, row.Against, row.Difference, row.Points };
                for (int j = 0; j < numbers.Length; j++)
                    Label(R(580 + 74 * j, y, 67, 39), numbers[j].ToString(), normal, j == 7 ? Accent : Text, TextAnchor.MiddleCenter);
            }
        }

        void Fixtures()
        {
            for (int i = 0; i < 7; i++)
                if (Button(R(75 + i * 160, 209, 148, 39), $"Round {i + 1}", fixtureRound == i)) fixtureRound = i;
            int index = 0;
            foreach (Fixture f in season.Fixtures.Where(f => f.Round == fixtureRound))
            {
                float y = 268 + 70 * index++;
                bool own = f.Home == League.UserClub || f.Away == League.UserClub;
                Fill(R(75, y, 1126, 55), own ? new Color32(37, 68, 73, 255) : RowColor);
                Label(R(94, y, 412, 55), season.Team(f.Home), normal, Text, TextAnchor.MiddleRight);
                Label(R(517, y, 241, 55), f.Played ? $"{f.HomeGoals}  –  {f.AwayGoals}" : "vs", heading, own ? Accent : Text, TextAnchor.MiddleCenter);
                Label(R(772, y, 410, 55), season.Team(f.Away));
            }
        }

        void Squad()
        {
            Label(R(75, 207, 520, 34), "Starting five & substitute", heading);
            Lineup(75, 254, 514, season.Player);
            Label(R(620, 208, 570, 34), "Free agents", heading);
            int j = 0;
            foreach (int id in Enumerable.Range(42, 8).Except(season.Squad))
            {
                float y = 255 + j++ * 49;
                Fill(R(620, y, 570, 41), RowColor);
                Label(R(637, y, 536, 41), season.Player(id) + "   ·   " + RoleName(League.Players[id].Position), normal, Muted);
            }
            Label(R(620, 371, 570, 29), "Select two roster rows to swap their places.", small, Muted);
            Label(R(620, 406, 570, 29), "Start GK, DEF, MID, FWD and one extra outfielder.", small, Muted);
            bool dirty = !lineup.SequenceEqual(season.Squad);
            if (Button(R(620, 454, 239, 44), "Save Lineup", true, dirty && League.ValidSquad(lineup)))
            {
                var copy = JsonUtility.FromJson<Season>(JsonUtility.ToJson(season));
                copy.Squad = (int[])lineup.Clone();
                if (Persist(copy)) feedback = "Lineup saved.";
            }
            if (Button(R(873, 454, 237, 44), "Reset changes", false, dirty))
            {
                lineup = (int[])season.Squad.Clone();
                selectedSlot = -1;
                feedback = "";
            }
            Label(R(620, 523, 570, 28), feedback, small, feedback == "Lineup saved." ? Accent : Alert);
        }

        void Statistics()
        {
            if (Button(R(75, 207, 65, 40), "<")) statsClub = (statsClub + 8) % 9;
            if (Button(R(482, 207, 65, 40), ">")) statsClub = (statsClub + 1) % 9;
            Label(R(153, 207, 314, 40), statsClub == 8 ? "Free agents" : season.Team(statsClub), heading, Text, TextAnchor.MiddleCenter);
            string[] columns = { "POS", "SPD", "SHO", "PAS", "DEF", "STA", "GK", "APP", "G", "A" };
            float start = 430, step = 76;
            for (int i = 0; i < columns.Length; i++) Label(R(start + step * i, 254, step, 32), columns[i], small, Muted, TextAnchor.MiddleCenter);
            int[] players = statsClub == 8 ? Enumerable.Range(42, 8).Except(season.Squad).ToArray() : League.Squad(season, statsClub);
            for (int i = 0; i < players.Length; i++)
            {
                Footballer p = League.Players[players[i]];
                float y = 289 + 49 * i;
                Fill(R(75, y, 1126, 42), RowColor);
                Label(R(90, y, 325, 42), $"#{p.Number}   {season.Player(p.Id)}");
                string[] values = { RoleName(p.Position), p.Speed.ToString(), p.Shooting.ToString(), p.Passing.ToString(), p.Defending.ToString(),
                    p.Stamina.ToString(), p.Goalkeeping.ToString(), season.Appearances[p.Id].ToString(), season.Goals[p.Id].ToString(), season.Assists[p.Id].ToString() };
                for (int n = 0; n < values.Length; n++) Label(R(start + n * step, y, step, 42), values[n], small, n >= 7 ? Accent : Text, TextAnchor.MiddleCenter);
            }
        }

        void Scoreboard()
        {
            Fill(R(266, 18, 748, 59), PanelColor);
            Fill(R(267, 18, 6, 59), League.Kits[season.Primary]);
            Crest(R(283, 28, 36, 36), season.Badge, League.Kits[season.Primary]);
            Fill(R(1007, 18, 6, 59), League.Kits[match.Opponent]);
            Label(R(326, 18, 211, 59), season.ClubName, normal, Text, TextAnchor.MiddleRight);
            Label(R(549, 18, 182, 59), ResultLine, heading, Accent, TextAnchor.MiddleCenter);
            Label(R(742, 18, 255, 59), season.Team(match.Opponent));
            Fill(R(593, 78, 94, 34), Background);
            Label(R(593, 78, 94, 34), match.Clock, normal, Text, TextAnchor.MiddleCenter);
            if (!string.IsNullOrWhiteSpace(match.Notice))
            {
                Fill(R(376, 140, 528, 54), PanelColor);
                Label(R(386, 140, 508, 54), match.Notice, heading, Accent, TextAnchor.MiddleCenter);
            }
            Fill(R(34, 625, 304, 65), PanelColor);
            Label(R(49, 632, 272, 28), season.Player(match.SelectedId));
            Fill(R(49, 669, 272, 6), RowColor);
            Fill(R(49, 669, 272 * match.Energy, 6), Accent);
            Fill(R(361, 644, 884, 46), PanelColor);
            Label(R(372, 644, 862, 46), "WASD Move     Shift Sprint     J Pass     K Shoot     L Tackle     Q Switch     Esc Pause / Sub", small, Text, TextAnchor.MiddleCenter);
        }

        void Pause()
        {
            Fill(R(0, 0, 1280, 720), new Color(0, 0, 0, .68f));
            Fill(R(342, 88, 596, 546), PanelColor);
            Label(R(380, 113, 520, 66), "Match paused", title);
            if (Button(R(380, 197, 520, 46), "Resume", true)) { SetPause(false); return; }
            Label(R(380, 264, 520, 33), match.SubUsed ? "Substitution used" : "Bring on " + season.Player(match.ReserveId), heading);
            if (!match.SubUsed)
            {
                for (int slot = 1; slot < 5; slot++)
                {
                    int current = match.StarterId(slot);
                    if (Button(R(380, 312 + 46 * (slot - 1), 520, 39), "Replace " + season.Player(current), false, match.CanSub(slot)))
                        match.Substitute(slot);
                }
            }
            if (confirmExit)
            {
                Label(R(380, 507, 520, 30), "The current match result will be discarded.", small, Alert);
                if (Button(R(380, 552, 250, 43), "Exit to League")) { EndMatchView(); return; }
                if (Button(R(650, 552, 250, 43), "Cancel")) confirmExit = false;
            }
            else if (Button(R(380, 552, 520, 43), "Leave Match")) confirmExit = true;
        }

        void Result()
        {
            Fill(R(0, 0, 1280, 720), new Color(0, 0, 0, .72f));
            Fill(R(270, 154, 740, 410), PanelColor);
            Label(R(290, 186, 700, 62), "Full time", title, Text, TextAnchor.MiddleCenter);
            Label(R(290, 277, 700, 59), ResultLine, title, Accent, TextAnchor.MiddleCenter);
            Label(R(290, 351, 700, 39), $"{season.ClubName}  ·  {season.Team(match.Opponent)}", normal, Text, TextAnchor.MiddleCenter);
            if (savedResult)
            {
                if (Button(R(468, 463, 344, 47), "Return to League", true)) EndMatchView();
            }
            else
            {
                Feedback(324, 412, 660);
                if (Button(R(468, 463, 344, 47), "Retry Save", true)) CommitResult();
            }
        }

        void Champion()
        {
            Standing winner = League.Table(season)[0];
            for (int i = 0; i < 84; i++)
            {
                float x = (i * 113) % 1280;
                float y = Mathf.Repeat((i * 193) % 720 + Time.unscaledTime * (27 + i % 6 * 10), 720);
                Fill(R(x, y, 4 + i % 6, 7 + i % 3), League.Kits[i % League.Kits.Length]);
            }
            Fill(R(288, 96, 704, 528), PanelColor);
            int pattern = winner.Club == League.UserClub ? season.Badge : winner.Club % 3;
            Color color = League.Kits[winner.Club == League.UserClub ? season.Primary : winner.Club];
            Crest(R(552, 124, 176, 176), pattern, color);
            Label(R(312, 311, 656, 45), "League Champion", heading, Accent, TextAnchor.MiddleCenter);
            Label(R(312, 367, 656, 68), season.Team(winner.Club), title, Text, TextAnchor.MiddleCenter);
            Label(R(312, 449, 656, 31), $"{winner.Points} points  ·  {winner.Won} wins  ·  {winner.For} goals", normal, Muted, TextAnchor.MiddleCenter);
            if (Button(R(459, 537, 362, 45), "Back to Standings", true)) OpenHub();
        }

        void Feedback(float x, float y, float width)
        {
            if (!string.IsNullOrEmpty(feedback)) Label(R(x, y, width, 30), feedback, small, Alert);
        }
    }
}
