using System.Text;
using Copper68k.Compiler.Backend;
using Copper68k.Compiler.Metadata;
using Copper68k.Compiler.Output;

namespace Copper68k.Compiler;

/// <summary>Compiles a closed set of CIL methods into a linked 68k image.</summary>
public static class M68kCompiler
{
	/// <summary>Compiles and links one assembly.</summary>
	public static M68kCompilationResult Compile(M68kCompilationRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentException.ThrowIfNullOrWhiteSpace(request.AssemblyPath);

		using var module = new CompilationModule(
			request.AssemblyPath,
			request.ExternalCallResolvers);
		var entry = module.ResolveEntryPoint(request.EntryPoint);
		var generated = new M68kCodeGenerator(module, request).Generate(entry);

		return request.OutputFormat switch
		{
			M68kOutputFormat.Hunk => LinkHunk(generated, request),
			M68kOutputFormat.KickstartRom => LinkRom(generated, request),
			M68kOutputFormat.Assembly => WriteAssembly(generated, request),
			_ => throw new M68kCompilationException(
				M68kDiagnosticIds.InvalidOutputOptions,
				$"Unknown output format {request.OutputFormat}.")
		};
	}

	private static M68kCompilationResult WriteAssembly(
		GeneratedProgram program,
		M68kCompilationRequest request)
	{
		var imports = new Dictionary<string, uint>(request.Imports, StringComparer.Ordinal);
		foreach (var import in program.Assembler.ExternalTargets)
		{
			imports.TryAdd(import, 0);
		}

		var linked = program.Assembler.Link(0, imports);
		var symbols = CreateSymbols(program, linked, 0);
		var entryOffset = checked((uint)linked.Labels[program.EntryLabel]);
		var text = program.Assembler.RenderAssembly(request.Cpu);
		return new M68kCompilationResult(
			Encoding.UTF8.GetBytes(text),
			linked.Bytes,
			entryOffset,
			symbols,
			linked.Relocations,
			CreateMap(request, entryOffset, symbols, linked.Relocations),
			text);
	}

	private static M68kCompilationResult LinkHunk(
		GeneratedProgram program,
		M68kCompilationRequest request)
	{
		var linked = program.Assembler.Link(0, request.Imports);
		var symbols = CreateSymbols(program, linked, 0);
		var entryOffset = checked((uint)linked.Labels[program.EntryLabel]);
		var image = HunkWriter.Write(
			linked.Bytes,
			linked.Relocations,
			symbols,
			request.Hunk);
		return new M68kCompilationResult(
			image,
			linked.Bytes,
			entryOffset,
			symbols,
			linked.Relocations,
			CreateMap(request, entryOffset, symbols, linked.Relocations));
	}

	private static M68kCompilationResult LinkRom(
		GeneratedProgram program,
		M68kCompilationRequest request)
	{
		var romBase = KickstartRomWriter.GetBaseAddress(request.Rom);
		var codeOrigin = checked(romBase + 8);
		var linked = program.Assembler.Link(codeOrigin, request.Imports);
		var symbols = CreateSymbols(program, linked, codeOrigin);
		var entryPoint = checked(codeOrigin + (uint)linked.Labels[program.EntryLabel]);
		var image = KickstartRomWriter.Write(linked.Bytes, entryPoint, request.Rom);
		return new M68kCompilationResult(
			image,
			linked.Bytes,
			entryPoint,
			symbols,
			linked.Relocations,
			CreateMap(request, entryPoint, symbols, linked.Relocations));
	}

	private static IReadOnlyList<M68kSymbol> CreateSymbols(
		GeneratedProgram program,
		LinkedCode linked,
		uint origin)
	{
		var methodOffsets = new List<(string Name, int Offset)>();
		foreach (var method in program.Methods)
		{
			var label = $"method:{System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(method.Handle):X8}";
			methodOffsets.Add((method.DisplayName, linked.Labels[label]));
		}

		methodOffsets.Sort(static (left, right) => left.Offset.CompareTo(right.Offset));
		var result = new List<M68kSymbol>(methodOffsets.Count);
		for (var index = 0; index < methodOffsets.Count; index++)
		{
			var current = methodOffsets[index];
			var end = index + 1 < methodOffsets.Count
				? methodOffsets[index + 1].Offset
				: linked.Bytes.Length;
			result.Add(new M68kSymbol(
				current.Name,
				checked(origin + (uint)current.Offset),
				end - current.Offset));
		}

		foreach (var export in program.Exports)
		{
			var label = M68kCodeGenerator.ExportLabel(export.Name);
			result.Add(new M68kSymbol(
				export.Name,
				checked(origin + (uint)linked.Labels[label]),
				0));
		}

		foreach (var platformBase in program.PlatformBases.Where(
			item => item.Binding.BaseSource == M68kExternalBaseSource.WritableSlot))
		{
			result.Add(new M68kSymbol(
				platformBase.Binding.SlotSymbol!,
				checked(origin + (uint)linked.Labels[platformBase.Label!]),
				4));
		}

		return result
			.OrderBy(symbol => symbol.Address)
			.ThenBy(symbol => symbol.Name, StringComparer.Ordinal)
			.ToArray();
	}

	private static string CreateMap(
		M68kCompilationRequest request,
		uint entryPoint,
		IReadOnlyList<M68kSymbol> symbols,
		IReadOnlyList<M68kRelocation> relocations)
	{
		var map = new StringBuilder();
		map.AppendLine($"CPU {request.Cpu}");
		map.AppendLine($"FORMAT {request.OutputFormat}");
		map.AppendLine($"ENTRY {entryPoint:X8}");
		map.AppendLine("SYMBOLS");
		foreach (var symbol in symbols)
		{
			map.AppendLine($"{symbol.Address:X8} {symbol.Size,6} {symbol.Name}");
		}

		map.AppendLine("RELOCATIONS");
		foreach (var relocation in relocations)
		{
			map.AppendLine($"{relocation.Offset:X8} {relocation.Target}");
		}

		return map.ToString();
	}
}
