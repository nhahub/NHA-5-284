using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace BankingApi.Tests.Accounts;

public class AccountsIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("banking_accounts_test")
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
    public async Task PostAccounts_Authenticated_Returns201WithUniqueAccountNumber()
    {
        // Arrange
        var token = await RegisterAndLoginAsync("accountuser1@example.com", "Password123!");
        var request = new HttpRequestMessage(HttpMethod.Post, "/accounts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateAccountResponse>();
        body.Should().NotBeNull();
        body!.AccountId.Should().NotBeEmpty();
        body.AccountNumber.Should().NotBeNullOrWhiteSpace();
        body.AccountNumber.Length.Should().Be(10);
    }

    [Fact]
    public async Task GetAccounts_Unauthenticated_Returns401Unauthorized()
    {
        // Act
        var response = await _client.GetAsync("/accounts");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostAccounts_Unauthenticated_Returns401Unauthorized()
    {
        // Act
        var response = await _client.PostAsync("/accounts", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAccounts_EnforcesOwnership_ReturnsOnlyCallerAccounts()
    {
        // Arrange: User 1
        var token1 = await RegisterAndLoginAsync("alice.accounts@example.com", "Password123!");
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/accounts");
        req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        var res1 = await _client.SendAsync(req1);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);
        var created1 = await res1.Content.ReadFromJsonAsync<CreateAccountResponse>();

        // Arrange: User 2
        var token2 = await RegisterAndLoginAsync("bob.accounts@example.com", "Password123!");
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/accounts");
        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        var res2 = await _client.SendAsync(req2);
        res2.StatusCode.Should().Be(HttpStatusCode.Created);
        var created2 = await res2.Content.ReadFromJsonAsync<CreateAccountResponse>();

        // Act: User 1 gets accounts
        var getReq1 = new HttpRequestMessage(HttpMethod.Get, "/accounts");
        getReq1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        var getRes1 = await _client.SendAsync(getReq1);
        getRes1.StatusCode.Should().Be(HttpStatusCode.OK);
        var list1 = await getRes1.Content.ReadFromJsonAsync<List<AccountDto>>();

        // Assert: User 1 sees only their account
        list1.Should().NotBeNull();
        list1.Should().HaveCount(1);
        list1![0].AccountId.Should().Be(created1!.AccountId);
        list1[0].AccountNumber.Should().Be(created1.AccountNumber);
        list1[0].Balance.Should().Be(0m);
        list1[0].Currency.Should().Be("EGP");

        // Act: User 2 gets accounts
        var getReq2 = new HttpRequestMessage(HttpMethod.Get, "/accounts");
        getReq2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        var getRes2 = await _client.SendAsync(getReq2);
        getRes2.StatusCode.Should().Be(HttpStatusCode.OK);
        var list2 = await getRes2.Content.ReadFromJsonAsync<List<AccountDto>>();

        // Assert: User 2 sees only their account
        list2.Should().NotBeNull();
        list2.Should().HaveCount(1);
        list2![0].AccountId.Should().Be(created2!.AccountId);
        list2[0].AccountNumber.Should().Be(created2.AccountNumber);
    }

    private async Task<string> RegisterAndLoginAsync(string email, string password)
    {
        await _client.PostAsJsonAsync("/auth/register", new { email, password });
        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new { email, password });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        return loginBody!.Token;
    }

    private sealed record CreateAccountResponse(Guid AccountId, string AccountNumber);
    private sealed record LoginResponse(string Token, DateTime ExpiresAt);
    private sealed record AccountDto(Guid AccountId, string AccountNumber, decimal Balance, string Currency, DateTime CreatedAt);
}
