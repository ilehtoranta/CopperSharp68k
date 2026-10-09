/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Compiler.Framework;

internal static class StringBuilderFrameworkSurface
{
	private static readonly FrameworkTypeId BuilderType = FrameworkTypeId.Named("System.Runtime", "System.Text.StringBuilder");
	private static readonly FrameworkTypeId HandlerType = FrameworkTypeId.Named("System.Runtime", "AppendInterpolatedStringHandler", BuilderType);
	private static readonly FrameworkTypeId ChunkEnumeratorType = FrameworkTypeId.Named("System.Runtime", "ChunkEnumerator", BuilderType);
	private static readonly FrameworkTypeId ChunkIndexType = FrameworkTypeId.Named("System.Runtime", "ManyChunkInfo", ChunkEnumeratorType);
	// Exact public definitions from the pinned 10.0.9 contract-compatible input.
	// This inventory supplies effects; it does not enable unlisted body admission.
	internal static readonly IReadOnlyList<FrameworkMemberId> Members = new FrameworkMemberId[] {
		Member(".ctor", 0x20, 0, 0, FrameworkTypeId.Primitive("System.Void"), []),
		Member(".ctor", 0x20, 0, 1, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.Int32")]),
		Member(".ctor", 0x20, 0, 1, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.String")]),
		Member(".ctor", 0x20, 0, 2, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int32")]),
		Member(".ctor", 0x20, 0, 2, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.Primitive("System.Int32")]),
		Member(".ctor", 0x20, 0, 4, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int32")]),
		Member("Append", 0x20, 0, 1, BuilderType, [FrameworkTypeId.Named("System.Runtime", "System.Decimal")]),
		Member("Append", 0x20, 0, 1, BuilderType, [FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlyMemory`1"), [FrameworkTypeId.Primitive("System.Char")])]),
		Member("Append", 0x20, 0, 1, BuilderType, [FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Char")])]),
		Member("Append", 0x20, 0, 1, BuilderType, [FrameworkTypeId.ByReference(FrameworkTypeId.Named("System.Runtime", "AppendInterpolatedStringHandler", BuilderType))]),
		Member("Append", 0x20, 0, 1, BuilderType, [BuilderType]),
		Member("Append", 0x20, 0, 1, BuilderType, [FrameworkTypeId.Primitive("System.Boolean")]),
		Member("Append", 0x20, 0, 1, BuilderType, [FrameworkTypeId.Primitive("System.Byte")]),
		Member("Append", 0x20, 0, 1, BuilderType, [FrameworkTypeId.Primitive("System.Char")]),
		Member("Append", 0x20, 0, 1, BuilderType, [FrameworkTypeId.SzArray(FrameworkTypeId.Primitive("System.Char"))]),
		Member("Append", 0x20, 0, 1, BuilderType, [FrameworkTypeId.Primitive("System.Double")]),
		Member("Append", 0x20, 0, 1, BuilderType, [FrameworkTypeId.Primitive("System.Int16")]),
		Member("Append", 0x20, 0, 1, BuilderType, [FrameworkTypeId.Primitive("System.Int32")]),
		Member("Append", 0x20, 0, 1, BuilderType, [FrameworkTypeId.Primitive("System.Int64")]),
		Member("Append", 0x20, 0, 1, BuilderType, [FrameworkTypeId.Primitive("System.Object")]),
		Member("Append", 0x20, 0, 1, BuilderType, [FrameworkTypeId.Primitive("System.SByte")]),
		Member("Append", 0x20, 0, 1, BuilderType, [FrameworkTypeId.Primitive("System.Single")]),
		Member("Append", 0x20, 0, 1, BuilderType, [FrameworkTypeId.Primitive("System.String")]),
		Member("Append", 0x20, 0, 1, BuilderType, [FrameworkTypeId.Primitive("System.UInt16")]),
		Member("Append", 0x20, 0, 1, BuilderType, [FrameworkTypeId.Primitive("System.UInt32")]),
		Member("Append", 0x20, 0, 1, BuilderType, [FrameworkTypeId.Primitive("System.UInt64")]),
		Member("Append", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Named("System.Runtime", "System.IFormatProvider"), FrameworkTypeId.ByReference(FrameworkTypeId.Named("System.Runtime", "AppendInterpolatedStringHandler", BuilderType))]),
		Member("Append", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Pointer(FrameworkTypeId.Primitive("System.Char")), FrameworkTypeId.Primitive("System.Int32")]),
		Member("Append", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Char"), FrameworkTypeId.Primitive("System.Int32")]),
		Member("Append", 0x20, 0, 3, BuilderType, [BuilderType, FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int32")]),
		Member("Append", 0x20, 0, 3, BuilderType, [FrameworkTypeId.SzArray(FrameworkTypeId.Primitive("System.Char")), FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int32")]),
		Member("Append", 0x20, 0, 3, BuilderType, [FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int32")]),
		Member("AppendFormat", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Object")])]),
		Member("AppendFormat", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.Primitive("System.Object")]),
		Member("AppendFormat", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.SzArray(FrameworkTypeId.Primitive("System.Object"))]),
		Member("AppendFormat", 0x20, 0, 3, BuilderType, [FrameworkTypeId.Named("System.Runtime", "System.IFormatProvider"), FrameworkTypeId.Named("System.Runtime", "System.Text.CompositeFormat"), FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Object")])]),
		Member("AppendFormat", 0x20, 0, 3, BuilderType, [FrameworkTypeId.Named("System.Runtime", "System.IFormatProvider"), FrameworkTypeId.Named("System.Runtime", "System.Text.CompositeFormat"), FrameworkTypeId.SzArray(FrameworkTypeId.Primitive("System.Object"))]),
		Member("AppendFormat", 0x20, 0, 3, BuilderType, [FrameworkTypeId.Named("System.Runtime", "System.IFormatProvider"), FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Object")])]),
		Member("AppendFormat", 0x20, 0, 3, BuilderType, [FrameworkTypeId.Named("System.Runtime", "System.IFormatProvider"), FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.Primitive("System.Object")]),
		Member("AppendFormat", 0x20, 0, 3, BuilderType, [FrameworkTypeId.Named("System.Runtime", "System.IFormatProvider"), FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.SzArray(FrameworkTypeId.Primitive("System.Object"))]),
		Member("AppendFormat", 0x20, 0, 3, BuilderType, [FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.Primitive("System.Object"), FrameworkTypeId.Primitive("System.Object")]),
		Member("AppendFormat", 0x20, 0, 4, BuilderType, [FrameworkTypeId.Named("System.Runtime", "System.IFormatProvider"), FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.Primitive("System.Object"), FrameworkTypeId.Primitive("System.Object")]),
		Member("AppendFormat", 0x20, 0, 4, BuilderType, [FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.Primitive("System.Object"), FrameworkTypeId.Primitive("System.Object"), FrameworkTypeId.Primitive("System.Object")]),
		Member("AppendFormat", 0x20, 0, 5, BuilderType, [FrameworkTypeId.Named("System.Runtime", "System.IFormatProvider"), FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.Primitive("System.Object"), FrameworkTypeId.Primitive("System.Object"), FrameworkTypeId.Primitive("System.Object")]),
		Member("AppendFormat", 0x30, 1, 3, BuilderType, [FrameworkTypeId.Named("System.Runtime", "System.IFormatProvider"), FrameworkTypeId.Named("System.Runtime", "System.Text.CompositeFormat"), FrameworkTypeId.GenericMethodParameter(0)]),
		Member("AppendFormat", 0x30, 2, 4, BuilderType, [FrameworkTypeId.Named("System.Runtime", "System.IFormatProvider"), FrameworkTypeId.Named("System.Runtime", "System.Text.CompositeFormat"), FrameworkTypeId.GenericMethodParameter(0), FrameworkTypeId.GenericMethodParameter(1)]),
		Member("AppendFormat", 0x30, 3, 5, BuilderType, [FrameworkTypeId.Named("System.Runtime", "System.IFormatProvider"), FrameworkTypeId.Named("System.Runtime", "System.Text.CompositeFormat"), FrameworkTypeId.GenericMethodParameter(0), FrameworkTypeId.GenericMethodParameter(1), FrameworkTypeId.GenericMethodParameter(2)]),
		Member("AppendJoin", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Char"), FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Object")])]),
		Member("AppendJoin", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Char"), FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.String")])]),
		Member("AppendJoin", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Char"), FrameworkTypeId.SzArray(FrameworkTypeId.Primitive("System.Object"))]),
		Member("AppendJoin", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Char"), FrameworkTypeId.SzArray(FrameworkTypeId.Primitive("System.String"))]),
		Member("AppendJoin", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Object")])]),
		Member("AppendJoin", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.String")])]),
		Member("AppendJoin", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.SzArray(FrameworkTypeId.Primitive("System.Object"))]),
		Member("AppendJoin", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.SzArray(FrameworkTypeId.Primitive("System.String"))]),
		Member("AppendJoin", 0x30, 1, 2, BuilderType, [FrameworkTypeId.Primitive("System.Char"), FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Collections.Generic.IEnumerable`1"), [FrameworkTypeId.GenericMethodParameter(0)])]),
		Member("AppendJoin", 0x30, 1, 2, BuilderType, [FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Collections.Generic.IEnumerable`1"), [FrameworkTypeId.GenericMethodParameter(0)])]),
		Member("AppendLine", 0x20, 0, 0, BuilderType, []),
		Member("AppendLine", 0x20, 0, 1, BuilderType, [FrameworkTypeId.ByReference(FrameworkTypeId.Named("System.Runtime", "AppendInterpolatedStringHandler", BuilderType))]),
		Member("AppendLine", 0x20, 0, 1, BuilderType, [FrameworkTypeId.Primitive("System.String")]),
		Member("AppendLine", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Named("System.Runtime", "System.IFormatProvider"), FrameworkTypeId.ByReference(FrameworkTypeId.Named("System.Runtime", "AppendInterpolatedStringHandler", BuilderType))]),
		Member("Clear", 0x20, 0, 0, BuilderType, []),
		Member("CopyTo", 0x20, 0, 3, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Span`1"), [FrameworkTypeId.Primitive("System.Char")]), FrameworkTypeId.Primitive("System.Int32")]),
		Member("CopyTo", 0x20, 0, 4, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.SzArray(FrameworkTypeId.Primitive("System.Char")), FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int32")]),
		Member("EnsureCapacity", 0x20, 0, 1, FrameworkTypeId.Primitive("System.Int32"), [FrameworkTypeId.Primitive("System.Int32")]),
		Member("Equals", 0x20, 0, 1, FrameworkTypeId.Primitive("System.Boolean"), [FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Char")])]),
		Member("Equals", 0x20, 0, 1, FrameworkTypeId.Primitive("System.Boolean"), [BuilderType]),
		Member("GetChunks", 0x20, 0, 0, FrameworkTypeId.Named("System.Runtime", "ChunkEnumerator", BuilderType), []),
		Member("Insert", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Named("System.Runtime", "System.Decimal")]),
		Member("Insert", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Char")])]),
		Member("Insert", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Boolean")]),
		Member("Insert", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Byte")]),
		Member("Insert", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Char")]),
		Member("Insert", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.SzArray(FrameworkTypeId.Primitive("System.Char"))]),
		Member("Insert", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Double")]),
		Member("Insert", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int16")]),
		Member("Insert", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int32")]),
		Member("Insert", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int64")]),
		Member("Insert", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Object")]),
		Member("Insert", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.SByte")]),
		Member("Insert", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Single")]),
		Member("Insert", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.String")]),
		Member("Insert", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.UInt16")]),
		Member("Insert", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.UInt32")]),
		Member("Insert", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.UInt64")]),
		Member("Insert", 0x20, 0, 3, BuilderType, [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.Primitive("System.Int32")]),
		Member("Insert", 0x20, 0, 4, BuilderType, [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.SzArray(FrameworkTypeId.Primitive("System.Char")), FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int32")]),
		Member("Remove", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int32")]),
		Member("Replace", 0x20, 0, 2, BuilderType, [FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Char")]), FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Char")])]),
		Member("Replace", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.Char"), FrameworkTypeId.Primitive("System.Char")]),
		Member("Replace", 0x20, 0, 2, BuilderType, [FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.Primitive("System.String")]),
		Member("Replace", 0x20, 0, 4, BuilderType, [FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Char")]), FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Char")]), FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int32")]),
		Member("Replace", 0x20, 0, 4, BuilderType, [FrameworkTypeId.Primitive("System.Char"), FrameworkTypeId.Primitive("System.Char"), FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int32")]),
		Member("Replace", 0x20, 0, 4, BuilderType, [FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int32")]),
		Member("ToString", 0x20, 0, 0, FrameworkTypeId.Primitive("System.String"), []),
		Member("ToString", 0x20, 0, 2, FrameworkTypeId.Primitive("System.String"), [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int32")]),
		Member("get_Capacity", 0x20, 0, 0, FrameworkTypeId.Primitive("System.Int32"), []),
		Member("get_Chars", 0x20, 0, 1, FrameworkTypeId.Primitive("System.Char"), [FrameworkTypeId.Primitive("System.Int32")]),
		Member("get_Length", 0x20, 0, 0, FrameworkTypeId.Primitive("System.Int32"), []),
		Member("get_MaxCapacity", 0x20, 0, 0, FrameworkTypeId.Primitive("System.Int32"), []),
		Member("set_Capacity", 0x20, 0, 1, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.Int32")]),
		Member("set_Chars", 0x20, 0, 2, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Char")]),
		Member("set_Length", 0x20, 0, 1, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.Int32")]),
	};

	// Exact private definitions reached by the pinned public acceptance graph.
	internal static readonly IReadOnlyList<FrameworkMemberId> Helpers = new FrameworkMemberId[] {
		Member(".ctor", 0x20, 0, 1, FrameworkTypeId.Primitive("System.Void"), [BuilderType]),
		Member(".ctor", 0x20, 0, 3, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int32"), BuilderType]),
		Member("<AppendFormat>g__MoveNext|121_0", 0x00, 0, 2, FrameworkTypeId.Primitive("System.Char"), [FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.ByReference(FrameworkTypeId.Primitive("System.Int32"))]),
		Member("Append", 0x20, 0, 2, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.ByReference(FrameworkTypeId.Primitive("System.Char")), FrameworkTypeId.Primitive("System.Int32")]),
		Member("AppendCore", 0x20, 0, 3, BuilderType, [BuilderType, FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int32")]),
		Member("AppendFormat", 0x30, 3, 6, BuilderType, [FrameworkTypeId.Named("System.Runtime", "System.IFormatProvider"), FrameworkTypeId.Named("System.Runtime", "System.Text.CompositeFormat"), FrameworkTypeId.GenericMethodParameter(0), FrameworkTypeId.GenericMethodParameter(1), FrameworkTypeId.GenericMethodParameter(2), FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Object")])]),
		Member("AppendJoinCore", 0x30, 1, 3, BuilderType, [FrameworkTypeId.ByReference(FrameworkTypeId.Primitive("System.Char")), FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Collections.Generic.IEnumerable`1"), [FrameworkTypeId.GenericMethodParameter(0)])]),
		Member("AppendJoinCore", 0x30, 1, 3, BuilderType, [FrameworkTypeId.ByReference(FrameworkTypeId.Primitive("System.Char")), FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.GenericMethodParameter(0)])]),
		Member("AppendSpanFormattable", 0x30, 1, 1, BuilderType, [FrameworkTypeId.GenericMethodParameter(0)]),
		Member("AppendWithExpansion", 0x20, 0, 1, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.Char")]),
		Member("AppendWithExpansion", 0x20, 0, 2, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.Char"), FrameworkTypeId.Primitive("System.Int32")]),
		Member("AppendWithExpansion", 0x20, 0, 2, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.ByReference(FrameworkTypeId.Primitive("System.Char")), FrameworkTypeId.Primitive("System.Int32")]),
		Member("ExpandByABlock", 0x20, 0, 1, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.Int32")]),
		Member("FindChunkForIndex", 0x20, 0, 1, BuilderType, [FrameworkTypeId.Primitive("System.Int32")]),
		Member("Insert", 0x20, 0, 3, BuilderType, [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Char")]), FrameworkTypeId.Primitive("System.Int32")]),
		Member("Insert", 0x20, 0, 3, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.ByReference(FrameworkTypeId.Primitive("System.Char")), FrameworkTypeId.Primitive("System.Int32")]),
		Member("InsertSpanFormattable", 0x30, 1, 2, BuilderType, [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.GenericMethodParameter(0)]),
		Member("MakeRoom", 0x20, 0, 5, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.ByReference(BuilderType), FrameworkTypeId.ByReference(FrameworkTypeId.Primitive("System.Int32")), FrameworkTypeId.Primitive("System.Boolean")]),
		Member("Next", 0x20, 0, 1, BuilderType, [BuilderType]),
		Member("Remove", 0x20, 0, 4, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.ByReference(BuilderType), FrameworkTypeId.ByReference(FrameworkTypeId.Primitive("System.Int32"))]),
		Member("ReplaceAllInChunk", 0x20, 0, 4, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Int32")]), BuilderType, FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Char")])]),
		Member("ReplaceInPlaceAtChunk", 0x20, 0, 4, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.ByReference(BuilderType), FrameworkTypeId.ByReference(FrameworkTypeId.Primitive("System.Int32")), FrameworkTypeId.ByReference(FrameworkTypeId.Primitive("System.Char")), FrameworkTypeId.Primitive("System.Int32")]),
		Member("StartsWith", 0x20, 0, 4, FrameworkTypeId.Primitive("System.Boolean"), [BuilderType, FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.Primitive("System.Int32"), FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Char")])]),
		Member("get_RemainingCurrentChunk", 0x20, 0, 0, FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Span`1"), [FrameworkTypeId.Primitive("System.Char")]), []),
	};

	private static readonly FrameworkTypeId Void = FrameworkTypeId.Primitive("System.Void");
	private static readonly FrameworkTypeId Integer = FrameworkTypeId.Primitive("System.Int32");
	private static readonly FrameworkTypeId Text = FrameworkTypeId.Primitive("System.String");
	private static readonly FrameworkTypeId GenericValue = FrameworkTypeId.GenericMethodParameter(0);
	private static readonly FrameworkTypeId CharacterSpan = FrameworkTypeId.GenericInstantiation(
		FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive("System.Char")]);
	internal static readonly IReadOnlyList<FrameworkMemberId> NestedMembers = new FrameworkMemberId[] {
		NestedMember(ChunkEnumeratorType, ".ctor", 0x20, 0, Void, BuilderType),
		NestedMember(ChunkEnumeratorType, "ChunkCount", 0, 0, Integer, BuilderType),
		NestedMember(ChunkEnumeratorType, "GetEnumerator", 0x20, 0, ChunkEnumeratorType),
		NestedMember(ChunkEnumeratorType, "MoveNext", 0x20, 0, FrameworkTypeId.Primitive("System.Boolean")),
		NestedMember(ChunkEnumeratorType, "get_Current", 0x20, 0,
			FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlyMemory`1"), [FrameworkTypeId.Primitive("System.Char")])),
		NestedMember(ChunkIndexType, ".ctor", 0x20, 0, Void, BuilderType, Integer),
		NestedMember(ChunkIndexType, "MoveNext", 0x20, 0, FrameworkTypeId.Primitive("System.Boolean"), FrameworkTypeId.ByReference(BuilderType)),
		NestedMember(HandlerType, ".ctor", 0x20, 0, Void, Integer, Integer, BuilderType),
		NestedMember(HandlerType, ".ctor", 0x20, 0, Void, Integer, Integer, BuilderType, FrameworkTypeId.Named("System.Runtime", "System.IFormatProvider")),
		NestedMember(HandlerType, "AppendLiteral", 0x20, 0, Void, Text),
		NestedMember(HandlerType, "AppendFormatted", 0x30, 1, Void, GenericValue),
		NestedMember(HandlerType, "AppendFormatted", 0x30, 1, Void, GenericValue, Text),
		NestedMember(HandlerType, "AppendFormatted", 0x30, 1, Void, GenericValue, Integer),
		NestedMember(HandlerType, "AppendFormatted", 0x30, 1, Void, GenericValue, Integer, Text),
		NestedMember(HandlerType, "AppendFormatted", 0x20, 0, Void, CharacterSpan),
		NestedMember(HandlerType, "AppendFormatted", 0x20, 0, Void, CharacterSpan, Integer, Text),
		NestedMember(HandlerType, "AppendFormatted", 0x20, 0, Void, Text),
		NestedMember(HandlerType, "AppendFormatted", 0x20, 0, Void, Text, Integer, Text),
		NestedMember(HandlerType, "AppendFormatted", 0x20, 0, Void, FrameworkTypeId.Primitive("System.Object"), Integer, Text),
		NestedMember(HandlerType, "AppendFormattedWithTempSpace", 0x30, 1, Void, GenericValue, Integer, Text),
		NestedMember(HandlerType, "AppendCustomFormatter", 0x30, 1, Void, GenericValue, Text),
	};

	private static readonly IReadOnlyList<FrameworkMemberId> AllMembers = Members.Concat(Helpers).Concat(NestedMembers).ToArray();
	internal static readonly IReadOnlyList<FrameworkMemberId> PublicMembers = Members.Concat(NestedMembers.Where(member =>
		member.DeclaringType.Equals(HandlerType) && member.Name is not ("AppendCustomFormatter" or "AppendFormattedWithTempSpace") ||
		member.DeclaringType.Equals(ChunkEnumeratorType) && member.Name is "GetEnumerator" or "MoveNext" or "get_Current")).ToArray();

	private static FrameworkMemberId NestedMember(FrameworkTypeId owner, string name, byte header, int generic,
		FrameworkTypeId result, params FrameworkTypeId[] parameters) => new(owner, name,
			new FrameworkMethodSignatureId(header, generic, parameters.Length, result, parameters));

	private static FrameworkMemberId Member(string name, byte header, int generic, int required,
		FrameworkTypeId result, FrameworkTypeId[] parameters) => new(BuilderType, name,
			new FrameworkMethodSignatureId(header, generic, required, result, parameters));

	public static bool TryGetEffects(FrameworkMemberId member, out FrameworkEffectSummary summary)
	{
		summary = null!;
		if (!TryGetDefinition(member, out var definition)) return false;
		summary = Effects(definition);
		return true;
	}

	public static bool TryGetPublicDefinition(FrameworkMemberId member, out FrameworkMemberId definition)
	{
		if (TryGetDefinition(member, out var candidate) && PublicMembers.Contains(candidate)) {
			definition = candidate;
			return true;
		}
		definition = null!;
		return false;
	}

	private static bool TryGetDefinition(FrameworkMemberId member, out FrameworkMemberId definition)
	{
		definition = null!;
		member = FrameworkImplementationProfile.Canonicalize(member);
		if (!member.DeclaringType.Equals(BuilderType) && !member.DeclaringType.Equals(HandlerType) &&
			!member.DeclaringType.Equals(ChunkEnumeratorType) && !member.DeclaringType.Equals(ChunkIndexType)) return false;
		foreach (var candidate in AllMembers)
		{
			if (!member.DeclaringType.Equals(candidate.DeclaringType) || member.Name != candidate.Name || member.Signature.Header != candidate.Signature.Header ||
				member.Signature.GenericParameterCount != candidate.Signature.GenericParameterCount ||
				member.MethodTypeArguments.Length != 0 && member.MethodTypeArguments.Length != candidate.Signature.GenericParameterCount)
				continue;
			var signature = candidate.Signature;
			if (member.MethodTypeArguments.Length != 0)
				signature = new FrameworkMethodSignatureId(signature.Header, signature.GenericParameterCount, signature.RequiredParameterCount,
					Substitute(signature.ReturnType, member.MethodTypeArguments), signature.ParameterTypes.Select(type => Substitute(type, member.MethodTypeArguments)).ToArray());
			// MethodSpec observations retain the definition signature and carry
			// arguments separately; specialized bodies can expose substituted types.
			if (!member.Signature.Equals(signature) && !(candidate.Signature.GenericParameterCount != 0 && member.Signature.Equals(candidate.Signature))) continue;
			definition = candidate;
			return true;
		}
		return false;
	}

	private static FrameworkTypeId Substitute(FrameworkTypeId type, IReadOnlyList<FrameworkTypeId> arguments) => type.Kind switch {
		FrameworkTypeKind.GenericMethodParameter => arguments[type.GenericParameterIndex],
		FrameworkTypeKind.GenericInstantiation => FrameworkTypeId.GenericInstantiation(type.ElementType!, type.GenericArguments.Select(item => Substitute(item, arguments)).ToArray()),
		_ => type
	};

	private static FrameworkEffectSummary Effects(FrameworkMemberId member)
	{
		// Conservative declarations for StringBuilder's own storage operations.
		// User formatting/enumeration callbacks remain separate reachable calls;
		// these flags are not a transitive purity summary for arbitrary callbacks.
		var flags = FrameworkEffects.ReadsManagedMemory | FrameworkEffects.MayThrow;
		var features = new List<FrameworkFeature> { FrameworkFeature.StringBuilder, FrameworkFeature.ManagedMemory,
			FrameworkFeature.ManagedObjects, FrameworkFeature.ManagedArrays, FrameworkFeature.ManagedStrings };
		var readOnly = member.Name.StartsWith("get_", StringComparison.Ordinal) ||
			member.Name is "Equals" or "GetChunks" or "FindChunkForIndex" or "Next" or "StartsWith" or "ChunkCount" or "GetEnumerator";
		var copyOrCharacterStore = member.Name is "CopyTo" or "set_Chars" or "ReplaceInPlaceAtChunk" or "<AppendFormat>g__MoveNext|121_0" ||
			member.Name == "Remove" && member.Signature.ParameterTypes.Length == 4 ||
			member.Name == ".ctor" && member.DeclaringType.Equals(BuilderType) && member.Signature.ParameterTypes.SequenceEqual([BuilderType]) ||
			member.Name == "MoveNext" || member.DeclaringType.Equals(HandlerType) && member.Name == ".ctor";
		if (!readOnly && member.Name != "ToString") flags |= FrameworkEffects.WritesManagedMemory;
		if (!readOnly && !copyOrCharacterStore || member.Name == "GetChunks") {
			flags |= FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect;
			features.Add(FrameworkFeature.ManagedGc);
		}
		if (member.Signature.ParameterTypes.Any(IsSpan) || IsSpan(member.Signature.ReturnType) ||
			member.Name is "GetChunks" or "AppendSpanFormattable" or "InsertSpanFormattable" ||
			member.DeclaringType.Equals(ChunkEnumeratorType) || member.DeclaringType.Equals(ChunkIndexType))
			features.Add(FrameworkFeature.Spans);
		if (member.Signature.ParameterTypes.Any(type => type.Kind == FrameworkTypeKind.Pointer || IsSpan(type) ||
			type.Kind == FrameworkTypeKind.ByReference && type.ElementType!.Equals(FrameworkTypeId.Primitive("System.Char")))) {
			flags |= FrameworkEffects.ReadsNativeMemory;
			if (member.Name == "CopyTo" && member.Signature.ParameterTypes.Any(IsSpan)) flags |= FrameworkEffects.WritesNativeMemory;
			features.Add(FrameworkFeature.NativeMemory);
		}
		if (member.Name is "AppendSpanFormattable" or "InsertSpanFormattable") features.Add(FrameworkFeature.Numerics);
		if (member.Signature.ParameterTypes.Any(type => type.Kind == FrameworkTypeKind.GenericInstantiation &&
			type.ElementType!.FullMetadataName == "System.Collections.Generic.IEnumerable`1")) features.Add(FrameworkFeature.ManagedCollections);
		if (member.Name is "AppendFormat" or "<AppendFormat>g__MoveNext|121_0" || member.DeclaringType.Equals(HandlerType) || member.Signature.ParameterTypes.Any(type =>
			type.Kind == FrameworkTypeKind.ByReference && type.ElementType!.Equals(
				FrameworkTypeId.Named("System.Runtime", "AppendInterpolatedStringHandler", BuilderType))))
			features.Add(FrameworkFeature.StringInterpolation);
		features.Add(FrameworkFeature.ManagedExceptions);
		return new(flags, features.Distinct().ToArray());
	}

	private static bool IsSpan(FrameworkTypeId type) => type.Kind == FrameworkTypeKind.GenericInstantiation &&
		type.ElementType!.FullMetadataName is "System.Span`1" or "System.ReadOnlySpan`1";
}
