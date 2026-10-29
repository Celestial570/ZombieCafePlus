using System.Runtime.InteropServices;
using Raylib_cs;
using ZombieCafe.Assets;
using ZombieCafe.Data;

namespace ZombieCafe.Audio;

/// <summary>
/// Sound effects (.caf, decoded to WAV on first use) and streamed music (.mp3) from the bundle, played by the
/// same ids the original uses. Effects get a few voices each so quick repeats overlap like on the device.
/// </summary>
public sealed class AudioSystem : IDisposable
{
    const int Voices = 3;

    readonly IpaArchive ipa;
    readonly Dictionary<int, (Sound Main, Sound[] Aliases)?> effects = new();
    readonly Dictionary<int, int> nextVoice = new();
    readonly Random rng = new();

    Music music;
    IntPtr musicData, musicType;
    bool musicLoaded;
    int[]? playlist;   // the cafe's background tracks, picked at random as each one ends

    public bool Enabled { get; }
    public float EffectsVolume { get; set; } = 0.8f;

    /// <summary>FUN_0004e1a8: the music's volume (0..1), applied to what is playing now too.</summary>
    public float MusicVolume
    {
        get => musicVolume;
        set
        {
            musicVolume = Math.Clamp(value, 0f, 1f);
            if (musicLoaded)
                Raylib.SetMusicVolume(music, musicVolume);
        }
    }
    float musicVolume = 0.45f;

    public AudioSystem(IpaArchive ipa)
    {
        this.ipa = ipa;
        if (Environment.GetEnvironmentVariable("ZC_MUTE") == "1")
            return;
        Raylib.InitAudioDevice();
        Enabled = Raylib.IsAudioDeviceReady();
    }

    /// <summary>FUN_00053dd4: play sound effect <paramref name="id"/>.</summary>
    public void Play(int id)
    {
        if (!Enabled || id < 0 || id >= SoundNames.Effects.Length)
            return;
        if (!effects.TryGetValue(id, out var entry))
        {
            entry = Load(SoundNames.Effects[id]);
            effects[id] = entry;
        }
        if (entry is not { } e)
            return;
        int v = nextVoice.GetValueOrDefault(id);
        nextVoice[id] = (v + 1) % (Voices + 1);
        var s = v == 0 ? e.Main : e.Aliases[v - 1];
        Raylib.SetSoundVolume(s, EffectsVolume);
        Raylib.PlaySound(s);
    }

    /// <summary>FUN_00053e60: stop sound effect <paramref name="id"/> wherever it is playing.</summary>
    public void Stop(int id)
    {
        if (!Enabled || !effects.TryGetValue(id, out var entry) || entry is not { } e)
            return;
        Raylib.StopSound(e.Main);
        foreach (var a in e.Aliases)
            Raylib.StopSound(a);
    }

    (Sound, Sound[])? Load(string file)
    {
        var bytes = ipa.TryRead(file);
        if (bytes == null)
            return null;
        try
        {
            var wav = CafAudio.ToWav(bytes);
            var wave = Raylib.LoadWaveFromMemory(".wav", wav);
            var sound = Raylib.LoadSoundFromWave(wave);
            Raylib.UnloadWave(wave);
            var aliases = new Sound[Voices];
            for (int i = 0; i < Voices; i++)
                aliases[i] = Raylib.LoadSoundAlias(sound);
            return (sound, aliases);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>FUN_00051f78: start music track <paramref name="id"/> (stops the current one).</summary>
    public void PlayMusic(int id, bool loop = false)
    {
        playlist = null;
        Start(id, loop);
    }

    /// <summary>A short cue (the original's second music channel) over the background music.</summary>
    public void PlayMusicStinger(int id)
    {
        // a cue interrupts the background; the playlist resumes when it ends
        var keep = playlist;
        Start(id, false);
        playlist = keep;
    }

    /// <summary>The cafe's background music: one of these tracks after another, at random.</summary>
    public void PlayPlaylist(params int[] ids)
    {
        playlist = ids;
        Start(ids[rng.Next(ids.Length)], false);
    }

    void Start(int id, bool loop)
    {
        StopMusic();
        if (!Enabled || id < 0 || id >= SoundNames.Music.Length)
            return;
        var bytes = ipa.TryRead(SoundNames.Music[id]);
        if (bytes == null)
            return;
        // raylib decodes the stream from this memory while it plays, so it must outlive the call
        musicData = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, musicData, bytes.Length);
        musicType = Marshal.StringToHGlobalAnsi(".mp3");
        unsafe
        {
            music = Raylib.LoadMusicStreamFromMemory((sbyte*)musicType, (byte*)musicData, bytes.Length);
        }
        musicLoaded = music.FrameCount > 0;
        if (!musicLoaded)
        {
            StopMusic();
            return;
        }
        music.Looping = loop;
        Raylib.SetMusicVolume(music, MusicVolume);
        Raylib.PlayMusicStream(music);
    }

    public void StopMusic()
    {
        if (musicLoaded)
        {
            Raylib.StopMusicStream(music);
            Raylib.UnloadMusicStream(music);
            musicLoaded = false;
        }
        if (musicData != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(musicData);
            musicData = IntPtr.Zero;
        }
        if (musicType != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(musicType);
            musicType = IntPtr.Zero;
        }
    }

    /// <summary>Feeds the music stream; call once per frame.</summary>
    public void Update()
    {
        if (!musicLoaded)
            return;
        Raylib.UpdateMusicStream(music);
        if (!Raylib.IsMusicStreamPlaying(music) && !music.Looping && playlist != null)
            Start(playlist[rng.Next(playlist.Length)], false);
    }

    public void Dispose()
    {
        StopMusic();
        foreach (var e in effects.Values)
        {
            if (e is not { } s)
                continue;
            foreach (var a in s.Aliases)
                Raylib.UnloadSoundAlias(a);
            Raylib.UnloadSound(s.Main);
        }
        effects.Clear();
        if (Enabled)
            Raylib.CloseAudioDevice();
    }
}
