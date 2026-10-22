using System.Runtime.InteropServices;

namespace Rimtera.UI;

/// <summary>
/// Dependency-free audio: a software mixer streaming 16-bit stereo PCM to the Windows waveOut API.
/// Sound effects are synthesised once at startup (no audio files), music is generated live as
/// slow chord pads with a sparse melody whose key and mood follow the biome and time of day, and
/// rain plays as a filtered-noise ambience loop. If no audio device is available everything is a
/// silent no-op.
/// </summary>
internal sealed class AudioEngine : IDisposable
{
    public const int SampleRate = 44100;
    private const int BufferFrames = 2048;
    private const int BufferCount = 4;

    private readonly object _gate = new();
    private readonly Dictionary<string, float[]> _effects = new();
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

    public void Play(string name, float volume = 1f, float pan = 0f)
    {
        if (!Available || !_effects.TryGetValue(name, out var samples))
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
            _music.Render(_left, _right, MusicVolume * 0.35f);
        }

        // Rain: brown-ish noise, louder with intensity.
        var rain = _rainLevel * AmbientVolume * 0.25f;
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
    }

    // ---------------------------------------------------------------- generative music

    /// <summary>
    /// Generative score in three original style voices, chosen per biome, over a modern groove and an ambient bed:
    /// <list type="bullet">
    /// <item>Vivaldi: driving sixteenth-note string figuration, harpsichord continuo, walking root-fifth bass.</item>
    /// <item>Bach: the same motor figuration answered a bar later by an imitating voice an octave down, over a
    ///   walking eighth-note bass (counterpoint).</item>
    /// <item>Tchaikovsky: a lyrical stepwise melody with wide vibrato over lush strings and harp, often a 3/4 waltz.</item>
    /// </list>
    /// Each biome has its own key, tempo, harmony, meter and drum groove (four-on-the-floor, breakbeat, half-time,
    /// tribal toms, waltz, syncopated funk). A sustained pad and a stereo echo add ambience; drums and density grow with tension.
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

        private enum Composer : byte { Vivaldi, Bach, Tchaikovsky }

        private enum Groove : byte { Four, Break, Half, Tribal, Waltz, Funk }

        private readonly record struct Style(int Root, int Bpm, int[] Progression, bool Minor, float Brightness,
            Composer Composer, int Meter, Groove Groove, float Pad);

        // Lead strings (two detuned voices), the imitating second voice (Bach), harpsichord/harp plucks, bass, pad.
        private float _leadFreq, _leadPhaseA, _leadPhaseB, _leadEnv, _vibrato;
        private float _canonFreq, _canonPhase, _canonEnv;
        private readonly int[] _barNotes = new int[16], _lastBarNotes = new int[16];
        private int _melodyNote;
        private readonly float[] _pluckFreq = new float[4], _pluckPhase = new float[4], _pluckEnv = new float[4];
        private int _nextPluck;
        private float _bassFreq, _bassPhase, _bassEnv;
        private readonly float[] _padFreq = new float[3], _padPhase = new float[3];
        private float _padEnv;
        // Drums: kick/tom sweep, clap/snare noise, hat and shaker noise; sidechain pump follows the kick.
        private float _kickEnv, _kickPhase, _kickPitch = 85f, _snareEnv, _hatEnv, _pump = 1f, _hatPrev;
        // Ambience: a stereo feedback echo with a soft low-pass in the loop.
        private readonly float[] _echoLeft = new float[SampleRate], _echoRight = new float[SampleRate];
        private int _echoIndex;
        private float _echoLowLeft, _echoLowRight;

        private static readonly int[] Major = [0, 2, 4, 5, 7, 9, 11];
        private static readonly int[] HarmonicMinor = [0, 2, 3, 5, 7, 8, 11];

        // Harmony: a circle-of-fifths sequence, a bright cadence loop, a driving minor sequence, and a romantic
        // loop with a lift to the relative minor.
        private static readonly int[] FifthsCircle = [0, 3, 6, 2, 5, 1, 4, 0];
        private static readonly int[] BrightLoop = [0, 4, 5, 3, 0, 3, 4, 4];
        private static readonly int[] MinorDrive = [0, 5, 3, 4, 0, 3, 4, 4];
        private static readonly int[] Romantic = [0, 5, 3, 4, 0, 2, 3, 4];

        // Sixteenth-note figurations over six chord tones; -1 = rest.
        private static readonly int[][] Figures =
        [
            [0, 1, 2, 3, 2, 1, 0, 1, 0, 1, 2, 3, 4, 3, 2, 1],
            [0, 2, 1, 3, 2, 4, 3, 5, 4, 2, 3, 1, 2, 0, 1, 0],
            [3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 4, 4, 3, 3],
            [5, 4, 3, 4, 5, 4, 3, 2, 3, 2, 1, 2, 3, 2, 1, 0],
            [0, -1, 2, -1, 4, -1, 2, -1, 1, -1, 3, -1, 5, -1, 3, -1],
        ];

        private Style CurrentStyle() => Theme switch
        {
            "title" => new(55, 92, Romantic, false, 0.8f, Composer.Tchaikovsky, 3, Groove.Waltz, 0.9f),
            "grassland" => new(57, 124, BrightLoop, false, 1f, Composer.Vivaldi, 4, Groove.Four, 0.45f),
            "canopy" => new(59, 128, FifthsCircle, false, 1f, Composer.Vivaldi, 4, Groove.Tribal, 0.55f),
            "coast" or "ocean" => new(60, 104, Romantic, false, 0.85f, Composer.Tchaikovsky, 3, Groove.Waltz, 0.85f),
            "frozen" => new(62, 112, FifthsCircle, false, 1.1f, Composer.Bach, 4, Groove.Half, 0.7f),
            "crystal" => new(64, 120, FifthsCircle, false, 1.15f, Composer.Bach, 4, Groove.Break, 0.6f),
            "fungal" => new(58, 96, Romantic, false, 0.85f, Composer.Tchaikovsky, 3, Groove.Waltz, 0.9f),
            "junk" => new(57, 132, BrightLoop, false, 1f, Composer.Vivaldi, 4, Groove.Break, 0.4f),
            "forest" => new(49, 115, BrightLoop, false, 0.9f, Composer.Vivaldi, 4, Groove.Funk, 0.5f),
            "radiated" => new(62, 124, MinorDrive, true, 1f, Composer.Bach, 4, Groove.Break, 0.6f),
            "underground" => new(55, 110, FifthsCircle, false, 0.85f, Composer.Bach, 4, Groove.Half, 0.75f),
            "molten" or "core" => new(52, 136, MinorDrive, true, 1f, Composer.Vivaldi, 4, Groove.Four, 0.6f),
            "boss" => new(52, 150, MinorDrive, true, 1.1f, Composer.Vivaldi, 4, Groove.Break, 0.5f),
            _ => new(57, 124, BrightLoop, false, 1f, Composer.Vivaldi, 4, Groove.Four, 0.5f)
        };

        public void Render(float[] left, float[] right, float volume)
        {
            if (volume <= 0.001f)
                return;
            var style = CurrentStyle();
            var bpm = Night && Theme != "boss" ? (int)(style.Bpm * 0.9f) : style.Bpm;
            var scale = style.Minor ? HarmonicMinor : Major;
            var stepsPerBar = style.Meter * 4;
            var sixteenth = Math.Max(1, (long)(SampleRate * 60.0 / bpm / 4));
            var drums = Theme != "title" || Tension > 0.2f;
            var lyrical = style.Composer == Composer.Tchaikovsky;
            var echoLeft = (int)Math.Min(_echoLeft.Length - 1, sixteenth * 3);
            var echoRight = (int)Math.Min(_echoRight.Length - 1, sixteenth * 4);
            for (var i = 0; i < left.Length; i++)
            {
                var step = (int)(_frame / sixteenth % stepsPerBar);
                if (_frame % sixteenth == 0)
                {
                    if (step == 0)
                        StartBar(style, scale);
                    var chord = ChordTones(style.Root, scale, style.Progression[_bar % style.Progression.Length]);
                    PlayLead(style, chord, scale, step, stepsPerBar);
                    PlayAccompaniment(style, chord, step);
                    if (drums)
                        PlayDrums(style.Groove, step);
                }

                // Lead strings with vibrato (wider and slower for the lyrical style), detached bowing on figuration.
                _vibrato += (lyrical ? 5f : 5.5f) / SampleRate;
                var vib = 1f + (lyrical ? 0.006f : 0.004f) * MathF.Sin(_vibrato * MathF.Tau);
                _leadPhaseA = Wrap(_leadPhaseA + _leadFreq * vib / SampleRate);
                _leadPhaseB = Wrap(_leadPhaseB + _leadFreq * vib * 1.004f / SampleRate);
                var lead = (Strings(_leadPhaseA, style.Brightness) + Strings(_leadPhaseB, style.Brightness)) * 0.5f;
                _leadEnv *= lyrical ? 0.99997f : 0.99985f;
                var stepPosition = (_frame % sixteenth) / (float)sixteenth;
                var articulation = !lyrical && stepPosition > 0.8f ? 1f - (stepPosition - 0.8f) / 0.2f * 0.85f : 1f;
                lead *= MathF.Min(1f, _leadEnv * 1.4f) * articulation * 0.15f;

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

                // Bass.
                _bassPhase = Wrap(_bassPhase + _bassFreq / SampleRate);
                _bassEnv *= 0.99992f;
                var bass = (MathF.Sin(_bassPhase * MathF.Tau) + 0.25f * MathF.Sin(2 * _bassPhase * MathF.Tau)) * _bassEnv * 0.2f;

                // Pad: sustained chord swelling in each bar (the ambience bed).
                _padEnv = MathF.Min(1f, _padEnv + 1f / (SampleRate * 0.6f));
                var pad = 0f;
                for (var n = 0; n < 3; n++)
                {
                    _padPhase[n] = Wrap(_padPhase[n] + _padFreq[n] / SampleRate);
                    pad += 1f - 4f * MathF.Abs(_padPhase[n] - 0.5f);
                }
                pad *= _padEnv * style.Pad * 0.05f;

                var beat = Drums(style.Groove) * (drums ? 0.8f + Tension * 0.35f : 0f);

                var dry = (lead + canon + plucks) * _pump + bass * _pump + pad + beat;
                // Ambience: a stereo echo (3/16 left, 1/4 right) fed by the melodic voices, softened each repeat.
                var send = (lead + canon + plucks + pad) * 0.5f;
                var tapLeft = _echoLeft[(_echoIndex - echoLeft + _echoLeft.Length) % _echoLeft.Length];
                var tapRight = _echoRight[(_echoIndex - echoRight + _echoRight.Length) % _echoRight.Length];
                _echoLowLeft += (tapLeft - _echoLowLeft) * 0.35f;
                _echoLowRight += (tapRight - _echoLowRight) * 0.35f;
                _echoLeft[_echoIndex] = send + _echoLowRight * 0.38f;
                _echoRight[_echoIndex] = send + _echoLowLeft * 0.38f;
                _echoIndex = (_echoIndex + 1) % _echoLeft.Length;
                var sampleLeft = MathF.Tanh((dry + _echoLowLeft * 0.42f) * 1.1f) * volume;
                var sampleRight = MathF.Tanh((dry + _echoLowRight * 0.42f) * 1.1f) * volume;
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
            // The pad holds the bar's chord, a little below the melody.
            var chord = ChordTones(style.Root, scale, style.Progression[_bar % style.Progression.Length]);
            for (var n = 0; n < 3; n++)
                _padFreq[n] = Midi(chord[n] - 12);
            _padEnv = 0.25f;
        }

        private void PlayLead(Style style, int[] chord, int[] scale, int step, int stepsPerBar)
        {
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
            var dense = Tension > 0.25f || style.Composer == Composer.Bach || Theme is "boss" or "grassland" or "canopy" or "junk" or "forest" or "core" or "molten";
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

        private void PlayAccompaniment(Style style, int[] chord, int step)
        {
            switch (style.Composer)
            {
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
            switch (groove)
            {
                case Groove.Four:
                    if (step % 4 == 0) Kick();
                    if (step % 8 == 4) _snareEnv = 1f;
                    if (step % 2 == 0) _hatEnv = step % 4 == 2 ? 0.9f : 0.5f;
                    break;
                case Groove.Break:
                    if (step is 0 or 10 || tense && step == 6) Kick();
                    if (step is 4 or 12) _snareEnv = 1f;
                    else if (step is 7 or 15) _snareEnv = 0.35f;
                    _hatEnv = step % 2 == 0 ? 0.6f : 0.35f;
                    break;
                case Groove.Half:
                    if (step == 0 || tense && step == 10) Kick();
                    if (step == 8) _snareEnv = 1f;
                    if (step % 2 == 0) _hatEnv = 0.45f;
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
                    // Syncopated funk: kick on the one and the "and" of three, snares on two and four
                    // with ghost notes, and accented off-beat hats for the shuffle.
                    if (step is 0 or 6 or 10 || tense && step == 14) Kick(step == 10 ? 95f : 80f);
                    if (step is 4 or 12) _snareEnv = 1f;
                    else if (step is 7 or 15 || !tense && step == 9) _snareEnv = 0.28f;
                    _hatEnv = step % 2 == 0 ? (step % 4 == 2 ? 0.7f : 0.45f) : 0.3f;
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
                _snareEnv *= 0.99955f;
            }
            if (_hatEnv > 0.001f)
            {
                var noise = (float)_random.NextDouble() * 2f - 1f;
                beat += (noise - _hatPrev) * _hatEnv * 0.065f;
                _hatPrev = noise;
                _hatEnv *= groove == Groove.Tribal ? 0.9993f : 0.9985f;
            }
            return beat;
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
    }

    /// <summary>
    /// `--music-preview [folder]`: renders 20 s of each music theme (calm, then tense for the second half) to
    /// 16-bit stereo WAV files so the score can be auditioned without playing. Returns the peak level per file.
    /// </summary>
    public static int RenderMusicPreviews(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var theme in new[] { "title", "grassland", "canopy", "frozen", "junk", "fungal", "crystal", "ocean", "underground", "radiated", "core", "boss" })
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
