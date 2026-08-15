using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using NLog;

namespace StoreManagement.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public abstract class BaseApiController : ControllerBase
    {
        protected Logger Logger { get; }

        protected BaseApiController()
        {
            Logger = LogManager.GetLogger(GetType().Name);
        }
        protected int CurrentUserId
        {
            get
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                               ?? User.FindFirst("userId")?.Value;

                if (int.TryParse(userIdClaim, out var userId))
                {
                    return userId;
                }

                throw new UnauthorizedAccessException("User identification claim missing or invalid.");
            }
        }
    }
}
