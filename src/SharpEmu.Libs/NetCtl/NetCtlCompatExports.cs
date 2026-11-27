// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;

namespace SharpEmu.Libs.NetCtl;

// NetCtl callbacks are only used for online state changes. Unregistering one
// during shutdown has no effect in the emulator, but the export must still
// return a defined value instead of leaving stale state in RAX.
public static class NetCtlCompatExports
{
    // Callback ids are small indices (libSceNetCtl hands out 0..7); anything
    // outside that range was never issued. Mirrors the invalid-argument code
    // the sibling NetCtlExports uses for malformed callback arguments.
    private const int MaxCallbackId = 8;
    private const int NetCtlCompatErrorInvalidArgument = unchecked((int)0x80412107);

    // Same "not registered" shape as NpStateCallbackState.NotRegistered: this
    // compat path holds no registration store of its own, so every
    // well-formed id is unknown here and reported instead of succeeding.
    private const int NetCtlCompatErrorNotRegistered = unchecked((int)0x80550009);

    [SysAbiExport(
        Nid = "Rqm2OnZMCz0",
        ExportName = "sceNetCtlUnregisterCallback",
        Target = Generation.Gen4,
        LibraryName = "libSceNetCtl")]
    public static int NetCtlUnregisterCallback(CpuContext ctx)
    {
        var id = unchecked((int)ctx[CpuRegister.Rdi]);
        if (id < 0 || id >= MaxCallbackId)
        {
            return ctx.SetReturn(NetCtlCompatErrorInvalidArgument);
        }

        return ctx.SetReturn(NetCtlCompatErrorNotRegistered);
    }
}
