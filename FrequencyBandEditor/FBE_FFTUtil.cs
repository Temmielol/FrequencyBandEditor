using UnityEngine;

public static class FBE_FFTUtil
{
    public static void DoFFT(float[] real, float[] imag)
    {
        int n = real.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                float t = real[i]; real[i] = real[j]; real[j] = t;
                t = imag[i]; imag[i] = imag[j]; imag[j] = t;
            }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            float a = -2f * Mathf.PI / len;
            float wR = Mathf.Cos(a), wI = Mathf.Sin(a);
            for (int i = 0; i < n; i += len)
            {
                float cR = 1f, cI = 0f;
                for (int j = 0; j < len / 2; j++)
                {
                    float uR = real[i + j], uI = imag[i + j];
                    float vR = real[i + j + len / 2] * cR - imag[i + j + len / 2] * cI;
                    float vI = real[i + j + len / 2] * cI + imag[i + j + len / 2] * cR;
                    real[i + j] = uR + vR; imag[i + j] = uI + vI;
                    real[i + j + len / 2] = uR - vR; imag[i + j + len / 2] = uI - vI;
                    float nc = cR * wR - cI * wI; cI = cR * wI + cI * wR; cR = nc;
                }
            }
        }
    }

    public static Color BlendAdd(Color dst, Color src)
    {
        return new Color(
            Mathf.Min(1f, dst.r + src.r * src.a),
            Mathf.Min(1f, dst.g + src.g * src.a),
            Mathf.Min(1f, dst.b + src.b * src.a),
            Mathf.Min(1f, dst.a + src.a * 0.5f));
    }

    public static void DrawTexLine(Color[] pixels, int size, float x0, float y0, float x1, float y1, Color col, Color glow)
    {
        float dx = x1 - x0, dy = y1 - y0;
        int steps = Mathf.Max(1, (int)Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)));
        float invSteps = 1f / steps;
        for (int i = 0; i <= steps; i++)
        {
            float t = i * invSteps;
            int px = (int)(x0 + dx * t);
            int py = (int)(y0 + dy * t);
            if (px >= 0 && px < size && py >= 0 && py < size)
                pixels[py * size + px] = BlendAdd(pixels[py * size + px], col);
            for (int gy = -1; gy <= 1; gy++)
                for (int gx = -1; gx <= 1; gx++)
                {
                    if (gx == 0 && gy == 0) continue;
                    int nx = px + gx, ny = py + gy;
                    if (nx >= 0 && nx < size && ny >= 0 && ny < size)
                        pixels[ny * size + nx] = BlendAdd(pixels[ny * size + nx], glow);
                }
        }
    }
}
