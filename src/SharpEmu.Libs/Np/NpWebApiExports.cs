// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;

namespace SharpEmu.Libs.Np;

// The original NpWebApi entry point uses the same (httpContextId, poolSize)
// signature as NpWebApi2Initialize and shares its library context lifetime.
// Titles that still import the v1 symbol otherwise leave a stale value in RAX
// and dereference it immediately after the call.
public static class NpWebApiExports
{
    private const int NpWebApiErrorInvalidArgument = unchecked((int)0x80553402);

    private static int _nextHandle;

    public static void ResetHandles() => Volatile.Write(ref _nextHandle, 0);

    // Hook for the runtime reset path, following the ResetRuntimeState pattern
    // other Libs expose: per-launch handle state must not leak across runs.
    public static void ResetRuntimeState() => ResetHandles();

    internal static void ResetForTests() => ResetRuntimeState();

    [SysAbiExport(
        Nid = "79M-JqvvGo0",
        ExportName = "sceNpWebApiCreateHandle",
        Target = Generation.Gen4,
        LibraryName = "libSceNpWebApi")]
    public static int NpWebApiCreateHandle(CpuContext ctx)
    {
        var handle = Interlocked.Increment(ref _nextHandle);
        return ctx.SetReturn(handle);
    }

    [SysAbiExport(
        Nid = "G3AnLNdRBjE",
        ExportName = "sceNpWebApiInitialize",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpWebApi")]
    public static int NpWebApiInitialize(CpuContext ctx)
    {
        var httpContextId = unchecked((int)ctx[CpuRegister.Rdi]);
        var poolSize = ctx[CpuRegister.Rsi];

        if (httpContextId <= 0 || poolSize == 0)
        {
            return ctx.SetReturn(NpWebApiErrorInvalidArgument);
        }

        return NpWebApi2Exports.NpWebApi2Initialize(ctx);
    }
}
