// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Collections.Generic;

namespace SharpEmu.Libs.Np;

/// <summary>
/// Queued NP state transitions and the guest callbacks registered for them.
/// The PS4 runtime delivers a transition only when the guest next polls it;
/// mirroring that keeps offline login/logout observable without a network.
/// State/reachability values and callback errors follow shadPS4:
/// OrbisNpState SignedOut = 1 (np_manager.h:22-26),
/// OrbisNpReachabilityState Unavailable = 0 (np_manager.h:28-32),
/// 0x80550008 = CALLBACK_ALREADY_REGISTERED and 0x80550009 =
/// CALLBACK_NOT_REGISTERED (np_error.h:17-18).
/// </summary>
internal static class NpStateCallbackState
{
    // 1 == OrbisNpState::SignedOut (np_manager.h:22-26: NotSignedUp = 0,
    // SignedOut = 1, SignedIn = 2); sceNpGetState reports SignedOut while
    // offline (np_manager.cpp:657-678).
    internal const int StateSignedOut = 1;
    // 0 == OrbisNpReachabilityState::Unavailable (np_manager.h:28-32);
    // offline reports Unavailable (np_manager.cpp:641-655).
    internal const int ReachabilityUnavailable = 0;

    internal readonly record struct StateEvent(ulong UserId, int State);

    private static readonly object Gate = new();
    private static readonly Queue<StateEvent> Pending = new();
    private static readonly Dictionary<int, (ulong Callback, ulong UserData)> CallbackA = new();
    private static (ulong Callback, ulong UserData)? _legacy;
    private static (ulong Callback, ulong UserData)? _toolkit;
    private static (ulong Callback, ulong UserData)? _reachability;
    private static int _nextCallbackId;
    private static bool _reachabilityReported;
    // Offline only ever queues a single SignedOut transition per login, so a
    // small cap is enough to observe it; beyond that drop the oldest instead
    // of growing without bound when the guest never polls.
    private const int MaxQueuedEvents = 8;

    internal static void QueueState(ulong userId, int state)
    {
        lock (Gate)
        {
            if (Pending.Count >= MaxQueuedEvents)
            {
                _ = Pending.Dequeue();
                Console.Error.WriteLine(
                    "[LOADER][TRACE] np.state_queue_full_drop_oldest");
            }

            Pending.Enqueue(new StateEvent(userId, state));
        }
    }

    internal static int RegisterLegacy(ulong callback, ulong userData)
    {
        lock (Gate)
        {
            if (_legacy is not null)
            {
                return AlreadyRegistered;
            }

            _legacy = (callback, userData);
            return 0;
        }
    }

    internal static int UnregisterLegacy()
    {
        lock (Gate)
        {
            if (_legacy is null)
            {
                return 0;
            }

            _legacy = null;
            return 0;
        }
    }

    internal static int RegisterA(ulong callback, ulong userData)
    {
        lock (Gate)
        {
            if (CallbackA.Count >= 16)
            {
                return AlreadyRegistered;
            }

            var id = ++_nextCallbackId;
            CallbackA[id] = (callback, userData);
            return id;
        }
    }

    internal static int UnregisterA(int id)
    {
        lock (Gate)
        {
            return CallbackA.Remove(id) ? 0 : NotRegistered;
        }
    }

    internal static void RegisterToolkit(ulong callback, ulong userData)
    {
        lock (Gate)
        {
            _toolkit = (callback, userData);
        }
    }

    internal static void RegisterReachability(ulong callback, ulong userData)
    {
        lock (Gate)
        {
            _reachability = (callback, userData);
            _reachabilityReported = false;
        }
    }

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            Pending.Clear();
            CallbackA.Clear();
            _legacy = null;
            _toolkit = null;
            _reachability = null;
            _reachabilityReported = false;
            _nextCallbackId = 0;
        }
    }

    internal static int AlreadyRegistered => unchecked((int)0x80550008);

    internal static int NotRegistered => unchecked((int)0x80550009);

    // 0x80550008 == ORBIS_NP_ERROR_CALLBACK_ALREADY_REGISTERED and 0x80550009 ==
    // ORBIS_NP_ERROR_CALLBACK_NOT_REGISTERED (np_error.h:17-18).
    internal static int DropPending()
    {
        lock (Gate)
        {
            var dropped = Pending.Count;
            Pending.Clear();
            return dropped;
        }
    }

    internal static bool TryTakeDispatch(out StateEvent stateEvent, out (ulong Callback, ulong UserData)? legacy,
        out (ulong Callback, ulong UserData)[] callbacks, out (ulong Callback, ulong UserData)? toolkit,
        out (ulong Callback, ulong UserData)? reachability)
    {
        lock (Gate)
        {
            if (Pending.Count == 0)
            {
                stateEvent = default;
                legacy = null;
                callbacks = Array.Empty<(ulong, ulong)>();
                toolkit = null;
                reachability = null;
                return false;
            }

            stateEvent = Pending.Dequeue();
            legacy = _legacy;
            callbacks = new (ulong, ulong)[CallbackA.Count];
            var index = 0;
            foreach (var entry in CallbackA.Values)
            {
                callbacks[index++] = entry;
            }

            toolkit = _toolkit;
            reachability = null;
            if (_reachability is not null && !_reachabilityReported)
            {
                _reachabilityReported = true;
                reachability = _reachability;
            }

            return true;
        }
    }
}
