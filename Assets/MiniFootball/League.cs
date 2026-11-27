using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MiniFootball
{
    public enum Role { Keeper, Defender, Midfielder, Forward }

    public sealed class Footballer
    {
        public readonly int Id, Number, Speed, Shooting, Passing, Defending, Stamina, Goalkeeping;
        public readonly string Name, FictionalName;
        public readonly Role Position;

        public Footballer(int id, string name, string fictionalName, Role role, int number, int pace, int shot, int pass, int defense, int stamina, int keeper)
        {
            Id = id; Name = name; FictionalName = fictionalName; Position = role; Number = number;
            Speed = pace; Shooting = shot; Passing = pass; Defending = defense; Stamina = stamina; Goalkeeping = keeper;
        }
    }

    public static class League
    {
        public const int UserClub = 7;
        public static readonly string[] Clubs = { "Real Madrid", "Barcelona", "Atlético Madrid", "Athletic Club", "Real Sociedad", "Villarreal", "Real Betis" };
        public static readonly string[] FictionalClubs = { "Sierra Royals", "Harbor Azul", "Capital Stripes", "Northern Lions", "Bay Union", "Golden Coast", "Olive Athletic" };
        public static readonly Color[] Kits = {
            new Color32(235,237,249,255), new Color32(34,84,219,255), new Color32(201,54,76,255),
            new Color32(226,106,47,255), new Color32(72,165,233,255), new Color32(239,210,68,255),
            new Color32(50,170,105,255), new Color32(173,95,245,255)
        };
        public static readonly string[] Badges = { "Diamond", "Shield", "Circle" };

        // Slot order is goalkeeper, defender, midfielder, forward, flex starter, reserve.
        static readonly string[][] Names = {
            new[] { "Thibaut Courtois", "Antonio Rüdiger", "Federico Valverde", "Kylian Mbappé", "Jude Bellingham", "Vinícius Júnior" },
            new[] { "Joan García", "Pau Cubarsí", "Pedri", "Lamine Yamal", "Gavi", "Raphinha" },
            new[] { "Jan Oblak", "Robin Le Normand", "Pablo Barrios", "Julián Álvarez", "Marcos Llorente", "Alexander Sørloth" },
            new[] { "Unai Simón", "Dani Vivian", "Oihan Sancet", "Nico Williams", "Aymeric Laporte", "Iñaki Williams" },
            new[] { "Álex Remiro", "Igor Zubeldia", "Carlos Soler", "Takefusa Kubo", "Beñat Turrientes", "Mikel Oyarzabal" },
            new[] { "Luiz Júnior", "Juan Foyth", "Pape Gueye", "Gerard Moreno", "Sergi Cardona", "Ayoze Pérez" },
            new[] { "Álvaro Valles", "Marc Bartra", "Isco", "Antony", "Pablo Fornals", "Cucho Hernández" },
            new[] { "Andriy Lunin", "Jules Koundé", "Alejandro Balde", "Arda Güler", "Dani Olmo", "Rodrygo", "Endrick", "Álex Baena" }
        };
        static readonly string[] First = { "Adrián", "Mateo", "Leo", "Bruno", "Nico", "Hugo", "Iker", "Joel" };
        static readonly string[] Last = { "Vega", "Ríos", "Solano", "Bosque", "Costa", "Prado", "Lago" };
        public static readonly Footballer[] Players = BuildDatabase();

        static Footballer[] BuildDatabase()
        {
            var players = new List<Footballer>(50);
            var regular = new[] { Role.Keeper, Role.Defender, Role.Midfielder, Role.Forward, Role.Midfielder, Role.Forward };
            var pool = new[] { Role.Keeper, Role.Defender, Role.Defender, Role.Midfielder, Role.Midfielder, Role.Forward, Role.Forward, Role.Midfielder };
            for (int club = 0; club < Names.Length; club++)
            for (int slot = 0; slot < Names[club].Length; slot++)
            {
                int id = players.Count;
                Role role = club == UserClub ? pool[slot] : regular[slot];
                if (slot == 4 && (club == 2 || club == 3 || club == 5)) role = Role.Defender;
                int Variation(int offset) => ((id * (offset * 2 + 3) + offset * 7) % 15) - 7;
                players.Add(new Footballer(id, Names[club][slot], First[id % 8] + " " + Last[id / 8], role,
                    new[] { 1, 4, 8, 9, 6, 11, 19, 21 }[slot],
                    (role == Role.Forward ? 80 : 70) + Variation(1),
                    (role == Role.Forward ? 81 : role == Role.Keeper ? 40 : 65) + Variation(2),
                    (role == Role.Midfielder ? 82 : 68) + Variation(3),
                    (role == Role.Defender ? 83 : 58) + Variation(4),
                    78 + Variation(5), (role == Role.Keeper ? 84 : 24) + Variation(6)));
            }
            return players.ToArray();
        }

        public static int[] Squad(Season season, int club) => club == UserClub ? (int[])season.Squad.Clone() : Enumerable.Range(club * 6, 6).ToArray();
        public static bool ValidSquad(IReadOnlyList<int> ids)
        {
            if (ids == null || ids.Count != 6 || ids.Distinct().Count() != 6 || ids.Any(id => id < 42 || id >= Players.Length)) return false;
            if (Players[ids[0]].Position != Role.Keeper) return false;
            var outfield = ids.Skip(1).Take(4).Select(id => Players[id].Position).ToArray();
            return outfield.Contains(Role.Defender) && outfield.Contains(Role.Midfielder) && outfield.Contains(Role.Forward);
        }

        public static Season NewSeason(string name, int primary, int trim, int badge, int[] squad, bool fictional)
        {
            var season = new Season { ClubName = name, Primary = primary, Trim = trim, Badge = badge, Squad = (int[])squad.Clone(), Fictional = fictional };
            int[] rotation = Enumerable.Range(0, 8).ToArray();
            for (int round = 0; round < 7; round++)
            {
                for (int pairing = 0; pairing < 4; pairing++)
                {
                    int a = rotation[pairing], b = rotation[7 - pairing];
                    if ((round + pairing) % 2 == 1) (a, b) = (b, a);
                    season.Fixtures.Add(new Fixture { Round = round, Home = a, Away = b });
                }
                int last = rotation[7];
                Array.Copy(rotation, 1, rotation, 2, 6);
                rotation[1] = last;
            }
            return season;
        }

        public static Standing[] Table(Season season)
        {
            var rows = Enumerable.Range(0, 8).Select(id => new Standing { Club = id }).ToArray();
            foreach (var f in season.Fixtures.Where(f => f.Played))
            {
                Update(rows[f.Home], f.HomeGoals, f.AwayGoals);
                Update(rows[f.Away], f.AwayGoals, f.HomeGoals);
            }
            return rows.OrderByDescending(s => s.Points).ThenByDescending(s => s.Difference)
                .ThenByDescending(s => s.For).ThenBy(s => s.Club).ToArray();
        }

        static void Update(Standing row, int goals, int against)
        {
            row.Played++; row.For += goals; row.Against += against;
            if (goals > against) row.Won++;
            else if (goals == against) row.Drawn++;
            else row.Lost++;
        }

        public static void FinishRound(Season season, Fixture userFixture, int userGoals, int opponentGoals, int[] appearances, int[] goals, int[] assists)
        {
            if (userFixture.Played) return;
            userFixture.HomeGoals = userFixture.Home == UserClub ? userGoals : opponentGoals;
            userFixture.AwayGoals = userFixture.Home == UserClub ? opponentGoals : userGoals;
            userFixture.Played = true;
            for (int id = 0; id < Players.Length; id++)
            {
                season.Appearances[id] += appearances[id];
                season.Goals[id] += goals[id];
                season.Assists[id] += assists[id];
            }
            foreach (Fixture fixture in season.Fixtures.Where(f => f.Round == userFixture.Round && !f.Played))
            {
                fixture.HomeGoals = SimulateTeam(season, fixture.Home);
                fixture.AwayGoals = SimulateTeam(season, fixture.Away);
                fixture.Played = true;
            }
        }

        static int SimulateTeam(Season season, int club)
        {
            int[] starters = Squad(season, club).Take(5).ToArray();
            foreach (int id in starters) season.Appearances[id]++;
            int goals = 0;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                int scorer = starters[UnityEngine.Random.Range(1, 5)];
                if (UnityEngine.Random.value > Players[scorer].Shooting / 240f) continue;
                goals++;
                season.Goals[scorer]++;
                int assist = starters[UnityEngine.Random.Range(1, 5)];
                if (assist != scorer) season.Assists[assist]++;
            }
            return goals;
        }

        static string SavePath => Path.Combine(Application.persistentDataPath, "mini-football-season.json");

        public static void Save(Season season)
        {
            string temp = SavePath + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(season, true));
            if (File.Exists(SavePath)) File.Replace(temp, SavePath, null);
            else File.Move(temp, SavePath);
        }

        public static Season Load()
        {
            try
            {
                if (!File.Exists(SavePath)) return null;
                var season = JsonUtility.FromJson<Season>(File.ReadAllText(SavePath));
                if (season == null || season.Version != 1 || !ValidSquad(season.Squad) ||
                    season.Fixtures?.Count != 28 || season.Appearances?.Length != 50 ||
                    season.Goals?.Length != 50 || season.Assists?.Length != 50 ||
                    season.Primary < 0 || season.Primary >= Kits.Length ||
                    season.Trim < 0 || season.Trim >= Kits.Length || season.Badge < 0 || season.Badge >= Badges.Length ||
                    string.IsNullOrWhiteSpace(season.ClubName)) return null;
                var expected = NewSeason(season.ClubName, season.Primary, season.Trim, season.Badge, season.Squad, season.Fictional);
                for (int i = 0; i < season.Fixtures.Count; i++)
                {
                    Fixture f = season.Fixtures[i], e = expected.Fixtures[i];
                    if (f == null || f.Home != e.Home || f.Away != e.Away || f.Round != e.Round ||
                        f.HomeGoals < 0 || f.AwayGoals < 0) return null;
                }
                for (int round = 0; round < 7; round++)
                {
                    int played = season.Fixtures.Count(f => f.Round == round && f.Played);
                    if (played != 0 && played != 4) return null;
                    if (played == 4 && round > 0 && season.Fixtures.Any(f => f.Round == round - 1 && !f.Played)) return null;
                }
                return season;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                Debug.LogWarning("Could not load season: " + exception.Message);
                return null;
            }
        }
    }

    [Serializable]
    public sealed class Season
    {
        public int Version = 1;
        public string ClubName;
        public int Primary, Trim, Badge;
        public bool Fictional;
        public int[] Squad, Appearances = new int[50], Goals = new int[50], Assists = new int[50];
        public List<Fixture> Fixtures = new List<Fixture>();
        public string Team(int club) => club == League.UserClub ? ClubName : (Fictional ? League.FictionalClubs : League.Clubs)[club];
        public string Player(int id) => Fictional ? League.Players[id].FictionalName : League.Players[id].Name;
        public Fixture Next => Fixtures.FirstOrDefault(f => !f.Played && (f.Home == League.UserClub || f.Away == League.UserClub));
    }

    [Serializable]
    public sealed class Fixture
    {
        public int Round, Home, Away, HomeGoals, AwayGoals;
        public bool Played;
    }

    public sealed class Standing
    {
        public int Club, Played, Won, Drawn, Lost, For, Against;
        public int Difference => For - Against;
        public int Points => 3 * Won + Drawn;
    }
}
