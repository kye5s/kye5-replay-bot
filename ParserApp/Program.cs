using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FortniteReplayReader;
using FortniteReplayReader.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ParserApp
{
    class Program
    {
        // ---------------- DEBUG FILE OUTPUT ---------------------
        private static void WriteDebug(string text)
        {
            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string path = Path.Combine(exeDir, "debug_dump.txt");
                File.AppendAllText(path, text + Environment.NewLine);
            }
            catch { }
        }
        // ---------------------------------------------------------

        // Weapon name mapping from DeathTags
        private static string IdentifyWeapon(IEnumerable<string> tags)
        {
            if (tags == null) return "Unknown";
            var t = tags.ToList();

            if (t.Any(x => x.Contains("weapon.ranged.sniper.heavy", StringComparison.OrdinalIgnoreCase)))
                return "Heavy Sniper";

            if (t.Any(x => x.Contains("weapon.ranged.sniper.bolt", StringComparison.OrdinalIgnoreCase)))
                return "Bolt-Action Sniper";

            if (t.Any(x => x.Contains("weapon.ranged.sniper.hunting", StringComparison.OrdinalIgnoreCase)))
                return "Hunting Rifle";

            if (t.Any(x => x.Contains("Weapon.Ranged.Shotgun.Pump", StringComparison.OrdinalIgnoreCase)))
                return "Pump Shotgun";

            if (t.Any(x => x.Contains("Item.Weapon.Ranged.SMG.Suppressed", StringComparison.OrdinalIgnoreCase)))
                return "Suppressed SMG";

            if (t.Any(x => x.Contains("Weapon.Ranged.SMG", StringComparison.OrdinalIgnoreCase)))
                return "SMG";

            if (t.Any(x => x.Contains("weapon.ranged.assault.standard", StringComparison.OrdinalIgnoreCase)))
                return "Assault Rifle";

            return "Unknown";
        }

        // Rarity extraction
        private static string IdentifyRarity(IEnumerable<string> tags)
        {
            if (tags == null) return "Unknown";

            foreach (var t in tags)
            {
                if (t.StartsWith("Rarity.", StringComparison.OrdinalIgnoreCase))
                {
                    string raw = t.Substring("Rarity.".Length);
                    return raw switch
                    {
                        "Common" => "Common",
                        "Uncommon" => "Uncommon",
                        "Rare" => "Rare",
                        "VeryRare" => "Epic",
                        "SuperRare" => "Legendary",
                        "UltraRare" => "Legendary",
                        _ => raw
                    };
                }
            }

            return "Unknown";
        }

        private static string MapPlatform(string platform)
        {
            return platform switch
            {
                "WIN" => "PC",
                "XBL" => "Xbox One",
                "XSX" => "Xbox Series X/S",
                "PSN" => "PlayStation",
                "SWT" => "Nintendo Switch",
                "MAC" => "Mac",
                "IOS" => "iOS",
                "AND" => "Android",
                _ => platform
            };
        }

        private static PlayerData FindPlayer(string id, IEnumerable<PlayerData> players)
        {
            if (id == null) return null;
            return players?.FirstOrDefault(p => p.PlayerId == id);
        }

        private static Dictionary<string, string> BuildNameMap(IEnumerable<PlayerData> players)
        {
            var map = new Dictionary<string, string>();

            foreach (var p in players)
            {
                if (p == null)
                    continue;

                string id = p.PlayerId ?? "";
                if (id == "")
                    continue;

                string display =
                    p.PlayerNameCustomOverride ??
                    p.PlayerName ??
                    p.StreamerModeName ??
                    id;

                map[id] = display;
            }

            return map;
        }

        // -------------------------------------------------------------
        // FUNCTION THAT PARSES REPLAY (your original CLI logic)
        // -------------------------------------------------------------
        public static string ParseReplayFile(string replayPath)
        {
            WriteDebug("=== WEB PARSE RUN ===");
            WriteDebug("Replay path: " + replayPath);

            if (!File.Exists(replayPath))
            {
                WriteDebug("Replay file does NOT exist!");
                return "{}";
            }

            try
            {
                var reader = new ReplayReader();
                FortniteReplay replay = reader.ReadReplay(replayPath);
                WriteDebug("Replay loaded successfully.");

                var nameMap = BuildNameMap(replay.PlayerData ?? Enumerable.Empty<PlayerData>());
                var killfeed = replay.KillFeed?.ToList() ?? new List<KillFeedEntry>();

                if (killfeed.Count == 0)
                {
                    WriteDebug("No killfeed.");
                    return "{}";
                }

                var merged = new List<(KillFeedEntry kf, double meters, string weapon, string rarity)>();

                for (int i = 0; i < killfeed.Count; i++)
                {
                    var kf = killfeed[i];
                    if (kf == null) continue;

                    string killerId = kf.FinisherOrDownerName ?? "";
                    string victimId = kf.PlayerName ?? "";
                    if (killerId == "" || victimId == "") continue;

                    var tags = kf.DeathTags ?? new List<string>();

                    string weapon = IdentifyWeapon(tags);
                    string rarity = IdentifyRarity(tags);

                    double distance = Math.Round((kf.Distance ?? 0) / 100.0, 2);

                    merged.Add((kf, distance, weapon, rarity));
                }

                if (merged.Count == 0) return "{}";

                var furthest = merged.OrderByDescending(x => x.meters).First();

                bool IsRealKill((KillFeedEntry kf, double meters, string weapon, string rarity) x)
                {
                    string killer = x.kf.FinisherOrDownerName ?? "";
                    string victim = x.kf.PlayerName ?? "";
                    if (killer == "" || victim == "") return false;
                    if (killer == victim) return false;
                    if (x.meters < 0.1) return false;
                    return true;
                }

                var final = merged.LastOrDefault(IsRealKill);
                if ((object)final.kf == null)
                    final = merged.Last();

                string GetName(string id) =>
                    nameMap.TryGetValue(id, out var n) ? n : id;

                PlayerData fk = FindPlayer(furthest.kf.FinisherOrDownerName, replay.PlayerData);
                PlayerData fv = FindPlayer(furthest.kf.PlayerName, replay.PlayerData);
                PlayerData lk = FindPlayer(final.kf.FinisherOrDownerName, replay.PlayerData);
                PlayerData lv = FindPlayer(final.kf.PlayerName, replay.PlayerData);

                JObject output = new JObject
                {
                    ["furthest"] = new JObject
                    {
                        ["distance"] = furthest.meters,
                        ["killer"] = GetName(furthest.kf.FinisherOrDownerName),
                        ["killer_platform"] = MapPlatform(fk?.Platform),
                        ["victim"] = GetName(furthest.kf.PlayerName),
                        ["victim_platform"] = MapPlatform(fv?.Platform),
                        ["weapon"] = furthest.weapon,
                        ["rarity"] = furthest.rarity
                    },
                    ["final"] = new JObject
                    {
                        ["distance"] = final.meters,
                        ["killer"] = GetName(final.kf.FinisherOrDownerName),
                        ["killer_platform"] = MapPlatform(lk?.Platform),
                        ["victim"] = GetName(final.kf.PlayerName),
                        ["victim_platform"] = MapPlatform(lv?.Platform),
                        ["weapon"] = final.weapon,
                        ["rarity"] = final.rarity
                    }
                };

                return output.ToString(Formatting.Indented);
            }
            catch (Exception ex)
            {
                WriteDebug("EXCEPTION: " + ex);
                return "{}";
            }
        }

        // -------------------------------------------------------------
        // MAIN ENTRY — ADDED SERVER MODE HERE
        // -------------------------------------------------------------
        static void Main(string[] args)
        {
            // If running as server: dotnet ParserApp.dll --server
            if (args.Length > 0 && args[0] == "--server")
            {
                StartWebServer();
                return;
            }

            // ORIGINAL MODE (DO NOT TOUCH)
            if (args.Length == 0)
            {
                Console.WriteLine("{}");
                return;
            }

            string result = ParseReplayFile(args[0]);
            Console.WriteLine(result);
        }

        // -------------------------------------------------------------
        // WEB SERVER
        // -------------------------------------------------------------
        public static void StartWebServer()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddRouting();

            var app = builder.Build();

            // fixed: use ReadFormAsync() and find the uploaded file reliably
            app.MapPost("/parse-replay", async (HttpRequest req) =>
            {
                try
                {
                    var form = await req.ReadFormAsync();
                    // try common names or fallback to any .replay file
                    var file = form.Files.FirstOrDefault(f =>
                        string.Equals(f.Name, "replay_file", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(f.Name, "file", StringComparison.OrdinalIgnoreCase)
                        || (f.FileName != null && f.FileName.EndsWith(".replay", StringComparison.OrdinalIgnoreCase))
                    );

                    if (file == null)
                    {
                        return Results.BadRequest("No replay_file uploaded.");
                    }

                    string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".replay");

                    using (var stream = File.Create(tempPath))
                        await file.CopyToAsync(stream);

                    string json = ParseReplayFile(tempPath);

                    // optional: delete temp file after parsing
                    try { File.Delete(tempPath); } catch { }

                    return Results.Content(json, "application/json");
                }
                catch (Exception ex)
                {
                    WriteDebug("Web handler error: " + ex);
                    return Results.StatusCode(500);
                }
            });

            app.Run("http://0.0.0.0:8080");
        }
    }
}
