// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace SharpEmu.CLI;

/// <summary>
/// Opt-in bundle for investigating a guest that stalls before its first input.
/// The individual switches stay available; this only turns them on together.
/// Output is intentionally large — redirect stderr to a file when using it.
/// </summary>
internal static class FullDiagnostics
{
    private const string EnableVariable = "SHARPEMU_LOG_EVERYTHING";

    private static readonly (string Name, string Value)[] Switches =
    [
        ("SHARPEMU_LOG_ALL_IMPORTS", "1"),
        ("SHARPEMU_LOG_ALL_IMPORT_RESULTS", "1"),
        ("SHARPEMU_LOG_IMPORT_PERIODIC", "1"),
        ("SHARPEMU_LOG_IMPORT_FRAMES", "1"),
        ("SHARPEMU_LOG_IMPORT_RECENT", "1"),
        ("SHARPEMU_LOG_EXPECTED_IMPORT_RESULTS", "1"),
        ("SHARPEMU_LOG_CONTEXT", "1"),
        ("SHARPEMU_LOG_GUEST_THREADS", "1"),
        ("SHARPEMU_LOG_USLEEP", "1"),
        ("SHARPEMU_LOG_FIBER", "1"),
        ("SHARPEMU_LOG_BOOTSTRAP", "1"),
        ("SHARPEMU_LOG_STACK_CHK", "1"),
        ("SHARPEMU_LOG_MODULE_DEPS", "1"),
        ("SHARPEMU_LOG_SEMA", "1"),
        ("SHARPEMU_LOG_PTHREADS", "1"),
        ("SHARPEMU_LOG_PTHREAD_CONDS", "1"),
        ("SHARPEMU_LOG_PTHREAD_FASTPATH", "1"),
        ("SHARPEMU_LOG_PAD_STATE", "1"),
        ("SHARPEMU_LOG_USER_SERVICE", "1"),
        ("SHARPEMU_LOG_NP", "1"),
        ("SHARPEMU_LOG_NP_WEB_API2", "1"),
        ("SHARPEMU_LOG_POSIX_SIGNALS", "1"),
        ("SHARPEMU_DUMP_FAULT_STACK_WINDOW", "1"),
        ("SHARPEMU_LOG_DISASM", "1"),
        ("SHARPEMU_LOG_DIRECT_MEMORY", "1"),
        ("SHARPEMU_LOG_VIDEOOUT", "1"),
        ("SHARPEMU_LOG_LAZY_COMMIT", "1"),
        ("SHARPEMU_LOG_POINTER_WINDOWS", "1"),
        ("SHARPEMU_LOG_REGISTER_WINDOWS", "1"),
        ("SHARPEMU_PERF_HLE", "1"),
        ("SHARPEMU_PERF_HLE_NODICT", "1"),
        ("SHARPEMU_LOG_DLSYM", "1"),
    ];

    internal static void Apply()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(EnableVariable),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        foreach (var (name, value) in Switches)
        {
            // Respect a pre-existing explicit opt-out (e.g. NAME=0): only fill
            // variables the caller left unset.
            if (Environment.GetEnvironmentVariable(name) is null)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        Console.Error.WriteLine(
            $"[LOADER][INFO] Full diagnostics enabled ({Switches.Length} switches); " +
            "expect a large stderr log.");
    }
}
