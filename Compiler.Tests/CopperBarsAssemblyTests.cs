using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Amiga;
using CopperSharp.Compiler;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CopperBarsAssemblyTests
{
	private const string EntryPoint = "CopperBarsExample.Program::Main";

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ushort NoInlineRawRead() =>
		APTR.ReadUInt16(APTR.FromPointer(0x00DF_F000), 2);

	public static ushort CallNoInlineRawRead() => NoInlineRawRead();

	[Fact]
	public void GeneratedCopperListCrossesVerticalWrapBeforePalLine280()
	{
		var result = Compile(M68kOutputFormat.Assembly);
		var buildCopperList = MethodBody(result, "CopperBarsExample.Program::BuildCopperList");
		var copperWait = MethodBody(result, "CopperBarsExample.Program::CopperWait");

		var wrap = HexImmediateIndex(buildCopperList, value: 0xFFDF);
		var physicalLine280 = HexImmediateIndex(buildCopperList, value: 280);
		var interruptRequest = HexImmediateIndex(buildCopperList, value: 0x8010);

		Assert.True(wrap >= 0, "BuildCopperList must emit the realizable line-255 wrap WAIT $FFDF.");
		Assert.True(physicalLine280 > wrap, "The low eight bits of line 280 must follow the wrap WAIT.");
		Assert.True(interruptRequest > physicalLine280, "The Copper INTREQ move must follow the line-280 WAIT.");
		Assert.Contains("lsl.w\t#8", copperWait, StringComparison.Ordinal);
		Assert.Contains("moveq\t#1", copperWait, StringComparison.Ordinal);
		Assert.Contains("or.w", copperWait, StringComparison.Ordinal);
	}

	[Fact]
	public void TakeoverLoopHasNoOsCallsAndCopperWritesStayWordSized()
	{
		var result = Compile(M68kOutputFormat.Assembly);
		var runDemo = MethodBody(result, "CopperBarsExample.Program::RunDemo");
		var writeCopperInstruction = MethodBody(
			result,
			"CopperBarsExample.Program::WriteCopperInstruction");

		Assert.DoesNotContain("\tjsr\t", runDemo, StringComparison.Ordinal);
		Assert.Equal(2, WordIndirectWrite().Matches(writeCopperInstruction).Count);
		Assert.DoesNotContain("move.b", writeCopperInstruction, StringComparison.Ordinal);
		Assert.DoesNotMatch(NonWordCustomControlWrite(), result.Text!);
	}

	[Fact]
	public void StrippedCopperBarsStaysWithinCorrectnessStageBudgets()
	{
		var result = Compile(M68kOutputFormat.Hunk);

		// Writable library bases now live outside ROM/code. Absolute references
		// preserve that section boundary and account for the additional fixups.
		Assert.True(result.Code.Length <= 1_322, $"Code budget exceeded: {result.Code.Length} bytes.");
		Assert.True(result.Image.Length <= 1_476, $"Stripped HUNK budget exceeded: {result.Image.Length} bytes.");
		Assert.Equal(25, result.Relocations.Count);
	}

	[Fact]
	public void GeneratedAssemblyStaysWithinCorrectnessStagePatternBudgets()
	{
		var assembly = Compile(M68kOutputFormat.Assembly).Text!;

		Assert.True(Instruction().Matches(assembly).Count <= 447);
		Assert.True(CallInstruction().Matches(assembly).Count <= 44);
		Assert.Empty(LongMaskConstant().Matches(assembly).Cast<Match>());
		// CopperWait normalizes its ushort ABI return, and WriteCopperWait
		// independently canonicalizes that call result before widening/store.
		Assert.True(WordZeroExtension().Matches(assembly).Count <= 2);
	}

	[Fact]
	public void ConstantHardwareReadersInlineButNoInliningIsPreserved()
	{
		var result = Compile(M68kOutputFormat.Assembly);
		Assert.DoesNotContain(
			result.Symbols,
			symbol => symbol.Name is
				"Amiga.Hardware.CustomChip::ReadInterruptRequest" or
				"Amiga.Hardware.CiaA::ReadPortA");
		var wait = MethodBody(result, "CopperBarsExample.Program::WaitForCopperUpdateWindow");
		var mouse = MethodBody(result, "Amiga.Hardware.CiaA::IsLeftMouseButtonPressed");
		Assert.Contains("move.w\t$00DFF01E", wait, StringComparison.Ordinal);
		Assert.DoesNotContain("\tbsr", wait, StringComparison.Ordinal);
		Assert.Contains("move.b\t$00BFE001", mouse, StringComparison.Ordinal);

		var noInline = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(CopperBarsAssemblyTests).Assembly.Location,
			EntryPoint = $"{typeof(CopperBarsAssemblyTests).FullName}::{nameof(CallNoInlineRawRead)}",
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = M68kOutputFormat.Assembly,
			ExceptionMode = M68kExceptionMode.Yolo
		});
		Assert.Contains(
			noInline.Symbols,
			symbol => symbol.Name.EndsWith($"::{nameof(NoInlineRawRead)}", StringComparison.Ordinal));
		var noInlineCaller = MethodBody(
			noInline,
			$"{typeof(CopperBarsAssemblyTests).FullName}::{nameof(CallNoInlineRawRead)}");
		Assert.DoesNotContain("$00DFF002", noInlineCaller, StringComparison.Ordinal);
		Assert.Matches("\\t(?:bsr|bra|jmp)", noInlineCaller);
	}

	[Fact]
	public void SdkMouseBooleanUsesCompactCanonicalMaterialization()
	{
		var result = Compile(M68kOutputFormat.Assembly);
		var mouse = MethodBody(result, "Amiga.Hardware.CiaA::IsLeftMouseButtonPressed");

		Assert.Contains("seq\td0\r\n\tneg.b\td0", mouse, StringComparison.Ordinal);
		Assert.DoesNotContain("ext.w\td0", mouse, StringComparison.Ordinal);
		Assert.DoesNotContain("neg.l\td0", mouse, StringComparison.Ordinal);
	}

	[Fact]
	public void WaitUpdateLoopsCoverFourBarsAndNineWaitsAtExactStrides()
	{
		var result = Compile(M68kOutputFormat.Assembly);
		var bars = MethodBody(result, "CopperBarsExample.Program::UpdateCopperWaits");
		var waits = MethodBody(result, "CopperBarsExample.Program::UpdateBarWaits");

		Assert.Matches(@"\tmoveq\t#8,d[0-7]", bars);
		Assert.Matches(@"\tmoveq\t#4,d[0-7]\r?\n\tcmp\.l", bars);
		Assert.Matches(@"\tmoveq\t#72,d[0-7]", bars);
		Assert.Matches(@"\tmoveq\t#24,d[0-7]", bars);
		Assert.Matches(@"\tmoveq\t#8,d[0-7]\r?\n\tcmp\.l", waits);
		Assert.Matches(@"\tlsl\.l\t#3,d[0-7]", waits);
		Assert.Single(CallInstruction().Matches(bars).Cast<Match>());
		Assert.Single(CallInstruction().Matches(waits).Cast<Match>());
	}

	private static M68kCompilationResult Compile(M68kOutputFormat outputFormat) =>
		AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(CopperBarsExample.Program).Assembly.Location,
			EntryPoint = EntryPoint,
			Cpu = M68kCpuTarget.M68000,
			OutputFormat = outputFormat,
			RuntimeProfile = M68kRuntimeProfile.Application,
			ExceptionMode = M68kExceptionMode.Yolo,
			Hunk = new HunkOutputOptions { IncludeSymbols = false }
		});

	private static string MethodBody(M68kCompilationResult result, string symbolName)
	{
		var assembly = Assert.IsType<string>(result.Text);
		var labels = TopLevelMethodLabel().Matches(assembly);
		var methods = result.Symbols
			.Where(symbol => symbol.Name.Contains("::", StringComparison.Ordinal))
			.ToArray();
		Assert.Equal(methods.Length, labels.Count);

		var methodIndex = Array.FindIndex(methods, symbol => symbol.Name == symbolName);
		Assert.True(methodIndex >= 0, $"Generated symbol was not found: {symbolName}.");
		var start = labels[methodIndex].Index;
		var end = methodIndex + 1 < labels.Count ? labels[methodIndex + 1].Index : assembly.Length;
		return assembly[start..end];
	}

	private static int HexImmediateIndex(string assembly, uint value)
	{
		foreach (Match match in HexImmediate().Matches(assembly))
		{
			if (uint.TryParse(
					match.Groups[1].Value,
					NumberStyles.HexNumber,
					CultureInfo.InvariantCulture,
					out var parsed) &&
				parsed == value)
			{
				return match.Index;
			}
		}
		return -1;
	}

	[GeneratedRegex(
		"^C68K_method_003A(?![^\\r\\n]*_003A(?:BB|end))[^\\r\\n]+:\\r?$",
		RegexOptions.Multiline)]
	private static partial Regex TopLevelMethodLabel();

	[GeneratedRegex("#\\$([0-9A-Fa-f]+)")]
	private static partial Regex HexImmediate();

	[GeneratedRegex("^\\s*move\\.w\\s+[^,]+,-?\\d*\\(a[0-7](?:,[^)]+)?\\)\\r?$", RegexOptions.Multiline)]
	private static partial Regex WordIndirectWrite();

	[GeneratedRegex("^\\s*move\\.(?:b|l)\\s+[^,]+,(?:136|150|154|156|158)\\(a[0-7]\\)\\r?$", RegexOptions.Multiline)]
	private static partial Regex NonWordCustomControlWrite();

	[GeneratedRegex("^\\t(?!dc\\.)[a-z]", RegexOptions.Multiline)]
	private static partial Regex Instruction();

	[GeneratedRegex("^\\t(?:bsr(?:\\.[sw])?|jsr)\\t", RegexOptions.Multiline)]
	private static partial Regex CallInstruction();

	[GeneratedRegex("#\\$0000(?:07FF|7FFF|8000)")]
	private static partial Regex LongMaskConstant();

	[GeneratedRegex("andi\\.l\\t#\\$0000FFFF")]
	private static partial Regex WordZeroExtension();
}
