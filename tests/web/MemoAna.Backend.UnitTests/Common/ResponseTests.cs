using MemoAna.Backend.Application.Common.Responses;

namespace MemoAna.Backend.UnitTests.Common;

/// <summary>Tests application response factories.</summary>
public sealed class ResponseTests
{
    [Fact]
    public void ResponseFactories_CreateExpectedResults()
    {
        Response<string> success =
            Response.Success("data");
        Response<string> enumerableFailure =
            Response.Failure<string>(["error"]);
        Response<string> paramsFailure =
            Response.Failure<string>("error", "second");

        Assert.True(success.Succeeded);
        Assert.Equal("data", success.Data);
        Assert.False(enumerableFailure.Succeeded);
        _ = Assert.Single(enumerableFailure.Errors);
        Assert.False(paramsFailure.Succeeded);
        Assert.Equal(2, paramsFailure.Errors.Count);
    }
}
