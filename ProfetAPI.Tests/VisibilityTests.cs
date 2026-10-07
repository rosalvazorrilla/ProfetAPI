using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Models;
using ProfetAPI.Services;
using Xunit;

namespace ProfetAPI.Tests;

/// <summary>
/// Pruebas de la regla "un vendedor solo ve a sus propios clientes". Usan una base en memoria con usuarios
/// inventados: no tocan datos reales.
/// </summary>
public class VisibilityTests
{
    // Cliente 1 = migrado · Cliente 2 = NO migrado (conserva la visibilidad de siempre)
    private static (ApplicationDbContext db, VisibilityService vis) Build()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("vis-" + Guid.NewGuid()).Options;
        var db = new ApplicationDbContext(options);

        db.Customers.Add(new Customer { Id = 1, Name = "Migrado", IsMigrated = true });
        db.Customers.Add(new Customer { Id = 2, Name = "Legacy", IsMigrated = false });
        db.Accounts.Add(new Account { AccountId = 10, CustomerId = 1, Name = "Cuenta migrada" });
        db.Accounts.Add(new Account { AccountId = 20, CustomerId = 2, Name = "Cuenta legacy" });

        // Equipo liderado por "lider" con "ana" dentro
        db.Teams.Add(new Team { Id = 100, CustomerId = 1, Name = "Equipo A", LeaderId = "lider" });
        db.UserTeams.Add(new UserTeam { UserId = "ana", TeamId = 100 });

        db.Leads.AddRange(
            new Lead { LeadId = 1, AccountId = 10, OwnerUserId = "ana", Name = "De Ana" },
            new Lead { LeadId = 2, AccountId = 10, OwnerUserId = "beto", Name = "De Beto" },
            new Lead { LeadId = 3, AccountId = 10, OwnerUserId = null, Name = "Sin asignar" },
            new Lead { LeadId = 4, AccountId = 20, OwnerUserId = "beto", Name = "Legacy de Beto" });
        db.SaveChanges();
        return (db, new VisibilityService(db));
    }

    private static ClaimsPrincipal User(string id, string role) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id), new Claim(ClaimTypes.Role, role) }, "test"));

    [Theory]
    [InlineData("AdminGlobal")]
    [InlineData("PM")]
    [InlineData("Admin")]
    [InlineData("ManagerAdmin")]
    [InlineData("Manager")]
    public async Task Roles_con_visibilidad_total_ven_todo(string role)
    {
        var (_, vis) = Build();
        var scope = await vis.ForAccountAsync(User("cualquiera", role), 10);
        Assert.True(scope.All);
    }

    [Theory]
    [InlineData("UserCRM")]
    [InlineData("User")]
    [InlineData("UserEdit")]
    public async Task Vendedor_solo_se_ve_a_si_mismo(string role)
    {
        var (_, vis) = Build();
        var scope = await vis.ForAccountAsync(User("beto", role), 10);
        Assert.False(scope.All);
        Assert.Equal(new[] { "beto" }, scope.UserIds);
        Assert.True(scope.Includes("beto"));
        Assert.False(scope.Includes("ana"));
        Assert.False(scope.Includes(null)); // lo no asignado no es de ningún vendedor
    }

    [Fact]
    public async Task Vendedor_que_lidera_un_equipo_ve_lo_de_su_equipo()
    {
        var (_, vis) = Build();
        var scope = await vis.ForAccountAsync(User("lider", "UserCRM"), 10);
        Assert.False(scope.All);
        Assert.True(scope.Includes("lider"));
        Assert.True(scope.Includes("ana"));    // miembro de su equipo
        Assert.False(scope.Includes("beto"));  // fuera de su equipo
    }

    [Fact]
    public async Task Cliente_no_migrado_conserva_la_visibilidad_de_siempre()
    {
        var (_, vis) = Build();
        var scope = await vis.ForAccountAsync(User("beto", "UserCRM"), 20);
        Assert.True(scope.All);
    }

    [Fact]
    public async Task Rol_desconocido_no_se_restringe_por_error()
    {
        var (_, vis) = Build();
        var scope = await vis.ForAccountAsync(User("x", "RolNuevo"), 10);
        Assert.True(scope.All);
    }

    // ── El filtro que protege las rutas /leads/{id} ──────────────────────────────

    private static async Task<IActionResult?> RunLeadFilter(ApplicationDbContext db, VisibilityService vis, ClaimsPrincipal user, long leadId)
    {
        var http = new DefaultHttpContext { User = user };
        var route = new RouteData(); route.Values["id"] = leadId.ToString();
        var actionContext = new ActionContext(http, route, new ActionDescriptor());
        var executing = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), new object());
        bool nextCalled = false;
        await new LeadVisibilityFilter(db, vis).OnActionExecutionAsync(executing, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), new object()));
        });
        return nextCalled ? null : executing.Result;
    }

    [Fact]
    public async Task Filtro_deja_pasar_el_prospecto_propio()
    {
        var (db, vis) = Build();
        Assert.Null(await RunLeadFilter(db, vis, User("ana", "UserCRM"), 1));
    }

    [Fact]
    public async Task Filtro_oculta_el_prospecto_de_otro_vendedor()
    {
        var (db, vis) = Build();
        Assert.IsType<NotFoundObjectResult>(await RunLeadFilter(db, vis, User("ana", "UserCRM"), 2));
    }

    [Fact]
    public async Task Filtro_oculta_el_prospecto_sin_asignar_a_un_vendedor_pero_no_al_admin()
    {
        var (db, vis) = Build();
        Assert.IsType<NotFoundObjectResult>(await RunLeadFilter(db, vis, User("ana", "UserCRM"), 3));
        Assert.Null(await RunLeadFilter(db, vis, User("jefa", "Admin"), 3));
    }

    [Fact]
    public async Task Filtro_deja_al_lider_abrir_el_prospecto_de_su_equipo_y_no_el_ajeno()
    {
        var (db, vis) = Build();
        Assert.Null(await RunLeadFilter(db, vis, User("lider", "UserCRM"), 1));
        Assert.IsType<NotFoundObjectResult>(await RunLeadFilter(db, vis, User("lider", "UserCRM"), 2));
    }

    [Fact]
    public async Task Filtro_no_restringe_en_cliente_no_migrado()
    {
        var (db, vis) = Build();
        Assert.Null(await RunLeadFilter(db, vis, User("ana", "UserCRM"), 4));
    }
}
