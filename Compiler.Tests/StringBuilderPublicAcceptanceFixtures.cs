/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Globalization;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private static bool SurfaceText(StringBuilder builder, string expected) => builder.ToString(0, builder.Length) == expected;

	public static unsafe int CoreLibStringBuilderPublicAcceptanceEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			if (!SurfaceText(new StringBuilder(), "")) return 100;
			if (!SurfaceText(new StringBuilder(8), "")) return 101;
			if (!SurfaceText(new StringBuilder(8, 64), "")) return 102;
			if (!SurfaceText(new StringBuilder("pq"), "pq")) return 103;
			if (!SurfaceText(new StringBuilder("pq", 8), "pq")) return 104;
			if (!SurfaceText(new StringBuilder("xpqy", 1, 2, 8), "pq")) return 105;
			var builder = new StringBuilder(16);
			var donor = new StringBuilder("xpqy");
			var chars = new char[2]; chars[0] = 'p'; chars[1] = 'q';
			char* pointer = stackalloc char[2]; pointer[0] = 'p'; pointer[1] = 'q';
			builder.Clear().Append((sbyte)-7); if (!SurfaceText(builder, "-7")) return 200;
			builder.Clear().Append((byte)7); if (!SurfaceText(builder, "7")) return 201;
			builder.Clear().Append((short)-7); if (!SurfaceText(builder, "-7")) return 202;
			builder.Clear().Append((ushort)7); if (!SurfaceText(builder, "7")) return 203;
			builder.Clear().Append(-7); if (!SurfaceText(builder, "-7")) return 204;
			builder.Clear().Append(7u); if (!SurfaceText(builder, "7")) return 205;
			builder.Clear().Append(-7L); if (!SurfaceText(builder, "-7")) return 206;
			builder.Clear().Append(7UL); if (!SurfaceText(builder, "7")) return 207;
			builder.Clear().Append(1.25f); if (!SurfaceText(builder, "1.25")) return 208;
			builder.Clear().Append(1.25d); if (!SurfaceText(builder, "1.25")) return 209;
			builder.Clear().Append(-7.50m); if (!SurfaceText(builder, "-7.50")) return 210;
			builder.Clear().Append(true); if (!SurfaceText(builder, "True")) return 211;
			builder.Clear().Append('p'); if (!SurfaceText(builder, "p")) return 212;
			builder.Clear().Append('p', 2); if (!SurfaceText(builder, "pp")) return 213;
			builder.Clear().Append("pq"); if (!SurfaceText(builder, "pq")) return 214;
			builder.Clear().Append("xpqy", 1, 2); if (!SurfaceText(builder, "pq")) return 215;
			builder.Clear().Append(chars); if (!SurfaceText(builder, "pq")) return 216;
			builder.Clear().Append(chars, 1, 1); if (!SurfaceText(builder, "q")) return 217;
			builder.Clear().Append(chars.AsSpan()); if (!SurfaceText(builder, "pq")) return 218;
			builder.Clear().Append(chars.AsMemory()); if (!SurfaceText(builder, "pq")) return 219;
			builder.Clear().Append(pointer, 2); if (!SurfaceText(builder, "pq")) return 220;
			builder.Clear().Append(donor); if (!SurfaceText(builder, "xpqy")) return 221;
			builder.Clear().Append(donor, 1, 2); if (!SurfaceText(builder, "pq")) return 222;
			builder.Clear().Append((object)7); if (!SurfaceText(builder, "7")) return 223;
			builder.Clear().Insert(0, (sbyte)-7); if (!SurfaceText(builder, "-7")) return 300;
			builder.Clear().Insert(0, (byte)7); if (!SurfaceText(builder, "7")) return 301;
			builder.Clear().Insert(0, (short)-7); if (!SurfaceText(builder, "-7")) return 302;
			builder.Clear().Insert(0, (ushort)7); if (!SurfaceText(builder, "7")) return 303;
			builder.Clear().Insert(0, -7); if (!SurfaceText(builder, "-7")) return 304;
			builder.Clear().Insert(0, 7u); if (!SurfaceText(builder, "7")) return 305;
			builder.Clear().Insert(0, -7L); if (!SurfaceText(builder, "-7")) return 306;
			builder.Clear().Insert(0, 7UL); if (!SurfaceText(builder, "7")) return 307;
			builder.Clear().Insert(0, 1.25f); if (!SurfaceText(builder, "1.25")) return 308;
			builder.Clear().Insert(0, 1.25d); if (!SurfaceText(builder, "1.25")) return 309;
			builder.Clear().Insert(0, -7.50m); if (!SurfaceText(builder, "-7.50")) return 310;
			builder.Clear().Insert(0, true); if (!SurfaceText(builder, "True")) return 311;
			builder.Clear().Insert(0, 'p'); if (!SurfaceText(builder, "p")) return 312;
			builder.Clear().Insert(0, "pq"); if (!SurfaceText(builder, "pq")) return 313;
			builder.Clear().Insert(0, "p", 2); if (!SurfaceText(builder, "pp")) return 314;
			builder.Clear().Insert(0, chars); if (!SurfaceText(builder, "pq")) return 315;
			builder.Clear().Insert(0, chars, 1, 1); if (!SurfaceText(builder, "q")) return 316;
			builder.Clear().Insert(0, chars.AsSpan()); if (!SurfaceText(builder, "pq")) return 317;
			builder.Clear().Insert(0, (object)7); if (!SurfaceText(builder, "7")) return 318;
			var objects = new object?[3]; objects[0] = 1; objects[1] = 2; objects[2] = 3;
			var objectSpan = new ReadOnlySpan<object?>(objects);
			builder.Clear().AppendFormat("{0}", (object)1); if (!SurfaceText(builder, "1")) return 400;
			builder.Clear().AppendFormat(CultureInfo.InvariantCulture, "{0}", (object)1); if (!SurfaceText(builder, "1")) return 401;
			builder.Clear().AppendFormat("{0}{1}", (object)1, (object)2); if (!SurfaceText(builder, "12")) return 402;
			builder.Clear().AppendFormat(CultureInfo.InvariantCulture, "{0}{1}", (object)1, (object)2); if (!SurfaceText(builder, "12")) return 403;
			builder.Clear().AppendFormat("{0}{1}{2}", (object)1, (object)2, (object)3); if (!SurfaceText(builder, "123")) return 404;
			builder.Clear().AppendFormat(CultureInfo.InvariantCulture, "{0}{1}{2}", (object)1, (object)2, (object)3); if (!SurfaceText(builder, "123")) return 405;
			builder.Clear().AppendFormat("{0}{1}{2}", objects); if (!SurfaceText(builder, "123")) return 406;
			builder.Clear().AppendFormat(CultureInfo.InvariantCulture, "{0}{1}{2}", objects); if (!SurfaceText(builder, "123")) return 407;
			builder.Clear().AppendFormat("{0}{1}{2}", objectSpan); if (!SurfaceText(builder, "123")) return 408;
			builder.Clear().AppendFormat(CultureInfo.InvariantCulture, "{0}{1}{2}", objectSpan); if (!SurfaceText(builder, "123")) return 409;
			var parsed = CompositeFormat.Parse("{0}");
			builder.Clear().AppendFormat<int>(CultureInfo.InvariantCulture, parsed, 1); if (!SurfaceText(builder, "1")) return 420;
			builder.Clear().AppendFormat<int, int>(CultureInfo.InvariantCulture, parsed, 1, 2); if (!SurfaceText(builder, "1")) return 421;
			builder.Clear().AppendFormat<int, int, int>(CultureInfo.InvariantCulture, parsed, 1, 2, 3); if (!SurfaceText(builder, "1")) return 422;
			builder.Clear().AppendFormat(CultureInfo.InvariantCulture, parsed, objects); if (!SurfaceText(builder, "1")) return 423;
			builder.Clear().AppendFormat(CultureInfo.InvariantCulture, parsed, objectSpan); if (!SurfaceText(builder, "1")) return 424;
			var strings = new string?[2]; strings[0] = "p"; strings[1] = "q";
			var stringSpan = new ReadOnlySpan<string?>(strings);
			var integers = new int[2]; integers[0] = 1; integers[1] = 2;
			builder.Clear().AppendJoin('|', strings); if (!SurfaceText(builder, "p|q")) return 500;
			builder.Clear().AppendJoin("|", strings); if (!SurfaceText(builder, "p|q")) return 501;
			builder.Clear().AppendJoin('|', stringSpan); if (!SurfaceText(builder, "p|q")) return 502;
			builder.Clear().AppendJoin("|", stringSpan); if (!SurfaceText(builder, "p|q")) return 503;
			builder.Clear().AppendJoin('|', objects); if (!SurfaceText(builder, "1|2|3")) return 504;
			builder.Clear().AppendJoin("|", objects); if (!SurfaceText(builder, "1|2|3")) return 505;
			builder.Clear().AppendJoin('|', objectSpan); if (!SurfaceText(builder, "1|2|3")) return 506;
			builder.Clear().AppendJoin("|", objectSpan); if (!SurfaceText(builder, "1|2|3")) return 507;
			builder.Clear().AppendJoin<int>('|', integers); if (!SurfaceText(builder, "1|2")) return 508;
			builder.Clear().AppendJoin<int>("|", integers); if (!SurfaceText(builder, "1|2")) return 509;
			builder.Clear();
			var handler0 = new StringBuilder.AppendInterpolatedStringHandler(1, 0, builder, CultureInfo.InvariantCulture);
			handler0.AppendLiteral("p");
			if (!ReferenceEquals(builder, builder.Append(ref handler0)) || !SurfaceText(builder, "p")) return 600;
			builder.Clear();
			var handler1 = new StringBuilder.AppendInterpolatedStringHandler(1, 0, builder, CultureInfo.InvariantCulture);
			handler1.AppendLiteral("p");
			if (!ReferenceEquals(builder, builder.Append(CultureInfo.InvariantCulture, ref handler1)) || !SurfaceText(builder, "p")) return 601;
			builder.Clear();
			var handler2 = new StringBuilder.AppendInterpolatedStringHandler(1, 0, builder, CultureInfo.InvariantCulture);
			handler2.AppendLiteral("p");
			if (!ReferenceEquals(builder, builder.AppendLine(ref handler2)) || !SurfaceText(builder, "p" + Environment.NewLine)) return 602;
			builder.Clear();
			var handler3 = new StringBuilder.AppendInterpolatedStringHandler(1, 0, builder, CultureInfo.InvariantCulture);
			handler3.AppendLiteral("p");
			if (!ReferenceEquals(builder, builder.AppendLine(CultureInfo.InvariantCulture, ref handler3)) || !SurfaceText(builder, "p" + Environment.NewLine)) return 603;
			builder.Clear().AppendLine(); if (!SurfaceText(builder, Environment.NewLine)) return 604;
			builder.Clear().AppendLine("p"); if (!SurfaceText(builder, "p" + Environment.NewLine)) return 605;
			builder.Clear().Append("pqp").Replace('p', 'x'); if (!SurfaceText(builder, "xqx")) return 700;
			builder.Clear().Append("pqp").Replace('p', 'x', 1, 2); if (!SurfaceText(builder, "pqx")) return 701;
			builder.Clear().Append("pqp").Replace("p", "xx"); if (!SurfaceText(builder, "xxqxx")) return 702;
			builder.Clear().Append("pqp").Replace("p", "xx", 1, 2); if (!SurfaceText(builder, "pqxx")) return 703;
			builder.Clear().Append("pqp").Replace("p".AsSpan(), "xx".AsSpan()); if (!SurfaceText(builder, "xxqxx")) return 704;
			builder.Clear().Append("pqp").Replace("p".AsSpan(), "xx".AsSpan(), 1, 2); if (!SurfaceText(builder, "pqxx")) return 705;
			builder.Clear().Append("pq");
			if (!builder.Equals("pq".AsSpan()) || !builder.Equals(new StringBuilder("pq"))) return 800;
			var destination = new char[4]; destination[0] = '!'; destination[3] = '!';
			builder.CopyTo(0, destination, 1, 2); if (destination[0] != '!' || destination[1] != 'p' || destination[2] != 'q' || destination[3] != '!') return 801;
			destination[1] = destination[2] = '?';
			builder.CopyTo(0, destination.AsSpan(1, 2), 2); if (destination[0] != '!' || destination[1] != 'p' || destination[2] != 'q' || destination[3] != '!') return 802;
			if (builder.ToString(1, 1) != "q") return 803;
			var snapshot = builder.ToString();
			if (snapshot != "pq") return 804;
			if (builder.Length != 2 || builder.MaxCapacity != int.MaxValue || builder.Capacity < 2) return 805;
			builder.Capacity = 32; if (builder.Capacity != 32 || builder.EnsureCapacity(64) < 64) return 806;
			builder.Length = 4; builder[2] = 'x'; builder[3] = 'y';
			if (builder[0] != 'p' || builder[3] != 'y' || !SurfaceText(builder, "pqxy")) return 807;
			var chunks = builder.GetChunks(); var total = 0;
			while (chunks.MoveNext()) total += chunks.Current.Length;
			if (total != 4) return 808;
			if (!ReferenceEquals(builder, builder.Remove(1, 2)) || !SurfaceText(builder, "py")) return 809;
			GC.Collect();
			if (!ReferenceEquals(builder, builder.Clear()) || builder.Length != 0 || snapshot != "pq") return 810;
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}
}
