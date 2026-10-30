using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mediator;
using MemoAna.Game.Models;
using MemoAna.Server.Application.Abstract;
using MemoAna.Server.Application.Relay.Handlers;
using MemoAna.Server.Application.Relay.Requests;
using MemoAna.Server.Application.Relay.Responses;
using MemoAna.Server.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace MemoAna.Server.Tests;

public class RelayServerControllerAndCqrsTests
{
    private sealed class FakeRelayService : IRelayService
    {
        public int Port => 35680;

        public Task<RelayResult<CreateRoomResponse>> CreateRoomAsync(
            string playerId, string roomName, string? password, string theme, int difficulty, string? playerName = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(RelayResult<CreateRoomResponse>.Ok(
                new CreateRoomResponse("room123", roomName, theme, difficulty, !string.IsNullOrEmpty(password), Port, "tokenA")));
        }

        public Task<RelayResult<IReadOnlyList<RelayRoomSummary>>> ListRoomsAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<RelayRoomSummary> list = [
                new RelayRoomSummary("room123", "Room 1", "Host", "Animals", 1, false, DateTime.UtcNow)
            ];
            return Task.FromResult(RelayResult<IReadOnlyList<RelayRoomSummary>>.Ok(list));
        }

        public Task<RelayResult<JoinRoomResponse>> JoinRoomAsync(
            string playerId, string roomId, string? password, string? playerName = null, CancellationToken cancellationToken = default)
        {
            if (roomId == "not-found")
                return Task.FromResult(RelayResult<JoinRoomResponse>.NotFound("Room does not exist."));
            if (roomId == "full")
                return Task.FromResult(RelayResult<JoinRoomResponse>.Conflict("Room is full."));
            if (roomId == "auth" && password != "secret")
                return Task.FromResult(RelayResult<JoinRoomResponse>.Unauthorized("Invalid password."));

            return Task.FromResult(RelayResult<JoinRoomResponse>.Ok(
                new JoinRoomResponse(roomId, "Room", "Animals", 1, Port, "tokenB")));
        }

        public Task<RelayResult> LeaveRoomAsync(string roomId, string playerId, CancellationToken cancellationToken = default)
        {
            if (roomId == "not-found")
                return Task.FromResult(RelayResult.NotFound("Room does not exist."));
            return Task.FromResult(RelayResult.Ok("Left."));
        }

        public Task<RelayResult> CloseRoomAsync(string roomId, CancellationToken cancellationToken = default)
        {
            if (roomId == "not-found")
                return Task.FromResult(RelayResult.NotFound("Room does not exist."));
            return Task.FromResult(RelayResult.Ok("Closed."));
        }

        public void CleanupExpiredRooms() { }
    }

    private sealed class FakeMediator(RelayServerHandlers handlers) : IMediator
    {
        public async ValueTask<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is CreateRoomCommand c)
                return (TResponse)(object)await handlers.Handle(c, cancellationToken);
            if (request is ListRoomsQuery q)
                return (TResponse)(object)await handlers.Handle(q, cancellationToken);
            if (request is JoinRoomCommand j)
                return (TResponse)(object)await handlers.Handle(j, cancellationToken);
            if (request is LeaveRoomCommand l)
                return (TResponse)(object)await handlers.Handle(l, cancellationToken);
            if (request is CloseRoomCommand cl)
                return (TResponse)(object)await handlers.Handle(cl, cancellationToken);

            throw new NotSupportedException();
        }

        public ValueTask<TResponse> Send<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default) =>
            Send((IRequest<TResponse>)command, cancellationToken);

        public ValueTask<TResponse> Send<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default) =>
            Send((IRequest<TResponse>)query, cancellationToken);

        public ValueTask<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => throw new NotSupportedException();
        public ValueTask Publish(object notification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamCommand<TResponse> command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamQuery<TResponse> query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Handler_Validation_RejectsInvalidCommandsWithBadRequest()
    {
        var handlers = new RelayServerHandlers(new FakeRelayService());

        // CreateRoomCommand validation
        var res1 = await handlers.Handle(new CreateRoomCommand("", "Room", null, "Theme", 1), default);
        Assert.Equal(RelayResultStatus.BadRequest, res1.Status);

        var res2 = await handlers.Handle(new CreateRoomCommand("player", "", null, "Theme", 1), default);
        Assert.Equal(RelayResultStatus.BadRequest, res2.Status);

        var res3 = await handlers.Handle(new CreateRoomCommand("player", "Room", null, "", 1), default);
        Assert.Equal(RelayResultStatus.BadRequest, res3.Status);

        var res4 = await handlers.Handle(new CreateRoomCommand("player", "Room", null, "Theme", -1), default);
        Assert.Equal(RelayResultStatus.BadRequest, res4.Status);

        // JoinRoomCommand validation
        var res5 = await handlers.Handle(new JoinRoomCommand("", "player", null), default);
        Assert.Equal(RelayResultStatus.BadRequest, res5.Status);

        var res6 = await handlers.Handle(new JoinRoomCommand("room", "", null), default);
        Assert.Equal(RelayResultStatus.BadRequest, res6.Status);

        // LeaveRoomCommand validation
        var res7 = await handlers.Handle(new LeaveRoomCommand("", "player"), default);
        Assert.Equal(RelayResultStatus.BadRequest, res7.Status);

        var res8 = await handlers.Handle(new LeaveRoomCommand("room", ""), default);
        Assert.Equal(RelayResultStatus.BadRequest, res8.Status);

        // CloseRoomCommand validation
        var res9 = await handlers.Handle(new CloseRoomCommand(""), default);
        Assert.Equal(RelayResultStatus.BadRequest, res9.Status);
    }

    [Fact]
    public async Task Controller_CreateRoom_Returns201Created()
    {
        var fakeRelay = new FakeRelayService();
        var handlers = new RelayServerHandlers(fakeRelay);
        var mediator = new FakeMediator(handlers);
        var controller = new RelayServerController(mediator);

        var dto = new CreateRoomDto("host1", "Test Room", null, "Animals", 1, "Alice");
        var result = await controller.CreateRoom(dto, default);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);
        var response = Assert.IsType<CreateRoomResponse>(createdResult.Value);
        Assert.Equal("room123", response.RoomId);
    }

    [Fact]
    public async Task Controller_ListRooms_Returns200Ok()
    {
        var fakeRelay = new FakeRelayService();
        var handlers = new RelayServerHandlers(fakeRelay);
        var mediator = new FakeMediator(handlers);
        var controller = new RelayServerController(mediator);

        var result = await controller.ListRooms(default);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
        var rooms = Assert.IsAssignableFrom<IReadOnlyList<RelayRoomSummary>>(okResult.Value);
        Assert.Single(rooms);
    }

    [Fact]
    public async Task Controller_JoinRoom_Success_Returns200Ok()
    {
        var fakeRelay = new FakeRelayService();
        var handlers = new RelayServerHandlers(fakeRelay);
        var mediator = new FakeMediator(handlers);
        var controller = new RelayServerController(mediator);

        var dto = new JoinRoomDto("guest1", null, "Bob");
        var result = await controller.JoinRoom("room123", dto, default);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, okResult.StatusCode);
        var response = Assert.IsType<JoinRoomResponse>(okResult.Value);
        Assert.Equal("room123", response.RoomId);
    }

    [Fact]
    public async Task Controller_JoinRoom_InvalidPassword_Returns401Unauthorized()
    {
        var fakeRelay = new FakeRelayService();
        var handlers = new RelayServerHandlers(fakeRelay);
        var mediator = new FakeMediator(handlers);
        var controller = new RelayServerController(mediator);

        var dto = new JoinRoomDto("guest1", "wrong-password", "Bob");
        var result = await controller.JoinRoom("auth", dto, default);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, objResult.StatusCode);
    }

    [Fact]
    public async Task Controller_JoinRoom_Full_Returns409Conflict()
    {
        var fakeRelay = new FakeRelayService();
        var handlers = new RelayServerHandlers(fakeRelay);
        var mediator = new FakeMediator(handlers);
        var controller = new RelayServerController(mediator);

        var dto = new JoinRoomDto("guest1", null, "Bob");
        var result = await controller.JoinRoom("full", dto, default);

        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflictResult.StatusCode);
    }

    [Fact]
    public async Task Controller_JoinRoom_NotFound_Returns404NotFound()
    {
        var fakeRelay = new FakeRelayService();
        var handlers = new RelayServerHandlers(fakeRelay);
        var mediator = new FakeMediator(handlers);
        var controller = new RelayServerController(mediator);

        var dto = new JoinRoomDto("guest1", null, "Bob");
        var result = await controller.JoinRoom("not-found", dto, default);

        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, notFoundResult.StatusCode);
    }

    [Fact]
    public async Task Controller_LeaveRoom_Success_Returns204NoContent()
    {
        var fakeRelay = new FakeRelayService();
        var handlers = new RelayServerHandlers(fakeRelay);
        var mediator = new FakeMediator(handlers);
        var controller = new RelayServerController(mediator);

        var dto = new LeaveRoomDto("guest1");
        var result = await controller.LeaveRoom("room123", dto, default);

        var noContentResult = Assert.IsType<NoContentResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
    }

    [Fact]
    public async Task Controller_CloseRoom_Success_Returns204NoContent()
    {
        var fakeRelay = new FakeRelayService();
        var handlers = new RelayServerHandlers(fakeRelay);
        var mediator = new FakeMediator(handlers);
        var controller = new RelayServerController(mediator);

        var result = await controller.CloseRoom("room123", default);

        var noContentResult = Assert.IsType<NoContentResult>(result);
        Assert.Equal(StatusCodes.Status204NoContent, noContentResult.StatusCode);
    }
}
