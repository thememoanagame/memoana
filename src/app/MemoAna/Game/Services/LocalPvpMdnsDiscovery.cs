using System.Collections.Concurrent;
using System.Net;
using Makaretu.Dns;
using MemoAna.Game.Models;

namespace MemoAna.Game.Services;

/// <summary>
/// DNS-SD discovery for Local PVP.  TCP remains the session transport; this
/// class only publishes and resolves the TCP endpoint.
/// </summary>
internal sealed class LocalPvpMdnsDiscovery : IDisposable
{
    internal const string ServiceType = "_memoana-pvp._tcp";
    private readonly ILogger logger;
    private readonly ConcurrentDictionary<string, PendingService> services = new(StringComparer.OrdinalIgnoreCase);
    private MulticastService? multicast;
    private ServiceDiscovery? discovery;
#if ANDROID
    private Android.Net.Wifi.WifiManager.MulticastLock? multicastLock;
#endif

    internal event Action<LocalPvpRoom>? RoomDiscovered;
    internal event Action<string>? RoomRemoved;

    internal LocalPvpMdnsDiscovery(ILogger logger) => this.logger = logger;

    internal void StartAdvertisement(string roomId, string hostName, int tcpPort, int protocolVersion)
    {
        Stop();
        multicast = new MulticastService();
        discovery = new ServiceDiscovery(multicast);
        ConfigureMulticast(multicast);
        ServiceProfile profile = new(new DomainName(roomId), new DomainName(ServiceType), checked((ushort)tcpPort));
        profile.AddProperty("room", roomId);
        profile.AddProperty("host", hostName);
        profile.AddProperty("protocol", "MemoAnaPvp");
        profile.AddProperty("version", protocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        discovery.Advertise(profile);
        multicast.Start();
        logger.LogInformation("Local PVP mDNS advertisement started. Service: {ServiceType}, Instance: {InstanceName}, RoomId: {RoomId}, TcpPort: {TcpPort}, HostName: {HostName}.", ServiceType, profile.FullyQualifiedName, roomId, tcpPort, hostName);
    }

    internal void StartBrowsing()
    {
        Stop();
        multicast = new MulticastService();
        discovery = new ServiceDiscovery(multicast);
        discovery.ServiceInstanceDiscovered += OnServiceInstanceDiscovered;
        discovery.ServiceInstanceShutdown += OnServiceInstanceShutdown;
        multicast.AnswerReceived += OnAnswerReceived;
        ConfigureMulticast(multicast);
        multicast.Start();
        multicast.SendQuery(ServiceType, type: DnsType.PTR);
        logger.LogInformation("Local PVP mDNS discovery started for service type {ServiceType}.", ServiceType);
    }

    private void OnServiceInstanceDiscovered(object? sender, ServiceInstanceDiscoveryEventArgs args)
    {
        string instance = args.ServiceInstanceName.ToString();
        logger.LogDebug("Local PVP mDNS service instance discovered: {InstanceName}.", instance);
        services.GetOrAdd(instance, _ => new PendingService(instance));
        multicast?.SendQuery(args.ServiceInstanceName, type: DnsType.SRV);
    }

    private void OnServiceInstanceShutdown(object? sender, ServiceInstanceShutdownEventArgs args)
    {
        string instance = args.ServiceInstanceName.ToString();
        if (services.TryRemove(instance, out PendingService? service) && service.RoomId is not null)
        {
            logger.LogInformation("Local PVP mDNS service disappeared. RoomId: {RoomId}, Instance: {InstanceName}.", service.RoomId, instance);
            RoomRemoved?.Invoke(service.RoomId);
        }
    }

    private void OnAnswerReceived(object? sender, MessageEventArgs args)
    {
        IEnumerable<ResourceRecord> records = args.Message.Answers.Concat(args.Message.AdditionalRecords);
        foreach (SRVRecord srv in records.OfType<SRVRecord>())
        {
            PendingService service = services.GetOrAdd(srv.Name.ToString(), key => new PendingService(key));
            service.Port = srv.Port;
            service.Host = srv.Target.ToString();
            multicast?.SendQuery(srv.Target, type: DnsType.A);
            multicast?.SendQuery(srv.Target, type: DnsType.AAAA);
            TryPublish(service);
        }

        foreach (TXTRecord txt in records.OfType<TXTRecord>())
        {
            if (!services.TryGetValue(txt.Name.ToString(), out PendingService? service))
                continue;
            foreach (string value in txt.Strings)
            {
                int separator = value.IndexOf('=');
                if (separator > 0)
                    service.Properties[value[..separator]] = value[(separator + 1)..];
            }
            TryPublish(service);
        }

        foreach (AddressRecord address in records.OfType<AddressRecord>())
        {
            foreach (PendingService service in services.Values.Where(value => string.Equals(value.Host, address.Name.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                service.Addresses[address.Address.AddressFamily] = address.Address;
                TryPublish(service);
            }
        }
    }

    private void ConfigureMulticast(MulticastService service)
    {
        service.NetworkInterfaceDiscovered += OnNetworkInterfaceDiscovered;
#if ANDROID
        try
        {
            Android.Net.Wifi.WifiManager? wifiManager =
                Android.App.Application.Context.GetSystemService(Android.Content.Context.WifiService)
                as Android.Net.Wifi.WifiManager;
            if (wifiManager is null)
                throw new InvalidOperationException("O serviço Wi-Fi do Android não está disponível.");

            Android.Net.Wifi.WifiManager.MulticastLock? acquiredLock =
                wifiManager.CreateMulticastLock("MemoAna.LocalPvp");
            if (acquiredLock is null)
                throw new InvalidOperationException("O Android não conseguiu criar o bloqueio multicast Wi-Fi.");
            acquiredLock.SetReferenceCounted(false);
            acquiredLock.Acquire();
            multicastLock = acquiredLock;
            logger.LogInformation("Local PVP Android Wi-Fi multicast lock acquired.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Local PVP could not acquire the Android Wi-Fi multicast lock.");
            throw;
        }
#endif
    }

    private void OnNetworkInterfaceDiscovered(object? sender, Makaretu.Dns.NetworkInterfaceEventArgs args)
    {
        logger.LogInformation(
            "Local PVP mDNS network interfaces discovered by Makaretu: {Interfaces}.",
            string.Join(", ", args.NetworkInterfaces));
    }

    private void TryPublish(PendingService service)
    {
        if (service.Port is null || service.Addresses.Count == 0 ||
            !service.Properties.TryGetValue("room", out string? roomId) || string.IsNullOrWhiteSpace(roomId) ||
            !service.Properties.TryGetValue("host", out string? hostName) || string.IsNullOrWhiteSpace(hostName) ||
            !service.Properties.TryGetValue("protocol", out string? protocol) || protocol != "MemoAnaPvp" ||
            !service.Properties.TryGetValue("version", out string? version) || version != "1")
            return;

        IPAddress address = service.Addresses.Values.FirstOrDefault(value => value.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            ?? service.Addresses.Values.First();
        LocalPvpRoom room = new(roomId, hostName, address.ToString(), service.Port.Value, DateTime.UtcNow);
        bool changed = service.PublishedRoom is null || service.PublishedRoom.HostAddress != room.HostAddress || service.PublishedRoom.TcpPort != room.TcpPort || service.PublishedRoom.HostName != room.HostName;
        service.RoomId = roomId;
        if (changed)
        {
            service.PublishedRoom = room;
            logger.LogInformation("Local PVP mDNS service resolved. Instance: {InstanceName}, RoomId: {RoomId}, HostName: {HostName}, HostAddress: {HostAddress}, TcpPort: {TcpPort}.", service.Instance, room.RoomId, room.HostName, room.HostAddress, room.TcpPort);
            RoomDiscovered?.Invoke(room);
        }
    }

    internal void Stop()
    {
        if (discovery is not null)
            discovery.Dispose();
        multicast?.Stop();
        multicast?.Dispose();
        discovery = null;
        multicast = null;
        services.Clear();
#if ANDROID
        if (multicastLock is not null)
        {
            try
            {
                if (multicastLock.IsHeld)
                    multicastLock.Release();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Local PVP Android Wi-Fi multicast lock could not be released.");
            }
            finally
            {
                multicastLock.Dispose();
                multicastLock = null;
                logger.LogInformation("Local PVP Android Wi-Fi multicast lock released.");
            }
        }
#endif
    }

    public void Dispose() => Stop();

    private sealed class PendingService(string instance)
    {
        internal string Instance { get; } = instance;
        internal string? RoomId { get; set; }
        internal string? Host { get; set; }
        internal int? Port { get; set; }
        internal LocalPvpRoom? PublishedRoom { get; set; }
        internal Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<System.Net.Sockets.AddressFamily, IPAddress> Addresses { get; } = [];
    }
}
