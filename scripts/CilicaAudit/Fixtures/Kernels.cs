using System.Runtime.CompilerServices;

namespace CilicaAudit;

// Inputs are supplied in guest RAM at runtime so neither compiler can fold
// the benchmark to its answer. Hardware-like accesses deliberately use volatile.
public static unsafe class Kernels
{
    static int Input() => *(int*)0x4000;
    public static int Arithmetic() { int x = Input(); return ((x * 8 + x * 2) ^ (x >> 3)) + x % 16; }
    public static int CommonExpressions() { int x = Input(), a = x * 7 + 11, b = x * 7 + 11; return a * b + a; }
    public static int Inlining() { int x = Input(); return Mix(x, 3) + Mix(x, 5); }
    static int Mix(int x, int factor) { int y = x * factor; return y + (y >> 2); }
    public static int StructPromotion() { var p = new Pair { X = Input(), Y = 7 }; p.X += p.Y; return p.X * p.Y; }
    struct Pair { public int X, Y; }
    public static int ScalarReplacement() { var p = new Box(Input()); p.Value += 7; return p.Value * 3; }
    sealed class Box(int value) { public int Value = value; }
    public static int Devirtualization() { IValue value = new Value(Input()); return value.Get() + value.Get(); }
    interface IValue { int Get(); }
    sealed class Value(int value) : IValue { public int Get() => value * 3 + 1; }
    public static int Loop() { int n = Input(), sum = 0; for (int i = 0; i < n; i++) sum += i * 3 + 7; return sum; }
    public static int ArrayLoop() { int n = Input(); var a = new int[64]; for (int i = 0; i < a.Length; i++) a[i] = i + n; int sum = 0; for (int i = 0; i < a.Length; i++) sum += a[i]; return sum; }
    public static int Linq() { int n = Input(); return Enumerable.Range(0, n).Where(x => (x & 1) == 0).Select(x => x * 3).Sum(); }
    public static int ListGrowth() { var list = new List<int>(); int n = Input(); for (int i = 0; i < n; i++) list.Add(i); return list.Count + list[n - 1]; }
    public static int Aliasing() { int x = Input(); return Change(ref x, ref x); }
    static int Change(ref int a, ref int b) { a += 3; b *= 2; return a + b; }
    static volatile int observable;
    public static int Volatile() { observable = Input(); int a = observable; observable = a + 1; return a + observable; }
    public static int Finally() { int x = Input(); try { if (x == 13) throw new InvalidOperationException(); x *= 3; } catch (InvalidOperationException) { x = 17; } finally { observable = x + 1; } return x + observable; }
    public static int NativeWidth() { nuint x = (nuint)(uint)Input(); return (int)((x << 29) >> 29); }
    public static int CheckedOverflow() { int x = Input(); try { return checked(x * 100_000_000); } catch (OverflowException) { return 42; } }
}
