using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;

var options = MatchmakerOptions.FromEnvironment();
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(options.ListenAddress);

var app = builder.Build();
var rooms = new RoomManager(options, app.Logger);
app.Lifetime.ApplicationStopping.Register(rooms.StopAll);
app.UseWebSockets();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/custom/lobbies", (HttpContext context) =>
{
    context.Response.Headers["Access-Control-Allow-Origin"] = "*";
    context.Response.Headers["Cache-Control"] = "no-store";
    return Results.Ok(new { lobbies = rooms.ListCustomLobbies() });
});
app.MapGet("/", context => RelayRoom(context, () => rooms.TryReserve(), StatusCodes.Status503ServiceUnavailable));
app.MapGet("/custom/host", context =>
    RelayRoom(context, () => rooms.TryHostCustom(), StatusCodes.Status503ServiceUnavailable));
app.MapGet("/custom/join/{id:guid}", (HttpContext context, Guid id) =>
    RelayRoom(context, () => rooms.TryJoinCustom(id), StatusCodes.Status404NotFound));

await app.RunAsync();

async Task RelayRoom(HttpContext context, Func<RoomLease?> reserve, int unavailableStatus)
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
        return;
    }

    RoomLease? lease;
    try
    {
        lease = reserve();
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Could not start a game server.");
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return;
    }

    if (lease == null)
    {
        context.Response.StatusCode = unavailableStatus;
        return;
    }

    using (lease)
    {
        try
        {
            using var client = await context.WebSockets.AcceptWebSocketAsync();
            using var backend = await ConnectToRoom(lease.Room, options.StartTimeout, context.RequestAborted);
            await Relay(client, backend, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The player left while the connection was being set up or relayed.
        }
        catch (Exception exception) when (exception is WebSocketException || exception is TimeoutException || exception is InvalidOperationException)
        {
            app.Logger.LogWarning("Room {Room} connection ended: {Message}", lease.Room.Port, exception.Message);
        }
    }
}

static async Task<ClientWebSocket> ConnectToRoom(Room room, TimeSpan timeout, CancellationToken cancellationToken)
{
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    deadline.CancelAfter(timeout);

    while (!deadline.IsCancellationRequested)
    {
        if (room.Process.HasExited)
            throw new InvalidOperationException($"Game server exited with code {room.Process.ExitCode}.");

        var socket = new ClientWebSocket();
        try
        {
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{room.Port}/"), deadline.Token);
            return socket;
        }
        catch (WebSocketException)
        {
            socket.Dispose();
        }
        catch (OperationCanceledException)
        {
            socket.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException("Timed out waiting for the game server.");
        }

        try
        {
            await Task.Delay(200, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            break;
        }
    }

    cancellationToken.ThrowIfCancellationRequested();
    throw new TimeoutException("Timed out waiting for the game server.");
}

static async Task Relay(WebSocket client, WebSocket backend, CancellationToken cancellationToken)
{
    using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    Task toServer = CopyMessages(client, backend, cancellation.Token);
    Task toClient = CopyMessages(backend, client, cancellation.Token);

    await Task.WhenAny(toServer, toClient);
    cancellation.Cancel();

    try
    {
        await Task.WhenAll(toServer, toClient);
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    catch (WebSocketException) when (cancellation.IsCancellationRequested) { }
}

static async Task CopyMessages(WebSocket source, WebSocket destination, CancellationToken cancellationToken)
{
    byte[] buffer = new byte[16 * 1024];
    while (!cancellationToken.IsCancellationRequested)
    {
        WebSocketReceiveResult message = await source.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
        if (message.MessageType == WebSocketMessageType.Close)
            return;

        await destination.SendAsync(new ArraySegment<byte>(buffer, 0, message.Count), message.MessageType,
            message.EndOfMessage, cancellationToken);
    }
}

sealed class MatchmakerOptions
{
    public required string ServerExecutable { get; init; }
    public required string ListenAddress { get; init; }
    public required int FirstRoomPort { get; init; }
    public required int MaxRooms { get; init; }
    public required TimeSpan StartTimeout { get; init; }

    public static MatchmakerOptions FromEnvironment()
    {
        string? executable = Environment.GetEnvironmentVariable("FERAL_GAME_SERVER");
        if (string.IsNullOrWhiteSpace(executable))
            throw new InvalidOperationException("Set FERAL_GAME_SERVER to the full path of the Unity dedicated-server executable.");

        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable))
            throw new InvalidOperationException($"Game server executable does not exist: {executable}");

        int firstPort = ReadInt("FERAL_FIRST_ROOM_PORT", 20000, 1024, 65535);
        int maxRooms = ReadInt("FERAL_MAX_ROOMS", 16, 1, 256);
        if (firstPort + maxRooms - 1 > 65535)
            throw new InvalidOperationException("The room port range exceeds 65535.");

        return new MatchmakerOptions
        {
            ServerExecutable = executable,
            ListenAddress = Environment.GetEnvironmentVariable("FERAL_MATCHMAKER_URL") ?? "http://127.0.0.1:18774",
            FirstRoomPort = firstPort,
            MaxRooms = maxRooms,
            StartTimeout = TimeSpan.FromSeconds(ReadInt("FERAL_START_TIMEOUT_SECONDS", 30, 1, 300))
        };
    }

    private static int ReadInt(string name, int fallback, int min, int max)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
            return fallback;
        if (!int.TryParse(value, out int number) || number < min || number > max)
            throw new InvalidOperationException($"{name} must be between {min} and {max}.");
        return number;
    }
}

sealed record CustomListing(string host, int mode, int teamSize, int occupied, int capacity, int availableHumans,
    int humanCapacity);

sealed class Room
{
    public required int Port { get; init; }
    public required Process Process { get; init; }
    public int Connections { get; set; }
    public bool Sealed { get; set; }
    public bool JoiningClosed;
    public Guid Id { get; } = Guid.NewGuid();
    public bool Custom { get; init; }
    public CustomListing? Listing;
}

sealed class RoomLease : IDisposable
{
    private readonly RoomManager manager;
    private bool released;
    public Room Room { get; }

    public RoomLease(RoomManager manager, Room room)
    {
        this.manager = manager;
        Room = room;
    }

    public void Dispose()
    {
        if (released)
            return;
        released = true;
        manager.Release(Room);
    }
}

sealed class RoomManager
{
    private const int PlayersPerRoom = 4;
    private readonly object gate = new();
    private readonly List<Room> rooms = new();
    private readonly MatchmakerOptions options;
    private readonly ILogger logger;
    private bool stopping;

    public RoomManager(MatchmakerOptions options, ILogger logger)
    {
        this.options = options;
        this.logger = logger;
    }

    public RoomLease? TryReserve()
    {
        lock (gate)
        {
            if (stopping)
                return null;

            Room? room = rooms.Find(item => !item.Custom && !item.Sealed &&
                !Volatile.Read(ref item.JoiningClosed) && !item.Process.HasExited);
            room ??= StartRoom(custom: false);
            if (room == null) return null;
            room.Connections++;
            if (room.Connections == PlayersPerRoom) room.Sealed = true;
            return new RoomLease(this, room);
        }
    }

    public RoomLease? TryHostCustom()
    {
        lock (gate)
        {
            if (stopping) return null;
            var room = StartRoom(custom: true);
            if (room == null) return null;
            room.Connections = 1;
            return new RoomLease(this, room);
        }
    }

    public RoomLease? TryJoinCustom(Guid id)
    {
        lock (gate)
        {
            if (stopping) return null;
            var room = rooms.Find(entry => entry.Custom && entry.Id == id);
            if (room == null || !TryGetJoinable(room, out _)) return null;
            room.Connections++;
            return new RoomLease(this, room);
        }
    }

    public object[] ListCustomLobbies()
    {
        lock (gate)
        {
            var listings = new List<object>();
            foreach (var room in rooms)
            {
                if (!room.Custom || !TryGetJoinable(room, out var listing)) continue;
                listings.Add(new
                {
                    id = room.Id, listing.host, listing.mode, listing.teamSize, listing.occupied,
                    listing.capacity, availableHumans = Math.Min(listing.availableHumans,
                        listing.humanCapacity - room.Connections)
                });
            }
            return listings.ToArray();
        }
    }

    private static bool TryGetJoinable(Room room, out CustomListing listing)
    {
        listing = Volatile.Read(ref room.Listing)!;
        return !Volatile.Read(ref room.JoiningClosed) && !room.Process.HasExited &&
            listing is { availableHumans: > 0 } && room.Connections < listing.humanCapacity;
    }

    private Room? StartRoom(bool custom)
    {
        if (rooms.Count >= options.MaxRooms || FindAvailablePort() is not int port) return null;

        var start = new ProcessStartInfo(options.ServerExecutable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(options.ServerExecutable)!
        };
        foreach (var arg in new[] { "-batchmode", "-nographics", "-logFile", "-", "-port", port.ToString() })
            start.ArgumentList.Add(arg);
        if (custom) start.ArgumentList.Add("-custom-games");
        Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not launch game server.");
        var room = new Room { Port = port, Process = process, Custom = custom };
        process.OutputDataReceived += (_, message) =>
        {
            if (message.Data == null) return;
            if (message.Data.Contains("FERAL_MATCHMAKER_ROOM_CLOSED", StringComparison.Ordinal))
                Volatile.Write(ref room.JoiningClosed, true);
            else if (custom && message.Data.StartsWith("FERAL_CUSTOM_LOBBY ", StringComparison.Ordinal))
            {
                try
                {
                    var listing = JsonSerializer.Deserialize<CustomListing>(message.Data["FERAL_CUSTOM_LOBBY ".Length..]);
                    Volatile.Write(ref room.Listing, listing);
                }
                catch (JsonException exception)
                {
                    logger.LogWarning(exception, "Invalid listing from room {Port}.", room.Port);
                }
            }
            else logger.LogDebug("Room {Port}: {Message}", port, message.Data);
        };
        process.ErrorDataReceived += (_, message) =>
        {
            if (message.Data != null)
                logger.LogWarning("Room {Port}: {Message}", port, message.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        rooms.Add(room);
        logger.LogInformation("Started room on port {Port}, process {ProcessId}.", port, process.Id);
        return room;
    }

    public void Release(Room room)
    {
        lock (gate)
        {
            if (!rooms.Contains(room))
                return;
            room.Connections--;
            if (room.Sealed && !Volatile.Read(ref room.JoiningClosed))
                room.Sealed = false;
            if (room.Connections == 0)
            {
                rooms.Remove(room);
                StopRoom(room);
            }
        }
    }

    public void StopAll()
    {
        lock (gate)
        {
            stopping = true;
            foreach (Room room in rooms)
                StopRoom(room);
            rooms.Clear();
        }
    }

    private int? FindAvailablePort()
    {
        for (int port = options.FirstRoomPort; port < options.FirstRoomPort + options.MaxRooms; port++)
        {
            if (rooms.Exists(room => room.Port == port))
                continue;

            var probe = new TcpListener(IPAddress.Loopback, port);
            try
            {
                probe.Start();
                return port;
            }
            catch (SocketException) { }
            finally
            {
                probe.Stop();
            }
        }
        return null;
    }

    private void StopRoom(Room room)
    {
        try
        {
            if (!room.Process.HasExited)
            {
                room.Process.Kill(entireProcessTree: true);
                room.Process.WaitForExit(2000);
            }
        }
        catch (InvalidOperationException) { }
        finally
        {
            room.Process.Dispose();
        }
        logger.LogInformation("Stopped room on port {Port}.", room.Port);
    }
}
