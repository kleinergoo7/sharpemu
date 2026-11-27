// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;

namespace SharpEmu.Libs.VideoOut;

// Internal PS4 SDK helpers that only tune mode-selection policy for the guest.
// The emulator already presents the mode configured through sceVideoOutSetBufferAttribute
// and the flips, so accepting them keeps the guest from acting on a stale return value.
public static partial class VideoOutExports
{
    private static readonly object _internalModeGate = new();
    private static ulong _modeSetAnySelection;
    private static ulong _configureOutputModeSelection;

    internal static ulong ModeSetAnySelection
    {
        get
        {
            lock (_internalModeGate)
            {
                return _modeSetAnySelection;
            }
        }
    }

    internal static ulong ConfigureOutputModeSelection
    {
        get
        {
            lock (_internalModeGate)
            {
                return _configureOutputModeSelection;
            }
        }
    }

    internal static void ResetInternalModeForTests()
    {
        lock (_internalModeGate)
        {
            _modeSetAnySelection = 0;
            _configureOutputModeSelection = 0;
        }
    }

    [SysAbiExport(
        Nid = "pjkDsgxli6c",
        ExportName = "sceVideoOutModeSetAny_",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceVideoOut")]
    public static int VideoOutModeSetAny(CpuContext ctx)
    {
        // These helpers are undocumented internal entry points, so the request
        // is an opaque 32-bit mode selector: anything outside 32 bits cannot be
        // a mode the guest meant and is rejected, everything representable is
        // recorded as the requested selection.
        var mode = ctx[CpuRegister.Rdi];
        if (mode > uint.MaxValue)
        {
            return ctx.SetReturn(OrbisVideoOutErrorInvalidValue);
        }

        lock (_internalModeGate)
        {
            _modeSetAnySelection = mode;
        }

        TraceVideoOut($"videoout.mode_set_any mode=0x{mode:X}");
        return ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_OK);
    }

    [SysAbiExport(
        Nid = "N1bEoJ4SRw4",
        ExportName = "sceVideoOutConfigureOutputMode_",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceVideoOut")]
    public static int VideoOutConfigureOutputMode(CpuContext ctx)
    {
        var mode = ctx[CpuRegister.Rdi];
        if (mode > uint.MaxValue)
        {
            return ctx.SetReturn(OrbisVideoOutErrorInvalidValue);
        }

        lock (_internalModeGate)
        {
            _configureOutputModeSelection = mode;
        }

        TraceVideoOut($"videoout.configure_output_mode mode=0x{mode:X}");
        return ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_OK);
    }
}
