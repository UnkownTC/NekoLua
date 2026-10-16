using LabApi.Features.Wrappers;
using SecretLabNAudio.Core;
using SecretLabNAudio.Core.Extensions;
using SecretLabNAudio.Core.Pools;
using UnityEngine;

namespace NekoLua.SecretLabNAudioProxy;

public static class SoundManager
{
    public static AudioPlayer PlayGlobally(string path, Transform parent = null, bool loop = false, float volume = 1f)
    {
        return AudioPlayerPool.Rent(SpeakerSettings.GloballyAudible, parent, Vector3.zero)
            .UseFile(path, loop, volume)
            .PoolOnEnd();
    }
    public static AudioPlayer PlayGlobally(string path, Predicate<Player> condition, Transform parent = null, bool loop = false, float volume = 1f)
    {
        return AudioPlayerPool.Rent(SpeakerSettings.GloballyAudible, parent, Vector3.zero)
            .UseFile(path, loop, volume)
            .WithFilteredSendEngine(x => condition(x))
            .PoolOnEnd();
    }
    public static AudioPlayer PlayGlobally(string path, ICollection<Player> players, Transform parent = null, bool loop = false, float volume = 1f)
    {
        return AudioPlayerPool.Rent(SpeakerSettings.GloballyAudible, parent, Vector3.zero)
            .UseFile(path, loop, volume)
            .WithFilteredSendEngine(players.Contains)
            .PoolOnEnd();
    }
    public static AudioPlayer PlayLocally(string path, Vector3 pos, Transform parent = null, bool spatial = true, float minDistance = 1f, float maxDistance = 15f, bool loop = false, float volume = 1f)
    {
        return AudioPlayerPool.Rent(SpeakerSettings.Default
            with
        { IsSpatial = spatial, MaxDistance = maxDistance, MinDistance = minDistance }, parent, pos)
            .UseFile(path, loop, volume)
            .PoolOnEnd();
    }
    public static AudioPlayer PlayLocally(string path, Vector3 pos, Predicate<Player> condition, Transform parent = null, bool spatial = true, float minDistance = 1f, float maxDistance = 15f, bool loop = false, float volume = 1f)
    {
        return AudioPlayerPool.Rent(SpeakerSettings.Default
            with
        { IsSpatial = spatial, MaxDistance = maxDistance, MinDistance = minDistance }, parent, pos)
            .UseFile(path, loop, volume)
            .WithFilteredSendEngine(x => condition(x))
            .PoolOnEnd();
    }
}
