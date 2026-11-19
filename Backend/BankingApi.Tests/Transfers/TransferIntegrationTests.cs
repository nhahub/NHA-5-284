using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using Xunit;
using BankingApi.Data;
using BankingApi.Accounts;

namespace BankingApi.Tests.Transfers;

public class TransferIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("banking_transfers_test")
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
                    ["AutoMigrate"] = "true",
                    ["Logging:LogLevel:Default"] = "Error",
                    ["Logging:LogLevel:Microsoft"] = "Error",
                    ["Logging:LogLevel:Microsoft.AspNetCore"] = "Error"
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
    public async Task PostTransfers_SufficientFunds_Returns200AndUpdatesBalances()
    {
        // Arrange
        var token1 = await RegisterAndLoginAsync("alice.trans@example.com", "Password123!");
        var token2 = await RegisterAndLoginAsync("bob.trans@example.com", "Password123!");

        var acc1 = await CreateAccountAsync(token1);
        var acc2 = await CreateAccountAsync(token2);

        await SetAccountBalanceAsync(acc1.AccountId, 500m);
        await SetAccountBalanceAsync(acc2.AccountId, 100m);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/transfers")
        {
            Content = JsonContent.Create(new
            {
                fromAccountId = acc1.AccountId,
                toAccountId = acc2.AccountId,
                amount = 200m
            })
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token1);

        // Act
        var response = await _client.SendAsync(requestMessage);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TransferResponse>();
        body.Should().NotBeNull();
        body!.TransactionId.Should().NotBeEmpty();

        var balance1 = await GetAccountBalanceAsync(acc1.AccountId);
        var balance2 = await GetAccountBalanceAsync(acc2.AccountId);

        balance1.Should().Be(300m);
        balance2.Should().Be(300m);
    }

    [Fact]
    public async Task PostTransfers_InsufficientFunds_Returns422AndNoBalanceChange()
    {
        // Arrange
        var token1 = await RegisterAndLoginAsync("alice.insuff@example.com", "Password123!");
        var token2 = await RegisterAndLoginAsync("bob.insuff@example.com", "Password123!");

        var acc1 = await CreateAccountAsync(token1);
        var acc2 = await CreateAccountAsync(token2);

        await SetAccountBalanceAsync(acc1.AccountId, 50m);
        await SetAccountBalanceAsync(acc2.AccountId, 100m);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/transfers")
        {
            Content = JsonContent.Create(new
            {
                fromAccountId = acc1.AccountId,
                toAccountId = acc2.AccountId,
                amount = 100m
            })
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token1);

        // Act
        var response = await _client.SendAsync(requestMessage);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var balance1 = await GetAccountBalanceAsync(acc1.AccountId);
        var balance2 = await GetAccountBalanceAsync(acc2.AccountId);

        balance1.Should().Be(50m);
        balance2.Should().Be(100m);
    }

    [Fact]
    public async Task PostTransfers_CrossUser_Returns403Forbidden()
    {
        // Arrange
        var token1 = await RegisterAndLoginAsync("alice.cross@example.com", "Password123!");
        var token2 = await RegisterAndLoginAsync("bob.cross@example.com", "Password123!");

        var acc1 = await CreateAccountAsync(token1);
        var acc2 = await CreateAccountAsync(token2);

        await SetAccountBalanceAsync(acc1.AccountId, 500m);

        // Bob tries to transfer from Alice's account
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/transfers")
        {
            Content = JsonContent.Create(new
            {
                fromAccountId = acc1.AccountId,
                toAccountId = acc2.AccountId,
                amount = 100m
            })
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);

        // Act
        var response = await _client.SendAsync(requestMessage);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ConcurrentTransfers_FromSameAccount_OnlyOneSucceedsIfInsufficientForBoth()
    {
        // Arrange
        var token1 = await RegisterAndLoginAsync("alice.conc@example.com", "Password123!");
        var token2 = await RegisterAndLoginAsync("bob.conc@example.com", "Password123!");

        var acc1 = await CreateAccountAsync(token1);
        var acc2 = await CreateAccountAsync(token2);

        await SetAccountBalanceAsync(acc1.AccountId, 150m);

        var createRequest1 = () =>
        {
            var msg = new HttpRequestMessage(HttpMethod.Post, "/transfers")
            {
                Content = JsonContent.Create(new
                {
                    fromAccountId = acc1.AccountId,
                    toAccountId = acc2.AccountId,
                    amount = 100m
                })
            };
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token1);
            return _client.SendAsync(msg);
        };

        // Act: Fire two concurrent transfers of 100m each from an account with 150m balance
        var task1 = createRequest1();
        var task2 = createRequest1();

        var responses = await Task.WhenAll(task1, task2);

        // Assert: Exactly one succeeds (200) and one fails (422)
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity).Should().Be(1);

        var finalBalance = await GetAccountBalanceAsync(acc1.AccountId);
        finalBalance.Should().Be(50m);
    }

    [Fact]
    public async Task GetAccountTransactions_Owner_ReturnsIncomingAndOutgoingOrderedByDateDesc()
    {
        // Arrange
        var token1 = await RegisterAndLoginAsync("alice.txns@example.com", "Password123!");
        var token2 = await RegisterAndLoginAsync("bob.txns@example.com", "Password123!");

        var acc1 = await CreateAccountAsync(token1);
        var acc2 = await CreateAccountAsync(token2);

        await SetAccountBalanceAsync(acc1.AccountId, 1000m);

        // Transfer 1: acc1 -> acc2 (50m)
        await PostTransferAsync(token1, acc1.AccountId, acc2.AccountId, 50m);
        // Transfer 2: acc2 -> acc1 (20m) - wait, acc2 needs funds or transfer back. Let's transfer from acc1 to acc2 again or fund acc2.
        await SetAccountBalanceAsync(acc2.AccountId, 100m);
        await PostTransferAsync(token2, acc2.AccountId, acc1.AccountId, 20m);

        // Act
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"/accounts/{acc1.AccountId}/transactions");
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        var response = await _client.SendAsync(requestMessage);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var txns = await response.Content.ReadFromJsonAsync<List<TransactionDto>>();
        txns.Should().NotBeNull();
        txns.Should().HaveCount(2);
        // Ordered by CreatedAt DESC: second transfer should be first
        txns![0].Amount.Should().Be(20m);
        txns[1].Amount.Should().Be(50m);
    }

    [Fact]
    public async Task GetAccountTransactions_NonOwner_Returns403Forbidden()
    {
        // Arrange
        var token1 = await RegisterAndLoginAsync("alice.notowner@example.com", "Password123!");
        var token2 = await RegisterAndLoginAsync("bob.notowner@example.com", "Password123!");

        var acc1 = await CreateAccountAsync(token1);

        // Act
        var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"/accounts/{acc1.AccountId}/transactions");
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        var response = await _client.SendAsync(requestMessage);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<string> RegisterAndLoginAsync(string email, string password)
    {
        await _client.PostAsJsonAsync("/auth/register", new { email, password });
        var loginResponse = await _client.PostAsJsonAsync("/auth/login", new { email, password });
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        return loginBody!.Token;
    }

    private async Task<CreateAccountResponse> CreateAccountAsync(string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/accounts");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await _client.SendAsync(req);
        return (await res.Content.ReadFromJsonAsync<CreateAccountResponse>())!;
    }

    private async Task SetAccountBalanceAsync(Guid accountId, decimal balance)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var acc = await db.Accounts.FindAsync(accountId);
        if (acc != null)
        {
            acc.Balance = balance;
            await db.SaveChangesAsync();
        }
    }

    private async Task<decimal> GetAccountBalanceAsync(Guid accountId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var acc = await db.Accounts.FindAsync(accountId);
        return acc?.Balance ?? 0m;
    }

    private async Task PostTransferAsync(string token, Guid fromId, Guid toId, decimal amount)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/transfers")
        {
            Content = JsonContent.Create(new { fromAccountId = fromId, toAccountId = toId, amount })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed record LoginResponse(string Token, DateTime ExpiresAt);
    private sealed record CreateAccountResponse(Guid AccountId, string AccountNumber);
    private sealed record TransferResponse(Guid TransactionId);
    private sealed record TransactionDto(Guid Id, Guid FromAccountId, Guid ToAccountId, decimal Amount, string Status, DateTime CreatedAt);
}
