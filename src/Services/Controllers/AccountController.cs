using CoreSyncServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CoreSyncServer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AccountController(UserManager<ApplicationUser> userManager) : ControllerBase
{
    public record ProfileDto(string UserName, string? Email);
    public record UpdateProfileRequest(string UserName, string Email);

    [HttpGet("profile")]
    public async Task<ActionResult<ProfileDto>> GetProfile()
    {
        var user = await userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user is null) return NotFound();

        return Ok(new ProfileDto(user.UserName!, user.Email));
    }

    [HttpPut("profile")]
    public async Task<ActionResult<ProfileDto>> UpdateProfile(UpdateProfileRequest request)
    {
        var user = await userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user is null) return NotFound();

        user.UserName = request.UserName;
        user.Email = request.Email;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors.Select(e => e.Description));
        }

        return Ok(new ProfileDto(user.UserName!, user.Email));
    }

    [HttpDelete]
    public async Task<ActionResult> DeleteAccount()
    {
        var user = await userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user is null) return NotFound();

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors.Select(e => e.Description));
        }

        return NoContent();
    }
}
