// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System.Buffers.Binary;
using SharpEmu.HLE;
using SharpEmu.ShaderCompiler;
using SharpEmu.ShaderCompiler.Resources;
using SharpEmu.ShaderCompiler.Tests.Resources;
using SharpEmu.ShaderCompiler.Vulkan;
using Xunit;
using static SharpEmu.ShaderCompiler.Tests.Resources.ResourceTestProgram;

namespace SharpEmu.ShaderCompiler.Tests;

public sealed class Gen5DataShareSwizzleTests
{
    [Theory]
    [InlineData(0x801Bu)]
    [InlineData(0x041Fu)]
    [InlineData(0xC020u)]
    [InlineData(0xC420u)]
    [InlineData(0xC021u)]
    [InlineData(0x0050u)]
    [InlineData(0x8000u)]
    public void SupportedModesCompile(uint selection)
    {
        var instruction = DecodeSwizzle(selection);
        Assert.Equal("DsSwizzleB32", instruction.Opcode);
        Assert.Equal(new[] { Gen5Operand.Vector(4) }, instruction.Sources);
        Assert.Equal(new[] { Gen5Operand.Vector(4) }, instruction.Destinations);
        Assert.Equal(selection, Assert.IsType<Gen5DataShareControl>(instruction.Control).SingleOffsetBytes);
        var request = Request(CreateReadbackProgram(selection, true));
        Assert.True(Gen5SpirvTranslator.TryCompileProgram(request, out _, out var spirvError), spirvError);
    }

    private static Gen5ShaderInstruction DecodeSwizzle(uint selection)
    {
        uint[] words = [0xD8D40000u | selection, (4u << 24) | 4u, 0xBF810000];
        var bytes = new byte[words.Length * sizeof(uint)];
        for (var index = 0; index < words.Length; index++)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * sizeof(uint)), words[index]);
        Assert.True(Gen5ShaderTranslator.TryDecodeProgram(new CpuContext(new InstructionMemory(bytes), Generation.Gen5),
            0x1000, out var program, out var error), error);
        return program.Instructions[0];
    }

    [Theory]
    [InlineData(0xE000u, false, "unsupported FFT")]
    [InlineData(0xFFFFu, false, "unsupported FFT")]
    [InlineData(0x801Bu, true, "not valid on the global data share")]
    public void UnsupportedFormsRemainRejected(uint selection, bool globalShare, string message)
    {
        var instruction = DataShare(0, "DsSwizzleB32", globalShare, [Gen5Operand.Vector(4)],
            [4], selection & 255, selection >> 8);
        var request = Request(Program(instruction, EndProgram(8)));
        Assert.False(Gen5SpirvTranslator.TryCompileProgram(request, out _, out var spirvError));
        Assert.Contains(message, spirvError);
    }

    [Theory]
    [InlineData(0x041Fu, false)]
    [InlineData(0x041Fu, true)]
    [InlineData(0xC020u, false)]
    [InlineData(0xC020u, true)]
    [InlineData(0x8000u, false)]
    [InlineData(0x8000u, true)]
    public void SwizzleWithoutGraphicsSubgroups_EmitsNoShuffle(uint selection, bool enableGraphicsSubgroupOperations)
    {
        // A pixel stage without native subgroup support is a one-lane wave: DS_SWIZZLE_B32
        // reads its own lane, so no shuffle (and no GroupNonUniformShuffle capability) is emitted.
        var program = Program(DecodeSwizzle(selection), EndProgram(8));
        var (plan, resources, layout) = Prepare(program, ShaderStage.Pixel, userDataCount: 0);
        var request = new ShaderCompileRequest(plan, resources, layout)
        {
            PixelOutputs = [new Gen5PixelOutputBinding(0, 0, Gen5PixelOutputKind.Float)],
            EnableGraphicsSubgroupOperations = enableGraphicsSubgroupOperations,
        };
        Assert.True(Gen5SpirvTranslator.TryCompileProgram(request, out var shader, out var error), error);

        var module = new SpirvModuleInspector(shader.Spirv);
        Assert.Equal(
            module.Opcodes.Contains((ushort)SpirvOp.GroupNonUniformShuffle),
            module.Capabilities.Contains((uint)SpirvCapability.GroupNonUniformShuffle));
        Assert.Equal(enableGraphicsSubgroupOperations, module.Opcodes.Contains((ushort)SpirvOp.GroupNonUniformShuffle));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BpermuteWithoutGraphicsSubgroups_EmitsNoShuffle(bool enableGraphicsSubgroupOperations)
    {
        // DS_BPERMUTE_B32 shares the swizzle fallback: without subgroup lane access it reads its own lane.
        var program = Program(
            DataShare(0, "DsBpermuteB32", gds: false, [Gen5Operand.Vector(0), Gen5Operand.Vector(1)], [2]),
            EndProgram(8));
        var (plan, resources, layout) = Prepare(program, ShaderStage.Pixel, userDataCount: 0);
        var request = new ShaderCompileRequest(plan, resources, layout)
        {
            PixelOutputs = [new Gen5PixelOutputBinding(0, 0, Gen5PixelOutputKind.Float)],
            EnableGraphicsSubgroupOperations = enableGraphicsSubgroupOperations,
        };
        Assert.True(Gen5SpirvTranslator.TryCompileProgram(request, out var shader, out var error), error);

        var module = new SpirvModuleInspector(shader.Spirv);
        Assert.Equal(
            module.Opcodes.Contains((ushort)SpirvOp.GroupNonUniformShuffle),
            module.Capabilities.Contains((uint)SpirvCapability.GroupNonUniformShuffle));
        Assert.Equal(enableGraphicsSubgroupOperations, module.Opcodes.Contains((ushort)SpirvOp.GroupNonUniformShuffle));
    }

    public static Gen5ShaderProgram CreateReadbackProgram(uint selection, bool maskOddLanes)
    {
        return Program(
            Vop2(0, "VAddI32", 4, Operand(1000), Gen5Operand.Vector(0)),
            Vop2(4, "VAndB32", 6, Operand(maskOddLanes ? 1u : 0u), Gen5Operand.Vector(0)),
            Vopc(8, "VCmpxEqU32", Operand(0), 6),
            DecodeSwizzle(selection) with { Pc = 12 },
            MoveScalar(20, 126, uint.MaxValue),
            MoveScalar(24, 127, uint.MaxValue),
            Vop2(28, "VLshlrevB32", 5, Operand(2), Gen5Operand.Vector(0)),
            BufferAccess(32, "BufferStoreDword", 4, vectorData: 4, offsetEnabled: true, vectorAddress: 5),
            EndProgram(40));
    }

    private sealed class InstructionMemory(byte[] bytes) : ICpuMemory
    {
        public bool TryRead(ulong address, Span<byte> destination)
        {
            if (address < 0x1000 || destination.Length > bytes.Length ||
                address - 0x1000 > (ulong)(bytes.Length - destination.Length)) return false;
            bytes.AsSpan((int)(address - 0x1000), destination.Length).CopyTo(destination);
            return true;
        }

        public bool TryWrite(ulong address, ReadOnlySpan<byte> source) => false;
    }
}
