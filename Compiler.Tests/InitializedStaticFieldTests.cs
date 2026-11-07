/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace CopperSharp.Compiler.Tests;

public sealed class InitializedStaticFieldTests
{
	public static IEnumerable<object[]> DirectAccessCases()
	{
		foreach (var cpu in new[] { M68kCpuTarget.M68000, M68kCpuTarget.M68020, M68kCpuTarget.M68040 })
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.FixedPoint, M68kPeepholeOptimizationMode.Disabled })
		foreach (var format in new[] { M68kOutputFormat.Hunk, M68kOutputFormat.Assembly })
		foreach (var crossModule in new[] { false, true })
			yield return [cpu, mode, format, crossModule];
	}

	[Theory]
	[MemberData(nameof(DirectAccessCases))]
	public void DirectRvaFieldAccessCannotSilentlyUseZeroInitializedStorage(
		M68kCpuTarget cpu, M68kPeepholeOptimizationMode mode, M68kOutputFormat format, bool crossModule)
	{
		var fixture = CreateFixture(crossModule);
		var error = Assert.Throws<M68kCompilationException>(() => M68kCompiler.Compile(new M68kCompilationRequest {
			AssemblyPath = fixture.Application,
			ManagedAssemblyPaths = crossModule ? [fixture.Data] : [],
			EntryPoint = "RvaStaticProbe::Entry",
			Cpu = cpu,
			OutputFormat = format,
			RuntimeProfile = M68kRuntimeProfile.Freestanding,
			MemoryManagement = M68kMemoryManagement.None,
			ExceptionMode = M68kExceptionMode.Yolo,
			PeepholeOptimization = mode,
			IncludedExportNames = []
		}));
		Assert.Equal(M68kDiagnosticIds.UnsupportedInstruction, error.DiagnosticId);
		Assert.Contains("RVA-initialized static field", error.Message, StringComparison.Ordinal);
		Assert.Contains("RvaStaticProbe::Data", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ReproductionContainsTheLittleEndianSwitchTableBytesInThePe()
	{
		var fixture = CreateFixture(false);
		using var stream = File.OpenRead(fixture.Data);
		using var pe = new PEReader(stream);
		var reader = pe.GetMetadataReader();
		var field = reader.FieldDefinitions.Select(reader.GetFieldDefinition)
			.Single(f => reader.GetString(f.Name) == "Data");
		Assert.True(field.Attributes.HasFlag(FieldAttributes.HasFieldRVA));
		Assert.Equal("3412452356346745785689679A785713",
			Convert.ToHexString(pe.GetSectionData(field.GetRelativeVirtualAddress()).GetContent(0, 16).AsSpan()));
	}

	private static (string Application, string Data) CreateFixture(bool crossModule)
	{
		var directory = Path.Combine(AppContext.BaseDirectory, "InitializedStaticFieldProbes", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		var dataAssembly = new PersistedAssemblyBuilder(new AssemblyName("RvaStaticData"), typeof(object).Assembly);
		var dataModule = dataAssembly.DefineDynamicModule("RvaStaticData");
		var dataType = dataModule.DefineType("RvaStaticProbe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
		var field = dataType.DefineInitializedData("Data",
			Convert.FromHexString("3412452356346745785689679A785713"), FieldAttributes.Public | FieldAttributes.Static);
		var applicationAssembly = crossModule
			? new PersistedAssemblyBuilder(new AssemblyName("RvaStaticConsumer"), typeof(object).Assembly) : dataAssembly;
		var applicationType = crossModule
			? applicationAssembly.DefineDynamicModule("RvaStaticConsumer").DefineType("RvaStaticProbe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed)
			: dataType;
		var entry = applicationType.DefineMethod("Entry", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
		var il = entry.GetILGenerator();
		// The second table entry starts with byte 0x45. A byte read isolates
		// lost initialization from the separate multi-byte endian mismatch.
		il.Emit(OpCodes.Ldsflda, field);
		il.Emit(OpCodes.Ldc_I4_2);
		il.Emit(OpCodes.Add);
		il.Emit(OpCodes.Ldind_U1);
		il.Emit(OpCodes.Ret);
		dataType.CreateType();
		if (crossModule) applicationType.CreateType();
		var dataPath = Path.Combine(directory, "RvaStaticData.dll");
		dataAssembly.Save(dataPath);
		if (!crossModule) return (dataPath, dataPath);
		var applicationPath = Path.Combine(directory, "RvaStaticConsumer.dll");
		applicationAssembly.Save(applicationPath);
		return (applicationPath, dataPath);
	}
}
