using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Models; // Necesario para OpenApiInfo
using ProfetAPI.Data;
using ProfetAPI.Hubs;
using ProfetAPI.Models;
using System.Reflection; // <--- AGREGA ESTO (Necesario para leer el XML)
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// --- 1. Conexi�n a la Base de Datos ---
var connectionString = configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// --- 2. Configuraci�n de Identity ---
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// --- 3. Configuraci�n de Autenticaci�n JWT ---
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.SaveToken = true;
    options.RequireHttpsMetadata = false;
    options.TokenValidationParameters = new TokenValidationParameters()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidAudience = builder.Configuration["JWT:ValidAudience"],
        ValidIssuer = builder.Configuration["JWT:ValidIssuer"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["JWT:Secret"]))
    };
    // SignalR pasa el token por query string (?access_token=...)
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                context.Token = accessToken;
            return Task.CompletedTask;
        }
    };
});

// --- 4. Configuraci�n de CORS ---
// SetIsOriginAllowed permite cualquier origen pero con AllowCredentials,
// que SignalR necesita para WebSockets (AllowAnyOrigin() + AllowCredentials() son incompatibles).
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// --- 5. HttpClient para llamadas a 2Chat API ---
builder.Services.AddHttpClient();
builder.Services.AddScoped<ProfetAPI.Services.TwoChatService>();

// --- 5c. Servicio de Email ---
builder.Services.AddScoped<ProfetAPI.Services.IEmailService, ProfetAPI.Services.EmailService>();

// --- 5d. Servicio de Webhooks Salientes ---
builder.Services.AddScoped<ProfetAPI.Services.IWebhookDispatcherService, ProfetAPI.Services.WebhookDispatcherService>();

// --- 5b. Servicios de Controladores, SignalR y Swagger ---
builder.Services.AddSignalR();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    // A. Informaci�n B�sica
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Profet API",
        Version = "v1",
        Description = "API Backend para gesti�n comercial y referidos."
    });

    // B. Habilitar Anotaciones (T�tulos y descripciones en controladores)
    c.EnableAnnotations();

    // C. Configurar Seguridad JWT (El bot�n del candado)
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. \r\n\r\n Enter 'Bearer' [space] and then your token.\r\n\r\nExample: \"Bearer 12345abcdef\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement()
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                },
                Scheme = "oauth2",
                Name = "Bearer",
                In = ParameterLocation.Header,
            },
            new List<string>()
        }
    });

    // D. Integrar comentarios XML (Para que el front lea tus explicaciones)
    // NOTA: Aseg�rate de haber hecho el paso del .csproj antes de correr esto
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    c.IncludeXmlComments(xmlPath);
});

// --- Construir la App ---
var app = builder.Build();

// --- 6. Pipeline HTTP ---

// Mover Swagger FUERA del if(Development) para que se vea en Azure
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Profet API v1");
    c.RoutePrefix = string.Empty; // Opcional: Esto pone Swagger en la p�gina principal
});

if (app.Environment.IsDevelopment())
{
    // Configuraciones extra solo para dev si necesitas
}

app.UseHttpsRedirection();

// Archivos estáticos (logos, favicons subidos por clientes)
app.UseStaticFiles();

// CORS — usa la política definida arriba (SetIsOriginAllowed + AllowCredentials)
app.UseCors();

// AUTH
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<WhatsAppHub>("/hubs/whatsapp");

app.Run();