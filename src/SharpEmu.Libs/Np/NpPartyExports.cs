// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;

namespace SharpEmu.Libs.Np;

// NP Party is an online feature with no effect on offline play. Titles still
// call its entry points during startup and shutdown; without these exports the
// stale RAX is used as a handle and the guest crashes in its cleanup path.
public static class NpPartyExports
{
    private static int _initialized;

    public static void ResetRuntimeState() => Volatile.Write(ref _initialized, 0);

    [SysAbiExport(
        Nid = "lhYCTQmBkds",
        ExportName = "sceNpPartyInitialize",
        Target = Generation.Gen4,
        LibraryName = "libSceNpParty")]
    public static int NpPartyInitialize(CpuContext ctx)
    {
        Volatile.Write(ref _initialized, 1);
        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "oLYkibiHqRA",
        ExportName = "sceNpPartyTerminate",
        Target = Generation.Gen4,
        LibraryName = "libSceNpParty")]
    public static int NpPartyTerminate(CpuContext ctx)
    {
        Volatile.Write(ref _initialized, 0);
        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "3e4k2mzLkmc",
        ExportName = "sceNpPartyCheckCallback",
        Target = Generation.Gen4,
        LibraryName = "libSceNpParty")]
    public static int NpPartyCheckCallback(CpuContext ctx) => ctx.SetReturn(0);

    [SysAbiExport(
        Nid = "kA88gbv71ao",
        ExportName = "sceNpPartyRegisterHandler",
        Target = Generation.Gen4,
        LibraryName = "libSceNpParty")]
    public static int NpPartyRegisterHandler(CpuContext ctx) =>
        // Offline play is never in a party, so registration has no party to join.
        ctx.SetReturn(unchecked((int)0x80552506));
}
