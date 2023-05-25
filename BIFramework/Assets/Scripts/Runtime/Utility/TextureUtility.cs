using System.Threading;
using UnityEngine;
using System.IO;
using System.Collections;
using System;
using XLua;
using Object = UnityEngine.Object;

[LuaCallCSharp]
public class TextureUtility {
    public static Texture2D ScaleTextureBilinear(Texture2D originalTexture, float scaleFactor) {
        Texture2D newTexture = new Texture2D(Mathf.CeilToInt(originalTexture.width * scaleFactor),
            Mathf.CeilToInt(originalTexture.height * scaleFactor));
        float scale = 1.0f / scaleFactor;
        int maxX = originalTexture.width - 1;
        int maxY = originalTexture.height - 1;
        for (int y = 0; y < newTexture.height; y++) {
            for (int x = 0; x < newTexture.width; x++) {
                // Bilinear Interpolation
                float targetX = x * scale;
                float targetY = y * scale;
                int x1 = Mathf.Min(maxX, Mathf.FloorToInt(targetX));
                int y1 = Mathf.Min(maxY, Mathf.FloorToInt(targetY));
                int x2 = Mathf.Min(maxX, x1 + 1);
                int y2 = Mathf.Min(maxY, y1 + 1);

                float u = targetX - x1;
                float v = targetY - y1;
                float w1 = (1 - u) * (1 - v);
                float w2 = u * (1 - v);
                float w3 = (1 - u) * v;
                float w4 = u * v;
                Color color1 = originalTexture.GetPixel(x1, y1);
                Color color2 = originalTexture.GetPixel(x2, y1);
                Color color3 = originalTexture.GetPixel(x1, y2);
                Color color4 = originalTexture.GetPixel(x2, y2);
                Color color = new Color(Mathf.Clamp01(color1.r * w1 + color2.r * w2 + color3.r * w3 + color4.r * w4),
                    Mathf.Clamp01(color1.g * w1 + color2.g * w2 + color3.g * w3 + color4.g * w4),
                    Mathf.Clamp01(color1.b * w1 + color2.b * w2 + color3.b * w3 + color4.b * w4),
                    Mathf.Clamp01(color1.a * w1 + color2.a * w2 + color3.a * w3 + color4.a * w4));
                newTexture.SetPixel(x, y, color);
            }
        }

        return newTexture;
    }

    public static Texture2D ClipWhite(Texture2D orgin) {
        var pic = orgin;
        var mipCount = orgin.mipmapCount;

        for (int i = 0; i < mipCount; i++) {
            var cols = pic.GetPixels(i);
            for (int j = 0; j < cols.Length; j++) {
                if (cols[j] == Color.white) cols[j] = new Color(0, 0, 0, 0);
            }

            pic.SetPixels(cols, i);
        }

        pic.Apply();
        return pic;
    }

    public static Texture2D ClipAlphaEdge(Texture2D orgin) {
        try {
            var left = 0;
            var top = 0;
            var right = orgin.width;
            var botton = orgin.height;

            // 左侧
            for (var i = 0; i < orgin.width; i++) {
                var find = false;
                for (var j = 0; j < orgin.height; j++) {
                    var color = orgin.GetPixel(i, j);
                    if (Math.Abs(color.a) > 0) {
                        find = true;
                        break;
                    }
                }

                if (find) {
                    left = i;
                    break;
                }
            }

            // 右侧
            for (var i = orgin.width - 1; i >= 0; i--) {
                var find = false;
                for (var j = 0; j < orgin.height; j++) {
                    var color = orgin.GetPixel(i, j);
                    if (Math.Abs(color.a) > 0) {
                        find = true;
                        break;
                    }
                }

                if (find) {
                    right = i;
                    break;
                }
            }

            // 上侧
            for (var j = 0; j < orgin.height; j++) {
                var find = false;
                for (var i = 0; i < orgin.width; i++) {
                    var color = orgin.GetPixel(i, j);
                    if (Math.Abs(color.a) > 0) {
                        find = true;
                        break;
                    }
                }

                if (find) {
                    top = j;
                    break;
                }
            }

            // 下侧
            for (var j = orgin.height - 1; j >= 0; j--) {
                var find = false;
                for (var i = 0; i < orgin.width; i++) {
                    var color = orgin.GetPixel(i, j);
                    if (Math.Abs(color.a) > 0) {
                        find = true;
                        break;
                    }
                }

                if (find) {
                    botton = j;
                    break;
                }
            }

            // 创建新纹理
            var width = right - left;
            var height = botton - top;

            var result = new Texture2D(width, height, TextureFormat.ARGB32, false);
            //result.alphaIsTransparency = true;

            // 复制有效颜色区块
            var colors = orgin.GetPixels(left, top, width, height);
            result.SetPixels(0, 0, width, height, colors);

            result.Apply();
            return result;
        }
        catch (Exception e) {
            throw e;
        }
    }

    public static Texture2D Copy(Texture2D tex) {
        return new Texture2D(tex.width, tex.height, tex.format, false);
    }

    /// <summary>
    /// Applies sepia effect to the texture.
    /// </summary>
    /// <param name="tex"> Texture to process.</param>
    public static Texture2D SetSepia(Texture2D tex) {
        Texture2D t = Copy(tex);
        Color[] colors = tex.GetPixels();

        for (int i = 0; i < colors.Length; i++) {
            float alpha = colors[i].a;
            float grayScale = ((colors[i].r * .299f) + (colors[i].g * .587f) + (colors[i].b * .114f));
            Color c = new Color(grayScale, grayScale, grayScale);
            colors[i] = new Color(c.r * 1, c.g * 0.95f, c.b * 0.82f, alpha);
        }

        t.SetPixels(colors);
        t.Apply();
        return t;
    }

    /// <summary>
    /// Applies grayscale effect to the texture and changes colors to grayscale.
    /// </summary>
    /// <param name="tex"> Texture to process.</param>
    public static Texture2D SetGrayscale(Texture2D tex) {
        Texture2D t = Copy(tex);

        Color[] colors = tex.GetPixels();
        for (int i = 0; i < colors.Length; i++) {
            float val = (colors[i].r + colors[i].g + colors[i].b) / 3;
            colors[i] = new Color(val, val, val);
        }

        t.SetPixels(colors);
        t.Apply();
        return t;
    }

    /// <summary>
    /// Pixelates the texture.
    /// </summary>
    /// <param name="tex"> Texture to process.</param>
    /// <param name="size"> Size of the pixel.</param>
    public static Texture2D SetPixelate(Texture2D tex, int size) {
        Texture2D t = Copy(tex);
        Rect rectangle = new Rect(0, 0, tex.width, tex.height);
        for (int xx = (int) rectangle.x; xx < rectangle.x + rectangle.width && xx < tex.width; xx += size) {
            for (int yy = (int) rectangle.y; yy < rectangle.y + rectangle.height && yy < tex.height; yy += size) {
                int offsetX = size / 2;
                int offsetY = size / 2;
                while (xx + offsetX >= tex.width) offsetX--;
                while (yy + offsetY >= tex.height) offsetY--;
                Color pixel = tex.GetPixel(xx + offsetX, yy + offsetY);
                for (int x = xx; x < xx + size && x < tex.width; x++)
                for (int y = yy; y < yy + size && y < tex.height; y++)
                    t.SetPixel(x, y, pixel);
            }
        }

        t.Apply();
        return t;
    }

    /// <summary>
    /// Inverts colors of the texture.
    /// </summary>
    /// <param name="tex"> Texture to process.</param>
    public static Texture2D SetNegative(Texture2D tex) {
        Texture2D t = Copy(tex);
        Color[] colors = tex.GetPixels();
        Color pixel;

        for (int i = 0; i < colors.Length; i++) {
            pixel = colors[i];
            colors[i] = new Color(1 - pixel.r, 1 - pixel.g, 1 - pixel.b);
        }

        t.SetPixels(colors);
        t.Apply();
        return t;
    }

    /// <summary>
    /// Sets the foggy effect.雾化效果
    /// </summary>
    /// <returns>texture processed.</returns>
    /// <param name="tex">texture to process.</param>
    public static Texture2D SetFoggy(Texture2D tex) {
        Texture2D t = Copy(tex);
        Color pixel;

        for (int x = 1; x < tex.width - 1; x++)
        for (int y = 1; y < tex.height - 1; y++) {
            int k = UnityEngine.Random.Range(0, 123456);
            //像素块大小
            int dx = x + k % 19;
            int dy = y + k % 19;
            if (dx >= tex.width)
                dx = tex.width - 1;
            if (dy >= tex.height)
                dy = tex.height - 1;
            pixel = tex.GetPixel(dx, dy);
            t.SetPixel(x, y, pixel);
        }

        t.Apply();

        return t;
    }

    /// <summary>
    /// Sets the soft effect.柔化效果
    /// </summary>
    /// <returns>texture processed.</returns>
    /// <param name="tex">texture to process.</param>
    public static Texture2D SetSoft(Texture2D tex) {
        Texture2D t = Copy(tex);
        int[] Gauss = {1, 2, 1, 2, 4, 2, 1, 2, 1};
        for (int x = 1; x < tex.width - 1; x++)
        for (int y = 1; y < tex.height - 1; y++) {
            float r = 0, g = 0, b = 0;
            int Index = 0;
            for (int col = -1; col <= 1; col++)
            for (int row = -1; row <= 1; row++) {
                Color pixel = tex.GetPixel(x + row, y + col);
                r += pixel.r * Gauss[Index];
                g += pixel.g * Gauss[Index];
                b += pixel.b * Gauss[Index];
                Index++;
            }

            r /= 16;
            g /= 16;
            b /= 16;
            //处理颜色值溢出
            r = r > 1 ? 1 : r;
            r = r < 0 ? 0 : r;
            g = g > 1 ? 1 : g;
            g = g < 0 ? 0 : g;
            b = b > 1 ? 1 : b;
            b = b < 0 ? 0 : b;
            t.SetPixel(x - 1, y - 1, new Color(r, g, b));
        }

        t.Apply();

        return t;
    }

    /// <summary>
    /// Sets the sharp.锐化效果
    /// </summary>
    /// <returns>The sharp.</returns>
    /// <param name="tex">Tex.</param>
    public static Texture2D SetSharp(Texture2D tex) {
        Texture2D t = Copy(tex);
        Color pixel;
        //拉普拉斯模板
        int[] Laplacian = {-1, -1, -1, -1, 9, -1, -1, -1, -1};
        for (int x = 1; x < tex.width - 1; x++)
        for (int y = 1; y < tex.height - 1; y++) {
            float r = 0, g = 0, b = 0;
            int index = 0;
            for (int col = -1; col <= 1; col++)
            for (int row = -1; row <= 1; row++) {
                pixel = tex.GetPixel(x + row, y + col);
                r += pixel.r * Laplacian[index];
                g += pixel.g * Laplacian[index];
                b += pixel.b * Laplacian[index];
                index++;
            }

            //处理颜色值溢出
            r = r > 1 ? 1 : r;
            r = r < 0 ? 0 : r;
            g = g > 1 ? 1 : g;
            g = g < 0 ? 0 : g;
            b = b > 1 ? 1 : b;
            b = b < 0 ? 0 : b;
            t.SetPixel(x - 1, y - 1, new Color(r, g, b));
        }

        t.Apply();
        return t;
    }

    /// <summary>
    /// Sets the relief.浮雕效果
    /// </summary>
    /// <returns>The relief.</returns>
    /// <param name="tex">Tex.</param>
    public static Texture2D SetRelief(Texture2D tex) {
        Texture2D t = Copy(tex);

        for (int x = 0; x < tex.width - 1; x++) {
            for (int y = 0; y < tex.height - 1; y++) {
                float r = 0, g = 0, b = 0;
                Color pixel1 = tex.GetPixel(x, y);
                Color pixel2 = tex.GetPixel(x + 1, y + 1);
                r = Mathf.Abs(pixel1.r - pixel2.r + 0.5f);
                g = Mathf.Abs(pixel1.g - pixel2.g + 0.5f);
                b = Mathf.Abs(pixel1.b - pixel2.b + 0.5f);
                if (r > 1)
                    r = 1;
                if (r < 0)
                    r = 0;
                if (g > 1)
                    g = 1;
                if (g < 0)
                    g = 0;
                if (b > 1)
                    b = 1;
                if (b < 0)
                    b = 0;
                t.SetPixel(x, y, new Color(r, g, b));
            }
        }

        t.Apply();
        return t;
    }
}

public class TransparencyCapture {
    public static Texture2D Capture(Rect pRect, Camera mainCamera) {
        Camera lCamera = mainCamera;
        Texture2D lOut;
        var lPreClearFlags = lCamera.clearFlags;
        var lPreBackgroundColor = lCamera.backgroundColor;
        {
            lCamera.clearFlags = CameraClearFlags.Color;
            //make two captures with black and white background
            lCamera.backgroundColor = Color.black;
            lCamera.Render();
            var lBlackBackgroundCapture = CaptureView(pRect);
            lCamera.backgroundColor = Color.white;
            lCamera.Render();
            var lWhiteBackgroundCapture = CaptureView(pRect);
            for (int x = 0; x < lWhiteBackgroundCapture.width; ++x) {
                for (int y = 0; y < lWhiteBackgroundCapture.height; ++y) {
                    Color lColorWhenBlack = lBlackBackgroundCapture.GetPixel(x, y);
                    Color lColorWhenWhite = lWhiteBackgroundCapture.GetPixel(x, y);
                    if (lColorWhenBlack != Color.clear) {
                        //set real color
                        lWhiteBackgroundCapture.SetPixel(x, y, GetColor(lColorWhenBlack, lColorWhenWhite));
                    }
                }
            }

            lWhiteBackgroundCapture.Apply();
            lOut = lWhiteBackgroundCapture;
            Object.DestroyImmediate(lBlackBackgroundCapture);
        }
        lCamera.backgroundColor = lPreBackgroundColor;
        lCamera.clearFlags = lPreClearFlags;
        return lOut;
    }

    //pColorWhenBlack!=Color.clear
    static Color GetColor(Color pColorWhenBlack, Color pColorWhenWhite) {
        float lAlpha = GetAlpha(pColorWhenBlack.r, pColorWhenWhite.r);
        lAlpha = lAlpha < 0.01f ? 0 : lAlpha;
        return new Color(pColorWhenBlack.r, pColorWhenBlack.g, pColorWhenBlack.b, lAlpha);
    }


    //           Color*Alpha      Color   Color+(1-Color)*(1-Alpha)=1+Color*Alpha-Alpha
    //0----------ColorWhenZero----Color---ColorWhenOne------------1
    static float GetAlpha(float pColorWhenZero, float pColorWhenOne) {
        //pColorWhenOne-pColorWhenZero=1-Alpha
        return 1 + pColorWhenZero - pColorWhenOne;
    }

    static Texture2D CaptureView(Rect pRect) {
        Texture2D lOut = new Texture2D((int) pRect.width, (int) pRect.height, TextureFormat.ARGB32, false);
        lOut.ReadPixels(pRect, 0, 0, false);
        return lOut;
    }
}