using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Handlers.Waiters;
using APIHandler.Models.WaiterList;
using APIHandler.Services.Waiters;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests.Handlers.Waiters;

public class GetWaitersByLocationIdHandlerTests
{
    private readonly Mock<IWaiterService> _mockWaiterService = new();
    private readonly Mock<ILambdaContext> _mockContext = new();
    private readonly GetWaitersByLocationIdHandler _handler;

    public GetWaitersByLocationIdHandlerTests()
    {
        _mockContext.SetupGet(context => context.Logger).Returns(Mock.Of<ILambdaLogger>());
        _handler = new GetWaitersByLocationIdHandler(_mockWaiterService.Object);
    }

    [Fact]
    public async Task HandleRequest_ShouldReturnOnlyWaiterIdsAndLocationIds_WhenWaitersExist()
    {
        var request = CreateRequest("location1");
        var waiters = new List<WaiterListItem>
        {
            new()
            {
                UserId = "user1",
                LocationId = "location1",
                CreatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                Reservations = ["reservation1"]
            },
            new()
            {
                UserId = "user2",
                LocationId = "location1",
                CreatedAt = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc),
                Reservations = ["reservation2", "reservation3"]
            }
        };
        _mockWaiterService.Setup(service => service.GetWaitersByLocationIdAsync("location1"))
            .ReturnsAsync(waiters);

        var response = await _handler.HandleRequest(request, _mockContext.Object);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("application/json", response.Headers["Content-Type"]);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Collection(document.RootElement.EnumerateArray(),
            waiter => AssertWaiterResponse(waiter, "user1", "location1"),
            waiter => AssertWaiterResponse(waiter, "user2", "location1"));
        _mockWaiterService.Verify(service => service.GetWaitersByLocationIdAsync("location1"), Times.Once);
    }

    [Fact]
    public async Task HandleRequest_ShouldReturnEmptyArray_WhenNoWaitersMatchLocation()
    {
        var request = CreateRequest("location-without-waiters");
        _mockWaiterService.Setup(service => service.GetWaitersByLocationIdAsync("location-without-waiters"))
            .ReturnsAsync(new List<WaiterListItem>());

        var response = await _handler.HandleRequest(request, _mockContext.Object);

        Assert.Equal(200, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Empty(document.RootElement.EnumerateArray());
        _mockWaiterService.Verify(service => service.GetWaitersByLocationIdAsync("location-without-waiters"), Times.Once);
    }

    [Fact]
    public async Task HandleRequest_ShouldReturnBadRequest_WhenPathParametersAreNull()
    {
        var request = new APIGatewayProxyRequest { PathParameters = null };

        var response = await _handler.HandleRequest(request, _mockContext.Object);

        AssertBadRequest(response);
        _mockWaiterService.Verify(service => service.GetWaitersByLocationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleRequest_ShouldReturnBadRequest_WhenLocationIdIsMissing()
    {
        var request = new APIGatewayProxyRequest
        {
            PathParameters = new Dictionary<string, string> { ["id"] = "location1" }
        };

        var response = await _handler.HandleRequest(request, _mockContext.Object);

        AssertBadRequest(response);
        _mockWaiterService.Verify(service => service.GetWaitersByLocationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public async Task HandleRequest_ShouldReturnBadRequest_WhenLocationIdIsInvalid(string? locationId)
    {
        var request = CreateRequest(locationId);

        var response = await _handler.HandleRequest(request, _mockContext.Object);

        AssertBadRequest(response);
        _mockWaiterService.Verify(service => service.GetWaitersByLocationIdAsync(It.IsAny<string>()), Times.Never);
    }

    private static APIGatewayProxyRequest CreateRequest(string? locationId) => new()
    {
        PathParameters = new Dictionary<string, string> { ["locationId"] = locationId! }
    };

    private static void AssertWaiterResponse(JsonElement waiter, string userId, string locationId)
    {
        Assert.Equal(JsonValueKind.Object, waiter.ValueKind);
        Assert.Equal(new[] { "locationId", "userId" },
            waiter.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(userId, waiter.GetProperty("userId").GetString());
        Assert.Equal(locationId, waiter.GetProperty("locationId").GetString());
    }

    private static void AssertBadRequest(APIGatewayProxyResponse response)
    {
        Assert.Equal(400, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("error").GetString()));
    }
}
