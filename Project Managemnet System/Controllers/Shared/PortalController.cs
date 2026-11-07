using Microsoft.AspNetCore.Mvc;
using Project_Managemnet_System.Infrastructure;

namespace Project_Managemnet_System.Controllers.Shared;

/// <summary>Base for controllers used by both the member portal and the /Admin portal.</summary>
public abstract class PortalController : Controller
{
    protected string UserId => User.GetUserId();

    protected bool IsAdminPortal => string.Equals(RouteData.Values["area"] as string, "Admin", StringComparison.OrdinalIgnoreCase);

    protected void Flash(string message, string kind = "success")
    {
        TempData["Flash"] = message;
        TempData["FlashKind"] = kind;
    }
}
