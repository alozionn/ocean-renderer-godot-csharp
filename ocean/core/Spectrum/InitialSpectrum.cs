using System;
using System.Numerics;

namespace Ocean.Core.Spectrum;

/// <summary>CPU port of spectrum.glsl: the initial spectrum h0(k) and its Hermitian partner, per texel.</summary>
public static class InitialSpectrum
{
    public static int WrappedIndex(int id, int n) => id - (id >= n / 2 ? n : 0);

    public static uint Hash(uint x)
    {
        unchecked
        {
            uint state = x * 747796405u + 2891336453u;
            uint word = ((state >> (int)((state >> 28) + 4u)) ^ state) * 277803737u;
            return (word >> 22) ^ word;
        }
    }

    public static uint Seed(int x, int y, uint seed) => unchecked(Hash((uint)x + Hash((uint)y + Hash(seed))));

    public static uint CascadeSeed(int cascade) => (uint)(cascade * 7919 + 17);

    /// <summary>Standard normal complex sample (Box-Muller), identical to the shader's.</summary>
    public static Vector2 Gaussian(int x, int y, uint seed)
    {
        uint h1 = Hash(Seed(x, y, seed));
        uint h2 = Hash(h1);
        float u1 = ((h1 >> 8) + 1f) / 16777217f;
        float u2 = (h2 >> 8) / 16777216f;
        float r = MathF.Sqrt(-2f * MathF.Log(u1));
        return new Vector2(r * MathF.Cos(2f * MathF.PI * u2), r * MathF.Sin(2f * MathF.PI * u2));
    }

    /// <summary>Amplitude sqrt(2 S(kx, kz)) dk for one wave vector (0 at k = 0).</summary>
    public static float Amplitude(float kx, float kz, float patchSize, float wind, float windDirectionRad, float fetch, float suppression)
    {
        float k = MathF.Sqrt(kx * kx + kz * kz);
        if (k < 1e-6f) return 0;
        float omega = MathF.Sqrt((float)Jonswap.G * k);
        float wp = (float)Jonswap.PeakAngularFrequency(wind, fetch);
        float theta = MathF.Atan2(kz, kx) - windDirectionRad;
        theta -= 2f * MathF.PI * MathF.Floor((theta + MathF.PI) / (2f * MathF.PI));
        float s = (float)(Jonswap.Spectrum(omega, wind, fetch) * Jonswap.DonelanBanner(theta, omega / wp));
        s *= (float)Jonswap.G / (2f * omega) / k * MathF.Exp(-k * k * suppression * suppression);
        float dk = 2f * MathF.PI / patchSize;
        return MathF.Sqrt(2f * s) * dk;
    }

    /// <summary>(h0(k).xy, conj(h0(-k)).xy) for texel (x, y) of an N x N spectrum; Nyquist row/column are zero.</summary>
    public static Vector4 Texel(int x, int y, int n, float patchSize, float wind, float windDirectionRad, float fetch, float suppression, uint seed)
    {
        if (x == n / 2 || y == n / 2) return Vector4.Zero;
        Vector2 H0(int ix, int iy)
        {
            float kx = 2f * MathF.PI * WrappedIndex(ix, n) / patchSize, kz = 2f * MathF.PI * WrappedIndex(iy, n) / patchSize;
            return Gaussian(ix, iy, seed) * Amplitude(kx, kz, patchSize, wind, windDirectionRad, fetch, suppression) / MathF.Sqrt(2f);
        }
        Vector2 a = H0(x, y), b = H0((n - x) % n, (n - y) % n);
        return new Vector4(a.X, a.Y, b.X, -b.Y);
    }
}
