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
    public static int NpUnregisterStateCallback(CpuContext ctx)
    {
        // Registration hands out integer ids through the CallbackA store (see
        // NpStateCallbackState.RegisterA); unregister consults the same store
        // so an unknown id reports NOT_REGISTERED instead of succeeding.
        var id = unchecked((int)ctx[CpuRegister.Rdi]);
        var result = NpStateCallbackState.UnregisterA(id);
        if (result != 0)
        {
            // No CallbackA entry matched: fall back to the legacy single-slot
            // registration, which carries no id. It is still cleared so that
            // shutdown teardown takes effect, but the unknown handle is
            // reported to the guest.
            NpStateCallbackState.UnregisterLegacy();
        }

        return ctx.SetReturn(result);
    }
}
