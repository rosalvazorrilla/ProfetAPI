using ProfetAPI.Services;
using SkiaSharp;
using Xunit;

namespace ProfetAPI.Tests;

/// <summary>La compresión de logos y fotos: achica, conserva proporciones y nunca empeora el archivo.</summary>
public class ImageOptimizerTests
{
    private static MemoryStream Png(int w, int h, bool transparent = false)
    {
        using var bmp = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(transparent ? SKColors.Transparent : SKColors.White);
            using var paint = new SKPaint { Color = SKColors.Teal, IsAntialias = true };
            for (int i = 0; i < 40; i++) canvas.DrawCircle((i * 97) % w, (i * 53) % h, 20 + i % 30, paint); // contenido con detalle
        }
        using var img = SKImage.FromBitmap(bmp);
        return new MemoryStream(img.Encode(SKEncodedImageFormat.Png, 100).ToArray());
    }

    private static (int w, int h) Size(byte[] bytes)
    {
        using var b = SKBitmap.Decode(bytes);
        return (b.Width, b.Height);
    }

    [Fact]
    public void Un_logo_grande_se_achica_a_600x200_manteniendo_proporcion_y_pesa_menos()
    {
        using var input = Png(2400, 800);
        var originalSize = input.Length;
        var r = ImageOptimizer.Optimize(input, "image/png", 600, 200)!;
        Assert.True(r.Optimized);
        Assert.Equal((600, 200), Size(r.Bytes));
        Assert.True(r.Bytes.Length < originalSize);
        Assert.Equal(".png", r.Extension);
    }

    [Fact]
    public void Nunca_agranda_una_imagen_pequena()
    {
        using var input = Png(120, 60);
        var r = ImageOptimizer.Optimize(input, "image/png", 600, 200)!;
        Assert.Equal((120, 60), Size(r.Bytes));
    }

    [Fact]
    public void El_logo_conserva_la_transparencia()
    {
        using var input = Png(1200, 400, transparent: true);
        var r = ImageOptimizer.Optimize(input, "image/png", 600, 200)!;
        using var decoded = SKBitmap.Decode(r.Bytes);
        bool anyTransparent = false;
        for (int x = 0; x < decoded.Width && !anyTransparent; x += 5)
            for (int y = 0; y < decoded.Height; y += 5)
                if (decoded.GetPixel(x, y).Alpha == 0) { anyTransparent = true; break; }
        Assert.True(anyTransparent); // el fondo transparente se conserva
    }

    [Fact]
    public void La_foto_de_perfil_sale_cuadrada_256_en_webp_y_pesa_mucho_menos()
    {
        using var input = Png(3000, 2000);
        var originalSize = input.Length;
        var r = ImageOptimizer.Optimize(input, "image/png", 256, 256, squareCrop: true, toWebp: true, quality: 82)!;
        Assert.Equal((256, 256), Size(r.Bytes));
        Assert.Equal(".webp", r.Extension);
        Assert.True(r.Bytes.Length < originalSize / 5);
    }

    [Fact]
    public void SVG_e_ICO_pasan_sin_tocarse()
    {
        var svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'/>");
        var r = ImageOptimizer.Optimize(new MemoryStream(svg), "image/svg+xml", 600, 200)!;
        Assert.False(r.Optimized);
        Assert.Equal(svg, r.Bytes);
    }

    [Fact]
    public void Un_archivo_que_no_es_imagen_se_rechaza()
    {
        var r = ImageOptimizer.Optimize(new MemoryStream(new byte[] { 1, 2, 3, 4 }), "image/png", 600, 200);
        Assert.Null(r);
    }
}
