// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;

namespace SharpEmu.Libs.Np;

// NP Party is an online feature with no effect on offline play. Titles still
// call its entry points during startup and shutdown; without these exports the
// stale RAX is used as a handle and the guest crashes in its cleanup path.
public static class NpPartyExports
{
    // The offsets below are emulator-assigned within the NP Party family
    // (0x805525xx, evidenced by the NOT_IN_PARTY code); the conditions they
    // report mirror real firmware lifetime errors.
    private const int NpPartyErrorAlreadyInitialized = unchecked((int)0x80552501);
    private const int NpPartyErrorNotInitialized = unchecked((int)0x80552502);
    private const int NpPartyErrorInvalidArgument = unchecked((int)0x80552503);
    private const int NpPartyErrorNotInParty = unchecked((int)0x80552506);

    private static int _initialized;
    private static readonly object _handlerGate = new();
    private static ulong _handlerCallback;
    private static ulong _handlerUserData;

    public static void ResetRuntimeState()
    {
        Volatile.Write(ref _initialized, 0);
        lock (_handlerGate)
        {
            _handlerCallback = 0;
            _handlerUserData = 0;
        }
    }

    internal static ulong RegisteredHandlerCallback
    {
        get
        {
            lock (_handlerGate)
            {
                return _handlerCallback;
            }
        }
    }

    internal static ulong RegisteredHandlerUserData
    {
        get
        {
            lock (_handlerGate)
            {
                return _handlerUserData;
            }
        }
    }

    [SysAbiExport(
        Nid = "lhYCTQmBkds",
        ExportName = "sceNpPartyInitialize",
        Target = Generation.Gen4,
        LibraryName = "libSceNpParty")]
    public static int NpPartyInitialize(CpuContext ctx)
    {
        if (Volatile.Read(ref _initialized) != 0)
        {
            TraceParty("initialize rejected: already initialized");
            return ctx.SetReturn(NpPartyErrorAlreadyInitialized);
        }

        Volatile.Write(ref _initialized, 1);
        TraceParty("initialize");
        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "oLYkibiHqRA",
        ExportName = "sceNpPartyTerminate",
        Target = Generation.Gen4,
        LibraryName = "libSceNpParty")]
    public static int NpPartyTerminate(CpuContext ctx)
    {
        if (Volatile.Read(ref _initialized) == 0)
        {
            TraceParty("terminate rejected: not initialized");
            return ctx.SetReturn(NpPartyErrorNotInitialized);
        }

        Volatile.Write(ref _initialized, 0);
        lock (_handlerGate)
        {
            _handlerCallback = 0;
            _handlerUserData = 0;
        }

        TraceParty("terminate");
        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "3e4k2mzLkmc",
        ExportName = "sceNpPartyCheckCallback",
        Target = Generation.Gen4,
        LibraryName = "libSceNpParty")]
    public static int NpPartyCheckCallback(CpuContext ctx)
    {
        if (Volatile.Read(ref _initialized) == 0)
        {
            TraceParty("check_callback rejected: not initialized");
            return ctx.SetReturn(NpPartyErrorNotInitialized);
        }

        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "kA88gbv71ao",
        ExportName = "sceNpPartyRegisterHandler",
        Target = Generation.Gen4,
        LibraryName = "libSceNpParty")]
    public static int NpPartyRegisterHandler(CpuContext ctx)
    {
        var callback = ctx[CpuRegister.Rdi];
        var userData = ctx[CpuRegister.Rsi];
        if (callback == 0)
        {
            TraceParty("register_handler rejected: null callback");
            return ctx.SetReturn(NpPartyErrorInvalidArgument);
        }

        // Offline play is never in a party, so registration still reports
        // NOT_IN_PARTY. The registration is stored (single slot, mirroring the
        // legacy slot in NpStateCallbackState) so diagnostics and tests can
        // observe what the guest registered.
        lock (_handlerGate)
        {
            _handlerCallback = callback;
            _handlerUserData = userData;
        }

        TraceParty($"register_handler cb=0x{callback:X16} userdata=0x{userData:X16} result=NOT_IN_PARTY");
        return ctx.SetReturn(NpPartyErrorNotInParty);
    }

    private static void TraceParty(string message)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SHARPEMU_LOG_NP"), "1", StringComparison.Ordinal))
        {
            return;
        }

        Console.Error.WriteLine($"[LOADER][TRACE] np.party_{message}");
    }
}
