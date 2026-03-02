// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;

// Tests for consecutive shift folding optimization in morph:
//   (x shift c1) shift c2 -> x shift (c1 + c2)
// This covers issue #74020 where repeated integer division by powers of two
// generates redundant shift instructions.

public class ShiftCombining
{
    public static int TestEntryPoint()
    {
        int result = 100;

        // === RSH (>>) combining ===
        if (!TestRshCombine()) result--;

        // === RSZ (>>>) combining ===
        if (!TestRszCombine()) result--;

        // === Boundary cases ===
        if (!TestBoundaryCases()) result--;

        // === Division pattern (original issue #74020) ===
        if (!TestDivisionPattern()) result--;

        return result;
    }

    // --- RSH ---

    static bool TestRshCombine()
    {
        bool pass = true;
        pass &= Check(RshCombine_3_4_Int32(100), 100 >> 7, "RSH combine 3+4 int32");
        pass &= Check(RshCombine_3_4_Int32(-100), -100 >> 7, "RSH combine 3+4 int32 negative");
        pass &= Check(RshCombine_5_10_Int32(int.MaxValue), int.MaxValue >> 15, "RSH combine 5+10 int32");
        
        pass &= CheckL(RshCombine_10_20_Int64(100L), 100L >> 30, "RSH combine 10+20 int64");
        pass &= CheckL(RshCombine_10_20_Int64(-100L), -100L >> 30, "RSH combine 10+20 int64 negative");
        return pass;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int RshCombine_3_4_Int32(int x) => (x >> 3) >> 4;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int RshCombine_5_10_Int32(int x) => (x >> 5) >> 10;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static long RshCombine_10_20_Int64(long x) => (x >> 10) >> 20;

    // --- RSZ ---

    static bool TestRszCombine()
    {
        bool pass = true;
        pass &= CheckU(RszCombine_3_4_UInt32(100u), 100u >>> 7, "RSZ combine 3+4 uint32");
        pass &= CheckU(RszCombine_5_10_UInt32(uint.MaxValue), uint.MaxValue >>> 15, "RSZ combine 5+10 uint32");

        pass &= CheckUL(RszCombine_10_20_UInt64(100uL), 100uL >>> 30, "RSZ combine 10+20 uint64");
        pass &= CheckUL(RszCombine_15_15_UInt64(ulong.MaxValue), ulong.MaxValue >>> 30, "RSZ combine 15+15 uint64");
        return pass;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static uint RszCombine_3_4_UInt32(uint x) => (x >>> 3) >>> 4;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static uint RszCombine_5_10_UInt32(uint x) => (x >>> 5) >>> 10;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static ulong RszCombine_10_20_UInt64(ulong x) => (x >>> 10) >>> 20;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static ulong RszCombine_15_15_UInt64(ulong x) => (x >>> 15) >>> 15;

    // --- Boundary cases ---

    static bool TestBoundaryCases()
    {
        bool pass = true;
        // Shift by 1+1 = 2
        pass &= Check(RshCombine_1_1_Int32(256), 256 >> 2, "RSH 1+1 int32");

        // Shift by 15+16 = 31 (exact bitwidth - 1 for int32)
        pass &= Check(RshCombine_15_16_Int32(int.MaxValue), int.MaxValue >> 31, "RSH 15+16=31 int32");

        // Shift by 31+31 = 62 (for int64, still within range)
        pass &= CheckL(RshCombine_31_31_Int64(long.MaxValue), long.MaxValue >> 62, "RSH 31+31=62 int64");
        return pass;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int RshCombine_1_1_Int32(int x) => (x >> 1) >> 1;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int RshCombine_15_16_Int32(int x) => (x >> 15) >> 16;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static long RshCombine_31_31_Int64(long x) => (x >> 31) >> 31;


    // --- Division pattern (issue #74020) ---
    // x / 4 / 8 lowers to consecutive shifts: (x >> 2) >> 3

    static bool TestDivisionPattern()
    {
        bool pass = true;
        pass &= Check(DivCombine_4_8_Int32(1024), 1024 / 32, "div 4 then 8 == div 32");
        pass &= Check(DivCombine_4_8_Int32(-1024), -1024 / 32, "div 4 then 8 == div 32 negative");
        pass &= Check(DivCombine_2_2_Int32(int.MaxValue), int.MaxValue / 4, "div 2 then 2 == div 4 MaxValue");
        return pass;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int DivCombine_4_8_Int32(int x) => x / 4 / 8;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int DivCombine_2_2_Int32(int x) => x / 2 / 2;


    // --- Helpers ---

    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool Check(int actual, int expected, string label)
    {
        if (actual != expected)
        {
            Console.WriteLine($"FAIL {label}: expected {expected}, got {actual}");
            return false;
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool CheckU(uint actual, uint expected, string label)
    {
        if (actual != expected)
        {
            Console.WriteLine($"FAIL {label}: expected {expected}, got {actual}");
            return false;
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool CheckL(long actual, long expected, string label)
    {
        if (actual != expected)
        {
            Console.WriteLine($"FAIL {label}: expected {expected}, got {actual}");
            return false;
        }
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool CheckUL(ulong actual, ulong expected, string label)
    {
        if (actual != expected)
        {
            Console.WriteLine($"FAIL {label}: expected {expected}, got {actual}");
            return false;
        }
        return true;
    }
}
