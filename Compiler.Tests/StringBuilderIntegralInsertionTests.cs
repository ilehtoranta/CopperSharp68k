/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Text;
using CopperSharp.Compiler.Framework;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderIntegralInsertionTests
{
	[Fact]
	public void IntegralInsertionMatchesHostAndClosesTheReleasedGraph()
	{
		Assert.Equal(42, CompilerFixtures.StringBuilderStableIntegralInsertionEntry());
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var analysis = M68kCompiler.AnalyzeFramework(new M68kCompilationRequest {
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::StringBuilderStableIntegralInsertionEntry",
			IncludedExportNames = [], ExceptionMode = M68kExceptionMode.Full,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		});
		File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "stringbuilder-integral-insertion-analysis.json"),
			System.Text.Json.JsonSerializer.Serialize(analysis));
		Assert.True(analysis.IsCompatible, string.Join("\n", analysis.Members.Where(member => member.Status == M68kFrameworkCompatibilityStatus.Unsupported)
			.Select(member => member.Member.DisplayName + ": " + member.Reason)));
	}
}

public static partial class CompilerFixtures
{
	private static StringBuilder InsertStableInteger(StringBuilder builder, int index, int width, int sample)
	{
		return width switch {
			0 => builder.Insert(index, sample == 0 ? sbyte.MinValue : sample == 1 ? sbyte.MaxValue : (sbyte)0),
			1 => builder.Insert(index, sample == 1 ? byte.MaxValue : (byte)0),
			2 => builder.Insert(index, sample == 0 ? short.MinValue : sample == 1 ? short.MaxValue : (short)0),
			3 => builder.Insert(index, sample == 1 ? ushort.MaxValue : (ushort)0),
			4 => builder.Insert(index, sample == 0 ? int.MinValue : sample == 1 ? int.MaxValue : 0),
			5 => builder.Insert(index, sample == 1 ? uint.MaxValue : 0u),
			6 => builder.Insert(index, sample == 0 ? long.MinValue : sample == 1 ? long.MaxValue : 0L),
			_ => builder.Insert(index, sample == 1 ? ulong.MaxValue : 0UL)
		};
	}

	public static int StringBuilderStableIntegralInsertionEntry()
	{
		string[] minimum = ["-128", "0", "-32768", "0", "-2147483648", "0", "-9223372036854775808", "0"];
		string[] maximum = ["127", "255", "32767", "65535", "2147483647", "4294967295", "9223372036854775807", "18446744073709551615"];
		for (var width = 0; width < 8; width++)
		for (var capacity = 0; capacity < 2; capacity++)
		{
			var builder = new StringBuilder(capacity == 0 ? 1 : 64);
			for (var sample = 0; sample < 3; sample++)
			{
				builder.Clear().Append("ab");
				GC.Collect();
				if (!ReferenceEquals(InsertStableInteger(builder, sample, width, sample), builder)) return 1;
				var text = sample == 0 ? minimum[width] : sample == 1 ? maximum[width] : "0";
				var snapshot = builder.ToString(); GC.Collect();
				builder.Clear().Append("reuse"); GC.Collect();
				if (snapshot.Length != text.Length + 2 || builder.ToString() != "reuse") return 2;
				for (var position = 0; position < snapshot.Length; position++)
				{
					var expected = position >= sample && position < sample + text.Length ? text[position - sample] :
						"ab"[position < sample ? position : position - text.Length];
					if (snapshot[position] != expected) return 6;
				}
			}
			for (var invalid = 0; invalid < 2; invalid++)
			{
				try { InsertStableInteger(builder, invalid == 0 ? -1 : builder.Length + 1, width, 1); return 3; }
				catch (ArgumentOutOfRangeException) { GC.Collect(); }
				if (builder.ToString() != "reuse") return 4;
			}
			builder.Insert(0, 0);
			if (builder.ToString() != "0reuse") return 5;
		}
		return 42;
	}
}
