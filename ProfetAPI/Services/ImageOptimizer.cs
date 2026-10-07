using SkiaSharp;

namespace ProfetAPI.Services;

/// <summary>
/// Reduce el peso de logotipos y fotos antes de guardarlos: los achica a un tamaño razonable y los recomprime.
/// Menos datos que descargar en el celular. SVG e ICO se guardan tal cual (ya son livianos o no se pueden reescalar).
/// Si recomprimir no mejora el archivo, se conserva el original.
/// </summary>
public static class ImageOptimizer
{
    public record Result(byte[] Bytes, string Extension, string ContentType, bool Optimized);

    public static Result? Optimize(Stream input, string contentType, int maxWidth, int maxHeight, bool squareCrop = false, bool toWebp = false, int quality = 85)
    {
        using var ms = new MemoryStream();
        input.CopyTo(ms);
        var original = ms.ToArray();
        var ct = (contentType ?? "").ToLowerInvariant();

        if (ct.Contains("svg")) return new Result(original, ".svg", "image/svg+xml", false);
        if (ct.Contains("icon")) return new Result(original, ".ico", "image/x-icon", false);

        SKBitmap? bitmap;
        try { bitmap = SKBitmap.Decode(original); }
        catch (ArgumentNullException) { return null; }   // Skia no reconoce el formato: no es una imagen
        if (bitmap == null) return null;
        using var _ = bitmap;

        // Recorte cuadrado centrado (fotos de perfil)
        SKBitmap working = bitmap;
        SKBitmap? cropped = null;
        if (squareCrop && bitmap.Width != bitmap.Height)
        {
            var side = Math.Min(bitmap.Width, bitmap.Height);
            var rect = new SKRectI((bitmap.Width - side) / 2, (bitmap.Height - side) / 2, (bitmap.Width - side) / 2 + side, (bitmap.Height - side) / 2 + side);
            cropped = new SKBitmap(side, side);
            bitmap.ExtractSubset(cropped, rect);
            working = cropped;
        }

        // Ajuste al tamaño máximo (sin agrandar nunca)
        var scale = Math.Min(1.0, Math.Min((double)maxWidth / working.Width, (double)maxHeight / working.Height));
        SKBitmap? resized = null;
        if (scale < 1.0)
        {
            var info = new SKImageInfo(Math.Max(1, (int)Math.Round(working.Width * scale)), Math.Max(1, (int)Math.Round(working.Height * scale)), working.ColorType, working.AlphaType);
            resized = working.Resize(info, SKFilterQuality.High);
            if (resized != null) working = resized;
        }

        try
        {
            SKEncodedImageFormat format; string ext; string mime;
            if (toWebp || ct.Contains("webp")) { format = SKEncodedImageFormat.Webp; ext = ".webp"; mime = "image/webp"; }
            else if (ct.Contains("jpeg") || ct.Contains("jpg")) { format = SKEncodedImageFormat.Jpeg; ext = ".jpg"; mime = "image/jpeg"; }
            else { format = SKEncodedImageFormat.Png; ext = ".png"; mime = "image/png"; }

            using var image = SKImage.FromBitmap(working);
            using var data = image.Encode(format, format == SKEncodedImageFormat.Png ? 100 : quality);
            var bytes = data.ToArray();

            // Si no se achicó la imagen y el resultado pesa más que el original (mismo formato), se deja el original.
            var sameFormat = (!toWebp && (ext == ".png" && ct.Contains("png")) || (ext == ".jpg" && (ct.Contains("jpeg") || ct.Contains("jpg"))) || (ext == ".webp" && ct.Contains("webp")));
            if (scale >= 1.0 && !squareCrop && sameFormat && bytes.Length >= original.Length)
                return new Result(original, ext, mime, false);

            return new Result(bytes, ext, mime, true);
        }
        finally
        {
            resized?.Dispose();
            cropped?.Dispose();
        }
    }
}
