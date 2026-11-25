// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;

namespace SharpEmu.Libs.VideoOut;

// Internal PS4 SDK helpers that only tune mode-selection policy for the guest.
// The emulator already presents the mode configured through sceVideoOutSetBufferAttribute
// and the flips, so accepting them keeps the guest from acting on a stale return value.
public static partial class VideoOutExports
{
    [SysAbiExport(
        Nid = "pjkDsgxli6c",
        ExportName = "sceVideoOutModeSetAny_",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceVideoOut")]
    public static int VideoOutModeSetAny(CpuContext ctx) => (int)OrbisGen2Result.ORBIS_GEN2_OK;

    [SysAbiExport(
        Nid = "N1bEoJ4SRw4",
        ExportName = "sceVideoOutConfigureOutputMode_",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceVideoOut")]
    public static int VideoOutConfigureOutputMode(CpuContext ctx) => (int)OrbisGen2Result.ORBIS_GEN2_OK;
}
