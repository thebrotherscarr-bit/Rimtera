using System.Runtime.InteropServices;

namespace Rimtera.UI;

/// <summary>
/// Dependency-free audio: a software mixer streaming 16-bit stereo PCM to the Windows waveOut API.
/// Sound effects are synthesised once at startup (no audio files), music is generated live as a
/// grooving generative score whose key, style and mood follow the biome and time of day, and
/// rain plays as a filtered-noise ambience loop. Recorded clips in Assets/Sounds replace synthesised
/// effects by name (see <see cref="LoadRecordedSounds"/>). If no audio device is available everything is a
/// silent no-op.
/// </summary>
internal sealed class AudioEngine : IDisposable
{
    public const int SampleRate = 44100;
    private const int BufferFrames = 2048;
    private const int BufferCount = 4;

    private readonly object _gate = new();
    private readonly Dictionary<string, float[]> _effects = new();
    private readonly Dictionary<string, List<float[]>> _recorded = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Voice> _voices = new();
    private readonly Thread? _thread;
    private readonly IntPtr _device;
    private readonly IntPtr[] _headers = new IntPtr[BufferCount];
    private readonly IntPtr[] _buffers = new IntPtr[BufferCount];
    private readonly short[] _scratch = new short[BufferFrames * 2];
    private volatile bool _running;
    private readonly Random _random = new(7);

    private MusicState _music = new();
    private float _rainLevel;
    private float _rainNoise;

    // Streamed loops (OGG in Assets/Music and Assets/Ambience): the music track, the ambience bed, the weather layer
    // and positional spot loops. The synth music fades out while a track plays and back in when none fits.
    private readonly Dictionary<string, string> _library = new(StringComparer.OrdinalIgnoreCase);
    private readonly StreamDeck _musicDeck, _bedDeck, _weatherDeck;
    private readonly Dictionary<string, StreamDeck> _spotDecks = new(StringComparer.OrdinalIgnoreCase);
    private float _synthMusicGain = 1f;

    public float SoundVolume { get; set; } = 1f;
    public float MusicVolume { get; set; } = 0.8f;
    public float AmbientVolume { get; set; } = 1f;
    public bool Available { get; }

    private sealed class Voice
    {
        public required float[] Samples;
        public int Position;
        public float Volume;
        public float Pan;
    }

    /// <summary>Selects the music: a mood (0 calm … 1 tense) and a key root in semitones.</summary>
    public void SetMusicMood(string theme, float tension, bool night)
    {
        lock (_gate)
        {
            _music.Theme = theme;
            _music.TargetTension = Math.Clamp(tension, 0f, 1f);
            _music.Night = night;
        }
    }

    public void SetRain(float level) => _rainLevel = Math.Clamp(level, 0f, 1f);

    /// <summary>True when a music track or ambience loop of that name is installed.</summary>
    public bool HasLoop(string name) => _library.ContainsKey(name);

    /// <summary>Plays a music track (crossfading from the last one), or null to hand back to the synth music.</summary>
    public void PlayMusic(string? slot) => _musicDeck.Play(slot);

    public string? MusicTrack => _musicDeck.Current;

    /// <summary>Sets the ambience bed and weather layer; the weather loop follows its intensity.</summary>
    public void SetAmbience(AmbienceChoice choice)
    {
        _bedDeck.Play(choice.Bed);
        _weatherDeck.Play(choice.Weather);
        _weatherDeck.Volume = 0.35f + 0.65f * choice.WeatherVolume;
    }

    /// <summary>A positional loop (waterfall, molten flow, machinery, fire) at a volume and pan; volume 0 fades it out.</summary>
    public void SetSpot(string slot, float volume, float pan)
    {
        if (!_spotDecks.TryGetValue(slot, out var deck))
        {
            if (volume <= 0.01f || !HasLoop(slot))
                return;
            lock (_gate)
                _spotDecks[slot] = deck = new StreamDeck(OpenLoop, SampleRate, 1.5f);
        }
        deck.Volume = Math.Clamp(volume, 0f, 1f);
        deck.Pan = Math.Clamp(pan, -1f, 1f);
        deck.Play(volume > 0.01f ? slot : null);
    }

    private IAudioStream? OpenLoop(string name)
    {
        try
        {
            return _library.TryGetValue(name, out var path) ? new VorbisFileStream(path) : null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            return null; // a damaged file leaves the slot on its fallback
        }
    }

    private void LoadLoops()
    {
        foreach (var folder in new[] { "Music", "Ambience" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", folder);
            if (Directory.Exists(path))
                foreach (var file in Directory.EnumerateFiles(path, "*.ogg"))
                    _library[Path.GetFileNameWithoutExtension(file)] = file;
        }
    }

    public void Play(string name, float volume = 1f, float pan = 0f)
    {
        if (!Available)
            return;
        // A specific cue (use_bow) without a file falls back along SoundCatalog's chain (shoot), ending at a synth effect.
        float[]? samples = null;
        for (var cue = name; cue is not null && samples is null; cue = SoundCatalog.Fallback(cue))
        {
            if (_recorded.TryGetValue(cue, out var takes))
                samples = takes[_random.Next(takes.Count)];
            else
                _effects.TryGetValue(cue, out samples);
        }
        if (samples is null)
            return;
        lock (_gate)
        {
            if (_voices.Count > 24)
                _voices.RemoveAt(0);
            // Slight random pitch-by-skip variation would need resampling; vary volume instead.
            _voices.Add(new Voice { Samples = samples, Volume = volume * (0.9f + (float)_random.NextDouble() * 0.2f), Pan = Math.Clamp(pan, -1f, 1f) });
        }
    }

    public AudioEngine()
    {
        Synthesize();
        LoadRecordedSounds(Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds"));
        LoadLoops();
        _musicDeck = new StreamDeck(OpenLoop, SampleRate, 3f);
        _bedDeck = new StreamDeck(OpenLoop, SampleRate, 4f);
        _weatherDeck = new StreamDeck(OpenLoop, SampleRate, 3f);
        try
        {
            var format = new WaveFormat
            {
                FormatTag = 1, Channels = 2, SamplesPerSec = SampleRate, BitsPerSample = 16,
                BlockAlign = 4, AvgBytesPerSec = SampleRate * 4, Size = 0
            };
            if (waveOutOpen(out _device, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0)
                return;
            var headerSize = Marshal.SizeOf<WaveHeader>();
            for (var i = 0; i < BufferCount; i++)
            {
                _buffers[i] = Marshal.AllocHGlobal(BufferFrames * 4);
                _headers[i] = Marshal.AllocHGlobal(headerSize);
                var header = new WaveHeader { Data = _buffers[i], BufferLength = BufferFrames * 4, Flags = 0 };
                Marshal.StructureToPtr(header, _headers[i], false);
                waveOutPrepareHeader(_device, _headers[i], headerSize);
                // Mark as done so the pump fills it immediately.
                header = Marshal.PtrToStructure<WaveHeader>(_headers[i]);
                header.Flags |= WhdrDone;
                Marshal.StructureToPtr(header, _headers[i], false);
            }
            Available = true;
            _running = true;
            _thread = new Thread(Pump) { IsBackground = true, Name = "Rimtera audio", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or OutOfMemoryException)
        {
            Available = false;
        }
    }

    private void Pump()
    {
        var headerSize = Marshal.SizeOf<WaveHeader>();
        while (_running)
        {
            var wrote = false;
            for (var i = 0; i < BufferCount; i++)
            {
                var header = Marshal.PtrToStructure<WaveHeader>(_headers[i]);
                if ((header.Flags & WhdrDone) == 0)
                    continue;
                Mix(_scratch);
                Marshal.Copy(_scratch, 0, _buffers[i], _scratch.Length);
                header.Flags &= ~WhdrDone;
                Marshal.StructureToPtr(header, _headers[i], false);
                waveOutWrite(_device, _headers[i], headerSize);
                wrote = true;
            }
            if (!wrote)
                Thread.Sleep(5);
        }
    }

    private readonly float[] _left = new float[BufferFrames];
    private readonly float[] _right = new float[BufferFrames];

    private void Mix(short[] output)
    {
        Array.Clear(_left);
        Array.Clear(_right);
        lock (_gate)
        {
            var sfx = SoundVolume * 0.8f;
            for (var v = _voices.Count - 1; v >= 0; v--)
            {
                var voice = _voices[v];
                var leftGain = voice.Volume * sfx * Math.Min(1f, 1f - voice.Pan);
                var rightGain = voice.Volume * sfx * Math.Min(1f, 1f + voice.Pan);
                var count = Math.Min(BufferFrames, voice.Samples.Length - voice.Position);
                for (var i = 0; i < count; i++)
                {
                    var sample = voice.Samples[voice.Position + i];
                    _left[i] += sample * leftGain;
                    _right[i] += sample * rightGain;
                }
                voice.Position += count;
                if (voice.Position >= voice.Samples.Length)
                    _voices.RemoveAt(v);
            }
            // The synth music steps aside (over about 2.5 s) while a streamed track plays.
            var synthTarget = _musicDeck.Sounding ? 0f : 1f;
            _synthMusicGain += Math.Clamp(synthTarget - _synthMusicGain, -BufferFrames / (2.5f * SampleRate), BufferFrames / (2.5f * SampleRate));
            if (_synthMusicGain > 0.001f)
                _music.Render(_left, _right, MusicVolume * 0.35f * _synthMusicGain);
        }
        _musicDeck.Render(_left, _right, BufferFrames, MusicVolume * 0.55f);
        _bedDeck.Render(_left, _right, BufferFrames, AmbientVolume * 0.45f);
        _weatherDeck.Render(_left, _right, BufferFrames, AmbientVolume * 0.5f);
        StreamDeck[] spots;
        lock (_gate)
            spots = _spotDecks.Values.ToArray();
        foreach (var deck in spots)
            deck.Render(_left, _right, BufferFrames, AmbientVolume * 0.5f);

        // Rain: brown-ish noise, louder with intensity; a recorded rain or storm loop replaces it.
        var rain = _weatherDeck.Sounding ? 0f : _rainLevel * AmbientVolume * 0.25f;
        for (var i = 0; i < BufferFrames; i++)
        {
            if (rain > 0.001f)
            {
                _rainNoise = _rainNoise * 0.96f + ((float)_random.NextDouble() * 2f - 1f) * 0.04f;
                var crackle = _random.NextDouble() < 0.002 ? ((float)_random.NextDouble() - 0.5f) * 0.6f : 0f;
                _left[i] += (_rainNoise * 3f + crackle) * rain;
                _right[i] += (_rainNoise * 3f - crackle) * rain;
            }
            output[i * 2] = (short)(Math.Clamp(SoftClip(_left[i]), -1f, 1f) * 32000);
            output[i * 2 + 1] = (short)(Math.Clamp(SoftClip(_right[i]), -1f, 1f) * 32000);
        }
    }

    private static float SoftClip(float x) => x / (1f + MathF.Abs(x) * 0.6f);

    // ---------------------------------------------------------------- synthesis

    /// <summary>The effects <see cref="Synthesize"/> makes; every <see cref="SoundCatalog"/> fallback chain ends at one.</summary>
    internal static readonly string[] SynthesisedCues =
        ["dig", "place", "break", "jump", "land", "swing", "zap", "shoot", "hit", "hurt", "death", "explosion", "pickup", "coin", "craft",
         "click", "hover", "splash", "thunder", "boss", "event", "bite", "door", "eat", "drink", "chest", "grab", "death_sting"];

    private void Synthesize()
    {
        var r = new Random(42);
        float Noise() => (float)r.NextDouble() * 2f - 1f;
        float[] Make(float seconds, Func<float, float, float> sample)
        {
            var count = (int)(seconds * SampleRate);
            var data = new float[count];
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)SampleRate;
                data[i] = sample(t, t / seconds);
            }
            return data;
        }
        float Env(float p, float attack = 0.02f) => p < attack ? p / attack : MathF.Pow(1f - (p - attack) / (1f - attack), 2.2f);
        float Sine(float t, float f) => MathF.Sin(MathF.PI * 2f * f * t);
        float Square(float t, float f) => Sine(t, f) >= 0 ? 1f : -1f;
        float lowpass = 0f;
        float Filtered(float input, float k) => lowpass += (input - lowpass) * k;

        _effects["dig"] = Make(0.12f, (t, p) => Filtered(Noise(), 0.25f) * Env(p, 0.05f) * 1.4f + Sine(t, 90) * Env(p) * 0.4f);
        _effects["place"] = Make(0.1f, (t, p) => (Sine(t, 140 - p * 60) * 0.7f + Filtered(Noise(), 0.1f)) * Env(p, 0.02f));
        _effects["break"] = Make(0.22f, (t, p) => Filtered(Noise(), 0.4f) * Env(p, 0.01f) * 1.2f);
        _effects["jump"] = Make(0.12f, (t, p) => Square(t, 220 + p * 260) * Env(p) * 0.18f);
        _effects["land"] = Make(0.1f, (t, p) => Filtered(Noise(), 0.12f) * Env(p, 0.01f) * 1.5f);
        _effects["swing"] = Make(0.18f, (t, p) => Filtered(Noise(), 0.08f + p * 0.2f) * MathF.Sin(MathF.PI * p) * 1.6f);
        _effects["zap"] = Make(0.2f, (t, p) => (Square(t, 880 - p * 600) * 0.15f + Sine(t, 1760 - p * 1200) * 0.15f) * Env(p, 0.01f));
        _effects["shoot"] = Make(0.16f, (t, p) => (Filtered(Noise(), 0.5f) * 0.8f + Sine(t, 300 - p * 200) * 0.5f) * Env(p, 0.005f));
        _effects["hit"] = Make(0.12f, (t, p) => (Filtered(Noise(), 0.35f) + Sine(t, 180) * 0.6f) * Env(p, 0.005f));
        _effects["hurt"] = Make(0.25f, (t, p) => (Square(t, 160 - p * 80) * 0.25f + Filtered(Noise(), 0.2f) * 0.4f) * Env(p, 0.01f));
        _effects["death"] = Make(0.45f, (t, p) => (Filtered(Noise(), 0.3f - p * 0.25f) * 1.2f + Sine(t, 110 - p * 70) * 0.5f) * Env(p, 0.01f));
        _effects["explosion"] = Make(0.9f, (t, p) => (Filtered(Noise(), 0.15f - p * 0.12f) * 2f + Sine(t, 55) * 0.8f) * Env(p, 0.005f));
        _effects["pickup"] = Make(0.1f, (t, p) => Sine(t, p < 0.5f ? 880 : 1320) * Env(p, 0.01f) * 0.35f);
        _effects["coin"] = Make(0.18f, (t, p) => (Sine(t, 1568) + Sine(t, 2093) * 0.6f) * Env(p, 0.005f) * 0.25f);
        _effects["craft"] = Make(0.5f, (t, p) => (Sine(t, 523) * (p < 0.33f ? 1 : 0) + Sine(t, 659) * (p is >= 0.33f and < 0.66f ? 1 : 0) + Sine(t, 784) * (p >= 0.66f ? 1 : 0)) * Env(p, 0.02f) * 0.3f);
        _effects["click"] = Make(0.04f, (t, p) => Square(t, 1200) * Env(p, 0.01f) * 0.12f);
        _effects["hover"] = Make(0.03f, (t, p) => Sine(t, 1800) * Env(p, 0.01f) * 0.08f);
        _effects["splash"] = Make(0.35f, (t, p) => Filtered(Noise(), 0.5f - p * 0.4f) * Env(p, 0.02f) * 0.9f);
        _effects["thunder"] = Make(2.2f, (t, p) => Filtered(Noise(), 0.03f + (p < 0.05f ? 0.4f : 0f)) * MathF.Pow(1f - p, 1.5f) * 4f);
        _effects["boss"] = Make(1.2f, (t, p) => (Sine(t, 55) + Sine(t, 82.4f) * 0.7f + Square(t, 110) * 0.15f) * Env(p, 0.1f) * 0.5f);
        _effects["event"] = Make(0.8f, (t, p) => (Sine(t, 392) * 0.4f + Sine(t, 523) * 0.3f + Sine(t, 659) * 0.3f) * Env(p, 0.05f) * 0.45f);
        _effects["bite"] = Make(0.15f, (t, p) => (Sine(t, 1200 + MathF.Sin(t * 60) * 200) * 0.3f) * Env(p, 0.01f));
        _effects["door"] = Make(0.2f, (t, p) => (Filtered(Noise(), 0.1f) * 0.8f + Sine(t, 70) * 0.4f) * Env(p, 0.02f));
        // PE-4 fallbacks: three crunchy bites, two gulps with a fizz, a latch and creak, a soft grab, and the death sting.
        _effects["eat"] = Make(0.42f, (t, p) =>
        {
            var bite = p * 3f % 1f; // three bites
            return Filtered(Noise(), 0.35f) * (bite < 0.08f ? bite / 0.08f : MathF.Pow(1f - (bite - 0.08f) / 0.92f, 3f)) * 0.9f;
        });
        _effects["drink"] = Make(0.5f, (t, p) =>
        {
            var gulp = p * 2f % 1f; // two gulps
            var tone = Sine(t, 180 + gulp * 140) * MathF.Sin(MathF.PI * gulp) * 0.45f;
            return tone + Filtered(Noise(), 0.6f) * 0.08f * (1f - p);
        });
        _effects["chest"] = Make(0.32f, (t, p) =>
            (p < 0.12f ? Square(t, 900) * 0.12f * (1f - p / 0.12f) : 0f) + Sine(t, 240 - p * 120) * Env(p, 0.15f) * 0.3f +
            Filtered(Noise(), 0.08f) * Env(p, 0.1f) * 0.6f);
        _effects["grab"] = Make(0.12f, (t, p) => (Sine(t, 660 + p * 220) * 0.3f + Filtered(Noise(), 0.2f) * 0.3f) * Env(p, 0.05f));
        _effects["death_sting"] = Make(1.3f, (t, p) =>
            (Sine(t, 220) * (p < 0.33f ? 1f : 0f) + Sine(t, 207.65f) * (p is >= 0.33f and < 0.66f ? 1f : 0f) + Sine(t, 146.83f) * (p >= 0.66f ? 1f : 0f) +
             Sine(t, 110) * 0.5f) * Env(p, 0.02f) * 0.35f);
    }

    // ---------------------------------------------------------------- recorded sounds

    /// <summary>Perceived loudness every recorded clip is levelled to (LUFS, measured by <see cref="Loudness"/>).</summary>
    public const float RecordedLoudness = -16f;

    /// <summary>
    /// Loads every WAV in <paramref name="folder"/> as a replacement for the effect it is named after
    /// (<c>jump.wav</c> replaces "jump"). Numbered takes (<c>jump_2.wav</c>, <c>jump_3.wav</c>) join it as
    /// variations, one picked at random per play. Every clip is levelled to the same perceived loudness
    /// (plus a small per-sound offset, see <see cref="LoudnessOffset"/>), so takes and sounds match however
    /// they were recorded. A clip with no matching effect still loads, ready for a trigger to play it by
    /// name. Unreadable files are skipped. Returns how many clips loaded.
    /// </summary>
    public int LoadRecordedSounds(string folder)
    {
        if (!Directory.Exists(folder))
            return 0;
        var loaded = 0;
        foreach (var path in Directory.EnumerateFiles(folder, "*.wav"))
        {
            var samples = DecodeWav(File.ReadAllBytes(path));
            if (samples is null || samples.Length == 0)
                continue;
            var name = Path.GetFileNameWithoutExtension(path);
            var cut = name.LastIndexOf('_');
            if (cut > 0 && int.TryParse(name[(cut + 1)..], out _))
                name = name[..cut];
            Level(samples, RecordedLoudness + LoudnessOffset(name));
            if (!_recorded.TryGetValue(name, out var takes))
                _recorded[name] = takes = new List<float[]>();
            takes.Add(samples);
            loaded++;
        }
        return loaded;
    }

    /// <summary>
    /// How far a sound sits from the common loudness: menu blips stay discreet, the constant actions sit a
    /// touch under, and the big moments land a little over.
    /// </summary>
    internal static float LoudnessOffset(string name) => SoundCatalog.Get(name)?.Offset ?? name.ToLowerInvariant() switch
    {
        "hover" => -12f,
        "click" => -8f,
        "jump" or "land" or "pickup" or "dig" or "place" => -2f,
        "boss" or "thunder" or "explosion" => 3f,
        _ => 0f
    };

    /// <summary>
    /// Brings a clip to the target loudness. Spiky clips whose peaks would pass 0.95 first are rounded off by a
    /// soft limiter (a knee from 0.5 easing into 0.95) and re-measured, so they reach the target without clipping.
    /// </summary>
    internal static void Level(float[] samples, float targetLufs)
    {
        const float knee = 0.5f, ceiling = 0.95f;
        for (var pass = 0; pass < 4; pass++)
        {
            var loudness = Loudness(samples);
            if (float.IsNegativeInfinity(loudness) || MathF.Abs(loudness - targetLufs) < 0.1f)
                return;
            var gain = MathF.Pow(10f, (targetLufs - loudness) / 20f);
            for (var i = 0; i < samples.Length; i++)
            {
                var x = samples[i] * gain;
                var size = MathF.Abs(x);
                if (size > knee)
                    x = MathF.CopySign(knee + (ceiling - knee) * MathF.Tanh((size - knee) / (ceiling - knee)), x);
                samples[i] = x;
            }
        }
    }

    /// <summary>
    /// Perceived loudness in LUFS (ITU BS.1770 K-weighting) of a clip's body: the mean power of its 50 ms blocks
    /// within 20 dB of the loudest, so silence and fading tails don't drag a short sound's reading down.
    /// </summary>
    internal static float Loudness(float[] samples)
    {
        // K-weighting: a +4 dB high shelf around 1.7 kHz (the head), then a 38 Hz high-pass (the ear).
        var k = MathF.Tan(MathF.PI * 1681.9745f / SampleRate);
        const float q = 0.70717525f;
        var vh = MathF.Pow(10f, 3.9998438f / 20f);
        var vb = MathF.Pow(vh, 0.49966677f);
        var a0 = 1f + k / q + k * k;
        float b0 = (vh + vb * k / q + k * k) / a0, b1 = 2f * (k * k - vh) / a0, b2 = (vh - vb * k / q + k * k) / a0;
        float a1 = 2f * (k * k - 1f) / a0, a2 = (1f - k / q + k * k) / a0;
        var kh = MathF.Tan(MathF.PI * 38.135471f / SampleRate);
        const float qh = 0.50032704f;
        var h0 = 1f + kh / qh + kh * kh;
        float c1 = 2f * (kh * kh - 1f) / h0, c2 = (1f - kh / qh + kh * kh) / h0;
        float x1 = 0, x2 = 0, y1 = 0, y2 = 0, z1 = 0, z2 = 0;
        const int block = SampleRate / 20;
        var powers = new List<double>();
        double sum = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var x = samples[i];
            var y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            var z = y - 2f * y1 + y2 - c1 * z1 - c2 * z2;
            x2 = x1; x1 = x; y2 = y1; y1 = y; z2 = z1; z1 = z;
            sum += z * z;
            if ((i + 1) % block == 0 || i == samples.Length - 1)
            {
                powers.Add(sum / ((i % block) + 1));
                sum = 0;
            }
        }
        var loudest = powers.Count == 0 ? 0 : powers.Max();
        if (loudest <= 1e-12)
            return float.NegativeInfinity;
        var body = powers.Where(power => power >= loudest * 0.01).Average();
        return (float)(-0.691 + 10 * Math.Log10(body));
    }

    /// <summary>
    /// Decodes a RIFF WAV (8/16/24/32-bit PCM or 32-bit float, any channel count, any rate) to mono floats
    /// at <see cref="SampleRate"/>, resampling linearly. Returns null for anything it cannot read.
    /// </summary>
    internal static float[]? DecodeWav(byte[] data)
    {
        if (data.Length < 44 || data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F' ||
            data[8] != 'W' || data[9] != 'A' || data[10] != 'V' || data[11] != 'E')
            return null;
        int format = 0, channels = 0, rate = 0, bits = 0, dataStart = -1, dataLength = 0;
        for (var at = 12; at + 8 <= data.Length;)
        {
            var id = System.Text.Encoding.ASCII.GetString(data, at, 4);
            var size = BitConverter.ToInt32(data, at + 4);
            if (size < 0)
                return null;
            var body = at + 8;
            if (id == "fmt " && body + 16 <= data.Length)
            {
                format = BitConverter.ToInt16(data, body);
                channels = BitConverter.ToInt16(data, body + 2);
                rate = BitConverter.ToInt32(data, body + 4);
                bits = BitConverter.ToInt16(data, body + 14);
                // WAVE_FORMAT_EXTENSIBLE keeps the real format tag at the start of its sub-format GUID.
                if (format == unchecked((short)0xFFFE) && size >= 40 && body + 26 <= data.Length)
                    format = BitConverter.ToInt16(data, body + 24);
            }
            else if (id == "data")
            {
                dataStart = body;
                dataLength = Math.Min(size, data.Length - body);
                break;
            }
            at = body + size + (size & 1);
        }
        var bytes = bits / 8;
        if (dataStart < 0 || channels < 1 || rate < 1000 || !(format == 1 && bytes is 1 or 2 or 3 or 4 || format == 3 && bytes == 4))
            return null;
        var frames = dataLength / (bytes * channels);
        var mono = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++)
            {
                var at = dataStart + (f * channels + c) * bytes;
                sum += format == 3 ? BitConverter.ToSingle(data, at) : bytes switch
                {
                    1 => (data[at] - 128) / 128f,
                    2 => BitConverter.ToInt16(data, at) / 32768f,
                    3 => ((data[at] | data[at + 1] << 8 | (sbyte)data[at + 2] << 16)) / 8388608f,
                    _ => BitConverter.ToInt32(data, at) / 2147483648f
                };
            }
            mono[f] = sum / channels;
        }
        if (rate == SampleRate)
            return mono;
        var resampled = new float[(int)((long)frames * SampleRate / rate)];
        for (var i = 0; i < resampled.Length; i++)
        {
            var position = i * (double)rate / SampleRate;
            var index = (int)position;
            var next = Math.Min(index + 1, frames - 1);
            var t = (float)(position - index);
            resampled[i] = mono[index] * (1f - t) + mono[next] * t;
        }
        return resampled;
    }

    // ---------------------------------------------------------------- generative music

    /// <summary>
    /// Generative score in five original style voices, chosen per biome, over a modern groove and an ambient bed:
    /// <list type="bullet">
    /// <item>Vivaldi: driving sixteenth-note string figuration, harpsichord continuo, walking root-fifth bass (or, with the
    ///   rock feel, an overdriven lead over power-chord chugs and a picked eighth-note bass).</item>
    /// <item>Bach: the same motor figuration answered a bar later by an imitating voice an octave down, over a
    ///   walking eighth-note bass (counterpoint).</item>
    /// <item>Tchaikovsky: a lyrical stepwise melody with wide vibrato over lush strings and harp, often a 3/4 waltz.</item>
    /// <item>Synth-pop (the title): a pulse-width-modulated arpeggiator, an octave synth bass and a square-wave hook
    ///   over I–V–vi–IV, with a drum machine and gated-reverb snare.</item>
    /// <item>Funk: a resonant filtered slap bass with ghost notes and octave pops, wah-swept clav stabs, and
    ///   call-and-response horn riffs with a pitch scoop, over Dorian/Mixolydian seventh-chord vamps.</item>
    /// </list>
    /// Each biome has its own key, mode (major, minor, Dorian, Mixolydian, Lydian, Phrygian dominant), tempo, swing,
    /// harmony, meter, funk kit and drum groove (four-on-the-floor, disco,
    /// breakbeat, half-time, tribal toms, waltz, syncopated funk, rock), with open hats, claps, a fill every fourth bar and
    /// a crash every eighth. A sustained pad and a stereo echo add ambience; drums and density grow with tension. The
    /// lighter biomes play airy (an octave shimmer on the pad, scattered bells, longer echoes, softer drums).
    /// All material is original and generated live.
    /// </summary>
    private sealed class MusicState
    {
        public string Theme = "title";
        public float TargetTension;
        public float Tension;
        public bool Night;
        private long _frame;
        private int _bar;
        private int _pattern;
        private readonly Random _random = new(11);

        private enum Composer : byte { Vivaldi, Bach, Tchaikovsky, Funk, SynthPop }

        private enum Groove : byte { Four, Break, Half, Tribal, Waltz, Funk, Disco, Rock, Pop }

        /// <summary>
        /// Production feel layered over a style: plain, rock (fuzz guitars, gritty bass), airy (shimmer, bells, long echo,
        /// soft drums) or retro (shimmer and long echo with the drum machine up front).
        /// </summary>
        private enum Feel : byte { Plain, Rock, Airy, Retro }

        /// <summary>Swing delays every off-sixteenth by that fraction of a sixteenth (0 straight, 0.33 full shuffle).</summary>
        private readonly record struct Style(int Root, int Bpm, int[] Progression, int[] Scale, float Brightness,
            Composer Composer, int Meter, Groove Groove, float Pad, float Swing, int Kit = 0, Feel Feel = Feel.Plain);

        /// <summary>
        /// A funk band's personality: its bass lines, clav patterns and horn riffs, plus how squelchy (low damping)
        /// and overdriven the bass is, how fast the wah sweeps (cycles per two beats), and how deep the horns scoop.
        /// </summary>
        private sealed record FunkKit(int[][] Bass, float[][] Scratch, int[][] Riffs, float Damping, float Drive, float WahRate, float Scoop);

        // Lead strings (two detuned voices), the imitating second voice (Bach), harpsichord/harp plucks, bass, pad.
        private float _leadFreq, _leadPhaseA, _leadPhaseB, _leadEnv, _vibrato;
        private float _canonFreq, _canonPhase, _canonEnv;
        private readonly int[] _barNotes = new int[16], _lastBarNotes = new int[16];
        private int _melodyNote;
        private readonly float[] _pluckFreq = new float[4], _pluckPhase = new float[4], _pluckEnv = new float[4];
        private int _nextPluck;
        private float _bassFreq, _bassPhase, _bassEnv;
        private readonly float[] _padFreq = new float[4], _padPhase = new float[4];
        private float _padEnv, _tremolo;
        // Funk: slap bass through a resonant low-pass, wah-filtered clav stabs, and a pitch scoop on the horn riffs.
        private float _slapFreq, _slapPhase, _slapEnv, _slapDecay, _slapFilterEnv;
        private Svf _slapFilter;
        private readonly float[] _clavFreq = new float[4], _clavPhase = new float[4];
        private float _clavEnv, _wahPhase, _leadBend = 1f;
        private Svf _clavFilter;
        // Rock: a fuzz rhythm guitar on power chords (root, fifth, octave, each doubled and detuned) into a cabinet
        // low-pass, and a tone filter taming the overdriven lead.
        private readonly float[] _guitarFreq = new float[3], _guitarPhase = new float[6];
        private float _guitarEnv, _guitarDecay, _guitarTone;
        private Svf _cabinet, _leadTone;
        // Air: an inharmonic bell that sparkles across the stereo field.
        private float _bellFreq, _bellPhase, _bellOvertone, _bellEnv, _bellPan;
        // Synth-pop: a pulse-width-modulated arpeggiator through a low-pass that closes after every note.
        private float _arpFreq, _arpPhase, _arpEnv, _pwm;
        private Svf _arpFilter;
        // Gated reverb: a backbeat snare that rings at full body, then is cut dead.
        private int _snareGate;
        // Drums: kick/tom sweep, clap/snare noise, hat and shaker noise; sidechain pump follows the kick.
        private float _kickEnv, _kickPhase, _kickPitch = 85f, _snareEnv, _hatEnv, _pump = 1f, _hatPrev;
        // Open hat (choked by the closed hat; a long decay doubles as a crash) and a three-slap clap.
        private float _openHatEnv, _openHatDecay = 0.99975f, _openHatPrev, _clapEnv;
        private int _clapAge;
        private Svf _clapFilter;
        // Ambience: a stereo feedback echo with a soft low-pass in the loop.
        private readonly float[] _echoLeft = new float[SampleRate], _echoRight = new float[SampleRate];
        private int _echoIndex;
        private float _echoLowLeft, _echoLowRight;

        private static readonly int[] Major = [0, 2, 4, 5, 7, 9, 11];
        private static readonly int[] HarmonicMinor = [0, 2, 3, 5, 7, 8, 11];
        private static readonly int[] Dorian = [0, 2, 3, 5, 7, 9, 10];
        private static readonly int[] Mixolydian = [0, 2, 4, 5, 7, 9, 10];
        private static readonly int[] MinorPentatonic = [0, 3, 5, 7, 10];
        private static readonly int[] MajorPentatonic = [0, 2, 4, 7, 9];
        private static readonly int[] Lydian = [0, 2, 4, 6, 7, 9, 11];
        private static readonly int[] PhrygianDominant = [0, 1, 4, 5, 7, 8, 10];

        // Harmony: a circle-of-fifths sequence, a bright cadence loop, a driving minor sequence, and a romantic
        // loop with a lift to the relative minor.
        private static readonly int[] FifthsCircle = [0, 3, 6, 2, 5, 1, 4, 0];
        private static readonly int[] BrightLoop = [0, 4, 5, 3, 0, 3, 4, 4];
        private static readonly int[] MinorDrive = [0, 5, 3, 4, 0, 3, 4, 4];
        private static readonly int[] Romantic = [0, 5, 3, 4, 0, 2, 3, 4];
        // Funk vamps, two bars a chord: i7–IV7 (Dorian), I7–bVII–IV (Mixolydian), and a restless i7–IV7 strut.
        private static readonly int[] DorianVamp = [0, 0, 3, 3, 0, 0, 3, 4];
        private static readonly int[] MixoVamp = [0, 0, 6, 6, 3, 3, 0, 4];
        private static readonly int[] Strut = [0, 3, 0, 3, 0, 3, 4, 4];
        // Biome colours: a floating Lydian sway, a descending cave line, a Phrygian lurch between I and bII,
        // and a climbing jungle loop.
        private static readonly int[] Drift = [0, 1, 0, 1, 4, 3, 1, 4];
        private static readonly int[] Descent = [0, 6, 5, 4, 3, 2, 3, 4];
        private static readonly int[] Lurch = [0, 1, 0, 6, 0, 1, 5, 1];
        private static readonly int[] Climb = [0, 1, 3, 4, 5, 4, 3, 4];
        // The eighties anthem loop: I–V–vi–IV.
        private static readonly int[] Anthem = [0, 4, 5, 3, 0, 4, 5, 3];

        // Arpeggiator order through the six chord tones, one per sixteenth.
        private static readonly int[] ArpOrder = [0, 1, 2, 3, 4, 3, 2, 1, 0, 1, 2, 3, 4, 5, 4, 3];

        // Rhythm guitar per sixteenth: 1 rings open, below 1 is a palm-muted chug. The gallop takes over when tense.
        private static readonly float[] Chug = [1, 0, 0.6f, 0.6f, 0.6f, 0, 0.6f, 0.6f, 1, 0, 0.6f, 0.6f, 0.6f, 0.6f, 1, 0];
        private static readonly float[] Gallop = [1, 0, 0.6f, 0.6f, 1, 0, 0.6f, 0.6f, 1, 0, 0.6f, 0.6f, 1, 0.6f, 1, 0.6f];

        // Sixteenth-note figurations over six chord tones; -1 = rest.
        private static readonly int[][] Figures =
        [
            [0, 1, 2, 3, 2, 1, 0, 1, 0, 1, 2, 3, 4, 3, 2, 1],
            [0, 2, 1, 3, 2, 4, 3, 5, 4, 2, 3, 1, 2, 0, 1, 0],
            [3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 4, 4, 3, 3],
            [5, 4, 3, 4, 5, 4, 3, 2, 3, 2, 1, 2, 3, 2, 1, 0],
            [0, -1, 2, -1, 4, -1, 2, -1, 1, -1, 3, -1, 5, -1, 3, -1],
        ];

        // Funk material. Bass lines are semitones over the chord root; Rest and Ghost (a muted dead note) are
        // markers, and anything an octave up is popped. Clav patterns are stab velocities per sixteenth. Horn riffs
        // are indices into the pentatonic (5+ climbs an octave), -1 = rest.
        private const int Rest = 99, Ghost = 98;

        private static readonly FunkKit[] FunkKits =
        [
            // 0 Disco: octave-bounce bass, off-beat skank, bright riffs, a round bass and a lazy wah.
            new(
                [
                    [0, Rest, 12, Rest, 0, Rest, 12, Rest, 0, Rest, 12, Rest, 0, Ghost, 12, 10],
                    [0, Rest, 12, Rest, 7, Rest, 12, Rest, 0, Rest, 12, Rest, 5, Ghost, 12, 7],
                ],
                [
                    [0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0],
                    [0, 0, 1, 0.35f, 0, 0, 1, 0, 0, 0, 1, 0.35f, 0, 0, 1, 0.5f],
                ],
                [
                    [7, -1, 8, -1, 9, -1, 7, -1, -1, -1, 5, -1, 7, -1, -1, -1],
                    [-1, -1, -1, -1, 9, 9, -1, 10, -1, 9, -1, 7, -1, -1, -1, -1],
                ],
                0.5f, 1f, 1f, 0.97f),
            // 1 Classic: syncopated sixteenth bass, chicken scratch, call-and-response horns.
            new(
                [
                    [0, Rest, Ghost, 12, Rest, 0, Ghost, Rest, 0, Rest, 12, Ghost, 10, Rest, 7, Ghost],
                    [0, Ghost, Rest, 0, 12, Rest, Ghost, 7, Rest, 0, Ghost, 12, Rest, 10, 12, Ghost],
                ],
                [
                    [0, 0, 1, 0, 0, 0.45f, 0, 0.8f, 0, 0, 1, 0, 0.45f, 0, 0.8f, 0.35f],
                    [1, 0, 0, 0.4f, 0, 0, 0.9f, 0, 0, 0.4f, 0, 0, 0.9f, 0, 0.4f, 0],
                ],
                [
                    [5, -1, -1, 7, -1, 5, -1, -1, 3, -1, 4, -1, 5, -1, -1, -1],
                    [-1, -1, 7, -1, 8, 7, -1, 5, -1, -1, -1, -1, 4, -1, 5, -1],
                ],
                0.4f, 1f, 0.5f, 0.94f),
            // 2 Grit: relentless ghosted sixteenths, an overdriven squelchy bass, fast wah, punchy unison stabs.
            new(
                [
                    [0, Ghost, 0, Ghost, 12, Ghost, 0, 10, Ghost, 0, Ghost, 7, 0, Ghost, 12, Ghost],
                    [0, Rest, Rest, Ghost, 0, Rest, 10, 12, Rest, Ghost, 7, Rest, 5, Rest, 3, 2],
                ],
                [
                    [0.8f, 0.3f, 0.3f, 0.3f, 0.8f, 0.3f, 0.3f, 0.3f, 0.8f, 0.3f, 0.3f, 0.3f, 0.8f, 0.3f, 0.6f, 0.3f],
                    [0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0],
                ],
                [
                    [0, -1, 0, -1, -1, 3, -1, 4, -1, -1, 0, -1, -1, -1, -1, -1],
                    [-1, -1, 5, -1, -1, -1, 5, -1, -1, 5, -1, -1, 7, -1, -1, -1],
                ],
                0.18f, 2.6f, 2f, 0.9f),
            // 3 Swamp: a loping walk under a heavy shuffle, sparse lazy horns, a slow wah and deep scoops.
            new(
                [
                    [0, Rest, Rest, 7, Rest, Rest, 10, Rest, 12, Rest, Rest, 10, Rest, 7, Rest, 5],
                    [0, Rest, 0, Rest, Ghost, Rest, 7, Rest, 10, Rest, Rest, 12, Ghost, 10, 7, Rest],
                ],
                [
                    [1, 0, 0, 0.4f, 0, 0, 0.9f, 0, 0, 0.4f, 0, 0, 0.9f, 0, 0.4f, 0],
                    [0, 0, 0, 0, 1, 0, 0, 0.5f, 0, 0, 0, 0, 1, 0, 0, 0],
                ],
                [
                    [-1, -1, -1, -1, 7, -1, -1, 5, -1, -1, 4, -1, 3, -1, -1, -1],
                    [3, 4, 5, 7, 8, 7, 5, 4, 5, -1, -1, -1, -1, -1, -1, -1],
                ],
                0.6f, 1.3f, 0.25f, 0.9f),
            // 4 Retro (synth-pop): a pulsing octave synth bass and a singing square-wave hook; no clav.
            new(
                [
                    [0, Rest, 12, Rest, 0, Rest, 12, Rest, 0, Rest, 12, Rest, 0, Rest, 12, Rest],
                    [0, 0, 12, 0, 0, 0, 12, 0, 0, 0, 12, 0, 0, 12, 0, 12],
                ],
                [new float[16]],
                [
                    [7, -1, -1, -1, 6, -1, 5, -1, 6, -1, -1, -1, -1, -1, -1, -1],
                    [5, -1, 6, -1, 7, -1, -1, 8, -1, -1, 7, -1, 6, -1, 5, -1],
                ],
                0.5f, 1.2f, 0f, 0.99f),
        ];

        private Style CurrentStyle() => Theme switch
        {
            "title" => new(55, 112, Anthem, Major, 1f, Composer.SynthPop, 4, Groove.Pop, 0.8f, 0f, 4, Feel.Retro),
            "grassland" => new(57, 126, MixoVamp, Mixolydian, 1f, Composer.Funk, 4, Groove.Disco, 0.45f, 0.04f, 0),
            "canopy" => new(59, 136, Climb, Mixolydian, 1f, Composer.Vivaldi, 4, Groove.Tribal, 0.55f, 0.1f),
            "coast" or "ocean" => new(60, 112, Romantic, Major, 0.85f, Composer.Tchaikovsky, 3, Groove.Waltz, 0.85f, 0f, Feel: Feel.Airy),
            "frozen" => new(62, 120, Drift, Lydian, 1.15f, Composer.Bach, 4, Groove.Half, 0.75f, 0.12f, Feel: Feel.Airy),
            "crystal" => new(64, 130, FifthsCircle, Major, 1.15f, Composer.Bach, 4, Groove.Break, 0.6f, 0.06f, Feel: Feel.Airy),
            "fungal" => new(58, 104, Romantic, Dorian, 0.85f, Composer.Tchaikovsky, 3, Groove.Waltz, 0.9f, 0f, Feel: Feel.Airy),
            "junk" => new(57, 124, Strut, Dorian, 1.1f, Composer.Funk, 4, Groove.Break, 0.3f, 0.1f, 2),
            "forest" => new(49, 112, DorianVamp, Dorian, 0.9f, Composer.Funk, 4, Groove.Funk, 0.5f, 0.18f, 1),
            "overgrowth" => new(53, 104, DorianVamp, Dorian, 0.85f, Composer.Funk, 4, Groove.Funk, 0.7f, 0.3f, 3),
            "radiated" => new(62, 134, MinorDrive, HarmonicMinor, 1f, Composer.Bach, 4, Groove.Break, 0.6f, 0.08f),
            "underground" => new(55, 118, Descent, Dorian, 0.85f, Composer.Bach, 4, Groove.Half, 0.75f, 0.16f),
            "molten" or "core" => new(52, 144, Lurch, PhrygianDominant, 1f, Composer.Vivaldi, 4, Groove.Rock, 0.4f, 0f, Feel: Feel.Rock),
            "boss" => new(52, 160, MinorDrive, HarmonicMinor, 1.1f, Composer.Vivaldi, 4, Groove.Rock, 0.35f, 0f, Feel: Feel.Rock),
            _ => new(57, 126, MixoVamp, Mixolydian, 1f, Composer.Funk, 4, Groove.Disco, 0.45f, 0.04f, 0)
        };

        public void Render(float[] left, float[] right, float volume)
        {
            if (volume <= 0.001f)
                return;
            var style = CurrentStyle();
            var bpm = Night && Theme != "boss" ? (int)(style.Bpm * 0.94f) : style.Bpm;
            var scale = style.Scale;
            var stepsPerBar = style.Meter * 4;
            var sixteenth = Math.Max(1, (long)(SampleRate * 60.0 / bpm / 4));
            // Swing: steps come in pairs; the off-sixteenth of each pair lands late.
            var pair = sixteenth * 2;
            var offbeat = (long)(sixteenth * (1f + style.Swing));
            var lyrical = style.Composer == Composer.Tchaikovsky;
            var funk = style.Composer == Composer.Funk;
            var synth = style.Composer == Composer.SynthPop;
            var slap = funk || synth;
            var kit = FunkKits[style.Kit];
            var rock = style.Feel == Feel.Rock;
            var airy = style.Feel == Feel.Airy;
            var spacious = airy || style.Feel == Feel.Retro;
            var echoLeft = (int)Math.Min(_echoLeft.Length - 1, sixteenth * 3);
            var echoRight = (int)Math.Min(_echoRight.Length - 1, sixteenth * 4);
            for (var i = 0; i < left.Length; i++)
            {
                var inPair = _frame % pair;
                var late = inPair >= offbeat;
                var step = (int)((_frame / pair * 2 + (late ? 1 : 0)) % stepsPerBar);
                if (inPair == 0 || inPair == offbeat)
                {
                    if (step == 0)
                        StartBar(style, scale);
                    var degree = style.Progression[_bar % style.Progression.Length];
                    var chord = ChordTones(style.Root, scale, degree);
                    PlayLead(style, chord, scale, step, stepsPerBar);
                    PlayAccompaniment(style, chord, scale, degree, step);
                    PlayDrums(style.Groove, step);
                    if (airy)
                        Chime(chord, step);
                }

                // Lead strings with vibrato (wider and slower for the lyrical style), detached bowing on figuration;
                // funk horns scoop up into each note and bite off short.
                _vibrato += (lyrical ? 5f : 5.5f) / SampleRate;
                var vib = 1f + (lyrical ? 0.006f : 0.004f) * MathF.Sin(_vibrato * MathF.Tau);
                _leadBend += (1f - _leadBend) * 0.0012f;
                _leadPhaseA = Wrap(_leadPhaseA + _leadFreq * vib * _leadBend / SampleRate);
                _leadPhaseB = Wrap(_leadPhaseB + _leadFreq * vib * _leadBend * 1.004f / SampleRate);
                var lead = (Strings(_leadPhaseA, style.Brightness) + Strings(_leadPhaseB, style.Brightness)) * 0.5f;
                if (synth)
                    lead = _leadTone.LowPass((_leadPhaseA < 0.5f ? 1f : -1f) + (_leadPhaseB < 0.5f ? 0.8f : -0.8f), 2400f, 0.7f) * 0.9f;
                else if (rock)
                    lead = _leadTone.LowPass(MathF.Tanh(lead * 3f), 3200f, 0.8f) * 1.2f;
                _leadEnv *= lyrical ? 0.99997f : synth ? 0.99996f : funk ? 0.9998f : 0.99985f;
                var stepPosition = (late ? inPair - offbeat : inPair) / (float)(late ? pair - offbeat : offbeat);
                var articulation = !lyrical && !synth && stepPosition > 0.8f ? 1f - (stepPosition - 0.8f) / 0.2f * 0.85f : 1f;
                lead *= MathF.Min(1f, _leadEnv * 1.4f) * articulation * (funk ? 0.21f : synth ? 0.13f : 0.15f);

                // Bach's imitating voice: last bar's line an octave lower.
                var canon = 0f;
                if (_canonEnv > 0.001f)
                {
                    _canonPhase = Wrap(_canonPhase + _canonFreq * vib / SampleRate);
                    canon = Strings(_canonPhase, style.Brightness * 0.8f) * _canonEnv * 0.09f;
                    _canonEnv *= 0.99985f;
                }

                // Harpsichord (baroque) or harp (romantic) plucks.
                var plucks = 0f;
                for (var v = 0; v < _pluckEnv.Length; v++)
                {
                    if (_pluckEnv[v] < 0.001f)
                        continue;
                    _pluckPhase[v] = Wrap(_pluckPhase[v] + _pluckFreq[v] / SampleRate);
                    var p = _pluckPhase[v] * MathF.Tau;
                    plucks += lyrical
                        ? (MathF.Sin(p) + 0.3f * MathF.Sin(2 * p)) * _pluckEnv[v]
                        : (MathF.Sin(p) + 0.55f * MathF.Sin(2 * p) + 0.32f * MathF.Sin(3 * p) + 0.15f * MathF.Sin(5 * p)) * _pluckEnv[v];
                    _pluckEnv[v] *= lyrical ? 0.99988f : 0.99975f;
                }
                plucks *= 0.065f;

                // Bass: a round sine for the classical styles; for funk and synth-pop a sawtooth through a resonant
                // low-pass whose cutoff snaps open on each pluck, over a sine sub.
                var bass = 0f;
                if (slap)
                {
                    _slapPhase = Wrap(_slapPhase + _slapFreq / SampleRate);
                    var cutoff = 110f + 2600f * _slapFilterEnv * _slapFilterEnv;
                    var growl = MathF.Tanh(_slapFilter.LowPass(2f * _slapPhase - 1f, cutoff, kit.Damping) * kit.Drive) / MathF.Tanh(kit.Drive);
                    bass = (growl * 0.75f + MathF.Sin(_slapPhase * MathF.Tau) * 0.6f) * _slapEnv * 0.21f;
                    _slapEnv *= _slapDecay;
                    _slapFilterEnv *= 0.9988f;
                }
                else
                {
                    _bassPhase = Wrap(_bassPhase + _bassFreq / SampleRate);
                    _bassEnv *= 0.99992f;
                    bass = MathF.Sin(_bassPhase * MathF.Tau) + 0.25f * MathF.Sin(2 * _bassPhase * MathF.Tau);
                    if (rock)
                        bass = MathF.Tanh((bass + 0.3f * (2f * _bassPhase - 1f)) * 1.8f);
                    bass *= _bassEnv * 0.2f;
                }

                // Clav: a narrow pulse chord through a band-pass that the stab opens and a beat-synced wah sweeps.
                var clav = 0f;
                _wahPhase = Wrap(_wahPhase + bpm / 120f * kit.WahRate / SampleRate);
                if (_clavEnv > 0.001f)
                {
                    for (var n = 0; n < _clavFreq.Length; n++)
                    {
                        _clavPhase[n] = Wrap(_clavPhase[n] + _clavFreq[n] / SampleRate);
                        clav += _clavPhase[n] < 0.28f ? 1f : -0.39f;
                    }
                    var wah = 500f + 2200f * (0.55f * _clavEnv + 0.45f * (0.5f + 0.5f * MathF.Sin(_wahPhase * MathF.Tau)));
                    clav = _clavFilter.BandPass(clav, wah, 0.45f) * _clavEnv * 0.11f;
                    _clavEnv *= 0.99945f;
                }

                // Rhythm guitar: detuned sawtooth power chords, fuzzed, then a cabinet low-pass that closes on palm mutes.
                var guitar = 0f;
                if (_guitarEnv > 0.001f)
                {
                    for (var n = 0; n < _guitarPhase.Length; n++)
                    {
                        _guitarPhase[n] = Wrap(_guitarPhase[n] + _guitarFreq[n / 2] * (n % 2 == 0 ? 1f : 1.006f) / SampleRate);
                        guitar += 2f * _guitarPhase[n] - 1f;
                    }
                    guitar = _cabinet.LowPass(MathF.Tanh(guitar * 2.5f), _guitarTone * (0.6f + 0.4f * _guitarEnv), 0.9f) * _guitarEnv * 0.11f;
                    _guitarEnv *= _guitarDecay;
                }

                // Arpeggiator: a pulse whose width drifts, through a low-pass that snaps open on each note.
                var arp = 0f;
                _pwm = Wrap(_pwm + 0.3f / SampleRate);
                if (_arpEnv > 0.001f)
                {
                    _arpPhase = Wrap(_arpPhase + _arpFreq / SampleRate);
                    var width = 0.5f + 0.35f * MathF.Sin(_pwm * MathF.Tau);
                    arp = _arpFilter.LowPass(_arpPhase < width ? 1f : -1f, 600f + 3400f * _arpEnv * _arpEnv, 0.6f) * _arpEnv * 0.06f;
                    _arpEnv *= 0.9997f;
                }

                // Bell: a sine with an inharmonic overtone at 2.76x, ringing out long.
                var bell = 0f;
                if (_bellEnv > 0.001f)
                {
                    _bellPhase = Wrap(_bellPhase + _bellFreq / SampleRate);
                    _bellOvertone = Wrap(_bellOvertone + _bellFreq * 2.76f / SampleRate);
                    bell = (MathF.Sin(_bellPhase * MathF.Tau) + 0.35f * _bellEnv * MathF.Sin(_bellOvertone * MathF.Tau)) * _bellEnv * 0.07f;
                    _bellEnv *= 0.99994f;
                }

                // Pad: sustained chord swelling in each bar (the ambience bed); funk's seventh chords add a fourth
                // voice and an electric-piano tremolo that pans across the stereo field, and airy and retro biomes a
                // shimmer an octave up.
                _padEnv = MathF.Min(1f, _padEnv + 1f / (SampleRate * 0.6f));
                var pad = 0f;
                for (var n = 0; n < _padFreq.Length; n++)
                {
                    if (_padFreq[n] <= 0f)
                        continue;
                    _padPhase[n] = Wrap(_padPhase[n] + _padFreq[n] / SampleRate);
                    pad += 1f - 4f * MathF.Abs(_padPhase[n] - 0.5f);
                    if (spacious)
                        pad += 0.5f * MathF.Sin(_padPhase[n] * 2f * MathF.Tau);
                }
                pad *= _padEnv * style.Pad * (spacious ? 0.065f : 0.05f);
                _tremolo = Wrap(_tremolo + 4.5f / SampleRate);
                var swirl = funk ? 0.35f * MathF.Sin(_tremolo * MathF.Tau) : 0f;

                var beat = Drums(style.Groove) * (0.8f + Tension * 0.35f) * (airy ? 0.65f : rock ? 1.05f : 1f);

                var dry = (lead + canon + plucks + clav * 0.8f + guitar + arp) * _pump + bass * _pump + beat;
                var dryLeft = dry + pad * (1f + swirl) + clav * 0.2f * _pump + bell * (1f - _bellPan);
                var dryRight = dry + pad * (1f - swirl) - clav * 0.2f * _pump + bell * (1f + _bellPan);
                // Ambience: a stereo echo (3/16 left, 1/4 right) fed by the melodic voices, softened each repeat;
                // airy and retro biomes hold the repeats longer and brighter.
                var send = (lead + canon + plucks + clav + pad + bell * 1.5f + arp) * 0.5f;
                var tapLeft = _echoLeft[(_echoIndex - echoLeft + _echoLeft.Length) % _echoLeft.Length];
                var tapRight = _echoRight[(_echoIndex - echoRight + _echoRight.Length) % _echoRight.Length];
                var softening = spacious ? 0.5f : 0.35f;
                var feedback = spacious ? 0.55f : 0.38f;
                var wet = spacious ? 0.6f : 0.42f;
                _echoLowLeft += (tapLeft - _echoLowLeft) * softening;
                _echoLowRight += (tapRight - _echoLowRight) * softening;
                _echoLeft[_echoIndex] = send + _echoLowRight * feedback;
                _echoRight[_echoIndex] = send + _echoLowLeft * feedback;
                _echoIndex = (_echoIndex + 1) % _echoLeft.Length;
                var sampleLeft = MathF.Tanh((dryLeft + _echoLowLeft * wet) * 1.1f) * volume;
                var sampleRight = MathF.Tanh((dryRight + _echoLowRight * wet) * 1.1f) * volume;
                left[i] += sampleLeft;
                right[i] += sampleRight;
                _frame++;
            }
        }

        private void StartBar(Style style, int[] scale)
        {
            _bar++;
            Tension += (TargetTension - Tension) * 0.5f;
            Array.Copy(_barNotes, _lastBarNotes, _barNotes.Length);
            Array.Fill(_barNotes, 0);
            if (_bar % 2 == 0)
                _pattern = Tension > 0.5f && _random.NextDouble() < 0.4 ? 2 + _random.Next(2) : _random.Next(Figures.Length);
            // The pad holds the bar's chord, a little below the melody (with the seventh for funk).
            var degree = style.Progression[_bar % style.Progression.Length];
            var chord = style.Composer == Composer.Funk ? SeventhChord(style.Root, scale, degree) : ChordTones(style.Root, scale, degree);
            for (var n = 0; n < _padFreq.Length; n++)
                _padFreq[n] = n < 3 || style.Composer == Composer.Funk ? Midi(chord[n] - 12) : 0f;
            _padEnv = 0.25f;
        }

        private void PlayLead(Style style, int[] chord, int[] scale, int step, int stepsPerBar)
        {
            if (style.Composer is Composer.Funk or Composer.SynthPop)
            {
                // Horn section (or synth hook): the riff calls on odd bars and answers lower on even bars (only once
                // things heat up).
                var call = _bar % 2 == 1;
                if (!call && Tension < 0.35f)
                    return;
                var riffs = FunkKits[style.Kit].Riffs;
                var riff = riffs[_pattern % riffs.Length][step % 16];
                if (riff < 0)
                    return;
                var pentatonic = scale[2] == 3 ? MinorPentatonic : MajorPentatonic;
                var degree = Math.Max(0, call ? riff : riff - 2);
                _leadFreq = Midi(style.Root + 12 + pentatonic[degree % 5] + 12 * (degree / 5));
                _leadEnv = 1f;
                _leadBend = FunkKits[style.Kit].Scoop;
                return;
            }
            if (style.Composer == Composer.Tchaikovsky)
            {
                // Lyrical line: a note per beat (two beats when calm), stepping through the scale toward a chord
                // tone on each downbeat, with an occasional leap up for a sigh.
                var beatSteps = Tension > 0.4f ? 4 : 8;
                if (step % beatSteps != 0)
                    return;
                if (_melodyNote == 0)
                    _melodyNote = chord[3];
                var target = step == 0 ? chord[3 + _random.Next(3)] : _melodyNote + (_random.NextDouble() < 0.5 ? 2 : -2);
                if (step != 0 && _random.NextDouble() < 0.15)
                    target = _melodyNote + 5;
                _melodyNote = Snap(Math.Clamp(target, chord[0] + 7, chord[5] + 7), style.Root, scale);
                _leadFreq = Midi(_melodyNote);
                _leadEnv = 1f;
                return;
            }
            var dense = Tension > 0.25f || style.Composer == Composer.Bach || Theme is "boss" or "canopy" or "core" or "molten";
            if (!dense && step % 2 == 1)
                return;
            var index = Figures[_pattern][step % 16];
            if (index >= 0)
            {
                var note = chord[index] + 12;
                _leadFreq = Midi(note);
                _leadEnv = 1f;
                if (step < _barNotes.Length)
                    _barNotes[step] = note;
            }
            // Bach: the second voice answers with last bar's line an octave down, on the off-sixteenths.
            if (style.Composer == Composer.Bach && step < _lastBarNotes.Length && _lastBarNotes[step] != 0 && step % 2 == 1)
            {
                _canonFreq = Midi(_lastBarNotes[step] - 12);
                _canonEnv = 1f;
            }
        }

        private void PlayAccompaniment(Style style, int[] chord, int[] scale, int degree, int step)
        {
            switch (style.Composer)
            {
                case Composer.Funk or Composer.SynthPop:
                {
                    // Slap bass and clav from the biome's kit, the line and pattern changing along with the riff.
                    var kit = FunkKits[style.Kit];
                    var line = kit.Bass[_pattern % kit.Bass.Length];
                    var cell = line[step % 16];
                    if (cell == Ghost)
                        Slap(Midi(chord[0] - 24), 0.45f, 0.9982f, 0.15f);
                    else if (cell != Rest)
                    {
                        var note = Snap(chord[0] - 24 + cell, style.Root, scale);
                        if (cell >= 12)
                            Slap(Midi(note), 1f, 0.9998f, 1f);
                        else
                            Slap(Midi(note), 1f, 0.99984f, 0.65f);
                    }
                    if (style.Composer == Composer.SynthPop)
                    {
                        // The arpeggiator runs sixteenths through the chord an octave up.
                        _arpFreq = Midi(chord[ArpOrder[step % 16]] + 12);
                        _arpEnv = step % 4 == 0 ? 1f : 0.75f;
                        break;
                    }
                    var scratch = kit.Scratch[(_pattern / 2) % kit.Scratch.Length];
                    var velocity = scratch[step % 16];
                    if (velocity > 0f && (Tension > 0.15f || step % 4 != 3))
                    {
                        var seventh = SeventhChord(style.Root, scale, degree);
                        for (var n = 0; n < _clavFreq.Length; n++)
                            _clavFreq[n] = Midi(seventh[n]);
                        _clavEnv = velocity;
                    }
                    break;
                }
                case Composer.Vivaldi when style.Feel == Feel.Rock:
                {
                    // Rock: power-chord chugs (galloping when tense) and a picked bass hammering the root in eighths.
                    var hit = (Tension > 0.55f ? Gallop : Chug)[step % 16];
                    if (hit > 0f)
                    {
                        var open = hit >= 1f;
                        _guitarFreq[0] = Midi(chord[0] - 12);
                        _guitarFreq[1] = Midi(chord[0] - 5);
                        _guitarFreq[2] = Midi(chord[0]);
                        _guitarEnv = open ? 1f : 0.7f;
                        _guitarDecay = open ? 0.99993f : 0.9992f;
                        _guitarTone = open ? 2800f : 1000f;
                    }
                    if (step % 2 == 0)
                    {
                        _bassFreq = Midi(chord[0] - 24);
                        _bassEnv = 1f;
                    }
                    break;
                }
                case Composer.Tchaikovsky when style.Meter == 3:
                    // Waltz: bass on beat one, chord plucks on beats two and three.
                    if (step == 0)
                    {
                        _bassFreq = Midi(chord[0] - 24);
                        _bassEnv = 1f;
                    }
                    else if (step % 4 == 0)
                        for (var n = 0; n < 3; n++)
                            Pluck(Midi(chord[n] - 12));
                    break;
                case Composer.Tchaikovsky:
                    // Harp arpeggio rising through the chord on the eighths; bass on beats one and three.
                    if (step % 2 == 0)
                        Pluck(Midi(chord[(step / 2) % 6] - 12));
                    if (step % 8 == 0)
                    {
                        _bassFreq = Midi(chord[0] - 24);
                        _bassEnv = 1f;
                    }
                    break;
                case Composer.Bach:
                    // Walking bass in eighths: root, third, fifth, then a passing step.
                    if (step % 2 == 0)
                    {
                        var walk = (step / 2 % 4) switch { 0 => chord[0], 1 => chord[1], 2 => chord[2], _ => chord[2] + 2 };
                        _bassFreq = Midi(walk - 24);
                        _bassEnv = 1f;
                    }
                    if (step % 4 == 2)
                        Pluck(Midi(chord[1] - 12));
                    break;
                default:
                    // Vivaldi: harpsichord continuo on the eighths, bass walking root-fifth-octave on the beats.
                    if (step % 2 == 0)
                        Pluck(Midi(chord[(step / 2) % 3] - 12));
                    if (step % 4 == 0)
                    {
                        var walk = (step / 4) switch { 0 => 0, 1 => 7, 2 => 12, _ => 7 };
                        _bassFreq = Midi(chord[0] - 24 + walk);
                        _bassEnv = 1f;
                    }
                    break;
            }
        }

        private void PlayDrums(Groove groove, int step)
        {
            var tense = Tension > 0.55f;
            void Kick(float pitch = 85f)
            {
                _kickEnv = 1f;
                _kickPhase = 0f;
                _kickPitch = pitch;
            }
            // Closed hats chop off a ringing open hat; every hit wobbles a little in level so the hats breathe.
            void Hat(float level)
            {
                _hatEnv = level * (0.85f + (float)_random.NextDouble() * 0.3f);
                _openHatEnv *= 0.15f;
            }
            void OpenHat(float level, float decay = 0.99975f)
            {
                _openHatEnv = level;
                _openHatDecay = decay;
            }
            void Clap(float level)
            {
                _clapEnv = level;
                _clapAge = 0;
            }

            // Crash at the top of every eighth bar, and a snare-and-tom fill to close every fourth.
            var driving = groove is Groove.Four or Groove.Break or Groove.Funk or Groove.Disco or Groove.Rock or Groove.Pop || tense && groove == Groove.Half;
            if (driving && step == 0 && _bar % 8 == 0)
                OpenHat(0.9f, 0.99993f);
            if (driving && _bar % 4 == 3 && step >= 12)
            {
                _snareEnv = 0.45f + (step - 12) * 0.18f;
                if (step is 13 or 15)
                    Kick(step == 13 ? 210f : 150f);
                if (step == 15)
                    Clap(0.8f);
                return;
            }

            switch (groove)
            {
                case Groove.Four:
                    if (step % 4 == 0) Kick();
                    if (step % 8 == 4) _snareEnv = 1f;
                    if (step % 4 == 2) OpenHat(0.55f);
                    else if (step % 2 == 0) Hat(0.5f);
                    break;
                case Groove.Disco:
                    // Four on the floor, open hats on every off-beat, clap stacked on the backbeat, sixteenth hats when hot.
                    if (step % 4 == 0) Kick(90f);
                    if (step is 4 or 12)
                    {
                        _snareEnv = 0.7f;
                        Clap(1f);
                    }
                    if (step % 4 == 2) OpenHat(0.7f);
                    else if (step % 4 == 0 || Tension > 0.3f) Hat(step % 2 == 0 ? 0.5f : 0.3f);
                    break;
                case Groove.Pop:
                    // Eighties drum machine: kick on the one and around the middle, a gated snare and clap on the
                    // backbeat, steady eighth hats.
                    if (step is 0 or 6 or 8 || tense && step == 14) Kick(70f);
                    if (step is 4 or 12)
                    {
                        _snareEnv = 1f;
                        _snareGate = SampleRate / 7;
                        Clap(0.8f);
                    }
                    if (step % 2 == 0) Hat(step % 4 == 2 ? 0.6f : 0.4f);
                    break;
                case Groove.Rock:
                    // Driving rock: kick on the one and around the middle, a cracking backbeat, the ride washing on
                    // every eighth, and double-kick sixteenths at full tension.
                    if (step is 0 or 6 or 8 || tense && step == 10 || Tension > 0.8f) Kick(75f);
                    if (step is 4 or 12)
                    {
                        _snareEnv = 1f;
                        Clap(0.35f);
                    }
                    if (step % 2 == 0) OpenHat(0.4f, 0.9996f);
                    break;
                case Groove.Break:
                    if (step is 0 or 10 || tense && step == 6) Kick();
                    if (step is 4 or 12) _snareEnv = 1f;
                    else if (step is 7 or 15) _snareEnv = 0.35f;
                    if (step == 14 && _bar % 2 == 1) OpenHat(0.5f);
                    else Hat(step % 2 == 0 ? 0.6f : 0.35f);
                    break;
                case Groove.Half:
                    if (step == 0 || tense && step == 10) Kick();
                    if (step == 8)
                    {
                        _snareEnv = 1f;
                        Clap(0.6f);
                    }
                    if (step % 2 == 0) Hat(0.45f);
                    else if (step is 7 or 15) Hat(0.25f);
                    break;
                case Groove.Tribal:
                    if (step is 0 or 3 or 6) Kick(110f);
                    if (step is 10 or 12) Kick(140f);
                    if (step == 8 && tense) _snareEnv = 0.8f;
                    _hatEnv = 0.3f;
                    break;
                case Groove.Waltz:
                    if (step == 0) Kick(70f);
                    if (step is 4 or 8) _snareEnv = 0.4f;
                    if (step % 2 == 0) _hatEnv = 0.35f;
                    break;
                case Groove.Funk:
                    // Syncopated funk: kick on the one and the "and" of three, snare and clap on two and four with
                    // ghost notes, accented hats, and an open hat lifting into the next bar.
                    if (step is 0 or 6 or 10 || tense && step == 14) Kick(step == 10 ? 95f : 80f);
                    if (step is 4 or 12)
                    {
                        _snareEnv = 1f;
                        Clap(0.7f);
                    }
                    else if (step is 7 or 15 || !tense && step == 9) _snareEnv = 0.28f;
                    if (step == 14) OpenHat(0.6f);
                    else if (step != 15) Hat(step % 2 == 0 ? (step % 4 == 2 ? 0.7f : 0.45f) : 0.3f);
                    break;
            }
        }

        private float Drums(Groove groove)
        {
            var beat = 0f;
            if (_kickEnv > 0.001f)
            {
                var sweep = _kickPitch * 0.53f + _kickPitch * _kickEnv * _kickEnv;
                _kickPhase = Wrap(_kickPhase + sweep / SampleRate);
                beat += MathF.Sin(_kickPhase * MathF.Tau) * _kickEnv * 0.5f;
                _kickEnv *= groove == Groove.Tribal ? 0.9992f : 0.9994f;
                _pump = 0.5f + 0.5f * (1f - _kickEnv);
            }
            else
                _pump = MathF.Min(1f, _pump + 0.0005f);
            if (_snareEnv > 0.001f)
            {
                beat += ((float)_random.NextDouble() * 2f - 1f) * _snareEnv * 0.2f + MathF.Sin(_frame * 190f * MathF.Tau / SampleRate) * _snareEnv * 0.06f;
                _snareEnv *= _snareGate > 0 ? 0.99995f : 0.99955f;
                if (_snareGate > 0 && --_snareGate == 0)
                    _snareEnv = 0f;
            }
            if (_hatEnv > 0.001f)
            {
                var noise = (float)_random.NextDouble() * 2f - 1f;
                beat += (noise - _hatPrev) * _hatEnv * 0.065f;
                _hatPrev = noise;
                _hatEnv *= groove == Groove.Tribal ? 0.9993f : 0.9985f;
            }
            if (_openHatEnv > 0.001f)
            {
                var noise = (float)_random.NextDouble() * 2f - 1f;
                beat += (noise - _openHatPrev) * _openHatEnv * 0.05f;
                _openHatPrev = noise;
                _openHatEnv *= _openHatDecay;
            }
            if (_clapEnv > 0.001f)
            {
                // Three quick slaps ~10 ms apart, then the room tail, all band-passed around 1.2 kHz.
                var slap = _clapAge < 1320 ? 1f - _clapAge % 440 / 440f : 1f;
                var noise = (float)_random.NextDouble() * 2f - 1f;
                beat += _clapFilter.BandPass(noise, 1200f, 0.7f) * slap * _clapEnv * 0.3f;
                if (_clapAge >= 1320)
                    _clapEnv *= 0.9993f;
                _clapAge++;
            }
            return beat;
        }

        /// <summary>Airy biomes: now and then a bell rings a high chord tone on an off-beat, panned somewhere new.</summary>
        private void Chime(int[] chord, int step)
        {
            if (step % 4 != 2 || _random.NextDouble() > 0.18 + Tension * 0.1)
                return;
            _bellFreq = Midi(chord[3 + _random.Next(3)] + 12);
            _bellPhase = _bellOvertone = 0f;
            _bellEnv = 1f;
            _bellPan = (float)_random.NextDouble() * 1.2f - 0.6f;
        }

        /// <summary>Plucks the slap bass: level, amplitude decay per sample, and how far the filter snaps open.</summary>
        private void Slap(float freq, float level, float decay, float bite)
        {
            _slapFreq = freq;
            _slapEnv = level;
            _slapDecay = decay;
            _slapFilterEnv = bite;
        }

        private void Pluck(float freq)
        {
            _pluckFreq[_nextPluck] = freq;
            _pluckPhase[_nextPluck] = 0f;
            _pluckEnv[_nextPluck] = 1f;
            _nextPluck = (_nextPluck + 1) % _pluckFreq.Length;
        }

        /// <summary>Six chord tones (triad over two octaves) for a scale degree, as MIDI notes.</summary>
        private static int[] ChordTones(int root, int[] scale, int degree)
        {
            var tones = new int[6];
            for (var n = 0; n < 6; n++)
            {
                var step = degree + (n % 3) * 2;
                tones[n] = root + scale[step % scale.Length] + 12 * (step / scale.Length) + 12 * (n / 3);
            }
            return tones;
        }

        /// <summary>Four-note seventh chord (root, third, fifth, seventh) for a scale degree, as MIDI notes.</summary>
        private static int[] SeventhChord(int root, int[] scale, int degree)
        {
            var tones = new int[4];
            for (var n = 0; n < 4; n++)
            {
                var step = degree + n * 2;
                tones[n] = root + scale[step % scale.Length] + 12 * (step / scale.Length);
            }
            return tones;
        }

        /// <summary>Nearest note of the scale (keeps lyrical steps in key).</summary>
        private static int Snap(int note, int root, int[] scale)
        {
            for (var offset = 0; offset < 12; offset++)
                foreach (var candidate in new[] { note - offset, note + offset })
                    if (scale.Contains(((candidate - root) % 12 + 12) % 12))
                        return candidate;
            return note;
        }

        /// <summary>Bowed-string tone: a band-limited sawtooth (first six harmonics); brightness tilts the upper ones.</summary>
        private static float Strings(float phase, float brightness)
        {
            var p = phase * MathF.Tau;
            return MathF.Sin(p) + 0.5f * MathF.Sin(2 * p) + 0.33f * brightness * MathF.Sin(3 * p)
                   + 0.25f * brightness * MathF.Sin(4 * p) + 0.2f * brightness * MathF.Sin(5 * p) + 0.12f * brightness * MathF.Sin(6 * p);
        }

        private static float Wrap(float phase) => phase - MathF.Floor(phase);

        private static float Midi(int note) => 440f * MathF.Pow(2f, (note - 69) / 12f);

        /// <summary>Chamberlin state-variable filter; damping runs from 0 (ringing) to 2 (dull).</summary>
        private struct Svf
        {
            private float _low, _band;

            public float LowPass(float input, float cutoff, float damping)
            {
                Step(input, cutoff, damping);
                return _low;
            }

            public float BandPass(float input, float cutoff, float damping)
            {
                Step(input, cutoff, damping);
                return _band;
            }

            private void Step(float input, float cutoff, float damping)
            {
                var f = 2f * MathF.Sin(MathF.PI * MathF.Min(cutoff, SampleRate / 6f) / SampleRate);
                _low += f * _band;
                _band += f * (input - _low - damping * _band);
            }
        }
    }

    /// <summary>
    /// `--music-preview [folder]`: renders 20 s of each music theme (calm, then tense for the second half) to
    /// 16-bit stereo WAV files so the score can be auditioned without playing. Returns the peak level per file.
    /// </summary>
    public static int RenderMusicPreviews(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { "title", "grassland", "canopy", "frozen", "junk", "forest", "overgrowth", "fungal", "crystal", "ocean", "underground", "radiated", "core", "boss" })
        {
            var music = new MusicState { Theme = theme };
            var seconds = 20;
            var left = new float[SampleRate * seconds];
            var right = new float[SampleRate * seconds];
            const int block = 2048;
            for (var start = 0; start < left.Length; start += block)
            {
                music.TargetTension = start < left.Length / 2 ? 0.1f : 0.8f;
                var count = Math.Min(block, left.Length - start);
                var l = new float[count];
                var r = new float[count];
                music.Render(l, r, 0.9f);
                Array.Copy(l, 0, left, start, count);
                Array.Copy(r, 0, right, start, count);
            }
            var path = Path.Combine(folder, $"music_{theme}.wav");
            using var writer = new BinaryWriter(File.Create(path));
            var dataBytes = left.Length * 4;
            writer.Write("RIFF"u8); writer.Write(36 + dataBytes); writer.Write("WAVE"u8);
            writer.Write("fmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(SampleRate);
            writer.Write(SampleRate * 4); writer.Write((short)4); writer.Write((short)16);
            writer.Write("data"u8); writer.Write(dataBytes);
            var peak = 0f;
            double sum = 0;
            for (var i = 0; i < left.Length; i++)
            {
                peak = MathF.Max(peak, MathF.Max(MathF.Abs(left[i]), MathF.Abs(right[i])));
                sum += left[i] * left[i];
                writer.Write((short)(Math.Clamp(left[i], -1f, 1f) * 32000));
                writer.Write((short)(Math.Clamp(right[i], -1f, 1f) * 32000));
            }
            Console.WriteLine($"{theme,-12} peak {peak:0.00}  rms {Math.Sqrt(sum / left.Length):0.000}  -> {path}");
        }
        return 0;
    }

    // ---------------------------------------------------------------- waveOut interop

    private const int WaveMapper = -1;
    private const int WhdrDone = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormat
    {
        public short FormatTag;
        public short Channels;
        public int SamplesPerSec;
        public int AvgBytesPerSec;
        public short BlockAlign;
        public short BitsPerSample;
        public short Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public int BufferLength;
        public int BytesRecorded;
        public IntPtr User;
        public int Flags;
        public int Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }

    [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr device, int deviceId, ref WaveFormat format, IntPtr callback, IntPtr instance, int flags);
    [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr device, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr device, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr device, IntPtr header, int size);
    [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr device);
    [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr device);

    public void Dispose()
    {
        if (!Available)
            return;
        _running = false;
        _thread?.Join(500);
        _musicDeck.Stop();
        _bedDeck.Stop();
        _weatherDeck.Stop();
        foreach (var deck in _spotDecks.Values)
            deck.Stop();
        waveOutReset(_device);
        var headerSize = Marshal.SizeOf<WaveHeader>();
        for (var i = 0; i < BufferCount; i++)
        {
            waveOutUnprepareHeader(_device, _headers[i], headerSize);
            Marshal.FreeHGlobal(_headers[i]);
            Marshal.FreeHGlobal(_buffers[i]);
        }
        waveOutClose(_device);
    }
}
