// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using SharpEmu.HLE;
using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace SharpEmu.Libs.Np;

public static partial class NpManagerExports
{
    private const int NpTitleIdSize = 16;
    private const int NpTitleSecretSize = 128;
    private const int NpErrorInvalidArgument = unchecked((int)0x80550003);
    private const int NpErrorSignedOut = unchecked((int)0x80550006);
    private static int _nextNpRequestId;
    private static readonly ConcurrentDictionary<int, int> _requestResults = new();

    [SysAbiExport(
        Nid = "3Zl8BePTh9Y",
        ExportName = "sceNpCheckCallback",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpCheckCallback(CpuContext ctx) => DispatchStateCallbacks(ctx, "sceNpCheckCallback");

    [SysAbiExport(Nid = "eiqMCt9UshI", ExportName = "sceNpCreateAsyncRequest", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceNpManager")]
    public static int NpCreateAsyncRequest(CpuContext ctx) => SetReturn(ctx, NextRequestId());

    [SysAbiExport(Nid = "KfGZg2y73oM", ExportName = "sceNpCheckNpReachability", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceNpManager")]
    public static int NpCheckNpReachability(CpuContext ctx)
    {
        var requestId = unchecked((int)ctx[CpuRegister.Rdi]);
        if (requestId <= 0)
        {
            return SetReturn(ctx, NpErrorInvalidArgument);
        }

        _requestResults[requestId] = NpErrorSignedOut;
        return SetReturn(ctx, NpErrorSignedOut);
    }

    // The account language and age are server requests like the reachability
    // check above, and the offline profile is signed out: each request fails
    // with SIGNED_OUT directly and through sceNpPollAsync.
    [SysAbiExport(Nid = "3Tcz5bNCfZQ", ExportName = "sceNpGetAccountLanguage2", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceNpManager")]
    public static int NpGetAccountLanguage2(CpuContext ctx) => SignedOutAccountRequest(ctx);

    [SysAbiExport(Nid = "+4DegjBqV1g", ExportName = "sceNpGetAccountAge", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceNpManager")]
    public static int NpGetAccountAge(CpuContext ctx) => SignedOutAccountRequest(ctx);

    private static int SignedOutAccountRequest(CpuContext ctx)
    {
        var requestId = unchecked((int)ctx[CpuRegister.Rdi]);
        var userId = unchecked((int)ctx[CpuRegister.Rsi]);
        if (requestId <= 0 || userId == -1 || ctx[CpuRegister.Rdx] == 0)
        {
            return SetReturn(ctx, NpErrorInvalidArgument);
        }

        _requestResults[requestId] = NpErrorSignedOut;
        return SetReturn(ctx, NpErrorSignedOut);
    }

    [SysAbiExport(Nid = "GpLQDNKICac", ExportName = "sceNpCreateRequest", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceNpManager")]
    public static int NpCreateRequest(CpuContext ctx) => SetReturn(ctx, NextRequestId());

    [SysAbiExport(Nid = "OzKvTvg3ZYU", ExportName = "sceNpAbortRequest", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceNpManager")]
    public static int NpAbortRequest(CpuContext ctx) => SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_OK);

    [SysAbiExport(Nid = "uqcPJLWL08M", ExportName = "sceNpPollAsync", Target = Generation.Gen4 | Generation.Gen5, LibraryName = "libSceNpManager")]
    public static int NpPollAsync(CpuContext ctx)
    {
        var requestId = unchecked((int)ctx[CpuRegister.Rdi]);
        var resultAddress = ctx[CpuRegister.Rsi];
        if (requestId <= 0)
        {
            return SetReturn(ctx, NpErrorInvalidArgument);
        }

        if (resultAddress != 0)
        {
            Span<byte> result = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(result, _requestResults.GetValueOrDefault(requestId));
            if (!ctx.Memory.TryWrite(resultAddress, result))
            {
                return SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
            }
        }

        return SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_OK);
    }

    private static int NextRequestId()
    {
        var id = Interlocked.Increment(ref _nextNpRequestId) & 0x7FFF_FFFF;
        return id == 0 ? 1 : id;
    }

    [SysAbiExport(
        Nid = "S7QTn72PrDw",
        ExportName = "sceNpDeleteRequest",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpDeleteRequest(CpuContext ctx)
    {
        _requestResults.TryRemove(unchecked((int)ctx[CpuRegister.Rdi]), out _);
        ctx[CpuRegister.Rax] = 0;
        return (int)OrbisGen2Result.ORBIS_GEN2_OK;
    }

    [SysAbiExport(
        Nid = "JELHf4xPufo",
        ExportName = "sceNpCheckCallbackForLib",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpCheckCallbackForLib(CpuContext ctx) => DispatchStateCallbacks(ctx, "sceNpCheckCallbackForLib");

    // Offline profile: the online-id payload is left untouched and the call
    // reports SIGNED_OUT on every generation. shadPS4 returns SIGNED_OUT
    // without writing the output when PSN is unavailable (np_manager.cpp:618-636).
    // The value-returning queries below (GetState/HasSignedUp/GetNpReachabilityState)
    // correctly differ by returning data + OK (np_manager.cpp:641-678, 726-746).
    [SysAbiExport(
        Nid = "XDncXQIJUSk",
        ExportName = "sceNpGetOnlineId",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpGetOnlineId(CpuContext ctx)
    {
        if (unchecked((int)ctx[CpuRegister.Rdi]) == -1 || ctx[CpuRegister.Rsi] == 0)
        {
            return SetReturn(ctx, NpErrorInvalidArgument);
        }

        return SetReturn(ctx, NpErrorSignedOut);
    }

    [SysAbiExport(
        Nid = "VfRSmPmj8Q8",
        ExportName = "sceNpRegisterStateCallback",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpRegisterStateCallback(CpuContext ctx)
    {
        var callback = ctx[CpuRegister.Rdi];
        var userData = ctx[CpuRegister.Rsi];
        if (callback == 0)
        {
            return SetReturn(ctx, NpErrorInvalidArgument);
        }

        var result = NpStateCallbackState.RegisterLegacy(callback, userData);
        TraceNp($"register_state_callback cb=0x{callback:X16} userdata=0x{userData:X16} result=0x{unchecked((uint)result):X8}");
        return SetReturn(ctx, result);
    }

    [SysAbiExport(
        Nid = "M3wFXbYQtAA",
        ExportName = "sceNpUnregisterStateCallbackA",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpUnregisterStateCallbackA(CpuContext ctx) =>
        SetReturn(ctx, NpStateCallbackState.UnregisterA(unchecked((int)ctx[CpuRegister.Rdi])));

    [SysAbiExport(
        Nid = "hw5KNqAAels",
        ExportName = "sceNpRegisterNpReachabilityStateCallback",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpRegisterNpReachabilityStateCallback(CpuContext ctx)
    {
        var callback = ctx[CpuRegister.Rdi];
        if (callback != 0)
        {
            NpStateCallbackState.RegisterReachability(callback, ctx[CpuRegister.Rsi]);
        }

        return SetReturn(ctx, callback == 0 ? NpErrorInvalidArgument : 0);
    }

    /// <summary>
    /// Accepts the premium-event callback and never invokes it. Offline sessions
    /// have no PS Plus / premium transitions to deliver, and leaving this NID
    /// unresolved returns NOT_FOUND which soft-locks titles that register it
    /// during settings / store probes (GTA V Enhanced).
    /// </summary>
    [SysAbiExport(
        Nid = "+yqjab2fUJA",
        ExportName = "sceNpRegisterPremiumEventCallback",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpRegisterPremiumEventCallback(CpuContext ctx)
    {
        TraceNp(
            $"register_premium_event_callback cb=0x{ctx[CpuRegister.Rdi]:X16} " +
            $"userdata=0x{ctx[CpuRegister.Rsi]:X16}");
        return ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_OK);
    }

    [SysAbiExport(
        Nid = "qQJfO8HAiaY",
        ExportName = "sceNpRegisterStateCallbackA",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpRegisterStateCallbackA(CpuContext ctx)
    {
        var callback = ctx[CpuRegister.Rdi];
        var userData = ctx[CpuRegister.Rsi];
        if (callback == 0)
        {
            return SetReturn(ctx, NpErrorInvalidArgument);
        }

        // The real ABI returns the callback id directly (np_manager.cpp:1007-1010
        // returns RegisterStateCallbackA, which returns i + 1 at np_manager.cpp:857-884);
        // there is no separate out-param, unlike what the 0-returning siblings suggest.
        var id = NpStateCallbackState.RegisterA(callback, userData);
        TraceNp($"register_state_callback_a cb=0x{callback:X16} userdata=0x{userData:X16} id={id}");
        return SetReturn(ctx, id);
    }

    [SysAbiExport(
        Nid = "0c7HbXRKUt4",
        ExportName = "sceNpRegisterStateCallbackForToolkit",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManagerForToolkit")]
    public static int NpRegisterStateCallbackForToolkit(CpuContext ctx)
    {
        var callback = ctx[CpuRegister.Rdi];
        if (callback != 0)
        {
            NpStateCallbackState.RegisterToolkit(callback, ctx[CpuRegister.Rsi]);
        }

        return SetReturn(ctx, callback == 0 ? NpErrorInvalidArgument : 0);
    }

    [SysAbiExport(
        Nid = "eQH7nWPcAgc",
        ExportName = "sceNpGetState",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpGetState(CpuContext ctx)
    {
        var stateAddress = ctx[CpuRegister.Rsi];
        if (stateAddress == 0)
        {
            return ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        // 1 == OrbisNpState::SignedOut (np_manager.h:22-26: NotSignedUp = 0,
        // SignedOut = 1, SignedIn = 2); sceNpGetState reports SignedOut while
        // offline (np_manager.cpp:657-678).
        Span<byte> stateBytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(stateBytes, 1);
        return ctx.Memory.TryWrite(stateAddress, stateBytes)
            ? ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_OK)
            : ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    [SysAbiExport(
        Nid = "rbknaUjpqWo",
        ExportName = "sceNpGetAccountIdA",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpGetAccountIdA(CpuContext ctx)
    {
        var userId = unchecked((int)ctx[CpuRegister.Rdi]);
        var accountIdAddress = ctx[CpuRegister.Rsi];
        if (userId == -1 || accountIdAddress == 0)
        {
            return SetReturn(ctx, NpErrorInvalidArgument);
        }

        // Offline profile: sceNpGetState reports SignedOut (1 == SignedOut per
        // np_manager.h:22-26; np_manager.cpp:657-678), so PSN profile queries
        // must agree instead of handing the guest a signed-in identity that can
        // never be resolved online. The out-param is left untouched on error,
        // matching the sibling queries. Value-returning queries (GetState,
        // HasSignedUp, GetNpReachabilityState) correctly differ by returning
        // data + OK (np_manager.cpp:641-678, 726-746).
        // shadPS4 zeroes *account_id before returning SIGNED_OUT
        // (np_manager.cpp:579-592), but the sibling SharpEmu queries leave
        // buffers untouched on error, so this keeps the single offline idiom.
        return SetReturn(ctx, NpErrorSignedOut);
    }

    [SysAbiExport(
        Nid = "JT+t00a3TxA",
        ExportName = "sceNpGetAccountCountryA",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpGetAccountCountryA(CpuContext ctx)
    {
        var userId = unchecked((int)ctx[CpuRegister.Rdi]);
        var countryAddress = ctx[CpuRegister.Rsi];
        if (userId == -1 || countryAddress == 0)
        {
            return SetReturn(ctx, NpErrorInvalidArgument);
        }

        // Offline: SIGNED_OUT without writing the output (np_manager.cpp:487-501).
        // Left untouched to match the sibling queries; see NpGetAccountIdA.
        return SetReturn(ctx, NpErrorSignedOut);
    }

    [SysAbiExport(
        Nid = "e-ZuhGEoeC4",
        ExportName = "sceNpGetNpReachabilityState",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpGetNpReachabilityState(CpuContext ctx)
    {
        var userId = unchecked((int)ctx[CpuRegister.Rdi]);
        var stateAddress = ctx[CpuRegister.Rsi];
        if (userId == -1 || stateAddress == 0)
        {
            return SetReturn(ctx, NpErrorInvalidArgument);
        }

        Span<byte> state = stackalloc byte[sizeof(uint)];
        // 0 == OrbisNpReachabilityState::Unavailable (np_manager.h:28-32);
        // offline reports Unavailable + OK (np_manager.cpp:641-655), unlike the
        // SIGNED_OUT profile queries above.
        BinaryPrimitives.WriteUInt32LittleEndian(state, 0);
        return ctx.Memory.TryWrite(stateAddress, state)
            ? SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_OK)
            : SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    [SysAbiExport(
        Nid = "Oad3rvY-NJQ",
        ExportName = "sceNpHasSignedUp",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpHasSignedUp(CpuContext ctx)
    {
        var resultAddress = ctx[CpuRegister.Rsi];
        if (resultAddress == 0 || unchecked((int)ctx[CpuRegister.Rdi]) == -1)
        {
            return SetReturn(ctx, NpErrorInvalidArgument);
        }

        Span<byte> signedUp = stackalloc byte[1];
        // Offline has no signed-up account: report false + OK
        // (np_manager.cpp:726-746), unlike the SIGNED_OUT profile queries above.
        signedUp[0] = 0;
        return ctx.Memory.TryWrite(resultAddress, signedUp)
            ? SetReturn(ctx, 0)
            : SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
    }

    [SysAbiExport(
        Nid = "Ec63y59l9tw",
        ExportName = "sceNpSetNpTitleId",
        Target = Generation.Gen4 | Generation.Gen5,
        LibraryName = "libSceNpManager")]
    public static int NpSetNpTitleId(CpuContext ctx)
    {
        var titleIdAddress = ctx[CpuRegister.Rdi];
        var titleSecretAddress = ctx[CpuRegister.Rsi];
        if (titleIdAddress == 0 || titleSecretAddress == 0)
        {
            return ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_ERROR_INVALID_ARGUMENT);
        }

        Span<byte> titleId = stackalloc byte[NpTitleIdSize];
        Span<byte> titleSecret = stackalloc byte[NpTitleSecretSize];
        if (!ctx.Memory.TryRead(titleIdAddress, titleId) ||
            !ctx.Memory.TryRead(titleSecretAddress, titleSecret))
        {
            return ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
        }

        TraceNp($"set_np_title_id title='{ReadTitleId(titleId)}'");
        return ctx.SetReturn(OrbisGen2Result.ORBIS_GEN2_OK);
    }

    private static int SetReturn(CpuContext ctx, int result)
    {
        ctx[CpuRegister.Rax] = unchecked((ulong)result);
        return result;
    }

    private static string ReadTitleId(ReadOnlySpan<byte> bytes)
    {
        var length = 0;
        while (length < 12 && length < bytes.Length && bytes[length] != 0)
        {
            length++;
        }

        return length == 0
            ? string.Empty
            : System.Text.Encoding.ASCII.GetString(bytes[..length]);
    }

    private static int DispatchStateCallbacks(CpuContext ctx, string reason)
    {
        var scheduler = GuestThreadExecution.Scheduler;
        if (scheduler is null)
        {
            // No guest thread pump exists to observe the transition, so drop
            // rather than grow the queue without bound.
            var dropped = NpStateCallbackState.DropPending();
            if (dropped > 0)
            {
                TraceNp($"dispatch_state_callback_dropped_no_scheduler count={dropped} from={reason}");
            }

            return SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_OK);
        }

        while (NpStateCallbackState.TryTakeDispatch(
                   out var stateEvent,
                   out var legacy,
                   out var callbacks,
                   out var toolkit,
                   out var reachability))
        {
            TraceNp(
                $"dispatch_state_callback user={stateEvent.UserId} state={stateEvent.State} " +
                $"from={reason} legacy={legacy is not null} a={callbacks.Length} toolkit={toolkit is not null}");

            if (legacy is { } classic)
            {
                _ = scheduler.TryCallGuestFunction(
                    ctx,
                    classic.Callback,
                    stateEvent.UserId,
                    unchecked((ulong)stateEvent.State),
                    0,
                    classic.UserData,
                    0,
                    0,
                    "np_state_callback",
                    out _,
                    out var legacyError);
                if (legacyError is not null)
                {
                    TraceNp($"state_callback_failed: {legacyError}");
                }
            }

            foreach (var (callback, userData) in callbacks)
            {
                _ = scheduler.TryCallGuestFunction(
                    ctx,
                    callback,
                    stateEvent.UserId,
                    unchecked((ulong)stateEvent.State),
                    userData,
                    0,
                    0,
                    "np_state_callback_a",
                    out _,
                    out var callbackError);
                if (callbackError is not null)
                {
                    TraceNp($"state_callback_a_failed: {callbackError}");
                }
            }

            if (toolkit is { } toolkitCallback)
            {
                _ = scheduler.TryCallGuestFunction(
                    ctx,
                    toolkitCallback.Callback,
                    stateEvent.UserId,
                    unchecked((ulong)stateEvent.State),
                    toolkitCallback.UserData,
                    0,
                    0,
                    "np_state_callback_toolkit",
                    out _,
                    out var toolkitError);
                if (toolkitError is not null)
                {
                    TraceNp($"state_callback_toolkit_failed: {toolkitError}");
                }
            }

            if (reachability is { } reachabilityCallback)
            {
                _ = scheduler.TryCallGuestFunction(
                    ctx,
                    reachabilityCallback.Callback,
                    stateEvent.UserId,
                    NpStateCallbackState.ReachabilityUnavailable,
                    reachabilityCallback.UserData,
                    0,
                    0,
                    "np_reachability_callback",
                    out _,
                    out var reachabilityError);
                if (reachabilityError is not null)
                {
                    TraceNp($"reachability_callback_failed: {reachabilityError}");
                }
            }
        }

        return SetReturn(ctx, (int)OrbisGen2Result.ORBIS_GEN2_OK);
    }

    private static void TraceNp(string message)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("SHARPEMU_LOG_NP"), "1", StringComparison.Ordinal))
        {
            return;
        }

        Console.Error.WriteLine($"[LOADER][TRACE] np.{message}");
    }
}
