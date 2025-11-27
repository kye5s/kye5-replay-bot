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

        static void Main(string[] args)
        {
            WriteDebug("=== NEW PARSE RUN ===");
            Console.WriteLine("Parser app started!");

            if (args.Length == 0)
            {
                Console.WriteLine("{}");
                return;
            }

            string replayPath = args[0];
            WriteDebug("Replay path: " + replayPath);

            if (!File.Exists(replayPath))
            {
                WriteDebug("Replay file does NOT exist!");
                Console.WriteLine("{}");
                return;
            }

            try
            {
                var reader = new ReplayReader();
                FortniteReplay replay = reader.ReadReplay(replayPath);

                WriteDebug("Replay loaded successfully.");

                var nameMap = BuildNameMap(replay.PlayerData ?? Enumerable.Empty<PlayerData>());
                WriteDebug("Player count: " + nameMap.Count);

                var killfeed = replay.KillFeed?.ToList() ?? new List<KillFeedEntry>();
                WriteDebug("Killfeed entries: " + killfeed.Count);

                var eliminations = replay.Eliminations != null
                    ? replay.Eliminations.Cast<object>().ToList()
                    : new List<object>();

                if (killfeed.Count == 0)
                {
                    WriteDebug("No killfeed.");
                    Console.WriteLine("{}");
                    return;
                }

                var merged = new List<(KillFeedEntry kf, double meters, string weapon, string rarity)>();

                // -------------------------------
                // Merge kills
                // -------------------------------
                for (int i = 0; i < killfeed.Count; i++)
                {
                    var kf = killfeed[i];
                    if (kf == null) continue;

                    string killerId = kf.FinisherOrDownerName ?? "";
                    string victimId = kf.PlayerName ?? "";
                    if (killerId == "" || victimId == "") continue;

                    var tags = kf.DeathTags ?? new List<string>();

                    // RAW DEATHTAGS DUMP
                    WriteDebug("=== DEATHTAGS DUMP BEGIN ===");
                    WriteDebug($"KILL #{i}  Killer={killerId}  Victim={victimId}");
                    foreach (var tag in tags)
                        WriteDebug("TAG: " + tag);
                    WriteDebug("=== DEATHTAGS DUMP END ===");

                    string weapon = IdentifyWeapon(tags);
                    string rarity = IdentifyRarity(tags);

                    double distance = Math.Round((kf.Distance ?? 0) / 100.0, 2);

                    merged.Add((kf, distance, weapon, rarity));
                }

                if (merged.Count == 0)
                {
                    WriteDebug("Merged list empty.");
                    Console.WriteLine("{}");
                    return;
                }

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
                        ["killer"] = GetName(furthest.kf.FinisherOrDownerName ?? "Unknown"),
                        ["killer_platform"] = MapPlatform(fk?.Platform),
                        ["victim"] = GetName(furthest.kf.PlayerName ?? "Unknown"),
                        ["victim_platform"] = MapPlatform(fv?.Platform),
                        ["weapon"] = furthest.weapon,
                        ["rarity"] = furthest.rarity
                    },

                    ["final"] = new JObject
                    {
                        ["distance"] = final.meters,
                        ["killer"] = GetName(final.kf.FinisherOrDownerName ?? "Unknown"),
                        ["killer_platform"] = MapPlatform(lk?.Platform),
                        ["victim"] = GetName(final.kf.PlayerName ?? "Unknown"),
                        ["victim_platform"] = MapPlatform(lv?.Platform),
                        ["weapon"] = final.weapon,
                        ["rarity"] = final.rarity
                    }
                };

                WriteDebug("Final output generated.");
                Console.WriteLine(output.ToString(Formatting.Indented));
            }
            catch (Exception ex)
            {
                WriteDebug("EXCEPTION: " + ex);
                Console.WriteLine("{}");
            }
        }
    }
}
