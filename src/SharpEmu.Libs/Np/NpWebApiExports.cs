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
    private static int _nextHandle;

    public static void ResetHandles() => Volatile.Write(ref _nextHandle, 0);

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
    public static int NpWebApiInitialize(CpuContext ctx) => NpWebApi2Exports.NpWebApi2Initialize(ctx);
}
