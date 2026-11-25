// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;

namespace SharpEmu.Libs.Audio;

public static class AudioInExports
{
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
    public static int Open(CpuContext ctx) =>
        // No AudioIn ports can be opened until an input backend is implemented.
        ctx.SetReturn(unchecked((int)0x80260109));

    [SysAbiExport(
        Nid = "Jh6WbHhnI68",
        ExportName = "sceAudioInClose",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceAudioIn")]
    public static int Close(CpuContext ctx) =>
        // Nothing can be open while Open always fails, so any handle is stale.
        ctx.SetReturn(unchecked((int)0x80260109));
}
