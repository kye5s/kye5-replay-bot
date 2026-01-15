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
                if (p == null) continue;
                if (string.IsNullOrEmpty(p.PlayerId)) continue;

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
        // PARSE REPLAY
        // -------------------------------------------------------------
        public static string ParseReplayFile(string replayPath)
        {
            WriteDebug("=== WEB PARSE RUN ===");

            var reader = new ReplayReader();
            FortniteReplay replay = reader.ReadReplay(replayPath);

            var players = replay.PlayerData ?? Enumerable.Empty<PlayerData>();
            var nameMap = BuildNameMap(players);
            var killfeed = replay.KillFeed?.ToList() ?? new List<KillFeedEntry>();

            var validKills = new List<(KillFeedEntry kf, double meters, string weapon, string rarity)>();

            foreach (var kf in killfeed)
            {
                if (kf == null) continue;

                var killer = FindPlayer(kf.FinisherOrDownerName, players);
                var victim = FindPlayer(kf.PlayerName, players);

                // ❌ invalid references
                if (killer == null || victim == null) continue;

                // ❌ self elimination
                if (killer.PlayerId == victim.PlayerId) continue;

                // ❌ teammate elimination
                if (killer.TeamIndex.HasValue &&
                    victim.TeamIndex.HasValue &&
                    killer.TeamIndex == victim.TeamIndex)
                    continue;

                double meters = Math.Round((kf.Distance ?? 0) / 100.0, 2);
                if (meters < 0.1) continue;

                var tags = kf.DeathTags ?? new List<string>();
                validKills.Add((kf, meters, IdentifyWeapon(tags), IdentifyRarity(tags)));
            }

            if (!validKills.Any())
                return "{}";

            var furthest = validKills.OrderByDescending(x => x.meters).First();
            var final = validKills.Last();

            string GetName(string id) =>
                nameMap.TryGetValue(id, out var n) ? n : id;

            PlayerData fk = FindPlayer(furthest.kf.FinisherOrDownerName, players);
            PlayerData fv = FindPlayer(furthest.kf.PlayerName, players);
            PlayerData lk = FindPlayer(final.kf.FinisherOrDownerName, players);
            PlayerData lv = FindPlayer(final.kf.PlayerName, players);

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

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("{}");
                return;
            }

            Console.WriteLine(ParseReplayFile(args[0]));
        }
    }
}
