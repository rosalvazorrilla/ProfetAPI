using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ProfetAPI.Services;

/// <summary>
/// Cada endpoint pide en Swagger el candado correcto: los de /api/external piden la API Key (X-Api-Key);
/// el resto pide el JWT (Bearer). Así el botón Authorize de cada documento solo ofrece lo que aplica.
/// </summary>
public class SwaggerSecurityFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var isExternal = context.MethodInfo.DeclaringType == typeof(Controllers.ExternalApiController);
        var scheme = isExternal ? "ApiKey" : "Bearer";
        operation.Security = new List<OpenApiSecurityRequirement>
        {
            new()
            {
                {
                    new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = scheme } },
                    new List<string>()
                }
            }
        };
    }
}
