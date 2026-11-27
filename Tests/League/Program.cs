using MiniFootball;
using UnityEngine;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

int[] lineup = { 42, 43, 45, 47, 46, 48 };
Require(League.Clubs.Length == 7 && League.UserClub == 7, "Expected seven AI teams and one user club.");
Require(League.Players.Length == 50 && League.Players.Select(p => p.Id).SequenceEqual(Enumerable.Range(0, 50)), "Expected 50 stable IDs.");
Require(League.Players.Select(p => p.Name).Distinct().Count() == 50, "Real names must be unique.");
Require(League.Players.Select(p => p.FictionalName).Distinct().Count() == 50, "Fictional names must be unique.");
Require(League.Players.All(p => new[] { p.Speed, p.Shooting, p.Passing, p.Defending, p.Stamina, p.Goalkeeping }.All(n => n is >= 1 and <= 99)), "Ratings must be in range.");
Require(League.ValidSquad(lineup), "Valid lineup rejected.");
Require(!League.ValidSquad(new[] { 43, 42, 45, 47, 46, 48 }), "A keeper must start in goal.");
Require(!League.ValidSquad(new[] { 42, 43, 45, 46, 49, 48 }), "A forward must start.");
Require(!League.ValidSquad(new[] { 42, 43, 45, 47, 46, 46 }), "Duplicate draft picks must be rejected.");
Require(!League.ValidSquad(new[] { 42, 43, 45, 47, 46, 0 }), "AI roster picks must be rejected.");

var season = League.NewSeason("Riverside FC", 7, 0, 1, lineup, true);
int[][] squads = Enumerable.Range(0, 8).Select(club => League.Squad(season, club)).ToArray();
Require(squads.All(s => s.Length == 6 && s.Distinct().Count() == 6), "Each team needs six unique players.");
Require(squads.SelectMany(s => s).Distinct().Count() == 48, "A footballer cannot be registered to two clubs.");
Require(Enumerable.Range(42, 8).Except(season.Squad).Count() == 2, "Two players must remain free agents.");
Require(season.Fixtures.Count == 28 && season.Fixtures.Select(f => (Math.Min(f.Home, f.Away), Math.Max(f.Home, f.Away))).Distinct().Count() == 28, "Each club pairing must occur once.");

for (int round = 0; round < 7; round++)
{
    Require(season.Fixtures.Where(f => f.Round == round).SelectMany(f => new[] { f.Home, f.Away }).Order().SequenceEqual(Enumerable.Range(0, 8)), "Each club must play once per round.");
    Fixture game = season.Next;
    Require(game.Round == round, "User fixtures must advance in order.");
    var appearances = new int[50];
    var goals = new int[50];
    var assists = new int[50];
    appearances[47] = goals[47] = assists[45] = 1;
    int userGoals = round == 6 ? 0 : 1;
    int aiGoals = round == 5 ? 1 : round == 6 ? 2 : 0;
    League.FinishRound(season, game, userGoals, aiGoals, appearances, goals, assists);
    Require((game.Home == League.UserClub ? game.HomeGoals : game.AwayGoals) == userGoals, "User score mapped to incorrect side.");
    League.FinishRound(season, game, 99, 99, appearances, goals, assists);
    Require(season.Fixtures.Count(f => f.Played) == (round + 1) * 4, "AI fixtures should complete with each user fixture.");
    Require(season.Appearances[47] == round + 1 && season.Goals[47] == round + 1, "Completing a match twice duplicated player stats.");
}

Standing[] table = League.Table(season);
Standing user = table.Single(row => row.Club == League.UserClub);
Require(season.Next == null && table.All(row => row.Played == 7), "Season must complete after seven user matches.");
Require(user.Points == 16 && user.Difference == 3 && user.For == 6, "Incorrect aggregate user standing.");
Require(table.Sum(r => r.For) == table.Sum(r => r.Against), "League goals for and against must balance.");

var tiebreak = League.NewSeason("Riverside FC", 7, 0, 1, lineup, true);
tiebreak.Fixtures = new List<Fixture>
{
    new Fixture { Home = 0, Away = 4, HomeGoals = 1, AwayGoals = 0, Played = true },
    new Fixture { Home = 1, Away = 5, HomeGoals = 3, AwayGoals = 2, Played = true },
    new Fixture { Home = 2, Away = 6, HomeGoals = 3, AwayGoals = 0, Played = true }
};
Require(League.Table(tiebreak).Take(3).Select(row => row.Club).SequenceEqual(new[] { 2, 1, 0 }), "Standings must break ties by goal difference and then goals scored.");

string saveFolder = Directory.CreateTempSubdirectory("mini-football-checks-").FullName;
Application.persistentDataPath = saveFolder;
try
{
    Require(League.Load() == null, "Expected no save in an empty folder.");
    League.Save(season);
    Season loaded = League.Load();
    Require(loaded != null && loaded.ClubName == season.ClubName && loaded.Fictional && loaded.Next == null, "Completed season failed to reload.");
    Require(loaded.Squad.SequenceEqual(lineup) && loaded.Goals.SequenceEqual(season.Goals), "Roster or stats failed to persist.");
    Require(League.Table(loaded).Select(row => row.Points).SequenceEqual(table.Select(row => row.Points)), "Standings changed after reload.");
    League.Save(League.NewSeason(season.ClubName, 7, 0, 0, lineup, false));
    Require(League.Load()?.Next?.Round == 0, "New season did not replace the previous save.");
    File.WriteAllText(Path.Combine(saveFolder, "mini-football-season.json"), "malformed");
    Require(League.Load() == null, "Malformed save should be rejected.");
}
finally
{
    Directory.Delete(saveFolder, true);
}

Console.WriteLine("League checks passed: database, squad validation, schedule, scoring, tie-breakers, statistics and save round trip.");
