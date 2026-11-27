using System.Text.Json;

namespace UnityEngine
{
    public readonly struct Color { }

    internal readonly struct Color32
    {
        public Color32(byte red, byte green, byte blue, byte alpha) { }
        public static implicit operator Color(Color32 value) => new Color();
    }

    internal static class Random
    {
        static readonly System.Random Generator = new System.Random(2026);
        public static int Range(int min, int max) => Generator.Next(min, max);
        public static float value => Generator.NextSingle();
    }

    internal static class Application
    {
        public static string persistentDataPath;
    }

    internal static class Debug
    {
        public static void LogWarning(string text) => Console.Error.WriteLine(text);
    }

    internal static class JsonUtility
    {
        public static T FromJson<T>(string json)
        {
            try { return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { IncludeFields = true }); }
            catch (JsonException error) { throw new ArgumentException("Invalid JSON", error); }
        }

        public static string ToJson<T>(T data, bool pretty = false)
            => JsonSerializer.Serialize(data, new JsonSerializerOptions { IncludeFields = true, WriteIndented = pretty, IgnoreReadOnlyProperties = true });
    }
}
