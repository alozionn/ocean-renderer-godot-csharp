using System;
using System.Numerics;

namespace Ocean.Core.Fft;

/// <summary>CPU Stockham radix-2 inverse FFT with exactly fft.glsl's indexing (no normalisation).</summary>
public static class ReferenceFft
{
    public static void Inverse(Complex[] line)
    {
        int n = line.Length, half = n / 2;
        var next = new Complex[n];
        for (int span = 1; span < n; span <<= 1)
        {
            for (int j = 0; j < half; j++)
            {
                Complex u = line[j], v = line[j + half];
                int k = j & (span - 1);
                Complex w = Complex.FromPolarCoordinates(1, Math.PI * k / span);
                int dst = ((j - k) << 1) + k;
                next[dst] = u + v * w;
                next[dst + span] = u - v * w;
            }
            Array.Copy(next, line, n);
        }
    }

    public static Complex[] NaiveInverseDft(Complex[] x)
    {
        int n = x.Length;
        var r = new Complex[n];
        for (int t = 0; t < n; t++)
            for (int k = 0; k < n; k++)
                r[t] += x[k] * Complex.FromPolarCoordinates(1, 2 * Math.PI * k * t / n);
        return r;
    }
}
