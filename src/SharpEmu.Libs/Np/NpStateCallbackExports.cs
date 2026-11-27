// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;

namespace SharpEmu.Libs.Np;

// Callback registration is not needed for offline play. Titles unregister
// callbacks when tearing down NP state; leaving the exports unresolved gives
// the guest a stale RAX that it uses as a pointer during cleanup.
public static class NpStateCallbackExports
{
    [SysAbiExport(
        Nid = "mjjTXh+NHWY",
        ExportName = "sceNpUnregisterStateCallback",
        Target = Generation.Gen4,
        LibraryName = "libSceNp")]
    public static int NpUnregisterStateCallback(CpuContext ctx) => ctx.SetReturn(0);
}
