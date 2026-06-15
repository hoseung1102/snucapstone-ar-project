using System;
using UnityEngine;

public static class StartupProbe
{
    static readonly long appStartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static void Mark(string tag, string message)
    {
        long elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - appStartMs;
        Debug.Log($"[ColdStartProbe] +{elapsed}ms {tag}: {message}");
    }
}
