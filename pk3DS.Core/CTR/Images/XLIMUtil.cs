namespace pk3DS.Core.CTR.Images;

public static class XLIMUtil
{
    internal static int GreatestCommonMultiple(int n, int m)
    {
        return (n + m - 1) / m * m;
    }

    internal static int NextLargestPow2(int x)
    {
        x--; // comment out to always take the next biggest power of two, even if x is already a power of two
        x |= x >> 1;
        x |= x >> 2;
        x |= x >> 4;
        x |= x >> 8;
        x |= x >> 16;
        return x + 1;
    }
}