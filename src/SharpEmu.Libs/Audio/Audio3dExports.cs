// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using System.Threading;
using SharpEmu.HLE;

namespace SharpEmu.Libs.Audio;

// PlayStation 4 3D audio: lifecycle, ports and objects with the validation and
// return codes of the reference implementation. Queued object audio is stored
// per the specified granularity padding. There is no host 3D backend yet, so
// mixing and output draining are covered separately.
public static class Audio3dExports
{
    private const int Audio3dErrorInvalidPort = unchecked((int)0x80EA0002);
    private const int Audio3dErrorInvalidObject = unchecked((int)0x80EA0003);
    private const int Audio3dErrorInvalidParameter = unchecked((int)0x80EA0004);
    private const int Audio3dErrorOutOfResources = unchecked((int)0x80EA0006);
    private const int Audio3dErrorNotReady = unchecked((int)0x80EA0007);
    private const int Audio3dErrorNotSupported = unchecked((int)0x80EA0008);

    private const uint BufferModeNoAdvance = 0;
    private const uint BufferModeAdvanceAndPush = 2;

    private const uint MaxPorts = 4;
    private const uint MaxObjects = 0x200;
    private const uint InvalidObjectId = 0xFFFFFFFF;
    private const uint InvalidPortId = 0xFFFFFFFF;

    private const uint AttributePcm = 1;
    private const uint AttributeGain = 5;
    private const uint AttributeResetState = 7;

    private const uint PcmFormatS16 = 0;
    private const uint PcmFormatFloat = 1;

    private const int OpenParametersSize = 0x28;
    private const int AttributeSize = 24;
    private const int PcmDescriptorSize = 24;

    private static readonly object _stateGate = new();
    private static bool _initialized;
    private static readonly Dictionary<uint, Audio3dPort> _ports = new();
    private static long _pushDropTraceCount;

    private sealed class Audio3dPort
    {
        public uint Granularity;
        public uint MaxObjects;
        public uint QueueDepth;
        public uint BufferMode;
        public readonly Dictionary<uint, Audio3dObject> Objects = new();
        public uint NextObjectId;
        public readonly Queue<byte[]> MixedQueue = new();
    }

    private sealed class Audio3dObject
    {
        public readonly Queue<PcmFrame> PcmQueue = new();
        public readonly Dictionary<uint, byte[]> Attributes = new();
        public bool Unreserved;
    }

    private sealed class PcmFrame
    {
        public required byte[] Data;
        public required uint Format;
    }

    private const float DownmixFront = 1.0f;
    private const float DownmixCenter = 0.7071f;
    private const float DownmixSurround = 0.7071f;

    // Mixes one granularity-sized stereo S16 frame from already-popped object
    // audio, mirroring the reference fold: mono folds to both channels, gain
    // applies per object (default 0 silences), output clamps to [-1, 1].
    internal static byte[] MixStereoFrames(
        List<(byte[] Data, uint Format, float Gain)> sources, uint granularity)
    {
        var mix = new float[granularity * 2];
        foreach (var (data, format, gain) in sources)
        {
            if (gain == 0.0f)
            {
                continue;
            }

            var bytesPerSample = format == PcmFormatFloat ? sizeof(float) : sizeof(short);
            var channels = data.Length / bytesPerSample / (int)Math.Max(granularity, 1);
            for (uint frame = 0; frame < granularity; frame++)
            {
                float left;
                float right;
                if (channels <= 1)
                {
                    left = ReadMonoSample(data, format, frame);
                    right = left;
                }
                else
                {
                    left = ReadChannelSample(data, format, channels, frame, 0) * DownmixFront;
                    right = ReadChannelSample(data, format, channels, frame, 1) * DownmixFront;
                    if (channels >= 3)
                    {
                        var center = ReadChannelSample(data, format, channels, frame, 2) * DownmixCenter;
                        left += center;
                        right += center;
                    }

                    if (channels >= 6)
                    {
                        left += ReadChannelSample(data, format, channels, frame, 4) * DownmixSurround;
                        right += ReadChannelSample(data, format, channels, frame, 5) * DownmixSurround;
                    }

                    if (channels >= 8)
                    {
                        left += ReadChannelSample(data, format, channels, frame, 6) * DownmixSurround;
                        right += ReadChannelSample(data, format, channels, frame, 7) * DownmixSurround;
                    }
                }

                mix[frame * 2] += left * gain;
                mix[frame * 2 + 1] += right * gain;
            }
        }

        var output = new byte[granularity * 4];
        for (var i = 0; i < mix.Length; i++)
        {
            var clamped = Math.Clamp(mix[i], -1.0f, 1.0f);
            BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(i * 2, 2), (short)(clamped * 32767.0f));
        }

        return output;
    }

    private static float ReadMonoSample(byte[] data, uint format, uint frame)
    {
        return format == PcmFormatFloat
            ? BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan((int)frame * 4, 4))
            : BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan((int)frame * 2, 2)) / 32768.0f;
    }

    private static float ReadChannelSample(byte[] data, uint format, int channels, uint frame, int channel)
    {
        if (format == PcmFormatFloat)
        {
            return BinaryPrimitives.ReadSingleLittleEndian(
                data.AsSpan(((int)frame * channels + channel) * 4, 4));
        }

        return BinaryPrimitives.ReadInt16LittleEndian(
            data.AsSpan(((int)frame * channels + channel) * 2, 2)) / 32768.0f;
    }

    private static float ReadGain(Audio3dObject obj)
    {
        if (obj.Attributes.TryGetValue(AttributeGain, out var blob) && blob.Length >= sizeof(float))
        {
            return BinaryPrimitives.ReadSingleLittleEndian(blob.AsSpan(0, 4));
        }

        return 0.0f;
    }

    private static void MixPortFrame(Audio3dPort port)
    {
        var sources = new List<(byte[] Data, uint Format, float Gain)>();
        foreach (var obj in port.Objects.Values)
        {
            if (obj.PcmQueue.Count == 0)
            {
                continue;
            }

            var frame = obj.PcmQueue.Dequeue();
            sources.Add((frame.Data, frame.Format, ReadGain(obj)));
        }

        var remove = new List<uint>();
        foreach (var (id, obj) in port.Objects)
        {
            if (obj.Unreserved && obj.PcmQueue.Count == 0)
            {
                remove.Add(id);
            }
        }

        foreach (var id in remove)
        {
            port.Objects.Remove(id);
        }

        port.MixedQueue.Enqueue(MixStereoFrames(sources, port.Granularity));
    }

    [SysAbiExport(
        Nid = "lw0qrdSjZt8",
        ExportName = "sceAudio3dPortAdvance",
        Target = Generation.Gen4,
        LibraryName = "libSceAudio3d")]
    public static int PortAdvance(CpuContext ctx)
    {
        var portId = unchecked((uint)ctx[CpuRegister.Rdi]);
        lock (_stateGate)
        {
            if (!_ports.TryGetValue(portId, out var port))
            {
                return ctx.SetReturn(Audio3dErrorInvalidPort);
            }

            if (port.BufferMode == BufferModeNoAdvance)
            {
                return ctx.SetReturn(Audio3dErrorNotSupported);
            }

            if (port.MixedQueue.Count >= port.QueueDepth)
            {
                return ctx.SetReturn(Audio3dErrorNotReady);
            }

            MixPortFrame(port);
            return ctx.SetReturn(0);
        }
    }

    [SysAbiExport(
        Nid = "ZOGrxWLgQzE",
        ExportName = "sceAudio3dPortFlush",
        Target = Generation.Gen4,
        LibraryName = "libSceAudio3d")]
    public static int PortFlush(CpuContext ctx)
    {
        var portId = unchecked((uint)ctx[CpuRegister.Rdi]);
        lock (_stateGate)
        {
            if (!_ports.TryGetValue(portId, out var port))
            {
                return ctx.SetReturn(Audio3dErrorInvalidPort);
            }

            var hasPending = false;
            foreach (var obj in port.Objects.Values)
            {
                if (obj.PcmQueue.Count != 0)
                {
                    hasPending = true;
                    break;
                }
            }

            if (port.MixedQueue.Count == 0 && hasPending)
            {
                MixPortFrame(port);
            }

            if (port.MixedQueue.Count == 0)
            {
                return ctx.SetReturn(0);
            }

            port.MixedQueue.Clear();
            return ctx.SetReturn(0);
        }
    }

    [SysAbiExport(
        Nid = "VEVhZ9qd4ZY",
        ExportName = "sceAudio3dPortPush",
        Target = Generation.Gen4,
        LibraryName = "libSceAudio3d")]
    public static int PortPush(CpuContext ctx)
    {
        var portId = unchecked((uint)ctx[CpuRegister.Rdi]);
        var blocking = unchecked((uint)ctx[CpuRegister.Rsi]);
        lock (_stateGate)
        {
            if (!_ports.TryGetValue(portId, out var port))
            {
                return ctx.SetReturn(Audio3dErrorInvalidPort);
            }

            if (port.BufferMode != BufferModeAdvanceAndPush)
            {
                return ctx.SetReturn(Audio3dErrorNotSupported);
            }

            if (port.MixedQueue.Count < port.QueueDepth)
            {
                return ctx.SetReturn(0);
            }

            // No host 3D backend is wired yet: submitted frames are released
            // after queue accounting instead of reaching audio output.
            port.MixedQueue.Dequeue();
            TracePushDropped(portId);
            if (blocking == 0)
            {
                return ctx.SetReturn(0);
            }

            while (port.MixedQueue.Count >= port.QueueDepth)
            {
                port.MixedQueue.Dequeue();
                TracePushDropped(portId);
            }

            return ctx.SetReturn(0);
        }
    }

    public static void ResetRuntimeState()
    {
        lock (_stateGate)
        {
            _initialized = false;
            _ports.Clear();
        }

        Interlocked.Exchange(ref _pushDropTraceCount, 0);
    }

    [SysAbiExport(
        Nid = "UmCvjSmuZIw",
        ExportName = "sceAudio3dInitialize",
        Target = Generation.Gen4,
        LibraryName = "libSceAudio3d")]
    public static int Initialize(CpuContext ctx)
    {
        if (ctx[CpuRegister.Rdi] != 0)
        {
            return ctx.SetReturn(Audio3dErrorInvalidParameter);
        }

        lock (_stateGate)
        {
            if (_initialized)
            {
                return ctx.SetReturn(Audio3dErrorNotReady);
            }

            _initialized = true;
        }

        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "WW1TS2iz5yc",
        ExportName = "sceAudio3dTerminate",
        Target = Generation.Gen4,
        LibraryName = "libSceAudio3d")]
    public static int Terminate(CpuContext ctx)
    {
        lock (_stateGate)
        {
            if (!_initialized)
            {
                return ctx.SetReturn(Audio3dErrorNotReady);
            }

            _ports.Clear();
            _initialized = false;
        }

        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "XeDDK0xJWQA",
        ExportName = "sceAudio3dPortOpen",
        Target = Generation.Gen4,
        LibraryName = "libSceAudio3d")]
    public static int PortOpen(CpuContext ctx)
    {
        var userId = ctx[CpuRegister.Rdi];
        var parametersAddress = ctx[CpuRegister.Rsi];
        var portIdAddress = ctx[CpuRegister.Rdx];
        if (userId != 0xFF || parametersAddress == 0 || portIdAddress == 0)
        {
            if (portIdAddress != 0 && !TryWriteUInt32(ctx, portIdAddress, InvalidPortId))
            {
                return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
            }

            return ctx.SetReturn(Audio3dErrorInvalidParameter);
        }

        if (!TryReadUInt64(ctx, parametersAddress, out var sizeThis))
        {
            return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
        }

        // The guest selects how many trailing fields are present.
        var granularity = 0u;
        var rate = 0u;
        var maxObjects = 512u;
        var queueDepth = 2u;
        var bufferMode = 0u;
        var numBeds = 2u;
        switch (sizeThis & ~0x7UL)
        {
            case 0x10:
                break;
            case 0x18:
                bufferMode = 1;
                break;
            case 0x20:
            case 0x28:
                break;
            default:
                if (!TryWriteUInt32(ctx, portIdAddress, InvalidPortId))
                {
                    return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
                }

                return ctx.SetReturn(Audio3dErrorInvalidParameter);
        }

        if (!TryReadUInt32(ctx, parametersAddress + 8, out granularity) ||
            !TryReadUInt32(ctx, parametersAddress + 12, out rate))
        {
            return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
        }

        if ((sizeThis & ~0x7UL) is 0x18 or 0x20 or 0x28)
        {
            if (!TryReadUInt32(ctx, parametersAddress + 16, out maxObjects) ||
                !TryReadUInt32(ctx, parametersAddress + 20, out queueDepth))
            {
                return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
            }
        }

        if ((sizeThis & ~0x7UL) is 0x20 or 0x28)
        {
            if (!TryReadUInt32(ctx, parametersAddress + 24, out bufferMode))
            {
                return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
            }
        }

        if ((sizeThis & ~0x7UL) == 0x28)
        {
            if (!TryReadUInt32(ctx, parametersAddress + 32, out numBeds))
            {
                return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
            }
        }

        if (rate != 0 ||
            granularity < 0x100 || (granularity & 0xFF) != 0 ||
            maxObjects == 0 || queueDepth == 0 ||
            (granularity == 0x100 && queueDepth > 0x40) ||
            (granularity == 0x200 && queueDepth > 0x1F) ||
            (granularity == 0x300 && queueDepth > 0x14) ||
            (queueDepth > 0xF && granularity > 0x3FF) ||
            bufferMode > 2 || (numBeds & ~1u) != 2)
        {
            if (!TryWriteUInt32(ctx, portIdAddress, InvalidPortId))
            {
                return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
            }

            return ctx.SetReturn(Audio3dErrorInvalidParameter);
        }

        if (maxObjects > MaxObjects)
        {
            TraceAudio3d($"port-open max-objects={maxObjects} exceeds limit={MaxObjects}");
            if (!TryWriteUInt32(ctx, portIdAddress, InvalidPortId))
            {
                return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
            }

            return ctx.SetReturn(Audio3dErrorOutOfResources);
        }

        lock (_stateGate)
        {
            if (!_initialized)
            {
                return ctx.SetReturn(Audio3dErrorNotReady);
            }

            uint id = InvalidPortId;
            for (var candidate = 0u; candidate < MaxPorts; candidate++)
            {
                if (!_ports.ContainsKey(candidate))
                {
                    id = candidate;
                    break;
                }
            }

            if (id == InvalidPortId)
            {
                if (!TryWriteUInt32(ctx, portIdAddress, InvalidPortId))
                {
                    return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
                }

                return ctx.SetReturn(Audio3dErrorOutOfResources);
            }

            _ports[id] = new Audio3dPort
            {
                Granularity = granularity,
                MaxObjects = maxObjects,
                QueueDepth = queueDepth,
                BufferMode = bufferMode,
            };

            if (!TryWriteUInt32(ctx, portIdAddress, id))
            {
                _ports.Remove(id);
                return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
            }

            return ctx.SetReturn(0);
        }
    }

    [SysAbiExport(
        Nid = "OyVqOeVNtSk",
        ExportName = "sceAudio3dPortClose",
        Target = Generation.Gen4,
        LibraryName = "libSceAudio3d")]
    public static int PortClose(CpuContext ctx)
    {
        var portId = unchecked((uint)ctx[CpuRegister.Rdi]);
        lock (_stateGate)
        {
            if (!_ports.Remove(portId))
            {
                return ctx.SetReturn(Audio3dErrorInvalidPort);
            }
        }

        return ctx.SetReturn(0);
    }

    [SysAbiExport(
        Nid = "jO2tec4dJ2M",
        ExportName = "sceAudio3dObjectReserve",
        Target = Generation.Gen4,
        LibraryName = "libSceAudio3d")]
    public static int ObjectReserve(CpuContext ctx)
    {
        var portId = unchecked((uint)ctx[CpuRegister.Rdi]);
        var objectIdAddress = ctx[CpuRegister.Rsi];
        if (objectIdAddress == 0)
        {
            return ctx.SetReturn(Audio3dErrorInvalidParameter);
        }

        if (!TryWriteUInt32(ctx, objectIdAddress, InvalidObjectId))
        {
            return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
        }

        lock (_stateGate)
        {
            if (!_ports.TryGetValue(portId, out var port))
            {
                return ctx.SetReturn(Audio3dErrorInvalidPort);
            }

            if (port.Objects.Count >= port.MaxObjects)
            {
                return ctx.SetReturn(Audio3dErrorOutOfResources);
            }

            do
            {
                port.NextObjectId++;
            } while (port.NextObjectId == 0 || port.NextObjectId == InvalidObjectId ||
                port.Objects.ContainsKey(port.NextObjectId));

            port.Objects.Add(port.NextObjectId, new Audio3dObject());
            if (!TryWriteUInt32(ctx, objectIdAddress, port.NextObjectId))
            {
                port.Objects.Remove(port.NextObjectId);
                return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
            }

            return ctx.SetReturn(0);
        }
    }

    [SysAbiExport(
        Nid = "1HXxo-+1qCw",
        ExportName = "sceAudio3dObjectUnreserve",
        Target = Generation.Gen4,
        LibraryName = "libSceAudio3d")]
    public static int ObjectUnreserve(CpuContext ctx)
    {
        var portId = unchecked((uint)ctx[CpuRegister.Rdi]);
        var objectId = unchecked((uint)ctx[CpuRegister.Rsi]);
        lock (_stateGate)
        {
            if (!_ports.TryGetValue(portId, out var port))
            {
                return ctx.SetReturn(Audio3dErrorInvalidPort);
            }

            if (!port.Objects.TryGetValue(objectId, out var obj) || obj.Unreserved)
            {
                return ctx.SetReturn(Audio3dErrorInvalidObject);
            }

            if (obj.PcmQueue.Count == 0)
            {
                port.Objects.Remove(objectId);
                return ctx.SetReturn(0);
            }

            obj.Unreserved = true;
            return ctx.SetReturn(0);
        }
    }

    [SysAbiExport(
        Nid = "4uyHN9q4ZeU",
        ExportName = "sceAudio3dObjectSetAttributes",
        Target = Generation.Gen4,
        LibraryName = "libSceAudio3d")]
    public static int ObjectSetAttributes(CpuContext ctx)
    {
        var portId = unchecked((uint)ctx[CpuRegister.Rdi]);
        var objectId = unchecked((uint)ctx[CpuRegister.Rsi]);
        var count = ctx[CpuRegister.Rdx];
        var arrayAddress = ctx[CpuRegister.Rcx];
        lock (_stateGate)
        {
            if (!_ports.TryGetValue(portId, out var port))
            {
                return ctx.SetReturn(Audio3dErrorInvalidPort);
            }

            if (count == 0 || arrayAddress == 0)
            {
                return ctx.SetReturn(Audio3dErrorInvalidParameter);
            }

            if (!port.Objects.TryGetValue(objectId, out var obj))
            {
                return ctx.SetReturn(Audio3dErrorInvalidObject);
            }

            if (obj.Unreserved)
            {
                return ctx.SetReturn(Audio3dErrorInvalidObject);
            }

            var entries = ReadAttributes(ctx, arrayAddress, count);
            if (entries is null)
            {
                return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
            }

            foreach (var (id, _, _) in entries)
            {
                if (id != AttributeResetState)
                {
                    continue;
                }

                obj.PcmQueue.Clear();
                obj.Attributes.Clear();
                break;
            }

            foreach (var (id, valueAddress, valueSize) in entries)
            {
                if (id == AttributeResetState)
                {
                    continue;
                }

                if (id == AttributePcm)
                {
                    var result = EnqueuePcm(ctx, port, obj, valueAddress, valueSize);
                    if (result != 0)
                    {
                        return ctx.SetReturn(result);
                    }

                    continue;
                }

                if (valueAddress != 0 && valueSize != 0)
                {
                    var bytes = new byte[valueSize];
                    if (!ctx.Memory.TryRead(valueAddress, bytes))
                    {
                        return ctx.SetReturn((int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT);
                    }

                    obj.Attributes[id] = bytes;
                }
            }

            return ctx.SetReturn(0);
        }
    }

    private static List<(uint Id, ulong ValueAddress, ulong ValueSize)>? ReadAttributes(
        CpuContext ctx, ulong arrayAddress, ulong count)
    {
        if (count > MaxObjects)
        {
            return null;
        }

        var entries = new List<(uint, ulong, ulong)>((int)count);
        Span<byte> entry = stackalloc byte[AttributeSize];
        for (var i = 0UL; i < count; i++)
        {
            if (!ctx.Memory.TryRead(arrayAddress + i * AttributeSize, entry))
            {
                return null;
            }

            entries.Add((
                BinaryPrimitives.ReadUInt32LittleEndian(entry),
                BinaryPrimitives.ReadUInt64LittleEndian(entry.Slice(8, 8)),
                BinaryPrimitives.ReadUInt64LittleEndian(entry.Slice(16, 8))));
        }

        return entries;
    }

    private static int EnqueuePcm(
        CpuContext ctx, Audio3dPort port, Audio3dObject obj, ulong valueAddress, ulong valueSize)
    {
        if (valueSize < PcmDescriptorSize)
        {
            TraceAudio3d($"object-set-attributes short-pcm-descriptor size={valueSize} expected={PcmDescriptorSize}");
            return Audio3dErrorInvalidParameter;
        }

        Span<byte> descriptor = stackalloc byte[PcmDescriptorSize];
        if (!ctx.Memory.TryRead(valueAddress, descriptor))
        {
            return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT;
        }

        var format = BinaryPrimitives.ReadUInt32LittleEndian(descriptor);
        var bufferAddress = BinaryPrimitives.ReadUInt64LittleEndian(descriptor.Slice(8, 8));
        var numSamples = BinaryPrimitives.ReadUInt32LittleEndian(descriptor.Slice(16, 4));
        if (format != PcmFormatS16 && format != PcmFormatFloat)
        {
            TraceAudio3d($"object-set-attributes unsupported-pcm-format format={format}");
            return Audio3dErrorInvalidParameter;
        }

        if (bufferAddress == 0 || numSamples == 0)
        {
            return Audio3dErrorInvalidParameter;
        }

        var bytesPerSample = format == PcmFormatFloat ? sizeof(float) : sizeof(short);
        if ((bufferAddress & (ulong)(bytesPerSample - 1)) != 0)
        {
            return Audio3dErrorInvalidParameter;
        }

        if (obj.PcmQueue.Count >= port.QueueDepth)
        {
            return Audio3dErrorNotReady;
        }

        var frameBytes = new byte[port.Granularity * bytesPerSample];
        var copySamples = Math.Min(numSamples, port.Granularity);
        if (!ctx.Memory.TryRead(bufferAddress, frameBytes.AsSpan(0, (int)(copySamples * bytesPerSample))))
        {
            return (int)OrbisGen2Result.ORBIS_GEN2_ERROR_MEMORY_FAULT;
        }

        obj.PcmQueue.Enqueue(new PcmFrame { Data = frameBytes, Format = format });
        return 0;
    }

    private static bool TryReadUInt32(CpuContext ctx, ulong address, out uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        if (!ctx.Memory.TryRead(address, bytes))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        return true;
    }

    private static bool TryReadUInt64(CpuContext ctx, ulong address, out ulong value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        if (!ctx.Memory.TryRead(address, bytes))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        return true;
    }

    private static bool TryWriteUInt32(CpuContext ctx, ulong address, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return ctx.Memory.TryWrite(address, bytes);
    }

    private static void TraceAudio3d(string message)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("SHARPEMU_LOG_AUDIO3D"), "1", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"[LOADER][TRACE] audio3d.{message}");
        }
    }

    private static void TracePushDropped(uint portId)
    {
        var n = Interlocked.Increment(ref _pushDropTraceCount);
        if (n <= 8 || n % 500 == 0)
        {
            TraceAudio3d($"port-push#{n} port={portId} frames-released-without-audio-output reason=no-host-backend");
        }
    }
}
