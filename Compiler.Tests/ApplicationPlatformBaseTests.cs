using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Copper68k;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	[Fact]
	public void ApplicationPlatformBaseObservesCallbackAndExplicitSlotWrites()
	{
		const uint baseA = 0x5000;
		const uint baseB = 0x6000;
		const uint baseC = 0x7000;
		foreach (var compact in new[] { false, true })
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest
			{
				AssemblyPath = typeof(ApplicationPlatformBaseFixtures).Assembly.Location,
				EntryPoint = typeof(ApplicationPlatformBaseFixtures).FullName + "::Entry",
				Cpu = M68kCpuTarget.M68000,
				OutputFormat = M68kOutputFormat.Hunk,
				RuntimeProfile = M68kRuntimeProfile.Application,
				ExceptionMode = M68kExceptionMode.Full,
				IncludedExportNames = [],
				Hunk = new() { IncludeSymbols = false },
				CodeSizeOptimizations = new()
				{
					CompactApplicationPlatformBases = compact
				},
				ExternalCallResolvers = [new ApplicationPlatformBaseResolver()]
			});
			var slot = Assert.Single(result.Symbols, symbol =>
				symbol.Name == "_DOSLibraryBase");
			foreach (var model in new[]
				{ M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
			foreach (var codeBase in new uint[] { 0x10000, 0x20000 })
			{
				const uint bssBase = 0x40000;
				var bus = LoadApplicationSizeHunk(result.Image, codeBase, bssBase);
				var slotAddress = HunkAddress(
					result.Image, slot.Address, codeBase, bssBase);
				var reads = 0;
				var registrations = 0;
				var mutations = 0;
				bus.RegisterGateway(baseA - 30, state =>
				{
					Assert.Equal(baseA, state.A[6]);
					reads++;
					state.D[0] = 10;
				});
				bus.RegisterGateway(baseA - 36, state =>
				{
					Assert.Equal(baseA, state.A[6]);
					Assert.NotEqual(0u, state.A[0]);
					registrations++;
				});
				bus.RegisterGateway(baseA - 42, state =>
				{
					Assert.Equal(baseA, state.A[6]);
					Assert.Equal(1, registrations);
					mutations++;
					// The callback was registered by the earlier native call. This
					// invocation has no callback operand, but can still re-enter managed
					// code and change its compiler-owned library-base slot.
					bus.WriteLong(slotAddress, baseB);
				});
				bus.RegisterGateway(baseB - 30, state =>
				{
					Assert.Equal(baseB, state.A[6]);
					reads++;
					state.D[0] = 20;
				});
				bus.RegisterGateway(baseC - 30, state =>
				{
					Assert.Equal(baseC, state.A[6]);
					reads++;
					state.D[0] = 30;
				});

				Assert.Equal(60u, Execute(bus, model, codeBase + result.EntryPoint));
				Assert.Equal(3, reads);
				Assert.Equal(1, registrations);
				Assert.Equal(1, mutations);
				Assert.Equal(baseC, bus.ReadLong(slotAddress));
			}
		}
	}

	private static uint HunkAddress(
		byte[] image,
		uint logicalAddress,
		uint codeBase,
		uint bssBase)
	{
		var count = BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(8, 4));
		Assert.InRange(count, 1u, 2u);
		if (count == 1)
		{
			return codeBase + logicalAddress;
		}
		var codeSizeWordOffset = checked((int)(6 + count) * 4);
		var codeBytes = BinaryPrimitives.ReadUInt32BigEndian(
			image.AsSpan(codeSizeWordOffset, 4)) * 4;
		return logicalAddress < codeBytes
			? codeBase + logicalAddress
			: bssBase + logicalAddress - codeBytes;
	}

	private sealed class ApplicationPlatformBaseResolver : IM68kExternalCallResolver
	{
		public bool TryResolve(
			M68kExternalMethod method,
			out M68kExternalCallConvention convention)
		{
			var read = method.DisplayName ==
				typeof(ApplicationPlatformBaseFixtures).FullName + "::Read";
			var register = method.DisplayName ==
				typeof(ApplicationPlatformBaseFixtures).FullName + "::Register";
			var dispatch = method.DisplayName ==
				typeof(ApplicationPlatformBaseFixtures).FullName + "::Dispatch";
			if (!read && !register && !dispatch)
			{
				convention = null!;
				return false;
			}
			convention = new M68kExternalCallConvention(
				"dos.library",
				M68kExternalBaseSource.WritableSlot,
				M68kRegister.A6,
				(short)(read ? -30 : register ? -36 : -42),
				SlotSymbol: "_DOSLibraryBase",
				ParameterRegisters: register ? [M68kRegister.A0] : [],
				ReturnRegister: M68kRegister.D0);
			return true;
		}
	}
}

public static unsafe class ApplicationPlatformBaseFixtures
{
	public static uint Entry()
	{
		global::Amiga.DOS.DOSLibraryBase = 0x5000;
		Register(&ChangeBase);
		var first = Read();
		Dispatch();
		var second = Read();
		global::Amiga.DOS.DOSLibraryBase = 0x7000;
		return first + second + Read();
	}

	private static void ChangeBase() =>
		global::Amiga.DOS.DOSLibraryBase = 0x6000;

	[MethodImpl(MethodImplOptions.InternalCall)]
	public static extern uint Read();

	[MethodImpl(MethodImplOptions.InternalCall)]
	public static extern void Register(delegate*<void> callback);

	[MethodImpl(MethodImplOptions.InternalCall)]
	public static extern void Dispatch();
}
