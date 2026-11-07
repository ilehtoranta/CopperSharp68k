using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class OneHotShiftTests
{
	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040)]
	public void LoadedArgumentsLeaveRoomForTheLiveAddressAcrossArgumentEvaluation(M68kCpuTarget target, M68kCpuModel model)
	{
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
		{
			var result = AmigaM68kCompiler.Compile(new() {
				AssemblyPath = typeof(OneHotShiftFixture).Assembly.Location,
				EntryPoint = typeof(OneHotShiftFixture).FullName + "::Empty", IncludedExportNames = ["onehot.pressure"], Cpu = target,
				MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
				RuntimeProfile = M68kRuntimeProfile.Freestanding, OutputFormat = M68kOutputFormat.Hunk, PeepholeOptimization = mode
			});
			var bus = new TestBus(0x100000); result.Code.CopyTo(bus.Memory.AsSpan(0x10000));
			foreach (var relocation in result.Relocations) {
				var at = 0x10000u + (uint)relocation.Offset; bus.WriteLong(at, bus.ReadLong(at) + 0x10000);
			}
			using var cpu = M68kCoreFactory.Default.Create(model, bus);
			foreach (var pointer in new uint[] { 0x71000, 0x72000 })
			foreach (var value in new uint[] { 0, 1, 0x80004000, 0xDEAD0001 })
			{
				bus.WriteLong(0x80000, 0x1000); bus.WriteLong(0x50000, 0x70000);
				bus.WriteLong(0x50008, pointer); bus.WriteLong(0x70004, value);
				cpu.Reset(0x10000 + result.Symbols.Single(s => s.Name == "onehot.pressure").Address, 0x80000);
				cpu.State.A[0] = 0x50000;
				for (var r = 2; r < 8; r++) cpu.State.D[r] = 0xD0000000u + (uint)r;
				for (var r = 2; r < 6; r++) cpu.State.A[r] = 0xA0000000u + (uint)r;
				cpu.State.A[6] = 0x50000;
				var minimumStack = 0x80000u;
				for (var n = 0; n < 1000 && cpu.State.ProgramCounter != 0x1000; n++) {
					cpu.ExecuteInstruction(); minimumStack = Math.Min(minimumStack, cpu.State.A[7]);
				}
				Assert.Equal(0x1000u, cpu.State.ProgramCounter); Assert.Equal(0x80004u, cpu.State.A[7]);
				Assert.Equal(value, cpu.State.D[0]); Assert.Equal(value, bus.ReadLong(pointer));
				Assert.Equal((byte)0x7F, bus.Memory[0x70000]);
				for (var r = 2; r < 8; r++) Assert.Equal(0xD0000000u + (uint)r, cpu.State.D[r]);
				for (var r = 2; r < 6; r++) Assert.Equal(0xA0000000u + (uint)r, cpu.State.A[r]);
				Assert.Equal(0x50000u, cpu.State.A[6]); Assert.InRange(0x80000u - minimumStack, 0u, 12u);
			}
		}
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040)]
	public void CallbackArgumentsSurviveMaskConstructionWithoutAnExtraSavedTemporary(M68kCpuTarget target, M68kCpuModel model)
	{
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
		{
			var result = AmigaM68kCompiler.Compile(new() {
				AssemblyPath = typeof(OneHotShiftFixture).Assembly.Location,
				EntryPoint = typeof(OneHotShiftFixture).FullName + "::Empty",
				IncludedExportNames = ["onehot.dispatch"], Cpu = target,
				MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
				RuntimeProfile = M68kRuntimeProfile.Freestanding, OutputFormat = M68kOutputFormat.Hunk,
				PeepholeOptimization = mode
			});
			var trace = new CallbackReadBus();
			var bus = trace.Inner;
			result.Code.CopyTo(bus.Memory.AsSpan(0x10000));
			foreach (var relocation in result.Relocations) {
				var at = 0x10000u + (uint)relocation.Offset;
				bus.WriteLong(at, bus.ReadLong(at) + 0x10000);
			}
			// The guest callback captures both volatile arguments, then clobbers
			// its scratch registers. The caller must finish with its own context.
			ushort[] callback = [0x23C9, 0x0005, 0x0010, 0x23C0, 0x0005, 0x0014,
				0x7000, 0x7200, 0x207C, 0xDEAD, 0x0000, 0x227C, 0xBEEF, 0x0000, 0x4E75];
			for (var i = 0; i < callback.Length; i++) bus.WriteWord(0x60000u + (uint)(i * 2), callback[i]);
			using var cpu = M68kCoreFactory.Default.Create(model, trace);
			foreach (var count in new byte[] { 0, 1, 7, 31, 32, 63, 128, 255 })
			{
				bus.WriteLong(0x80000, 0x1000);
				bus.WriteLong(0x50000, 0xFEDC1234);
				bus.Memory[0x50004] = count; bus.WriteLong(0x50008, 0x60000);
				trace.Reads.Clear();
				cpu.Reset(0x10000 + result.Symbols.Single(s => s.Name == "onehot.dispatch").Address, 0x80000);
				for (var r = 2; r < 8; r++) cpu.State.D[r] = 0xD0000000u + (uint)r;
				for (var r = 2; r < 6; r++) cpu.State.A[r] = 0xA0000000u + (uint)r;
				cpu.State.A[6] = 0x50000;
				var minimumStack = 0x80000u;
				for (var n = 0; n < 1000 && cpu.State.ProgramCounter != 0x1000; n++) {
					cpu.ExecuteInstruction(); minimumStack = Math.Min(minimumStack, cpu.State.A[7]);
				}
				Assert.Equal(0x1000u, cpu.State.ProgramCounter); Assert.Equal(0x80004u, cpu.State.A[7]);
				Assert.Equal(0xFEDC1234u, bus.ReadLong(0x50010));
				Assert.Equal(1u << count, bus.ReadLong(0x50014));
				Assert.Equal(0xFEDC1234u, cpu.State.D[0]);
				for (var r = 2; r < 8; r++) Assert.Equal(0xD0000000u + (uint)r, cpu.State.D[r]);
				for (var r = 2; r < 6; r++) Assert.Equal(0xA0000000u + (uint)r, cpu.State.A[r]);
				Assert.Equal(0x50000u, cpu.State.A[6]);
				Assert.Equal(new[] { (0x50008u, 4), (0x50000u, 4), (0x50004u, 1), (0x50010u, 4) }, trace.Reads);
				Assert.InRange(0x80000u - minimumStack, 0u, 4u);
			}
		}
	}

	private sealed class CallbackReadBus : IM68kBus
	{
		public readonly TestBus Inner = new(0x100000);
		public readonly List<(uint Address, int Width)> Reads = new();
		public void ResetExternalDevices(long cycle) => Inner.ResetExternalDevices(cycle);
		private void Read(uint at, int width) { if (at >= 0x50000 && at < 0x50018) Reads.Add((at, width)); }
		public byte ReadByte(uint at, ref long cycle, M68kBusAccessKind kind) { Read(at, 1); return Inner.ReadByte(at, ref cycle, kind); }
		public ushort ReadWord(uint at, ref long cycle, M68kBusAccessKind kind) { Read(at, 2); return Inner.ReadWord(at, ref cycle, kind); }
		public uint ReadLong(uint at, ref long cycle, M68kBusAccessKind kind) { Read(at, 4); return Inner.ReadLong(at, ref cycle, kind); }
		public void WriteByte(uint at, byte value, ref long cycle, M68kBusAccessKind kind) => Inner.WriteByte(at, value, ref cycle, kind);
		public void WriteWord(uint at, ushort value, ref long cycle, M68kBusAccessKind kind) => Inner.WriteWord(at, value, ref cycle, kind);
		public void WriteLong(uint at, uint value, ref long cycle, M68kBusAccessKind kind) => Inner.WriteLong(at, value, ref cycle, kind);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040)]
	public void MasksWrapCountsPreserveLiveInputsAndUseCorrectBranchFlags(M68kCpuTarget target, M68kCpuModel model)
	{
		string[] names = ["mask", "signed", "byte", "sbyte", "byte-load", "masked-live", "masked-seven", "live", "branch", "shared", "other-left", "right", "wide"];
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
		{
			var result = AmigaM68kCompiler.Compile(new() {
				AssemblyPath = typeof(OneHotShiftFixture).Assembly.Location,
				EntryPoint = typeof(OneHotShiftFixture).FullName + "::Empty",
				IncludedExportNames = names.Select(n => "onehot." + n).ToArray(), Cpu = target,
				MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
				RuntimeProfile = M68kRuntimeProfile.Freestanding, OutputFormat = M68kOutputFormat.Hunk,
				PeepholeOptimization = mode
			});
			var bus = new TestBus(0x100000);
			result.Code.CopyTo(bus.Memory.AsSpan(0x10000));
			foreach (var relocation in result.Relocations) {
				var at = 0x10000u + (uint)relocation.Offset;
				bus.WriteLong(at, bus.ReadLong(at) + 0x10000);
			}
			using var cpu = M68kCoreFactory.Default.Create(model, bus);
			foreach (var name in names)
			{
				var sawBitSet = false;
				var sawCountMask = false;
				foreach (var count in Enumerable.Range(-65, 131).Concat(new[] { 255, 256, int.MinValue, int.MaxValue }))
				for (ushort flags = 0; flags < 32; flags++)
				{
					bus.WriteLong(0x80000, 0x1000);
					cpu.Reset(0x10000 + result.Symbols.Single(s => s.Name == "onehot." + name).Address, 0x80000);
					cpu.State.StatusRegister = (ushort)(0x2000 | flags);
					cpu.State.D[0] = unchecked((uint)count);
					for (var r = 2; r < 8; r++) cpu.State.D[r] = 0xD0000000u + (uint)r;
					for (var r = 2; r < 7; r++) cpu.State.A[r] = 0xA0000000u + (uint)r;
					cpu.State.A[6] = 0x50000;
					bus.WriteLong(0x50000, 0xA5A5A5A5); bus.WriteLong(0x50004, 0x5A5A5A5A);
					if (name == "byte-load") bus.Memory[0x50000] = unchecked((byte)count);
					for (var n = 0; n < 1000 && cpu.State.ProgramCounter != 0x1000; n++) {
						var opcode = bus.ReadWord(cpu.State.ProgramCounter);
						sawBitSet |= (opcode & 0xF1F8) == 0x01C0;
						sawCountMask |= (opcode & 0xF1F8) == 0xC080 ||
							((opcode & 0xFFF8) == 0x0280 && bus.ReadLong(cpu.State.ProgramCounter + 2) == 31);
						cpu.ExecuteInstruction();
					}
					Assert.Equal(0x1000u, cpu.State.ProgramCounter); Assert.Equal(0x80004u, cpu.State.A[7]);
					var mask = 1u << count;
					if (name == "wide") {
						var wide = 1ul << count;
						Assert.Equal((uint)(wide >> 32), bus.ReadLong(0x50000)); Assert.Equal((uint)wide, bus.ReadLong(0x50004));
					} else {
						var expected = name switch {
							"live" => mask ^ unchecked((uint)count), "shared" => mask + 1,
							"masked-live" => mask ^ unchecked((uint)(count & 31)),
							"masked-seven" => 1u << (count & 7),
							"branch" => (int)mask < 0 ? 0x12345678u : mask == 0 ? 0xABCDEF01u : mask + 3,
							"other-left" => 2u << count, "right" => 1u >> count, _ => mask
						};
						Assert.Equal(expected, cpu.State.D[0]);
					}
					for (var r = 2; r < 8; r++) Assert.Equal(0xD0000000u + (uint)r, cpu.State.D[r]);
					for (var r = 2; r < 6; r++) Assert.Equal(0xA0000000u + (uint)r, cpu.State.A[r]);
					Assert.Equal(0x50000u, cpu.State.A[6]);
				}
				if (name is "mask" or "signed" or "live" or "shared") Assert.True(sawBitSet, name);
				else if (name is "other-left" or "right") Assert.False(sawBitSet, name);
				if (name is "mask" or "signed" or "byte-load") Assert.False(sawCountMask, "BSET already wraps the count: " + name);
			}
		}
	}
}
