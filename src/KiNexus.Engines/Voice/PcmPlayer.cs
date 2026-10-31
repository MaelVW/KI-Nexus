using NAudio.Wave;

namespace KiNexus.Engines.Voice;

/// <summary>
/// Spielt die Sprachausgabe von Piper ab (PCM, 16 Bit, mono) über den Windows-Standard-Lautsprecher (NAudio).
/// Stücke werden angehängt und lückenlos nacheinander gespielt. Misst nebenbei die Lautstärke für den Orb.
/// </summary>
public sealed class PcmPlayer : IDisposable
{
    private readonly Lock _lock = new();
    private WaveOutEvent? _output;
    private BufferedWaveProvider? _buffer;
    private LevelMeter? _meter;

    /// <summary>Lautstärke dessen, was gerade aus dem Lautsprecher kommt, 0 bis 1.</summary>
    public double Level => _meter?.Level ?? 0;

    /// <summary>Wie viel Ton noch wartet (ohne die ca. 0.3 s, die schon bei Windows liegen).</summary>
    public TimeSpan Buffered => _buffer?.BufferedDuration ?? TimeSpan.Zero;

    /// <summary>Öffnet den Lautsprecher mit dieser Abtastrate (Piper: meist 22050 Hz).</summary>
    public void Open(int sampleRate)
    {
        lock (_lock)
        {
            if (_buffer?.WaveFormat.SampleRate == sampleRate && _output is not null)
            {
                return;
            }

            CloseCore();
            // Platz für 5 Minuten Ton. So lang wird keine Antwort.
            _buffer = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, 1), TimeSpan.FromMinutes(5))
            {
                DiscardOnBufferOverflow = false,
                ReadFully = true,
            };
            _meter = new LevelMeter(_buffer);
            _output = new WaveOutEvent { DesiredLatency = 150, NumberOfBuffers = 2 };
            _output.Init(_meter);
            _output.Play();
        }
    }

    public void Enqueue(byte[] pcm)
    {
        lock (_lock)
        {
            _buffer?.AddSamples(pcm, 0, pcm.Length);
        }
    }

    /// <summary>Sofort still: alles Wartende wegwerfen.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _buffer?.ClearBuffer();
        }
    }

    public void Close()
    {
        lock (_lock)
        {
            CloseCore();
        }
    }

    public void Dispose() => Close();

    private void CloseCore()
    {
        _output?.Stop();
        _output?.Dispose();
        _output = null;
        _buffer = null;
        _meter = null;
    }

    /// <summary>Reicht den Ton durch und misst dabei die Lautstärke (RMS).</summary>
    private sealed class LevelMeter(IWaveProvider source) : IWaveProvider
    {
        private double _level;

        public WaveFormat WaveFormat => source.WaveFormat;

        public double Level => Volatile.Read(ref _level);

        public int Read(byte[] buffer, int offset, int count)
        {
            var read = source.Read(buffer, offset, count);
            double sum = 0;
            var samples = read / 2;
            for (var i = 0; i < samples; i++)
            {
                double sample = BitConverter.ToInt16(buffer, offset + (i * 2)) / 32768.0;
                sum += sample * sample;
            }

            Volatile.Write(ref _level, samples == 0 ? 0 : Math.Min(1, Math.Sqrt(sum / samples) * 4));
            return read;
        }
    }
}
