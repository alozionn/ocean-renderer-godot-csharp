using System;

namespace Ocean.Core.Spectrum;

/// <summary>JONSWAP spectrum with Donelan-Banner directional spreading (double precision CPU reference).</summary>
public static class Jonswap
{
    public const double G = 9.81;
    const double Gamma = 3.3;

    public static double Alpha(double wind, double fetch) => 0.076 * Math.Pow(wind * wind / (fetch * G), 0.22);

    public static double PeakAngularFrequency(double wind, double fetch) => 22.0 * Math.Pow(G * G / (wind * fetch), 1.0 / 3.0);

    public static double Spectrum(double omega, double wind, double fetch)
    {
        if (omega <= 0) return 0;
        double wp = PeakAngularFrequency(wind, fetch);
        double sigma = omega <= wp ? 0.07 : 0.09;
        double r = Math.Exp(-(omega - wp) * (omega - wp) / (2 * sigma * sigma * wp * wp));
        return Alpha(wind, fetch) * G * G / Math.Pow(omega, 5) * Math.Exp(-1.25 * Math.Pow(wp / omega, 4)) * Math.Pow(Gamma, r);
    }

    public static double SpreadingBeta(double omegaOverPeak)
    {
        if (omegaOverPeak < 0.95) return 2.61 * Math.Pow(omegaOverPeak, 1.3);
        if (omegaOverPeak < 1.6) return 2.28 * Math.Pow(omegaOverPeak, -1.3);
        return Math.Pow(10, -0.4 + 0.8393 * Math.Exp(-0.567 * Math.Log(omegaOverPeak * omegaOverPeak)));
    }

    /// <summary>Donelan-Banner directional spreading; theta is the angle from the wind, wrapped to [-pi, pi].</summary>
    public static double DonelanBanner(double theta, double omegaOverPeak)
    {
        double beta = SpreadingBeta(omegaOverPeak);
        double sech = 1.0 / Math.Cosh(beta * theta);
        return beta / (2 * Math.Tanh(beta * Math.PI)) * sech * sech;
    }
}
