// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;

namespace SharpEmu.Libs.Audio;

public static class AudioInExports
{
    private const int AudioInErrorNotOpened = unchecked((int)0x80260109);

    private static readonly bool TraceEnabled =
        string.Equals(Environment.GetEnvironmentVariable("SHARPEMU_LOG_AUDIOIN"), "1", StringComparison.Ordinal);

    [SysAbiExport(
        Nid = "BohEAQ7DlUE",
        ExportName = "sceAudioInGetSilentState",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceAudioIn")]
    public static int GetSilentState(CpuContext ctx) =>
        // No AudioIn ports can be opened until an input backend is implemented.
        ctx.SetReturn(unchecked((int)0x80260101));

    [SysAbiExport(
        Nid = "5NE8Sjc7VC8",
        ExportName = "sceAudioInOpen",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceAudioIn")]
    public static int Open(CpuContext ctx)
    {
        // Device, port and priority ids are never negative and the params
        // pointer must be provided. Rejecting those first keeps malformed
        // calls distinguishable from the missing-backend path below.
        var device = unchecked((int)ctx[CpuRegister.Rdi]);
        var port = unchecked((int)ctx[CpuRegister.Rsi]);
        var priority = unchecked((int)ctx[CpuRegister.Rdx]);
        var paramsAddress = ctx[CpuRegister.Rcx];
        if (device < 0 || port < 0 || priority < 0 || paramsAddress == 0)
        {
            Trace($"open rejected device={device} port={port} priority={priority} params=0x{paramsAddress:X16}");
            return ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        // No AudioIn ports can be opened until an input backend is implemented.
        Trace($"open device={device} port={port} priority={priority} params=0x{paramsAddress:X16} result=NOT_OPENED");
        return ctx.SetReturn(AudioInErrorNotOpened);
    }

    [SysAbiExport(
        Nid = "Jh6WbHhnI68",
        ExportName = "sceAudioInClose",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceAudioIn")]
    public static int Close(CpuContext ctx)
    {
        var handle = unchecked((int)ctx[CpuRegister.Rdi]);
        if (handle <= 0)
        {
            // Handles are positive; anything else was never issued by Open.
            Trace($"close rejected handle={handle}");
            return ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        // Nothing can be open while Open always fails, so a well-formed handle
        // is valid-but-not-open rather than a stale handle.
        Trace($"close handle={handle} result=NOT_OPENED");
        return ctx.SetReturn(AudioInErrorNotOpened);
    }

    private static void Trace(string message)
    {
        if (!TraceEnabled)
        {
            return;
        }

        Console.Error.WriteLine($"[LOADER][TRACE] audioin.{message}");
    }
}
