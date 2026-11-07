/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Runtime;
using CopperSharp.Tools.CilicaAudit;

namespace CopperSharp.Compiler.Tests;

public sealed class CheckedInt32MultiplicationTests
{
	private const int OverflowResult = unchecked((int)0xDEAD42EF);
	private static readonly (int Left, int Right)[] Pairs = [
		(0, int.MinValue), (int.MinValue, 0), (0, int.MaxValue), (1, int.MaxValue), (int.MaxValue, 1),
		(int.MinValue, 1), (1, int.MinValue), (int.MinValue, -1), (-1, int.MinValue),
		(int.MinValue, int.MinValue), (int.MaxValue, int.MaxValue), (int.MaxValue, 2), (int.MinValue, 2),
		(46340, 46340), (46341, 46341), (-46340, 46340), (-46341, 46341), (65535, 65535),
		(21, 100_000_000), (22, 100_000_000), (-21, 100_000_000), (-22, 100_000_000),
		(7, 13), (-7, 13), (7, -13), (-7, -13), (-1, -1), (int.MaxValue, -1)];

	public static IEnumerable<object[]> Targets()
	{
		foreach (var cpu in new[] { M68kCpuTarget.M68000, M68kCpuTarget.M68020, M68kCpuTarget.M68040, M68kCpuTarget.M68060 })
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint, M68kPeepholeOptimizationMode.Bounded })
			yield return [cpu, mode];
	}

	[Theory]
	[InlineData(0, 0)]
	[InlineData(1, 100_000_000)]
	[InlineData(-1, -100_000_000)]
	[InlineData(21, 2_100_000_000)]
	[InlineData(22, 42)]
	[InlineData(-21, -2_100_000_000)]
	[InlineData(-22, 42)]
	[InlineData(int.MinValue, 42)]
	[InlineData(int.MaxValue, 42)]
	public void BenchmarkOracleChecksBothSignedOverflowBoundaries(int input, int expected) =>
		Assert.Equal(expected, ReferenceResults.Get("CheckedOverflow", input));

	[Fact]
	public void RuntimeHelperMatchesClrForBoundariesAndDeterministicInputs()
	{
		foreach (var pair in Pairs) Check(pair.Left, pair.Right);
		var random = new Random(321711);
		for (int n = 0; n < 10_000; n++)
			Check((int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1), (int)random.NextInt64(int.MinValue, (long)int.MaxValue + 1));
		static void Check(int left, int right)
		{
			int expected;
			try { expected = checked(left * right); }
			catch (OverflowException) { Assert.Throws<OverflowException>(() => Integer32Arithmetic.MultiplyCheckedSigned(left, right)); return; }
			Assert.Equal(expected, Integer32Arithmetic.MultiplyCheckedSigned(left, right));
		}
	}

	[Theory]
	[MemberData(nameof(Targets))]
	public void ProductsDiscardedResultsAndExceptionUnwindingMatchClr(M68kCpuTarget cpu, M68kPeepholeOptimizationMode mode)
	{
		foreach (string entry in new[] { "Direct", "ThroughCall", "Discarded", "Finally", "NarrowAfterChecked", "ConstantFactor", "PlainNarrowLocal", "ArgumentHome" })
		{
			var result = Compile(entry, cpu, mode);
			foreach (var (left, right) in Pairs)
			{
				int expected;
				try
				{
					int product = entry is "PlainNarrowLocal" or "ArgumentHome" ? unchecked((byte)left) : entry == "ConstantFactor" ? checked(left * 100_000_000) :
						entry == "NarrowAfterChecked" ? checked((int)unchecked((ushort)left) * unchecked((ushort)right)) : checked(left * right);
					expected = entry == "Discarded" ? 0 : entry == "NarrowAfterChecked" ? unchecked((byte)product) : product;
				}
				catch (OverflowException) { expected = OverflowResult; }
				if (entry == "Finally") expected ^= 7;
				int actual = Execute(result, cpu, left, right);
				Assert.True(expected == actual, $"{entry}({left},{right}) on {cpu}/{mode}: expected {expected}, actual {actual}");
			}
		}
	}

	[Theory]
	[MemberData(nameof(Targets))]
	public void ConstantOverflowIsRetainedEvenWhenItsResultIsDiscarded(M68kCpuTarget cpu, M68kPeepholeOptimizationMode mode)
	{
		var directory = Path.Combine(AppContext.BaseDirectory, "CheckedInt32Probes", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		var assembly = new PersistedAssemblyBuilder(new AssemblyName("CheckedConstant"), typeof(object).Assembly);
		var module = assembly.DefineDynamicModule("CheckedConstant");
		var type = module.DefineType("CheckedConstant", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
		var method = type.DefineMethod("Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var il = method.GetILGenerator();
		il.DeclareLocal(typeof(int));
		il.BeginExceptionBlock();
		il.Emit(OpCodes.Ldc_I4, int.MaxValue); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Mul_Ovf); il.Emit(OpCodes.Pop);
		il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc_0);
		il.BeginCatchBlock(typeof(OverflowException)); il.Emit(OpCodes.Pop);
		il.Emit(OpCodes.Ldc_I4, OverflowResult); il.Emit(OpCodes.Stloc_0);
		il.EndExceptionBlock(); il.Emit(OpCodes.Ldloc_0); il.Emit(OpCodes.Ret); type.CreateType();
		var path = Path.Combine(directory, "CheckedConstant.dll"); assembly.Save(path);
		var result = M68kCompiler.Compile(Request(path, "CheckedConstant::Entry", cpu, mode));
		Assert.Equal(OverflowResult, Execute(result, cpu, 0, 0));
	}

	[Fact]
	public void LoweringPreservesTheOriginalCheckedInstructionOffsetAndRegistersItsHelper()
	{
		using var module = new CompilationModule(typeof(CheckedInt32Fixture).Assembly.Location,
			managedAssemblyPaths: [typeof(Integer32Arithmetic).Assembly.Location]);
		var method = module.ApplyTargetRuntimeOverride(module.ResolveEntryPoint(typeof(CheckedInt32Fixture).FullName + "::Direct"));
		var raw = typeof(CheckedInt32Fixture).GetMethod("Direct")!.GetMethodBody()!.GetILAsByteArray()!;
		var original = Assert.Single(CilInstructionDecoder.Decode(raw, "Direct").Where(i => i.OpCode == OpCodes.Mul_Ovf));
		var call = Assert.Single(method.Instructions.Where(i => i.Offset == original.Offset));
		Assert.Equal(OpCodes.Call, call.OpCode);
		Assert.Equal(original.NextOffset, call.NextOffset);
		Assert.Equal(0, call.Operand);
		Assert.Contains("Integer32Arithmetic::MultiplyCheckedSigned", module.ResolveMethodToken(0, method, call.Offset).Definition!.DisplayName);
	}

	[Fact]
	public void MissingNumericRuntimeReportsAnExplicitDiagnostic()
	{
		using var module = new CompilationModule(typeof(CheckedInt32Fixture).Assembly.Location);
		var error = Assert.Throws<M68kCompilationException>(() =>
			module.ApplyTargetRuntimeOverride(module.ResolveEntryPoint(typeof(CheckedInt32Fixture).FullName + "::Direct")));
		Assert.Equal(M68kDiagnosticIds.InvalidInput, error.DiagnosticId);
		Assert.Contains("Integer32Arithmetic::MultiplyCheckedSigned", error.Message);
	}

	[Fact]
	public void CheckedInt64MultiplicationRemainsExplicitlyUnsupported()
	{
		var error = Assert.Throws<M68kCompilationException>(() => Compile("Unsupported64", M68kCpuTarget.M68000, M68kPeepholeOptimizationMode.FixedPoint));
		Assert.Equal(M68kDiagnosticIds.UnsupportedInstruction, error.DiagnosticId);
		Assert.Contains("checked 64-bit", error.Message);
	}

	private static M68kCompilationResult Compile(string entry, M68kCpuTarget cpu, M68kPeepholeOptimizationMode mode) =>
		M68kCompiler.Compile(Request(typeof(CheckedInt32Fixture).Assembly.Location, typeof(CheckedInt32Fixture).FullName + "::" + entry, cpu, mode));

	private static M68kCompilationRequest Request(string path, string entry, M68kCpuTarget cpu, M68kPeepholeOptimizationMode mode) => new() {
		AssemblyPath = path, EntryPoint = entry, Cpu = cpu, IncludedExportNames = [], OutputFormat = M68kOutputFormat.Hunk,
		ManagedAssemblyPaths = [typeof(M68kRuntime).Assembly.Location, typeof(Integer32Arithmetic).Assembly.Location],
		ExceptionMode = M68kExceptionMode.Full, RuntimeProfile = M68kRuntimeProfile.Application,
		MemoryManagement = M68kMemoryManagement.None, PeepholeOptimization = mode
	};

	private static int Execute(M68kCompilationResult result, M68kCpuTarget target, int left, int right)
	{
		var bus = new TestBus(); result.Code.CopyTo(bus.Memory.AsSpan(0x10000));
		foreach (var relocation in result.Relocations) { uint at = 0x10000 + (uint)relocation.Offset; bus.WriteLong(at, bus.ReadLong(at) + 0x10000); }
		bus.WriteLong(0x200000, 0x1000); bus.WriteLong(0x4000, unchecked((uint)left)); bus.WriteLong(0x4004, unchecked((uint)right));
		// Copper68k has no separate 060 core; execute its common subset on 040.
		using var cpu = M68kCoreFactory.Default.Create(target == M68kCpuTarget.M68060 ? M68kCpuModel.M68040 : Enum.Parse<M68kCpuModel>(target.ToString()), bus);
		cpu.Reset(0x10000 + result.EntryPoint, 0x200000);
		for (int n = 0; n < 1_000_000; n++)
		{
			if (cpu.State.ProgramCounter == 0x1000) { Assert.Equal(0x200004u, cpu.State.A[7]); return unchecked((int)cpu.State.D[0]); }
			cpu.ExecuteInstruction(); Assert.False(cpu.State.Stopped || cpu.State.Halted);
		}
		throw new InvalidOperationException("Checked multiply execution exceeded its instruction budget.");
	}
}

public static unsafe class CheckedInt32Fixture
{
	private const int OverflowResult = unchecked((int)0xDEAD42EF);
	private static int Left() => *(int*)0x4000;
	private static int Right() => *(int*)0x4004;
	private static int cleanup;
	public static int Direct() { try { return checked(Left() * Right()); } catch (OverflowException) { return OverflowResult; } }
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int Product(int left, int right) => checked(left * right);
	public static int ThroughCall() { try { return Product(Left(), Right()); } catch (OverflowException) { return OverflowResult; } }
	public static int Discarded() { try { _ = checked(Left() * Right()); return 0; } catch (OverflowException) { return OverflowResult; } }
	public static int Finally() {
		int result;
		try { try { result = Product(Left(), Right()); } finally { cleanup = 7; } }
		catch (OverflowException) { result = OverflowResult; }
		return result ^ cleanup;
	}
	public static int NarrowAfterChecked() {
		ushort left = unchecked((ushort)Left()), right = unchecked((ushort)Right());
		try { return unchecked((byte)checked((int)left * right)); }
		catch (OverflowException) { return OverflowResult; }
	}
	public static int ConstantFactor() { try { return checked(Left() * 100_000_000); } catch (OverflowException) { return OverflowResult; } }
	public static int PlainNarrowLocal() { try { return unchecked((byte)Left()); } catch (OverflowException) { return OverflowResult; } }
	public static int ArgumentHome() => SetArgument(0);
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int SetArgument(int argument) { argument = unchecked((byte)Left()); return ReadArgument(ref argument); }
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ReadArgument(ref int argument) => argument;
	public static long Unsupported64() => checked((long)Left() * Right());
}
