/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

namespace CopperSharp.Tools.CilicaAudit;

// Shared with the test project so the benchmark's actual oracle is covered.
internal static class ReferenceResults
{
    public static int Get(string name, int x) => unchecked(name switch {
        "Arithmetic" => ((x * 8 + x * 2) ^ (x >> 3)) + x % 16,
        "CommonExpressions" => (x * 7 + 11) * (x * 7 + 11) + (x * 7 + 11),
        "Inlining" => x * 3 + ((x * 3) >> 2) + x * 5 + ((x * 5) >> 2),
        "StructPromotion" => (x + 7) * 7, "ScalarReplacement" => (x + 7) * 3,
        "Devirtualization" => (x * 3 + 1) * 2, "Loop" => 3 * x * (x - 1) / 2 + 7 * x,
        "ArrayLoop" => 2016 + 64 * x, "Linq" => 3 * ((x + 1) / 2) * (((x + 1) / 2) - 1),
        "LinqRangeSelect" => 3 * x * (x - 1) / 2, "ListGrowth" => 2 * x - 1,
        "Aliasing" => (x + 3) * 4, "Volatile" => 2 * x + 1,
        "Finally" => x == 13 ? 35 : 6 * x + 1, "NativeWidth" => x & 7,
        "SwitchTable" => (x & 7) switch { 0 => 0x1234, 1 => 0x2345, 2 => 0x3456, 3 => 0x4567, 4 => 0x5678, 5 => 0x6789, 6 => 0x789a, _ => 0x1357 },
        "CheckedOverflow" => CheckedProduct(x),
        _ => throw new ArgumentException(name) });

    static int CheckedProduct(int x)
    {
        try { return checked(x * 100_000_000); }
        catch (OverflowException) { return 42; }
    }
}
