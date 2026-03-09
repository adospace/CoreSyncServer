using CoreSyncServer.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CoreSyncServer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController(UserManager<ApplicationUser> userManager) : ControllerBase
{
    public record UserDto(string Id, string? UserName, string? Email, bool EmailConfirmed, bool LockoutEnabled, DateTimeOffset? LockoutEnd);
    public record CreateUserRequest(string UserName, string Email, string Password);
    public record UpdateUserRequest(string UserName, string Email, bool EmailConfirmed, bool LockoutEnabled);

    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> GetAll()
    {
        var users = await userManager.Users
            .OrderBy(u => u.UserName)
            .Select(u => new UserDto(u.Id, u.UserName, u.Email, u.EmailConfirmed, u.LockoutEnabled, u.LockoutEnd))
            .ToListAsync();

        return Ok(users);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> Get(string id)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        return Ok(new UserDto(user.Id, user.UserName, user.Email, user.EmailConfirmed, user.LockoutEnabled, user.LockoutEnd));
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request)
    {
        var user = new ApplicationUser
        {
            UserName = request.UserName,
            Email = request.Email,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors.Select(e => e.Description));
        }

        return CreatedAtAction(nameof(Get), new { id = user.Id },
            new UserDto(user.Id, user.UserName, user.Email, user.EmailConfirmed, user.LockoutEnabled, user.LockoutEnd));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<UserDto>> Update(string id, UpdateUserRequest request)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        user.UserName = request.UserName;
        user.Email = request.Email;
        user.EmailConfirmed = request.EmailConfirmed;
        user.LockoutEnabled = request.LockoutEnabled;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors.Select(e => e.Description));
        }

        return Ok(new UserDto(user.Id, user.UserName, user.Email, user.EmailConfirmed, user.LockoutEnabled, user.LockoutEnd));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(string id)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id == currentUserId)
        {
            return BadRequest(new[] { "You cannot delete your own account." });
        }

        var user = await userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors.Select(e => e.Description));
        }

        return NoContent();
    }
}
