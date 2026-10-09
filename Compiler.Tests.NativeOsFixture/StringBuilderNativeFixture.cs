using System.Globalization;
using System.Text;

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
}
