using System.IO;
using NLayer;

namespace Content.Server._Misfits.Administration;

/// <summary>Decodes admin MP3 uploads on the server so sandboxed clients receive PCM WAV.</summary>
public static class AdminMp3Converter
{
    // Keep each playback event below the default 32 MiB network fragment limit.
    public const int MaxDecodedBytes = 24 * 1024 * 1024;

    public static byte[] DecodeToWav(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var mp3 = new MpegFile(input);
        if (mp3.Channels is not (1 or 2) || mp3.SampleRate <= 0)
            throw new InvalidDataException("The MP3 must have one or two channels.");

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write(new byte[] { (byte) 'R', (byte) 'I', (byte) 'F', (byte) 'F' });
        writer.Write(0);
        writer.Write(new byte[] { (byte) 'W', (byte) 'A', (byte) 'V', (byte) 'E', (byte) 'f', (byte) 'm', (byte) 't', (byte) ' ' });
        writer.Write(16);
        writer.Write((short) 1);
        writer.Write((short) mp3.Channels);
        writer.Write(mp3.SampleRate);
        writer.Write(mp3.SampleRate * mp3.Channels * 2);
        writer.Write((short) (mp3.Channels * 2));
        writer.Write((short) 16);
        writer.Write(new byte[] { (byte) 'd', (byte) 'a', (byte) 't', (byte) 'a' });
        writer.Write(0);

        var samples = new float[8192];
        int count;
        while ((count = mp3.ReadSamples(samples, 0, samples.Length)) > 0)
        {
            if (output.Length + count * 2L > MaxDecodedBytes)
                throw new InvalidDataException("Decoded MP3 exceeds the 24 MiB playback limit.");

            for (var i = 0; i < count; i++)
                writer.Write((short) (Math.Clamp(samples[i], -1f, 1f) * short.MaxValue));
        }

        var length = checked((int) output.Length);
        output.Position = 4;
        writer.Write(length - 8);
        output.Position = 40;
        writer.Write(length - 44);
        return output.ToArray();
    }
}
