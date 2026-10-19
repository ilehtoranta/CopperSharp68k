using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests.NativeOsFixture;

public static class StringBuilderNativeFixture
{
    public static int Presized() => Text(32);
    public static int Growing() => Text(1);

    private static int Text(int capacity)
    {
        var builder = new StringBuilder(capacity);
        for (var i = 0; i < 8; i++) builder.Append("part");
        if (builder.Length != 32 || builder.Capacity < 32) return 11;
        Console.WriteLine(builder.ToString());
        return 42;
    }

    public static int IntegerFormat()
    {
        var builder = new StringBuilder(1);
        builder.AppendFormat(CultureInfo.InvariantCulture, "{0:D5}|{1:X8}", -42, 0x89ABCDEFu);
        Console.WriteLine(builder.ToString());
        return 42;
    }

    public static int IntegerHandler()
    {
        var builder = new StringBuilder(1);
        for (var i = 0; i < 8; i++) builder.Append(CultureInfo.InvariantCulture, $"[{i}]");
        Console.WriteLine(builder.ToString());
        return 42;
    }

    public static int CollectWithLiveBuilderAndSnapshot()
    {
        var builder = new StringBuilder(1);
        for (var i = 0; i < 8; i++)
        {
            builder.Append("part");
            // Repeated temporary builders/strings exercise the small managed heap.
            for (var j = 0; j < 32; j++)
            {
                var temporary = new StringBuilder(16);
                temporary.Append("discarded");
                if (temporary.ToString().Length != 9) return 12;
            }
            GC.Collect();
        }
        var snapshot = builder.ToString();
        GC.Collect();
        if (snapshot != "partpartpartpartpartpartpartpart" || builder.ToString() != snapshot) return 13;
        builder.Clear().Append("after collection");
        GC.Collect();
        Console.WriteLine(snapshot);
        Console.WriteLine(builder.ToString());
        return 42;
    }

    public static int CollectOnAllocationFailureWithLiveBuilderAndSnapshot()
    {
        var builder = new StringBuilder(1);
        for (var i = 0; i < 8; i++) builder.Append("part");
        var snapshot = builder.ToString();
        var observedSweeps = 0;
        for (var round = 0; round < 8; round++)
        {
            // Grow the live builder while retaining its independent old snapshot.
            builder.Append("part");
            for (var i = 0; i < 32; i++)
            {
                var before = ManagedPool.GetStaleBytes();
                if (AllocateDiscardedBuilder() != 9) return 14;
                // Collect resets this existing counter; ordinary allocation only
                // increases it. No explicit collection or disposal occurs here.
                if (ManagedPool.GetStaleBytes() < before) observedSweeps++;
            }
            if (snapshot != "partpartpartpartpartpartpartpart" ||
                builder.Length != 32 + (round + 1) * 4) return 15;
        }
        if (observedSweeps < 2 || builder.Capacity < 64) return 16;
        if (builder.ToString() != "partpartpartpartpartpartpartpartpartpartpartpartpartpartpartpart") return 17;
        builder.Clear().Append("after automatic collection");
        Console.WriteLine(snapshot);
        Console.WriteLine(builder.ToString());
        return 42;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int AllocateDiscardedBuilder()
    {
        // The backing char array alone consumes 256 payload bytes. 256 calls
        // allocate over 64 KiB on the test's 8 KiB heap, and return no reference.
        var temporary = new StringBuilder(128);
        temporary.Append("discarded");
        return temporary.ToString().Length;
    }
}
