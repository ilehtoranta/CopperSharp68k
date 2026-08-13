using System.Reflection;
using System.Runtime.CompilerServices;
using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class OutRefStructExecutionRegressionTests
{
	private const uint LoadAddress = 0x0001_0000;
	private const uint StackPointer = 0x0008_0000;
	private const uint ReturnSentinel = 0x0000_1000;

	[Fact]
	public void NestedRefSettersPopulateOutStructAndPreserveTrueReturnOnM68000()
	{
		Assert.Equal(42u, OutRefStructRegressionFixture.Entry());

		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = Assembly.GetExecutingAssembly().Location,
			EntryPoint =
				"CopperSharp.Compiler.Tests.OutRefStructRegressionFixture::Entry",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = M68kRuntimeProfile.Application,
		});

		Assert.Equal(42u, Execute(result));
	}

	private static uint Execute(M68kCompilationResult result)
	{
		var bus = new TestBus();
		result.Code.CopyTo(bus.Memory.AsSpan((int)LoadAddress));
		foreach (var relocation in result.Relocations)
		{
			var address = LoadAddress + (uint)relocation.Offset;
			bus.WriteLong(address, bus.ReadLong(address) + LoadAddress);
		}

		bus.WriteLong(StackPointer, ReturnSentinel);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(LoadAddress + result.EntryPoint, StackPointer);
		for (var instruction = 0; instruction < 20_000; instruction++)
		{
			if (cpu.State.ProgramCounter == ReturnSentinel)
			{
				return cpu.State.D[0];
			}

			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted,
				$"MC68000 halted at ${cpu.State.ProgramCounter:X8}, " +
				$"last opcode ${cpu.State.LastOpcode:X4}.");
		}

		throw new Xunit.Sdk.XunitException(
			$"MC68000 did not return; PC=${cpu.State.ProgramCounter:X8}.");
	}
}

public struct OutRefStructRegressionDescriptor
{
	public short Key;
	public ushort MinimumVersion;
	public bool VersionVerified;
	public byte ProfileMembership;
	public byte Visibility;
	public byte ReturnType;
	public byte Phase;
	public byte DataRegisterMask;
	public byte AddressRegisterMask;
	public bool PreserveD0;
}

public static class OutRefStructRegressionFixture
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint Entry()
	{
		OutRefStructRegressionDescriptor descriptor;
		if (!TryDescribe(-204, out descriptor))
		{
			return 1;
		}

		if (descriptor.Key != -204)
		{
			return 2;
		}

		if (descriptor.MinimumVersion != 40 ||
			!descriptor.VersionVerified ||
			descriptor.ProfileMembership != 7 ||
			descriptor.Visibility != 1 ||
			descriptor.ReturnType != 2 ||
			descriptor.Phase != 3 ||
			descriptor.DataRegisterMask != 5 ||
			descriptor.AddressRegisterMask != 10 ||
			!descriptor.PreserveD0)
		{
			return 3;
		}

		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool TryDescribe(
		short key,
		out OutRefStructRegressionDescriptor descriptor)
	{
		descriptor = default;
		descriptor.Key = key;
		if (key == -204)
		{
			SetClassic(ref descriptor);
			return true;
		}

		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void SetClassic(ref OutRefStructRegressionDescriptor descriptor)
	{
		SetCommon(ref descriptor);
		descriptor.MinimumVersion = 40;
		descriptor.VersionVerified = true;
		descriptor.ProfileMembership = 7;
		descriptor.Visibility = 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void SetCommon(ref OutRefStructRegressionDescriptor descriptor)
	{
		descriptor.ReturnType = 2;
		descriptor.Phase = 3;
		descriptor.DataRegisterMask = 5;
		descriptor.AddressRegisterMask = 10;
		descriptor.PreserveD0 = true;
	}
}
