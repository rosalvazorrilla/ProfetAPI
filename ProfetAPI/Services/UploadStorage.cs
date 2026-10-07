namespace ProfetAPI.Services;

/// <summary>
/// Carpeta donde se guardan los archivos que suben los usuarios (logos, favicons).
/// En Azure App Service la carpeta de la aplicación (wwwroot) se REEMPLAZA en cada deploy, así que
/// lo subido ahí se perdía con cada publicación; la carpeta HOME/data sí persiste entre deploys.
/// En local sigue siendo wwwroot/uploads.
/// </summary>
public static class UploadStorage
{
    public static string Root(IWebHostEnvironment env)
    {
        var onAzure = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));
        var home = Environment.GetEnvironmentVariable("HOME");
        if (onAzure && !string.IsNullOrEmpty(home))
            return Path.Combine(home, "data", "uploads");
        return Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"), "uploads");
    }
}
