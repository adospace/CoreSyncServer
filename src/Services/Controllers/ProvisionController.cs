using CoreSyncServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoreSyncServer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProvisionController(IProvisionService provisionService) : ControllerBase
{
    [HttpPost("{dataStoreId}/apply")]
    public async Task<ActionResult> Apply(int dataStoreId)
    {
        var result = await provisionService.ApplyProvisionAsync(dataStoreId);

        if (!result.Success)
            return BadRequest(new[] { result.Error });

        return NoContent();
    }

    [HttpPost("{dataStoreId}/remove")]
    public async Task<ActionResult> Remove(int dataStoreId)
    {
        var result = await provisionService.RemoveProvisionAsync(dataStoreId);

        if (!result.Success)
            return BadRequest(new[] { result.Error });

        return NoContent();
    }
}
