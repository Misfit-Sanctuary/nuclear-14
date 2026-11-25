namespace Content.Shared._Misfits.Administration;

/// <summary>Recognizes admin upload formats; the server converts MP3 to WAV before playback.</summary>
public static class AdminAudioFormat
{
    public const int MaxBytes = 3 * 1024 * 1024;
    public const int MaxPlaybackBytes = 24 * 1024 * 1024;

    public static bool TryGetExtension(byte[] data, out string extension, int maxBytes = MaxBytes)
    {
        extension = string.Empty;
        if (data.Length < 44 || data.Length > maxBytes)
            return false;

        if (data[0] == 'O' && data[1] == 'g' && data[2] == 'g' && data[3] == 'S')
        {
            extension = "ogg";
            return true;
        }

        if (data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F'
            && data[8] == 'W' && data[9] == 'A' && data[10] == 'V' && data[11] == 'E')
        {
            extension = "wav";
            return true;
        }

        if (data[0] == 'I' && data[1] == 'D' && data[2] == '3'
            || data[0] == 0xFF && (data[1] & 0xE0) == 0xE0)
        {
            extension = "mp3";
            return true;
        }

        return false;
    }
}
