using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Copper68k;
using CopperSharp.Runtime;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	[Theory]
	[InlineData(M68kRuntimeProfile.Application)]
	[InlineData(M68kRuntimeProfile.Freestanding)]
	public void ConsoleSizeSharingPreservesEveryWriteFailureAndDosOwnership(M68kRuntimeProfile profile)
	{
		foreach (var policy in new M68kCodeSizeOptions?[] { null, new() })
		{
			var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest {
				AssemblyPath = FixtureAssembly, EntryPoint = typeof(ConsolePackingSizeFixtures).FullName + "::WriteSequence",
				Cpu = M68kCpuTarget.M68000, OutputFormat = M68kOutputFormat.Hunk,
				RuntimeProfile = profile, ExceptionMode = M68kExceptionMode.Full,
				IncludedExportNames = [], Hunk = new() { IncludeSymbols = false }, CodeSizeOptimizations = policy });
			foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
			// -2: library open fails; -1: no output handle; 0: success; 1..9: short write.
			foreach (var failure in Enumerable.Range(-2, 12))
			{
				const uint execBase = 0x3000, dosBase = 0x5000, previous = 0x7000;
				var bus = CreateHunkBus(result);
				bus.WriteLong(4, execBase);
				var slot = HunkLoadAddress + result.Symbols.Single(symbol => symbol.Name == AmigaLibraryBaseSymbols.For("dos.library")).Address;
				bus.WriteLong(slot, previous);
				var opens = 0; var closes = 0; var outputs = 0; var writes = 0;
				var bytes = new List<byte>();
				bus.RegisterGateway(execBase - 552, state => { opens++; state.D[0] = failure == -2 ? 0 : dosBase; });
				bus.RegisterGateway(execBase - 414, state => { Assert.Equal(dosBase, state.A[1]); closes++; });
				bus.RegisterGateway(dosBase - 60, state => { outputs++; state.D[0] = failure == -1 ? 0u : 0x123u; });
				bus.RegisterGateway(dosBase - 48, state => {
					Assert.Equal(0x123u, state.D[1]);
					writes++;
					if (writes == failure) { state.D[0] = state.D[3] - 1; return; }
					for (var i = 0u; i < state.D[3]; i++) bytes.Add(bus.Memory[state.D[2] + i]);
					state.D[0] = state.D[3];
				});
				bus.RegisterGateway(execBase - 198, _ => throw new InvalidOperationException("Console output allocated native memory."));
				Assert.Equal(failure == 0 ? 7u : 42u, Execute(bus, model, HunkLoadAddress + result.EntryPoint, maxInstructions: 2_000_000));
				Assert.Equal(previous, bus.ReadLong(slot));
				Assert.Equal(failure == -2 ? 0 : opens, closes);
				if (failure > 0) Assert.Equal(failure, writes);
				if (failure < 0) Assert.Equal(0, writes);
				if (failure == 0)
				{
					Assert.Equal("\n-2147483648\n18446744073709551615\nTrueä123456789012345678901ä", Encoding.Latin1.GetString(bytes.ToArray()));
					Assert.Equal(9, writes);
					Assert.Equal(6, outputs);
					Assert.Equal(profile == M68kRuntimeProfile.Application ? 1 : 6, opens);
				}
			}
		}
	}

	[Fact]
	public void PackedIntegerSizePathsMatchInvariantFormattingAndZeroPadding()
	{
		var values = new HashSet<ulong> { 0, 1, uint.MaxValue, int.MaxValue, 0x8000_0000,
			long.MaxValue, 0x8000_0000_0000_0000, ulong.MaxValue };
		for (ulong power = 10; power <= 10_000_000_000_000_000_000;)
		{
			values.UnionWith([power - 1, power, power + 1]);
			if (power == 10_000_000_000_000_000_000) break;
			power *= 10;
		}
		var random = new Random(0x68000);
		for (var i = 0; i < 10_000; i++)
			values.Add(((ulong)(uint)random.NextInt64(0, 1L << 32) << 32) | (uint)random.NextInt64(0, 1L << 32));
		foreach (var value in values)
		{
			var length = ShadowIntegerFormatter.PackUInt32((uint)value, out var a, out var b, out var c);
			Check(((uint)value).ToString(CultureInfo.InvariantCulture), length, a, b, c);
			length = ShadowIntegerFormatter.PackInt32(unchecked((int)value), out a, out b, out c);
			Check(unchecked((int)value).ToString(CultureInfo.InvariantCulture), length, a, b, c);
			length = ShadowIntegerFormatter.PackUInt64((uint)(value >> 32), (uint)value, out a, out b, out c, out var d, out var e);
			Check(value.ToString(CultureInfo.InvariantCulture), length, a, b, c, d, e);
			length = ShadowIntegerFormatter.PackInt64((uint)(value >> 32), (uint)value, out a, out b, out c, out d, out e);
			Check(unchecked((long)value).ToString(CultureInfo.InvariantCulture), length, a, b, c, d, e);
		}

		static void Check(string expected, int length, params uint[] words)
		{
			Assert.Equal(expected.Length, length);
			var bytes = new byte[words.Length * 4];
			for (var i = 0; i < words.Length; i++) BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i * 4), words[i]);
			Assert.Equal(expected, Encoding.ASCII.GetString(bytes, 0, length));
			Assert.All(bytes.Skip(length), value => Assert.Equal(0, value));
		}
	}

	[Fact]
	public void PackedIntegerSizePathsDoNotAllocate()
	{
		_ = ConsolePackingSizeFixtures.Checksum();
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 10; i++) _ = ConsolePackingSizeFixtures.Checksum();
		Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
	}

	[Theory]
	[InlineData(M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kPeepholeOptimizationMode.Disabled)]
	public void PackedIntegerSizePathsExecuteAtRelocatedBases(M68kPeepholeOptimizationMode mode)
	{
		var expected = ConsolePackingSizeFixtures.Checksum();
		foreach (var policy in new M68kCodeSizeOptions?[] { null, new() })
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest {
				AssemblyPath = FixtureAssembly, EntryPoint = typeof(ConsolePackingSizeFixtures).FullName + "::Checksum",
				Cpu = M68kCpuTarget.M68000, OutputFormat = M68kOutputFormat.Hunk,
				RuntimeProfile = M68kRuntimeProfile.Application, ExceptionMode = M68kExceptionMode.Full,
				IncludedExportNames = [], Hunk = new() { IncludeSymbols = false }, CodeSizeOptimizations = policy,
				PeepholeOptimization = mode });
			Assert.DoesNotContain(M68kRuntimeImports.Allocate, result.Map);
			foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
			foreach (var load in new uint[] { 0x10000, 0x20000 })
				Assert.Equal(expected, Execute(LoadApplicationSizeHunk(result.Image, load, 0x40000), model,
					load + result.EntryPoint, maxInstructions: 20_000_000,
					afterReturn: state => Assert.Equal(StackPointer + 4, state.A[7])));
		}
	}
}

public static class ConsolePackingSizeFixtures
{
	public static int WriteSequence()
	{
		try
		{
			Console.Write((string?)null);
			Console.Write("");
			Console.WriteLine((string?)null);
			Console.WriteLine(int.MinValue);
			Console.WriteLine(ulong.MaxValue);
			Console.Write(true);
			Console.Write('ä');
			Console.Write("123456789012345678901ä");
			return 7;
		}
		catch (IOException) { return 42; }
	}

	public static uint Checksum()
	{
		uint hash = 0x12345678, high = 0, low = 0;
		for (var i = 0; i < 80; i++)
		{
			var length = ShadowIntegerFormatter.PackUInt32(low, out var a, out var b, out var c);
			hash = Mix(hash, length, a, b, c, 0, 0);
			length = ShadowIntegerFormatter.PackInt32(unchecked((int)low), out a, out b, out c);
			hash = Mix(hash, length, a, b, c, 0, 0);
			length = ShadowIntegerFormatter.PackUInt64(high, low, out a, out b, out c, out var d, out var e);
			hash = Mix(hash, length, a, b, c, d, e);
			length = ShadowIntegerFormatter.PackInt64(high, low, out a, out b, out c, out d, out e);
			hash = Mix(hash, length, a, b, c, d, e);
			low = unchecked(low * 1664525 + 1013904223);
			high = unchecked(high * 22695477 + 1);
		}
		return hash;
	}

	private static uint Mix(uint hash, int length, uint a, uint b, uint c, uint d, uint e) =>
		unchecked((hash << 5) ^ (hash >> 27) ^ (uint)length ^ a ^ b ^ c ^ d ^ e);
}
