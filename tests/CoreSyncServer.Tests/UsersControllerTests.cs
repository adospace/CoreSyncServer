using System.Net;
using System.Net.Http.Json;
using CoreSyncServer.Controllers;
using CoreSyncServer.Tests.Infrastructure;
using FluentAssertions;

namespace CoreSyncServer.Tests;

public class UsersControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public UsersControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetAll_ReturnsSeededAdminUser()
    {
        var client = _factory.CreateAuthenticatedClient();

        var users = await client.GetFromJsonAsync<List<UsersController.UserDto>>("/api/users");

        users.Should().NotBeNull();
        users.Should().Contain(u => u.UserName == "admin");
    }

    [Fact]
    public async Task GetAll_Unauthenticated_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/users");

        // Identity may redirect to login or return 401 depending on config
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Redirect, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_ExistingUser_ReturnsUser()
    {
        var client = _factory.CreateAuthenticatedClient();

        // The seeded admin user has this well-known ID
        var response = await client.GetAsync("/api/users/00000000-0000-0000-0000-000000000001");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await response.Content.ReadFromJsonAsync<UsersController.UserDto>();
        user!.UserName.Should().Be("admin");
    }

    [Fact]
    public async Task Get_NonExistentUser_ReturnsNotFound()
    {
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/api/users/non-existent-id");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_ValidUser_ReturnsCreated()
    {
        var client = _factory.CreateAuthenticatedClient();
        var request = new UsersController.CreateUserRequest("newuser", "new@example.com", "P@ssw0rd123!");

        var response = await client.PostAsJsonAsync("/api/users", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var user = await response.Content.ReadFromJsonAsync<UsersController.UserDto>();
        user!.UserName.Should().Be("newuser");
        user.Email.Should().Be("new@example.com");
        user.EmailConfirmed.Should().BeTrue();
    }

    [Fact]
    public async Task Create_WeakPassword_ReturnsBadRequest()
    {
        var client = _factory.CreateAuthenticatedClient();
        var request = new UsersController.CreateUserRequest("weakuser", "weak@example.com", "123");

        var response = await client.PostAsJsonAsync("/api/users", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_ExistingUser_ReturnsUpdatedUser()
    {
        var client = _factory.CreateAuthenticatedClient();

        // First create a user to update
        var createRequest = new UsersController.CreateUserRequest("toupdate", "update@example.com", "P@ssw0rd123!");
        var createResponse = await client.PostAsJsonAsync("/api/users", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<UsersController.UserDto>();

        // Update the user
        var updateRequest = new UsersController.UpdateUserRequest("updated", "updated@example.com", true, false);
        var response = await client.PutAsJsonAsync($"/api/users/{created!.Id}", updateRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<UsersController.UserDto>();
        updated!.UserName.Should().Be("updated");
        updated.Email.Should().Be("updated@example.com");
    }

    [Fact]
    public async Task Update_NonExistentUser_ReturnsNotFound()
    {
        var client = _factory.CreateAuthenticatedClient();
        var request = new UsersController.UpdateUserRequest("ghost", "ghost@example.com", true, false);

        var response = await client.PutAsJsonAsync("/api/users/non-existent-id", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_OtherUser_ReturnsNoContent()
    {
        var client = _factory.CreateAuthenticatedClient();

        // Create a user to delete
        var createRequest = new UsersController.CreateUserRequest("todelete", "delete@example.com", "P@ssw0rd123!");
        var createResponse = await client.PostAsJsonAsync("/api/users", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<UsersController.UserDto>();

        var response = await client.DeleteAsync($"/api/users/{created!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_OwnAccount_ReturnsBadRequest()
    {
        var client = _factory.CreateAuthenticatedClient(
            userId: AuthenticatedHttpClientExtensions.DefaultUserId);

        var response = await client.DeleteAsync(
            $"/api/users/{AuthenticatedHttpClientExtensions.DefaultUserId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_NonExistentUser_ReturnsNotFound()
    {
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.DeleteAsync("/api/users/non-existent-id");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
