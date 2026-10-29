using NVorbis;

namespace Rimtera.UI;

/// <summary>A looping source of interleaved float samples: an OGG file on disk, or a test signal.</summary>
internal interface IAudioStream : IDisposable
{
    int Channels { get; }
    int SampleRate { get; }

    /// <summary>Reads up to <paramref name="count"/> interleaved samples; 0 at the end of the stream.</summary>
    int Read(float[] buffer, int offset, int count);

    void Rewind();
}

/// <summary>An OGG Vorbis file streamed from disk with NVorbis (MIT): music tracks and ambience loops are never held whole.</summary>
internal sealed class VorbisFileStream : IAudioStream
{
    private readonly VorbisReader _reader;

    public VorbisFileStream(string path) => _reader = new VorbisReader(path);

    public int Channels => _reader.Channels;
    public int SampleRate => _reader.SampleRate;
    public int Read(float[] buffer, int offset, int count) => _reader.ReadSamples(buffer, offset, count - count % Channels);
    public void Rewind() => _reader.SeekTo(0);
    public void Dispose() => _reader.Dispose();
}

/// <summary>
/// One playing loop: reads its stream, loops it at the end, resamples to the mixer rate (linear), turns mono into
/// stereo, and fades its gain toward a target so tracks crossfade instead of cutting.
/// </summary>
internal sealed class StreamedTrack : IDisposable
{
    private readonly IAudioStream _stream;
    private readonly float[] _source = new float[4096];
    private readonly double _step;
    private int _sourceCount, _sourceIndex;
    private double _phase = 1.0;
    private float _prevLeft, _prevRight, _nextLeft, _nextRight;
    private bool _ended;

    public StreamedTrack(string name, IAudioStream stream, int outputRate, float fadeSeconds)
    {
        Name = name;
        _stream = stream;
        _step = stream.SampleRate / (double)outputRate;
        FadePerFrame = 1f / Math.Max(1f, fadeSeconds * outputRate);
    }

    public string Name { get; }
    public float Gain { get; set; }
    public float Target { get; set; } = 1f;
    public float Volume { get; set; } = 1f;
    public float Pan { get; set; }
    public float FadePerFrame { get; set; }

    /// <summary>Faded out (or ended) and safe to drop.</summary>
    public bool Finished => _ended || Target <= 0f && Gain <= 0f;

    public void Render(float[] left, float[] right, int frames, float bus)
    {
        var leftPan = Math.Min(1f, 1f - Pan);
        var rightPan = Math.Min(1f, 1f + Pan);
        for (var i = 0; i < frames && !_ended; i++)
        {
            Gain = Gain < Target ? Math.Min(Target, Gain + FadePerFrame) : Math.Max(Target, Gain - FadePerFrame);
            _phase += _step;
            while (_phase >= 1.0)
            {
                _phase -= 1.0;
                (_prevLeft, _prevRight) = (_nextLeft, _nextRight);
                if (!NextFrame(out _nextLeft, out _nextRight))
                {
                    _ended = true;
                    break;
                }
            }
            var t = (float)_phase;
            var gain = Gain * Volume * bus;
            left[i] += (_prevLeft + (_nextLeft - _prevLeft) * t) * gain * leftPan;
            right[i] += (_prevRight + (_nextRight - _prevRight) * t) * gain * rightPan;
        }
    }

    private bool NextFrame(out float left, out float right)
    {
        left = right = 0f;
        var channels = Math.Max(1, _stream.Channels);
        if (_sourceIndex + channels > _sourceCount && !Refill(channels))
            return false;
        left = _source[_sourceIndex];
        right = channels > 1 ? _source[_sourceIndex + 1] : left;
        _sourceIndex += channels;
        return true;
    }

    private bool Refill(int channels)
    {
        var usable = _source.Length - _source.Length % channels;
        _sourceIndex = 0;
        _sourceCount = _stream.Read(_source, 0, usable);
        if (_sourceCount < channels)
        {
            _stream.Rewind(); // loops are cut to repeat seamlessly (tools/audio/audio_post.py)
            _sourceCount = _stream.Read(_source, 0, usable);
        }
        return _sourceCount >= channels;
    }

    public void Dispose() => _stream.Dispose();
}

/// <summary>
/// A crossfading player for one layer (music, the ambience bed, weather, or one spot loop). Asking for a new loop
/// opens it off the audio thread and fades it in while the old one fades out; asking for null fades to silence.
/// </summary>
internal sealed class StreamDeck
{
    private readonly object _gate = new();
    private readonly List<StreamedTrack> _tracks = new();
    private readonly Func<string, IAudioStream?> _open;
    private readonly int _rate;
    private readonly float _fadeSeconds;
    private string? _wanted;
    private int _request;

    public StreamDeck(Func<string, IAudioStream?> open, int rate, float fadeSeconds)
    {
        _open = open;
        _rate = rate;
        _fadeSeconds = fadeSeconds;
    }

    /// <summary>The loop this deck is playing or fading in, if any.</summary>
    public string? Current => _wanted;

    public float Volume { get; set; } = 1f;
    public float Pan { get; set; }

    public void Play(string? name)
    {
        if (string.Equals(name, _wanted, StringComparison.OrdinalIgnoreCase))
            return;
        _wanted = name;
        var request = Interlocked.Increment(ref _request);
        lock (_gate)
            foreach (var track in _tracks)
                track.Target = 0f;
        if (name is null)
            return;
        Task.Run(() =>
        {
            var stream = _open(name);
            if (stream is null)
                return;
            lock (_gate)
            {
                if (request != _request)
                {
                    stream.Dispose(); // superseded while opening
                    return;
                }
                _tracks.Add(new StreamedTrack(name, stream, _rate, _fadeSeconds));
            }
        });
    }

    public void Render(float[] left, float[] right, int frames, float bus)
    {
        lock (_gate)
        {
            for (var i = _tracks.Count - 1; i >= 0; i--)
            {
                var track = _tracks[i];
                track.Volume = Volume;
                track.Pan = Pan;
                track.Render(left, right, frames, bus);
                if (track.Finished)
                {
                    track.Dispose();
                    _tracks.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>True once the wanted loop is audible (its stream opened and it is fading in).</summary>
    public bool Sounding
    {
        get
        {
            lock (_gate)
                return _tracks.Any(track => track.Target > 0f && string.Equals(track.Name, _wanted, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            foreach (var track in _tracks)
                track.Dispose();
            _tracks.Clear();
        }
    }
}
