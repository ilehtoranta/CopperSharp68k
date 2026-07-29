/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler;

/// <summary>Stable diagnostic identifiers produced by the compiler.</summary>
public static class M68kDiagnosticIds
{
	public const string InvalidInput = "C68K0001";
	public const string EntryPointNotFound = "C68K0002";
	public const string UnsupportedSignature = "C68K0003";
	public const string UnsupportedInstruction = "C68K0004";
	public const string InvalidEvaluationStack = "C68K0005";
	public const string UnresolvedImport = "C68K0006";
	public const string ImageOverflow = "C68K0007";
	public const string InvalidOutputOptions = "C68K0008";
	public const string InvalidMetadata = "C68K0009";
	public const string StaticAnalysis = "C68K0010";
	public const string UnsupportedPolymorphism = "C68K0011";
}

/// <summary>A compiler error tied to metadata and, when available, an IL offset.</summary>
public sealed class M68kCompilationException : Exception
{
	public M68kCompilationException(
		string diagnosticId,
		string message,
		string? method = null,
		int? ilOffset = null,
		Exception? innerException = null)
		: base(FormatMessage(diagnosticId, message, method, ilOffset), innerException)
	{
		DiagnosticId = diagnosticId;
		Method = method;
		IlOffset = ilOffset;
	}

	public string DiagnosticId { get; }

	public string? Method { get; }

	public int? IlOffset { get; }

	private static string FormatMessage(string id, string message, string? method, int? ilOffset)
	{
		var location = method is null
			? string.Empty
			: ilOffset is null
				? $" {method}"
				: $" {method} IL_{ilOffset.Value:X4}";
		return $"{id}:{location}: {message}";
	}
}
