// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;

namespace SharpEmu.Libs.NetCtl;

// NetCtl callbacks are only used for online state changes. Unregistering one
// during shutdown has no effect in the emulator, but the export must still
// return a defined value instead of leaving stale state in RAX.
public static class NetCtlCompatExports
{
    [SysAbiExport(
        Nid = "Rqm2OnZMCz0",
        ExportName = "sceNetCtlUnregisterCallback",
        Target = Generation.Gen4,
        LibraryName = "libSceNetCtl")]
    public static int NetCtlUnregisterCallback(CpuContext ctx) => ctx.SetReturn(0);
}
