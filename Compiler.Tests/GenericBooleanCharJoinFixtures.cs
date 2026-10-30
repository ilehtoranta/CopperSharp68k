/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Text;
using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
    public static int CoreLibStringBuilderGenericBooleanCharJoinsEntry()
    {
        if (CheckGenericJoin<bool>(false, true, "[False|True]") != 42) return 1;
        if (CheckGenericJoin<char>('\0', '\uFFFF', "[\0|\uFFFF]") != 42) return 2;
        if (CheckGenericJoin<char>('\uD800', '\uDC00', "[\uD800|\uDC00]") != 42) return 3;
        if (CheckGenericJoin<char>('Ω', 'A', "[Ω|A]") != 42) return 4;
        return 42;
    }
    public static int CoreLibGenericBooleanCharCallbackContractsEntry()
    {
        if (GenericJoinCallbacks<bool>(false, true, "False", "True") != 42) return 1;
        if (GenericJoinCallbacks<char>('\0', 'Ω', "\0", "Ω") != 42) return 2;
        if (CheckStructGenericJoin<bool>(true, false, "True", "False") != 42) return 3;
        return CheckStructGenericJoin<char>('\uD800', '\uDC00', "\uD800", "\uDC00");
    }
    public static int CoreLibGenericBooleanCharOwnershipEntry()
    {
        var state = new GenericJoinState(); var booleans = CreateOwnedGenericJoin(true, false, state); GC.Collect();
        if (!booleans.MoveNext() || !booleans.Current) return 1;
        GC.Collect();
        if (!booleans.MoveNext() || booleans.Current || booleans.MoveNext()) return 2;
        booleans.Dispose(); booleans.Dispose(); GC.Collect();
        if (booleans.Current || booleans.MoveNext() || state.Creations != 1 || state.Moves != 3 || state.Currents != 2 || state.Disposals != 1) return 3;
        state = new GenericJoinState(); var chars = CreateOwnedGenericJoin('Ω', '\uFFFF', state); GC.Collect();
        if (!chars.MoveNext() || chars.Current != 'Ω') return 4;
        GC.Collect();
        if (!chars.MoveNext() || chars.Current != '\uFFFF' || chars.MoveNext()) return 5;
        chars.Dispose(); chars.Dispose(); GC.Collect();
        if (chars.Current != '\0' || chars.MoveNext() || state.Creations != 1 || state.Moves != 3 || state.Currents != 2 || state.Disposals != 1) return 6;
        return 42;
    }
    private static int BooleanCharJoinAllocation<T>(T first, T second, string firstText, string secondText)
    {
        var values = new T[2]; values[0] = first; values[1] = second;
        for (var boxed = 0; boxed < 2; boxed++)
        for (var text = 0; text < 2; text++)
        for (var capacity = 1; capacity <= 192; capacity += 191)
        {
            var warm = new StringBuilder(192); AppendGenericJoin(warm, values, text != 0);
            var expected = "seed" + firstText + (text == 0 ? "|" : "::") + secondText;
            var succeeded = false;
            for (var failAt = 1; failAt <= 64; failAt++)
            {
                var state = new GenericJoinState();
                IEnumerable<T> source = boxed == 0 ? new GenericJoinSource<T>(values, state) : new StructGenericJoinSource<T>(values, state);
                var builder = new StringBuilder(capacity).Append("seed"); var snapshot = builder.ToString();
                var threw = false; SetStringBuilderAllocationFailure(failAt);
                try { AppendGenericJoin(builder, source, text != 0); }
                catch (OutOfMemoryException) { threw = true; }
                finally { SetStringBuilderAllocationFailure(0); }
                var failed = builder.ToString();
                if (state.Creations != 1 || state.Disposals != (failAt == 1 ? 0 : 1) || snapshot != "seed") return 100 + failAt;
                if (threw ? !expected.StartsWith(failed, StringComparison.Ordinal) || failed.Length < 4 : failed != expected) return 200 + failAt;
                if (failAt <= 2 && (failed != "seed" || state.Moves != 0 || state.Currents != 0)) return 300 + failAt;
                builder.Clear().Append("seed"); var retry = new GenericJoinState();
                AppendGenericJoin(builder, new StructGenericJoinSource<T>(values, retry), text != 0);
                if (builder.ToString() != expected || retry.Disposals != 1 || snapshot != "seed") return 400 + failAt;
                if (!threw) { succeeded = true; break; }
            }
            if (!succeeded) return 500;
        }
        return 42;
    }
    public static int CoreLibGenericBooleanCharAllocationEntry()
    {
        if (BooleanCharJoinAllocation<bool>(false, true, "False", "True") != 42) return 1;
        return BooleanCharJoinAllocation<char>('\0', '\u03A9', "\0", "\u03A9");
    }
    private static int BooleanCharJoinCapacity<T>(T first, T second)
    {
        var values = new T[2]; values[0] = first; values[1] = second;
        for (var boxed = 0; boxed < 2; boxed++)
        for (var character = 0; character < 2; character++)
        {
            var state = new GenericJoinState();
            IEnumerable<T> source = boxed == 0 ? new GenericJoinSource<T>(values, state) : new StructGenericJoinSource<T>(values, state);
            var builder = new StringBuilder(1, 4).Append("seed"); var snapshot = builder.ToString();
            try { if (character == 0) builder.AppendJoin("|", source); else builder.AppendJoin('|', source); return 1; }
            catch (ArgumentOutOfRangeException error) { if (error.ParamName != "valueCount" || error.ActualValue != null) throw new Exception(error.ParamName); }
            if (builder.ToString() != "seed" || builder.Capacity != 4 || snapshot != "seed") return 2;
            if (state.Creations != 1 || state.Moves != 1 || state.Currents != 1 || state.Disposals != 1) return 3;
            builder.Clear(); var retry = new GenericJoinState();
            builder.AppendJoin('|', new GenericJoinSource<T>(new T[0], retry)); GC.Collect();
            if (builder.Length != 0 || retry.Moves != 1 || retry.Currents != 0 || retry.Disposals != 1 || snapshot != "seed") return 4;
        }
        return 42;
    }
    public static int CoreLibGenericBooleanCharCapacityEntry()
    {
        if (BooleanCharJoinCapacity<bool>(false, true) != 42) return 1;
        return BooleanCharJoinCapacity<char>('\0', '\u03A9');
    }
}
