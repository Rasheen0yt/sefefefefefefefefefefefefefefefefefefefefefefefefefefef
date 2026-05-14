using System;

namespace GreedyDownloader.Helpers;

public static class ByteFormatter
{
    private static readonly string[] Suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Format(long bytes)
    {
        if (bytes == 0) return "0 B";
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1 && counter < Suffixes.Length - 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {Suffixes[counter]}";
    }

    public static string FormatSpeed(long bytesPerSecond)
    {
        return Format(bytesPerSecond) + "/s";
    }
}
