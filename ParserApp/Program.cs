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
                if (p?.PlayerId == null) continue;

                string display =
                    p.PlayerNameCustomOverride ??
                    p.PlayerName ??
                    p.StreamerModeName ??
                    p.PlayerId;

                map[p.PlayerId] = display;
            }

            return map;
        }

        // -------------------------------------------------------------
        // PARSER
        // -------------------------------------------------------------
        public static string ParseReplayFile(string replayPath)
        {
            if (!File.Exists(replayPath))
                return "{}";

            try
            {
                var reader = new ReplayReader();
                FortniteReplay replay = reader.ReadReplay(replayPath);

                var nameMap = BuildNameMap(replay.PlayerData ?? Enumerable.Empty<PlayerData>());
                var killfeed = replay.KillFeed?.ToList() ?? new List<KillFeedEntry>();

                var validKills = new List<(KillFeedEntry kf, double meters, string weapon, string rarity)>();

                foreach (var kf in killfeed)
                {
                    if (kf == null) continue;

                    string killerId = kf.FinisherOrDownerName;
                    string victimId = kf.PlayerName;

                    if (string.IsNullOrEmpty(killerId) || string.IsNullOrEmpty(victimId))
                        continue;

                    if (killerId == victimId)
                        continue; // self elim

                    var killer = FindPlayer(killerId, replay.PlayerData);
                    var victim = FindPlayer(victimId, replay.PlayerData);

                    if (killer == null || victim == null)
                        continue;

                    if (killer.TeamIndex == victim.TeamIndex)
                        continue; // team kill

                    if (kf.Distance == null || kf.Distance <= 0)
                        continue;

                    double meters = Math.Round(kf.Distance.Value / 100.0, 2);

                    if (meters < 0.1)
                        continue;

                    var tags = kf.DeathTags ?? new List<string>();

                    validKills.Add((
                        kf,
                        meters,
                        IdentifyWeapon(tags),
                        IdentifyRarity(tags)
                    ));
                }

                if (validKills.Count == 0)
                    return "{}";

                var furthest = validKills.OrderByDescending(x => x.meters).First();
                var final = validKills.Last();

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
            catch
            {
                return "{}";
            }
        }

        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--server")
            {
                StartWebServer();
                return;
            }

            if (args.Length == 0)
            {
                Console.WriteLine("{}");
                return;
            }

            Console.WriteLine(ParseReplayFile(args[0]));
        }

        public static void StartWebServer()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddRouting();
            var app = builder.Build();

            app.MapPost("/parse-replay", async (HttpRequest req) =>
            {
                var form = await req.ReadFormAsync();
                var file = form.Files.FirstOrDefault(f => f.FileName.EndsWith(".replay"));

                if (file == null)
                    return Results.BadRequest("No replay file.");

                string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".replay");
                using (var stream = File.Create(tempPath))
                    await file.CopyToAsync(stream);

                string json = ParseReplayFile(tempPath);
                try { File.Delete(tempPath); } catch { }

                return Results.Content(json, "application/json");
            });

            app.Run("http://0.0.0.0:8080");
        }
    }
}
