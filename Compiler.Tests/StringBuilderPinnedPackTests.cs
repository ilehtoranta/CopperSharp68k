/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using Copper68k;
using CopperSharp.Compiler.Framework;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	[Theory]
	[InlineData(M68kFloatingPointMode.Disabled)]
	[InlineData(M68kFloatingPointMode.SoftFloat)]
	public void StringBuilderUnadmittedCallsRetainFrameworkDiagnostics(M68kFloatingPointMode floatingPoint)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		// An unaudited release must retain the unsupported StringBuilder boundary.
		pack.Replace("packVersion", "10.0.10");
		var request = new M68kCompilationRequest {
			AssemblyPath = FixtureAssembly, EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::StringBuilderBenchmarkPresizedTextEntry",
			IncludedExportNames = [], FloatingPoint = floatingPoint,
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		};
		var analysis = M68kCompiler.AnalyzeFramework(request);
		Assert.False(analysis.IsCompatible);
		var append = Assert.Single(analysis.Members.Where(member => member.Member.TypeName == "System.Text.StringBuilder" && member.Member.Name == "Append"));
		Assert.Equal(M68kFrameworkCompatibilityStatus.Unsupported, append.Status);
		Assert.Contains(append.CallSites, site => site.Caller.EndsWith("::StringBuilderBenchmarkPresizedTextEntry", StringComparison.Ordinal) && site.IlOffset == 0x12 && site.RootPath.Count != 0);
		var exception = Assert.Throws<M68kCompilationException>(() => M68kCompiler.Compile(request));
		Assert.Equal(M68kDiagnosticIds.UnsupportedFrameworkMember, exception.DiagnosticId);
		Assert.Contains("Root path:", exception.Message, StringComparison.Ordinal);
		Assert.Contains("StringBuilderBenchmarkPresizedTextEntry", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void StringBuilderPinnedAdmissionInventoryRecordsTheClosedGraphs()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		foreach (var (entry, experimental) in new[] {
			("StringBuilderBenchmarkPresizedTextEntry", false),
			("StringBuilderBenchmarkPresizedTextEntry", true),
			("CoreLibStringBuilderPublicAcceptanceEntry", false),
			("CoreLibStringBuilderPublicAcceptanceEntry", true)
		})
		{
			var analysis = M68kCompiler.AnalyzeFramework(new M68kCompilationRequest {
				AssemblyPath = FixtureAssembly, EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::" + entry,
				IncludedExportNames = [], ManagedAssemblyPaths = [typeof(CopperSharp.Runtime.AmigaPal.EnvironmentPal).Assembly.Location],
				FloatingPoint = M68kFloatingPointMode.SoftFloat, ExceptionMode = M68kExceptionMode.Full,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				Heap = new M68kHeapOptions { StartAddress = 0x0010_0000, Size = 0x8000 },
				FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = experimental }
			});
			File.WriteAllText(Path.Combine(AppContext.BaseDirectory, $"stringbuilder-pinned-admission-{entry}-{experimental}.json"),
				System.Text.Json.JsonSerializer.Serialize(analysis, new System.Text.Json.JsonSerializerOptions {
					WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
				}));
			Assert.Equal(pack.Sha256, Assert.Single(analysis.ImplementationPack!.Assemblies).Sha256);
			Assert.True(analysis.IsCompatible, string.Join("\n", analysis.Members
				.Where(member => member.Status == M68kFrameworkCompatibilityStatus.Unsupported)
				.Select(member => member.Member.DisplayName + ": " + member.Reason)));
			if (experimental && entry == "CoreLibStringBuilderPublicAcceptanceEntry") {
				foreach (var item in analysis.Members.Where(item => item.Member.TypeName == "System.Text.StringBuilder" ||
					item.Member.TypeName.StartsWith("System.Text.StringBuilder+", StringComparison.Ordinal))) {
					Assert.NotEmpty(item.Effects);
					Assert.Contains("managed-stringbuilder", item.RequiredFeatures);
				}
				var genericPublic = analysis.Members.Where(item => item.Member.TypeName == "System.Text.StringBuilder" &&
					(item.Member.Name == "AppendFormat" && item.Member.GenericArity > 0 && item.Member.ParameterTypes.Count == 2 + item.Member.GenericArity ||
					 item.Member.Name == "AppendJoin" && item.Member.GenericArity == 1 && item.Member.ParameterTypes.Count == 2)).ToArray();
				Assert.NotEmpty(genericPublic);
				foreach (var item in genericPublic) {
					Assert.Contains("managed-stringbuilder", item.RequiredFeatures);
					Assert.Contains("MayAllocate", item.Effects);
				}
				foreach (var item in analysis.Members.Where(item => item.Member.TypeName == "System.Text.StringBuilder" && item.Member.Name == "AppendLine"))
					Assert.Contains("managed-stringbuilder", item.RequiredFeatures);
			}
		}
	}

	[Fact]
	public void StringBuilderPinnedPublicSignaturesMatchTheAuditedSurface()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var pinned = ReadStringBuilderSurface(pack.AssemblyPath);
		Assert.Equal(103, pinned.Length);
		Assert.Equal(ReadStringBuilderSurface(typeof(object).Assembly.Location), pinned);
		File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "stringbuilder-pinned-public-signatures.txt"), pinned);
	}

	private static string[] ReadStringBuilderSurface(string path)
	{
		using var stream = File.OpenRead(path);
		using var pe = new PEReader(stream);
		var reader = pe.GetMetadataReader();
		var type = reader.TypeDefinitions.Select(reader.GetTypeDefinition).Single(type =>
			reader.GetString(type.Namespace) == "System.Text" && reader.GetString(type.Name) == "StringBuilder");
		var provider = new StringBuilderSurfaceSignatureProvider();
		return type.GetMethods().Select(reader.GetMethodDefinition)
			.Where(method => (method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public &&
				(method.Attributes & MethodAttributes.Static) == 0)
			.Select(method => reader.GetString(method.Name) + " " + StringBuilderSurfaceSignatureProvider.Describe(method.DecodeSignature(provider, (object?)null)))
			.Order(StringComparer.Ordinal).ToArray();
	}

	private sealed class StringBuilderSurfaceSignatureProvider : ISignatureTypeProvider<string, object?>
	{
		public static string Describe(MethodSignature<string> signature) =>
			$"{signature.Header.RawValue:X2}:{signature.GenericParameterCount}:{signature.RequiredParameterCount} {signature.ReturnType}({string.Join(",", signature.ParameterTypes)})";
		public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{shape.Rank};{string.Join(",", shape.Sizes)};{string.Join(",", shape.LowerBounds)}]";
		public string GetByReferenceType(string elementType) => elementType + "&";
		public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr:" + Describe(signature);
		public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
		public string GetGenericMethodParameter(object? context, int index) => "!!" + index;
		public string GetGenericTypeParameter(object? context, int index) => "!" + index;
		public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType + (isRequired ? " modreq(" : " modopt(") + modifier + ")";
		public string GetPinnedType(string elementType) => elementType + " pinned";
		public string GetPointerType(string elementType) => elementType + "*";
		public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
		public string GetSZArrayType(string elementType) => elementType + "[]";
		public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
			$"{rawTypeKind:X2}:[{reader.GetString(reader.GetAssemblyDefinition().Name)}]{DefinitionName(reader, handle)}";
		private static string DefinitionName(MetadataReader reader, TypeDefinitionHandle handle)
		{
			var type = reader.GetTypeDefinition(handle);
			return type.GetDeclaringType().IsNil ? reader.GetString(type.Namespace) + "." + reader.GetString(type.Name) :
				DefinitionName(reader, type.GetDeclaringType()) + "+" + reader.GetString(type.Name);
		}
		public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
			$"{rawTypeKind:X2}:" + ReferenceName(reader, handle);
		private static string ReferenceName(MetadataReader reader, TypeReferenceHandle handle)
		{
			var type = reader.GetTypeReference(handle);
			if (type.ResolutionScope.Kind == HandleKind.TypeReference)
				return ReferenceName(reader, (TypeReferenceHandle)type.ResolutionScope) + "+" + reader.GetString(type.Name);
			var assembly = type.ResolutionScope.Kind == HandleKind.AssemblyReference
				? reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name)
				: reader.GetString(reader.GetAssemblyDefinition().Name);
			return $"[{assembly}]" + reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
		}
		public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) =>
			reader.GetTypeSpecification(handle).DecodeSignature(this, context);
	}

	[Fact]
	public void StringBuilderPinnedCoreLibHasTheReleasedPackIdentity()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		Assert.Equal("Microsoft.NETCore.App.Runtime.win-x64", catalog.Provenance.PackId);
		Assert.Equal("10.0.9", catalog.Provenance.PackVersion);
		Assert.Equal("win-x64", catalog.Provenance.RuntimeIdentifier);
		Assert.Equal(FrameworkImplementationPackTests.CoreLibPack.PinnedCoreLibSha256, pack.Sha256);
		Assert.StartsWith("10.0.9-servicing.", System.Diagnostics.FileVersionInfo.GetVersionInfo(pack.AssemblyPath).ProductVersion);
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderPublicSurfaceExecutesAgainstPinnedCoreLib(M68kCpuTarget target, M68kCpuModel model)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.FixedPoint, M68kPeepholeOptimizationMode.Disabled })
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest {
				AssemblyPath = FixtureAssembly, EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibStringBuilderPublicAcceptanceEntry",
				ManagedAssemblyPaths = [typeof(CopperSharp.Runtime.AmigaPal.EnvironmentPal).Assembly.Location],
				IncludedExportNames = [], Cpu = target, OutputFormat = M68kOutputFormat.Hunk,
				ExceptionMode = M68kExceptionMode.Full, PeepholeOptimization = mode, FloatingPoint = M68kFloatingPointMode.SoftFloat,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc, GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
				Heap = new M68kHeapOptions { StartAddress = 0x0010_0000, Size = 0x8000 },
				FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
			});
			var artifact = Path.Combine(AppContext.BaseDirectory, $"stringbuilder-pinned-public-{target}-{mode}");
			File.WriteAllText(artifact + ".map", result.Map);
			Assert.Contains("sha256=" + pack.Sha256, result.Map, StringComparison.Ordinal);
			Assert.True(HunkLoadAddress + result.Code.Length < 0x0010_0000);
			Assert.Equal(42u, Execute(CreateHunkBus(result), model, HunkLoadAddress + result.EntryPoint,
				initialStackPointer: 0x0020_0000, maxInstructions: 500_000_000));
		}
	}
}
