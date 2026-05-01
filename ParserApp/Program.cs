using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FortniteReplayReader;
using FortniteReplayReader.Models;

namespace ParserApp
{
    class Program
    {
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

        private static PlayerData FindPlayerByName(string name, IEnumerable<PlayerData> players)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return players?.FirstOrDefault(p =>
                string.Equals(p.PlayerNameCustomOverride, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.PlayerName, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.StreamerModeName, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.PlayerId, name, StringComparison.OrdinalIgnoreCase)
            );
        }

        private static bool IsRawId(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            if (name.Length == 32 && name.All(c => "0123456789ABCDEFabcdef".Contains(c)))
                return true;
            return false;
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

        public static string ParseReplayFile(string replayPath)
        {
            try
            {
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                File.WriteAllText(Path.Combine(exeDir, "debug_dump.txt"), "");
            }
            catch { }

            WriteDebug("=== WEB PARSE RUN ===");

            var reader = new ReplayReader();
            FortniteReplay replay = reader.ReadReplay(replayPath);

            var players = replay.PlayerData ?? Enumerable.Empty<PlayerData>();
            var nameMap = BuildNameMap(players);
            var killfeed = replay.KillFeed?.ToList() ?? new List<KillFeedEntry>();

            WriteDebug($"Killfeed count: {killfeed.Count}");
            WriteDebug($"Player count: {players.Count()}");

            foreach (var kf in killfeed)
            {
                try
                {
                    var tags = kf?.DeathTags != null ? string.Join(", ", kf.DeathTags) : "null";
                    WriteDebug($"KILL | killer={kf?.FinisherOrDownerName} | victim={kf?.PlayerName} | dist={kf?.Distance} | tags={tags}");
                }
                catch (Exception ex)
                {
                    WriteDebug($"KILL LOG ERROR: {ex.Message}");
                }
            }

            var validKills = new List<(KillFeedEntry kf, double meters, string weapon, string rarity, PlayerData killer, PlayerData victim, string resolvedKiller, string resolvedVictim)>();

            foreach (var kf in killfeed)
            {
                if (kf == null) continue;

                string killerName = kf.FinisherOrDownerName;
                string victimName = kf.PlayerName;

                // Try to resolve UUID to display name via name map
                if (!string.IsNullOrWhiteSpace(killerName) && nameMap.ContainsKey(killerName))
                    killerName = nameMap[killerName];
                if (!string.IsNullOrWhiteSpace(victimName) && nameMap.ContainsKey(victimName))
                    victimName = nameMap[victimName];

                // ❌ Reject still-unresolved raw UUIDs / empty names
                if (IsRawId(killerName)) continue;
                if (IsRawId(victimName)) continue;

                // ❌ Self-elimination
                if (string.Equals(killerName, victimName, StringComparison.OrdinalIgnoreCase)) continue;

                var killer = FindPlayerByName(killerName, players);
                var victim = FindPlayerByName(victimName, players);

                // ❌ Team elimination
                if (killer != null && victim != null &&
                    killer.TeamIndex.HasValue &&
                    victim.TeamIndex.HasValue &&
                    killer.TeamIndex == victim.TeamIndex)
                    continue;

                double meters = Math.Round((kf.Distance ?? 0) / 100.0, 2);
                if (meters < 0.1) continue;

                var tags = kf.DeathTags ?? new List<string>();
                validKills.Add((kf, meters, IdentifyWeapon(tags), IdentifyRarity(tags), killer, victim, killerName, victimName));
            }

            if (!validKills.Any())
                return "{}";

            var furthest = validKills.OrderByDescending(x => x.meters).First();
            var final = validKills.Last();

            var output = new JObject
            {
                ["furthest"] = new JObject
                {
                    ["distance"] = furthest.meters,
                    ["killer"] = furthest.resolvedKiller,
                    ["killer_platform"] = MapPlatform(furthest.killer?.Platform),
                    ["victim"] = furthest.resolvedVictim,
                    ["victim_platform"] = MapPlatform(furthest.victim?.Platform),
                    ["weapon"] = furthest.weapon,
                    ["rarity"] = furthest.rarity
                },
                ["final"] = new JObject
                {
                    ["distance"] = final.meters,
                    ["killer"] = final.resolvedKiller,
                    ["killer_platform"] = MapPlatform(final.killer?.Platform),
                    ["victim"] = final.resolvedVictim,
                    ["victim_platform"] = MapPlatform(final.victim?.Platform),
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
