using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Velox.Shared.Dtos;
using Velox.Shared.Models;
using Xunit;

namespace VeloxAPI.Tests.Tests;

public sealed class AuthTests : IClassFixture<VeloxApiFactory>
{
    private readonly HttpClient _client;
    private readonly string     _apiKey;

    public AuthTests(VeloxApiFactory factory)
    {
        var (_, key) = factory.SetupDepot("AuthTestDepot", "AUTH");
        _apiKey      = key;
        _client      = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
    }

    [Fact]
    public async Task Login_CorrectPin_ReturnsJwt()
    {
        // Create employee
        var create = await _client.PostAsJsonAsync("/api/v1/employees",
            new CreateEmployeeRequest { Name = "Alice", Pin = "1234", Role = EmployeeRole.Technician });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        // Login
        var resp = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Name = "Alice", Pin = "1234" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var login = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(login);
        Assert.NotEmpty(login!.Token);
        Assert.Equal("Alice", login.Name);
        Assert.Equal(EmployeeRole.Technician, login.Role);
        Assert.True(login.ExpiresAtUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Login_WrongPin_Returns401()
    {
        await _client.PostAsJsonAsync("/api/v1/employees",
            new CreateEmployeeRequest { Name = "Bob", Pin = "5678", Role = EmployeeRole.Technician });

        var resp = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Name = "Bob", Pin = "0000" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownEmployee_Returns401()
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Name = "Ghost", Pin = "9999" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Login_MissingFields_Returns400()
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Name = "", Pin = "" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}

public sealed class EmployeeTests : IClassFixture<VeloxApiFactory>
{
    private readonly VeloxApiFactory _factory;
    private readonly HttpClient _client;

    public EmployeeTests(VeloxApiFactory factory)
    {
        _factory     = factory;
        var (_, key) = factory.SetupDepot("EmpTestDepot", "EMP");
        _client      = factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-API-Key", key);
    }

    [Fact]
    public async Task CreateEmployee_ValidRequest_Returns200()
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/employees",
            new CreateEmployeeRequest { Name = "Charlie", Pin = "4321", Role = EmployeeRole.Manager });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var emp = await resp.Content.ReadFromJsonAsync<EmployeeDto>();
        Assert.NotNull(emp);
        Assert.Equal("Charlie", emp!.Name);
        Assert.Equal(EmployeeRole.Manager, emp.Role);
        Assert.True(emp.IsActive);
    }

    [Fact]
    public async Task CreateEmployee_ShortPin_Returns400()
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/employees",
            new CreateEmployeeRequest { Name = "Dave", Pin = "12" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task ListEmployees_AfterCreate_ContainsEmployee()
    {
        await _client.PostAsJsonAsync("/api/v1/employees",
            new CreateEmployeeRequest { Name = "Eve", Pin = "1111" });

        var resp = await _client.GetAsync("/api/v1/employees");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("count").GetInt32() >= 1);
    }

    [Fact]
    public async Task PatchEmployee_Deactivate_IsActiveBecomesfalse()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/employees",
            new CreateEmployeeRequest { Name = "Frank", Pin = "2222" });
        var emp    = await create.Content.ReadFromJsonAsync<EmployeeDto>();

        var patch = new PatchEmployeeRequest { IsActive = false };
        await _client.PatchAsJsonAsync($"/api/v1/employees/{emp!.Id}", patch);

        var get     = await _client.GetAsync($"/api/v1/employees/{emp.Id}");
        var updated = await get.Content.ReadFromJsonAsync<EmployeeDto>();
        Assert.False(updated!.IsActive);
    }

    [Fact]
    public async Task AssignAsset_ThenGetJobs_ReturnsJob()
    {
        // Create employee
        var create = await _client.PostAsJsonAsync("/api/v1/employees",
            new CreateEmployeeRequest { Name = "Grace", Pin = "3333" });
        var emp = await create.Content.ReadFromJsonAsync<EmployeeDto>();

        // Create asset
        var intake = await _client.PostAsJsonAsync("/api/v1/assets/intake",
            new IntakeRequest { SerialNumber = "SN-JOB-TEST-001" });
        var asset = await intake.Content.ReadFromJsonAsync<AssetDto>();

        // Assign
        var assign = await _client.PostAsJsonAsync($"/api/v1/assets/{asset!.Id}/assign",
            new AssignRequest { EmployeeId = emp!.Id, Actor = "Manager" });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);

        // Login as Grace and get jobs
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Name = "Grace", Pin = "3333" });
        var loginResp = await login.Content.ReadFromJsonAsync<LoginResponse>();

        var jobClient = _factory.CreateClient();
        jobClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {loginResp!.Token}");
        jobClient.DefaultRequestHeaders.Add("X-API-Key", _client.DefaultRequestHeaders.GetValues("X-API-Key").First());

        var jobs = await jobClient.GetAsync("/api/v1/jobs/mine");
        Assert.Equal(HttpStatusCode.OK, jobs.StatusCode);
        var body = await jobs.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("count").GetInt32() >= 1);
    }

    [Fact]
    public async Task CreateEmployee_WithStationId_SetsStation()
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/employees",
            new CreateEmployeeRequest { Name = "Henry", Pin = "5555", Role = EmployeeRole.Technician, StationId = "BENCH-07" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var emp = await resp.Content.ReadFromJsonAsync<EmployeeDto>();
        Assert.NotNull(emp);
        Assert.Equal("HENRY", emp!.Name.ToUpperInvariant());
        Assert.Equal("BENCH-07", emp.StationId);
    }

    [Fact]
    public async Task PatchEmployee_StationAndPin_UpdatesBoth()
    {
        var create = await _client.PostAsJsonAsync("/api/v1/employees",
            new CreateEmployeeRequest { Name = "Ian", Pin = "7777", StationId = "BENCH-01" });
        var emp = await create.Content.ReadFromJsonAsync<EmployeeDto>();

        var patch = new PatchEmployeeRequest { StationId = "BENCH-99", Pin = "8888" };
        var patchResp = await _client.PatchAsJsonAsync($"/api/v1/employees/{emp!.Id}", patch);
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);

        var get = await _client.GetAsync($"/api/v1/employees/{emp.Id}");
        var updated = await get.Content.ReadFromJsonAsync<EmployeeDto>();
        Assert.Equal("BENCH-99", updated!.StationId);

        // Verify login with new PIN succeeds
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest { Name = "Ian", Pin = "8888" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }
}
