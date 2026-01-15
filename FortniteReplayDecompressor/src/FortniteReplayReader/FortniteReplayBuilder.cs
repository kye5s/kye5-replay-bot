using FortniteReplayReader.Models;
using FortniteReplayReader.Models.NetFieldExports;
using FortniteReplayReader.Models.NetFieldExports.Weapons;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace FortniteReplayReader;

public class FortniteReplayBuilder
{
    private readonly GameData GameData = new();
    private readonly MapData MapData = new();
    private readonly List<KillFeedEntry> KillFeed = new();

    private readonly Dictionary<uint, uint> _actorToChannel = new();
    private readonly Dictionary<uint, uint> _channelToActor = new();
    private readonly Dictionary<uint, uint> _pawnChannelToStateChannel = new();

    private readonly Dictionary<uint, List<QueuedPlayerPawn>> _queuedPlayerPawns = new();
    private readonly HashSet<uint> _onlySpectatingPlayers = new();
    private readonly Dictionary<uint, PlayerData> _players = new();
    private readonly Dictionary<int?, TeamData> _teams = new();

    private float? ReplicatedWorldTimeSeconds = 0;
    private double? ReplicatedWorldTimeSecondsDouble = 0;

    public FortniteReplay Build(FortniteReplay replay)
    {
        UpdateTeamData();
        replay.GameData = GameData;
        replay.MapData = MapData;
        replay.KillFeed = KillFeed;
        replay.TeamData = _teams.Values;
        replay.PlayerData = _players.Values;
        return replay;
    }

    public void UpdatePlayerState(uint channelIndex, FortPlayerState state)
    {
        if (state.bOnlySpectator == true)
            return;

        var isNew = !_players.TryGetValue(channelIndex, out var player);
        if (isNew)
        {
            player = new PlayerData(state);
            _players[channelIndex] = player;
        }

        if (state.TeamIndex > 0)
            player.TeamIndex = state.TeamIndex;

        if (state.Distance != null || state.DeathCause != null || state.bDBNO == true)
        {
            UpdateKillFeed(channelIndex, player, state);
        }
    }

    private void UpdateKillFeed(uint channelIndex, PlayerData victim, FortPlayerState state)
    {
        if (!state.FinisherOrDowner.HasValue)
            return;

        if (!_actorToChannel.TryGetValue(state.FinisherOrDowner.Value, out var finisherChannel))
            return;

        if (!_players.TryGetValue(finisherChannel, out var finisher))
            return;

        // ❌ Self elimination
        if (finisher.Id == victim.Id)
            return;

        // ❌ Same-team elimination
        if (finisher.TeamIndex != null &&
            victim.TeamIndex != null &&
            finisher.TeamIndex == victim.TeamIndex)
            return;

        // ❌ Invalid distance
        if (state.Distance == null || state.Distance <= 0)
            return;

        var entry = new KillFeedEntry
        {
            ReplicatedWorldTimeSeconds = ReplicatedWorldTimeSeconds,
            ReplicatedWorldTimeSecondsDouble = ReplicatedWorldTimeSecondsDouble,

            FinisherOrDowner = finisher.Id,
            FinisherOrDownerName = finisher.PlayerId,
            FinisherOrDownerIsBot = finisher.IsBot,

            PlayerId = victim.Id,
            PlayerName = victim.PlayerId,
            PlayerIsBot = victim.IsBot,

            Distance = state.Distance,
            DeathCause = state.DeathCause,
            DeathLocation = state.DeathLocation,
            DeathCircumstance = state.DeathCircumstance,
            DeathTags = state.DeathTags?.Tags?.Select(t => t.TagName)
        };

        KillFeed.Add(entry);
    }

    private void UpdateTeamData()
    {
        foreach (var p in _players.Values)
        {
            if (p.TeamIndex == null)
                continue;

            if (!_teams.TryGetValue(p.TeamIndex, out var team))
            {
                team = new TeamData
                {
                    TeamIndex = p.TeamIndex,
                    PlayerIds = new List<int?>(),
                    PlayerNames = new List<string?>()
                };
                _teams[p.TeamIndex] = team;
            }

            team.PlayerIds.Add(p.Id);
            team.PlayerNames.Add(p.PlayerName ?? p.PlayerId);
        }
    }
}
