using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace BankingApi.Tests.Auth;

public class AuthIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("banking_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = _dbContainer.GetConnectionString(),
                    ["Jwt:Key"] = "SuperSecretKeyForIntegrationTesting12345!",
                    ["Jwt:Issuer"] = "BankingApi",
                    ["Jwt:Audience"] = "BankingApiUsers",
                    ["AutoMigrate"] = "true"
                });
            });
        });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _dbContainer.DisposeAsync();
    }

    [Fact]
    public async Task Register_WithValidCredentials_Returns201CreatedWithUserId()
    {
        // Arrange
        var request = new { email = "alice@example.com", password = "Password123!" };

        // Act
        var response = await _client.PostAsJsonAsync("/auth/register", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        body.Should().NotBeNull();
        body!.UserId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_Returns409Conflict()
    {
        // Arrange
        var request = new { email = "bob@example.com", password = "Password123!" };
        var firstResponse = await _client.PostAsJsonAsync("/auth/register", request);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Act
        var duplicateResponse = await _client.PostAsJsonAsync("/auth/register", request);

        // Assert
        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("", "Password123!")]
    [InlineData("charlie@example.com", "")]
    [InlineData("charlie@example.com", "short")]
    public async Task Register_WithInvalidInput_Returns400BadRequest(string email, string password)
    {
        // Arrange
        var request = new { email, password };

        // Act
        var response = await _client.PostAsJsonAsync("/auth/register", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_WithCorrectCredentials_Returns200OkWithValidJwtToken()
    {
        // Arrange: register a user first
        var email = "dan@example.com";
        var password = "Password123!";
        var registerResponse = await _client.PostAsJsonAsync("/auth/register", new { email, password });
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var registerBody = await registerResponse.Content.ReadFromJsonAsync<RegisterResponse>();

        // Act: login
        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new { email, password });

        // Assert
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        loginBody.Should().NotBeNull();
        loginBody!.Token.Should().NotBeNullOrWhiteSpace();
        loginBody.ExpiresAt.Should().BeAfter(DateTime.UtcNow);

        var tokenHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt = tokenHandler.ReadJwtToken(loginBody.Token);

        jwt.Claims.Should().Contain(c => (c.Type == "sub" || c.Type == System.Security.Claims.ClaimTypes.NameIdentifier) && c.Value == registerBody!.UserId.ToString());
        jwt.Claims.Should().Contain(c => (c.Type == "email" || c.Type == System.Security.Claims.ClaimTypes.Email) && c.Value == email);
        jwt.Claims.Should().Contain(c => (c.Type == "role" || c.Type == System.Security.Claims.ClaimTypes.Role) && c.Value == "Customer");
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401UnauthorizedWithGenericMessage()
    {
        // Arrange
        var email = "eve@example.com";
        await _client.PostAsJsonAsync("/auth/register", new { email, password = "Password123!" });

        // Act
        var response = await _client.PostAsJsonAsync("/auth/login", new { email, password = "WrongPassword999!" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Invalid email or password.");
    }

    [Fact]
    public async Task Login_WithNonExistentEmail_Returns401UnauthorizedWithGenericMessage()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/auth/login", new { email = "nonexistent@example.com", password = "Password123!" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        body.Should().NotBeNull();
        body!.Message.Should().Be("Invalid email or password.");
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401Unauthorized()
    {
        // Act
        var response = await _client.GetAsync("/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithValidToken_Returns200Ok()
    {
        // Arrange
        var email = "frank@example.com";
        var password = "Password123!";
        await _client.PostAsJsonAsync("/auth/register", new { email, password });
        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new { email, password });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", loginBody!.Token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed record RegisterResponse(Guid UserId);
    private sealed record LoginResponse(string Token, DateTime ExpiresAt);
    private sealed record ErrorResponse(string Message);
}
