using System;

namespace NINA.Plugin.QualitySessionMeter.Core;

/// <summary>Linear-pixel fit for resolved cores with too few bright pixels for a log fit.</summary>
internal static class CompactStarFit {
    private const int Radius = 3;
    private const int Parameters = 6;

    public static (bool Valid, double Ratio, double Fwhm) Fit(float[] pixels, int width, int x, int y,
        double background, double peak, double noise, double cx, double cy, double xx, double xy, double yy) {
        if (!(peak >= 20 * noise) || !double.IsFinite(peak)) return (false, 0, 0);
        // Fit the pixel integral, rather than requiring ten individually bright pixels.
        // Keep signed background-subtracted observations, including the faint wings.
        xx = Math.Max(.25, xx); yy = Math.Max(.25, yy);
        double det = xx * yy - xy * xy;
        if (det <= .01) { xy = 0; det = xx * yy; }
        double[] p = { Math.Log(peak), cx, cy, yy / det, -xy / det, xx / det };
        var data = new double[(Radius * 2 + 1) * (Radius * 2 + 1)];
        int index = 0;
        for (int dy = -Radius; dy <= Radius; dy++) for (int dx = -Radius; dx <= Radius; dx++)
            data[index++] = (pixels[(y + dy) * width + x + dx] - background) / peak;
        double error = Evaluate(p, data, peak, null, null), damping = .001;
        for (int iteration = 0; iteration < 18; iteration++) {
            var normal = new double[Parameters, Parameters]; var rhs = new double[Parameters];
            Evaluate(p, data, peak, normal, rhs);
            for (int i = 0; i < Parameters; i++) normal[i, i] += damping * Math.Max(1e-6, normal[i, i]);
            if (!Solve(normal, rhs)) return (false, 0, 0);
            var trial = new double[Parameters];
            for (int i = 0; i < Parameters; i++) trial[i] = p[i] + rhs[i];
            bool bounded = Math.Abs(trial[1]) <= 1.5 && Math.Abs(trial[2]) <= 1.5
                && trial[0] >= Math.Log(peak * .5) && trial[0] <= Math.Log(peak * 8)
                && trial[3] > 0 && trial[5] > 0 && trial[3] * trial[5] - trial[4] * trial[4] > .001;
            double next = bounded ? Evaluate(trial, data, peak, null, null) : double.PositiveInfinity;
            if (next < error) {
                double improvement = error - next; p = trial; error = next; damping = Math.Max(1e-7, damping / 3);
                if (improvement < 1e-9) break;
            } else damping *= 10;
        }
        double delta = Math.Sqrt((p[3] - p[5]) * (p[3] - p[5]) + 4 * p[4] * p[4]);
        double small = (p[3] + p[5] - delta) / 2, large = (p[3] + p[5] + delta) / 2;
        // Single-pixel artifacts cannot qualify as a resolved stellar profile.
        if (!(small > 0 && large > 0) || 1 / large < .2 || 1 / small > 16
            || Math.Sqrt(error / data.Length) > Math.Max(.025, 2 * noise / peak)) return (false, 0, 0);
        return (true, Math.Sqrt(large / small), 2.35482 / Math.Pow(small * large, .25));
    }

    private static double Evaluate(double[] p, double[] data, double peak, double[,] normal, double[] rhs) {
        double error = 0, amplitude = Math.Exp(p[0]) / peak; int index = 0;
        var derivative = new double[Parameters];
        for (int y = -Radius; y <= Radius; y++) for (int x = -Radius; x <= Radius; x++) {
            Array.Clear(derivative); double model = 0;
            // Midpoint quadrature over each sample's footprint. The same model handles
            // monochrome pixels and the aligned Bayer-cell averages used by capture.
            for (int sy = -1; sy <= 1; sy++) for (int sx = -1; sx <= 1; sx++) {
                double dx = x + sx / 3.0 - p[1], dy = y + sy / 3.0 - p[2];
                double value = amplitude * Math.Exp(-.5 * (p[3] * dx * dx + 2 * p[4] * dx * dy + p[5] * dy * dy)) / 9;
                model += value;
                if (normal == null) continue;
                derivative[0] += value; derivative[1] += value * (p[3] * dx + p[4] * dy);
                derivative[2] += value * (p[4] * dx + p[5] * dy);
                derivative[3] -= .5 * value * dx * dx; derivative[4] -= value * dx * dy;
                derivative[5] -= .5 * value * dy * dy;
            }
            double residual = data[index++] - model; error += residual * residual;
            if (normal == null) continue;
            for (int i = 0; i < Parameters; i++) {
                rhs[i] += derivative[i] * residual;
                for (int j = 0; j < Parameters; j++) normal[i, j] += derivative[i] * derivative[j];
            }
        }
        return error;
    }

    private static bool Solve(double[,] matrix, double[] rhs) {
        for (int k = 0; k < Parameters; k++) {
            int pivot = k;
            for (int i = k + 1; i < Parameters; i++) if (Math.Abs(matrix[i, k]) > Math.Abs(matrix[pivot, k])) pivot = i;
            if (Math.Abs(matrix[pivot, k]) < 1e-12) return false;
            for (int j = k; j < Parameters; j++) (matrix[k, j], matrix[pivot, j]) = (matrix[pivot, j], matrix[k, j]);
            (rhs[k], rhs[pivot]) = (rhs[pivot], rhs[k]);
            double div = matrix[k, k];
            for (int j = k; j < Parameters; j++) matrix[k, j] /= div;
            rhs[k] /= div;
            for (int i = 0; i < Parameters; i++) if (i != k) {
                double m = matrix[i, k];
                for (int j = k; j < Parameters; j++) matrix[i, j] -= m * matrix[k, j];
                rhs[i] -= m * rhs[k];
            }
        }
        return true;
    }
}
