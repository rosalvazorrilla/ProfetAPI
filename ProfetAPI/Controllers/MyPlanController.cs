using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProfetAPI.Data;
using ProfetAPI.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace ProfetAPI.Controllers;

/// <summary>El cliente ve su plan, sus límites y cuánto lleva usado.</summary>
[Route("api/my-plan")]
[ApiController]
[Authorize]
[SwaggerTag("Mi plan y límites")]
public class MyPlanController(ApplicationDbContext db, IPlanLimitsService planLimits) : ControllerBase
{
    private string? UserId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    private bool IsAdmin   => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value == "AdminGlobal";

    [HttpGet]
    [SwaggerOperation(Summary = "Plan contratado, límites y consumo del cliente")]
    public async Task<IActionResult> Get([FromQuery] int? customerId)
    {
        int custId;
        if (IsAdmin)
        {
            if (customerId == null) return BadRequest(new { message = "AdminGlobal debe indicar customerId." });
            custId = customerId.Value;
        }
        else
        {
            custId = await db.Users.Where(u => u.Id == UserId).Select(u => u.CustomerId).FirstOrDefaultAsync()
                  ?? await db.AccountInternalUsers.Where(u => u.UserId == UserId)
                       .Select(u => (int?)u.Account.CustomerId).FirstOrDefaultAsync() ?? 0;
            if (custId == 0) return NotFound(new { message = "Sin cliente asignado." });
        }

        var usage = await planLimits.GetUsageAsync(custId);
        var cap = await db.Customers.AsNoTracking().Where(c => c.Id == custId)
            .Select(c => c.AiQuantMonthlyCapUsd).FirstOrDefaultAsync();
        return Ok(new
        {
            planName = usage.PlanName,
            monthlyPrice = usage.MonthlyPrice,
            features = usage.Features.Select(f => new
            {
                code = f.Code, name = f.Name, included = f.Included, viaAddOn = f.ViaAddOn,
                limit = f.Limit, unlimited = f.Included && f.LimitText != null && f.Limit == null, used = f.Used,
            }),
            aiQuantMonthlyCapUsd = cap,
        });
    }
}
